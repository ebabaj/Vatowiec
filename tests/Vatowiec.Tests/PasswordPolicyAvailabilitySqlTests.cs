using System.Collections.Concurrent;
using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Vatowiec.Core.Infrastructure.Identity;
using Vatowiec.SharedContracts;

namespace Vatowiec.Tests;

public sealed partial class UserAdministrationSqlTests
{
    [PreservedVatSqlFact]
    public async Task MissingPolicySchema_RealApiReportsUnavailable_AndSqlLogsIdentify208_WithoutDefaultConfiguration()
    {
        await using var tx = await PreservedSystemTransaction.Open();
        // Bootstrap only a fictional account; the API itself always uses the real database policy provider.
        using var db = tx.Context();
        var setup = new ServiceCollection().AddLogging().AddSingleton(db); setup.AddVatowiecIdentity();
        setup.AddScoped<IPasswordPolicyProvider>(_ => new FixedPasswordPolicyProvider());
        using var provider = setup.BuildServiceProvider();
        var password = "Fixture!1a-" + Guid.NewGuid().ToString("N"); var actor = await Account(provider, true, password);
        var logs = new SqlNumberLogs(); var unavailable = new MissingPolicyRead();
        using var app = App(tx, unavailable, logs); using var client = Client(app);
        await Login(client, actor.UserName!, password);
        unavailable.Enabled = true; unavailable.ReadsToSkip = 1; // Session validation reads the real policy before the endpoint's failed read.
        var response = await client.GetAsync(PolicyRoute);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var error = (await response.Content.ReadFromJsonAsync<JsonElement>());
        Assert.Equal("password_policy_schema_missing", error.GetProperty("code").GetString());
        Assert.Contains(208, logs.Numbers);
        // No fake default response was returned and no configuration was inserted.
        Assert.False(error.TryGetProperty("minimumLength", out _));
    }
    private sealed class MissingPolicyRead : DbCommandInterceptor
    {
        public bool Enabled;
        public int ReadsToSkip;
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData data,
            InterceptionResult<DbDataReader> result, CancellationToken ct = default)
        {
            // Real SQL error without DDL, deleting data or changing existing configuration.
            if (Enabled && command.CommandText.Contains("[security].[PasswordPolicy]", StringComparison.Ordinal) && --ReadsToSkip < 0)
                command.CommandText = command.CommandText.Replace("[security].[PasswordPolicy]", "[security].[UnavailablePasswordPolicyForTest]", StringComparison.Ordinal);
            return ValueTask.FromResult(result);
        }
    }
    private sealed class SqlNumberLogs : ILoggerProvider
    {
        public ConcurrentBag<int> Numbers { get; } = [];
        public ILogger CreateLogger(string categoryName) => new NumberLogger(Numbers);
        public void Dispose() { }
        private sealed class NumberLogger(ConcurrentBag<int> numbers) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel level) => level >= LogLevel.Error;
            public void Log<TState>(LogLevel level, EventId id, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                if (state is IEnumerable<KeyValuePair<string, object?>> fields)
                    foreach (var field in fields)
                        if (field.Key == "SqlNumber" && field.Value is int number) numbers.Add(number);
                for (var error = exception; error != null; error = error.InnerException)
                    if (error is SqlException sql) numbers.Add(sql.Number);
            }
        }
    }
}
