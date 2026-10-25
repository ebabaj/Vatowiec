using System.Diagnostics;
using System.Diagnostics.Tracing;
using System.IO.Pipes;
using System.Data.Common;
using System.Runtime.Versioning;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Vatowiec.Core.Application;
using Vatowiec.Core.Domain;
using Vatowiec.Core.Infrastructure.Identity;
using Vatowiec.Core.Infrastructure.Personal;
using Vatowiec.Core.Infrastructure.Persistence;
using Vatowiec.Core.Infrastructure.Registries;
using Vatowiec.Desktop.Services;
using Vatowiec.SharedContracts;

namespace Vatowiec.Tests;

[Collection("LocalSql")]
[SupportedOSPlatform("windows")]
public sealed class PersonalDefaultsTests
{
    [Theory]
    [InlineData("a")]
    [InlineData("1")]
    [InlineData(" ")]
    public void PersonalAcceptsAnyNonemptyPasswordWhileNetworkDefaultsStayUnchanged(string password)
    {
        Assert.True(InstallationPasswordValidator.Validate(PersonalBootstrapDefaults.PasswordPolicy(), password).Succeeded);
        Assert.False(InstallationPasswordValidator.Validate(new PasswordPolicy(), password).Succeeded);
        Assert.Throws<PersonalSetupException>(() => PersonalInstallation.ValidateInput(PersonalSetupTests.Input("")));
        Assert.True(new PasswordPolicy().IsEnabled); Assert.Equal(12, new PasswordPolicy().MinimumLength);
        Assert.All(CurrencySeed.Rows(), row => Assert.True(row.IsActive));
        Assert.Equal(new[] { "CHF", "EUR", "GBP", "USD" }, CurrencySeed.Rows().Where(x => x.DownloadA).Select(x => x.Code).Order());
    }

    [PersonalLocalDbFact]
    public async Task RealPersonal_SimplePasswordDefaultsAdminEditsAndRestartNeverReinitialize()
    {
        var root = PersonalSetupTests.NewRoot(); Guid company; PersonalDatabaseTarget target;
        PasswordPolicyResponse editedPolicy; RegistryConfigurationResponse[] editedRegistries; CurrencyResponse editedEuro;
        using (var backend = new PersonalBackend(root))
        {
            Assert.True((await backend.Send(new())).Success);
            var empty = await backend.Send(PersonalSetupTests.Input("")); Assert.False(empty.Success); Assert.Contains("puste", empty.Message);
            var result = await backend.Send(PersonalSetupTests.Input("a")); Assert.True(result.Success, result.Message);
            var ready = await backend.Send(new() { Operation = "serve" }); Assert.True(ready.Success, ready.Message);
            target = new(backend.Connection!.InstallationId);
            using var api = backend.Connect(ready.ApiAddress!);
            var user = await api.Login("wizard.fixture", "a"); Assert.True(user.IsInstallationAdministrator);
            company = Assert.Single(await api.Companies()).CompanyId;
            var policy = await api.PasswordPolicy(); AssertDefaultPolicy(policy);
            Assert.Single(await api.PasswordPolicyHistory(0));
            var currencies = await api.Currencies(company); AssertCompanyDefaults(currencies);
            Assert.Empty((await api.Rates(company, null, null, null, null, 0, true)).Items);
            var registries = await api.RegistryConfigurations(); AssertRegistryDefaults(registries);
            await using (var db = System(target))
            {
                var changes = await db.RegistryConfigurationChanges.AsNoTracking().ToArrayAsync();
                Assert.Equal(2, changes.Length); Assert.All(changes, c => Assert.Equal(user.UserId, c.ActorId));
                Assert.All(changes, c => { Assert.Contains("\"Test\"", c.BeforeJson); Assert.Contains("\"Production\"", c.AfterJson); });
            }
            var euro = Assert.Single(currencies, c => c.Code == "EUR");
            editedEuro = await api.SaveCurrency(company, "EUR", new(Guid.NewGuid(), euro.RowVersion, true, false, false, false));
            Assert.True(editedEuro.IsActive); Assert.False(editedEuro.DownloadA || editedEuro.DownloadB || editedEuro.DownloadC);
            editedPolicy = await api.SavePasswordPolicy(new(true, 3, false, true, false, false, policy.RowVersion, false,
                PreventPasswordReuse: true, PreviousPasswordCount: 2, EnforcePasswordExpiration: true, PasswordValidityDays: 365));
            Assert.True(editedPolicy.IsEnabled && editedPolicy.PreventPasswordReuse && editedPolicy.EnforcePasswordExpiration);
            var vies = registries.Single(x => x.Source == "VIES");
            var testVies = await api.SaveRegistryConfiguration("VIES", new("Test", vies.TestUrl, vies.ProductionUrl, null, null, vies.RowVersion));
            var error = await Assert.ThrowsAsync<ApiException>(() => api.SaveRegistryConfiguration("VIES", new("Production", vies.TestUrl, vies.ProductionUrl, null, null, testVies.RowVersion)));
            Assert.Equal(400, error.StatusCode); Assert.Equal("production_confirmation_required", error.Code);
            editedRegistries = await api.RegistryConfigurations();
            await api.Logout();
        }
        using (var backend = new PersonalBackend(root))
        {
            Assert.True((await backend.Send(new())).Complete);
            var denied = await backend.Send(PersonalSetupTests.Input("other")); Assert.False(denied.Success);
            var ready = await backend.Send(new() { Operation = "serve" }); Assert.True(ready.Success, ready.Message);
            using var api = backend.Connect(ready.ApiAddress!);
            await api.Login("wizard.fixture", "a"); // Enabling policy never invalidates the existing hash.
            Assert.Equal(editedPolicy, await api.PasswordPolicy());
            Assert.Equal(editedRegistries, await api.RegistryConfigurations());
            Assert.Equal(editedEuro, Assert.Single(await api.Currencies(company), c => c.Code == "EUR"));
            Assert.Empty((await api.Rates(company, null, null, null, null, 0, true)).Items);
            Assert.DoesNotContain(await api.CurrencyHistory(company, 0), c => c.Kind == "Import");
            await api.Logout();
        }
        await using (var db = Company(target, company))
        {
            await PersonalBootstrapDefaults.InitializeCompany(db, target.InstallationId, Guid.NewGuid(), "Firma fikcyjna kreatora", null);
            Assert.Equal(Convert.FromBase64String(editedEuro.RowVersion), (await db.Currencies.SingleAsync(x => x.Code == "EUR")).RowVersion);
            Assert.Empty(await db.ExternalVerifications.ToArrayAsync());
            Assert.Single(await db.CurrencyOperations.Where(x => x.Kind == "PersonalInitialized").ToArrayAsync());
        }
        await PersonalInstallation.LocalDb("stop", target.Instance);
    }

    [PersonalLocalDbFact]
    public async Task LegacyUnfinishedBootstrapResumesWithPersonalDefaults_AndNextCompanyUsesSameInitializer()
    {
        var root = PersonalSetupTests.NewRoot();
        await CreateLegacyInterruptedBootstrap(root);
        var state = JsonSerializer.Deserialize<PersonalInstallationState>(await File.ReadAllTextAsync(Path.Combine(root, "installation.json")))!;
        var target = new PersonalDatabaseTarget(state.InstallationId);
        await using (var db = System(target)) { Assert.True((await db.PasswordPolicies.SingleAsync()).IsEnabled); Assert.False(await db.Users.AnyAsync()); }
        await using (var db = Company(target, state.CompanyId)) { Assert.Equal(149, await db.Currencies.CountAsync()); Assert.True((await db.Currencies.SingleAsync(x => x.Code == "EUR")).DownloadA); }
        await using (var failed = new SystemDbContext(new DbContextOptionsBuilder<SystemDbContext>().UseSqlServer(target.Connection(target.System)).AddInterceptors(new FailBootstrapAudit()).Options))
        {
            await using var tx = await failed.Database.BeginTransactionAsync();
            await UserAdministrationService.Lock(failed, default);
            var issuer = (await failed.InstallationIdentities.SingleAsync()).Id;
            using var httpProbe = new BootstrapHttpProbe();
            await Assert.ThrowsAsync<DbUpdateException>(() => PersonalBootstrapDefaults.InitializeSystem(failed, target.InstallationId, Guid.NewGuid(), issuer, "Fikcyjna próba atomowości"));
            Assert.Equal(0, httpProbe.Calls);
            await tx.RollbackAsync();
        }
        await using (var db = System(target))
        {
            Assert.True((await db.PasswordPolicies.SingleAsync()).IsEnabled);
            Assert.All(await db.RegistryConfigurations.ToArrayAsync(), c => Assert.Equal("Test", c.Environment));
            Assert.Empty(await db.PasswordPolicyChanges.ToArrayAsync()); Assert.Empty(await db.RegistryConfigurationChanges.ToArrayAsync());
        }
        using (var backend = new PersonalBackend(root))
        {
            Assert.False((await backend.Send(new())).Complete);
            var initialized = await backend.Send(PersonalSetupTests.Input("b")); Assert.True(initialized.Success, initialized.Message);
            var ready = await backend.Send(new() { Operation = "serve" }); Assert.True(ready.Success, ready.Message);
            using var api = backend.Connect(ready.ApiAddress!); await api.Login("wizard.fixture", "b");
            AssertDefaultPolicy(await api.PasswordPolicy()); AssertRegistryDefaults(await api.RegistryConfigurations());
            AssertCompanyDefaults(await api.Currencies(state.CompanyId)); await api.Logout();
        }
        // A second newly provisioned database on this SAME private instance. No existing databases touched.
        var nextCompany = Guid.NewGuid();
        var database = "Vatowiec_Personal_" + state.InstallationId.ToString("N") + "_Company_" + nextCompany.ToString("N");
        var sqlData = Path.Combine(root, "SqlData");
        using (var setup = new PersonalInstallation(root)) { Assert.True((await setup.Status()).Complete); }
        await using (var master = new SqlConnection(target.Connection("master")))
        {
            await master.OpenAsync(); await using var check = master.CreateCommand();
            check.CommandText = "SELECT CASE WHEN SERVERPROPERTY('IsLocalDB')=1 AND SERVERPROPERTY('ProductMajorVersion')=N'17' AND DB_ID(@database) IS NULL THEN 1 ELSE 0 END";
            check.Parameters.AddWithValue("@database", database); Assert.Equal(1, await check.ExecuteScalarAsync());
            var data = Path.Combine(sqlData, database + ".mdf"); var log = Path.Combine(sqlData, database + "_log.ldf");
            Assert.False(File.Exists(data) || File.Exists(log));
            check.CommandText = $"CREATE DATABASE [{database}] ON PRIMARY (NAME=N'{database}',FILENAME=N'{data.Replace("'", "''")}',SIZE=8MB,FILEGROWTH=8MB) LOG ON (NAME=N'{database}_log',FILENAME=N'{log.Replace("'", "''")}',SIZE=8MB,FILEGROWTH=8MB)";
            await check.ExecuteNonQueryAsync();
        }
        var connection = new SqlConnectionStringBuilder(target.Connection(target.Company)) { InitialCatalog = database }.ConnectionString;
        await using (var db = new CompanyDbContext(new DbContextOptionsBuilder<CompanyDbContext>().UseSqlServer(connection).Options, new CompanyContext(nextCompany)))
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"EXEC sys.sp_addextendedproperty @name=N'Vatowiec.Personal.Owner',@value={state.InstallationId.ToString("N")}");
            await db.Database.MigrateAsync();
            var before = (await db.Currencies.IgnoreQueryFilters().SingleAsync(x => x.Code == "PLN")).RowVersion.ToArray();
            using var httpProbe = new BootstrapHttpProbe();
            await PersonalBootstrapDefaults.InitializeCompany(db, state.InstallationId, Guid.NewGuid(), "Kolejna fikcyjna firma Personal", null);
            AssertCompanyDefaults((await db.Currencies.ToArrayAsync()).Select(c => new CurrencyResponse(nextCompany, c.Code, c.Name, c.IsActive, c.DownloadA, c.DownloadB, c.DownloadC, Convert.ToBase64String(c.RowVersion))).ToArray());
            Assert.Equal(before, (await db.Currencies.SingleAsync(x => x.Code == "PLN")).RowVersion);
            Assert.Single(await db.CurrencyOperations.ToArrayAsync()); Assert.Empty(await db.ExchangeRates.ToArrayAsync());
            Assert.Equal(0, httpProbe.Calls);
        }
        await PersonalInstallation.LocalDb("stop", target.Instance);
    }

    private static void AssertDefaultPolicy(PasswordPolicyResponse p)
    { Assert.False(p.IsEnabled || p.RequireUppercase || p.RequireLowercase || p.RequireDigit || p.RequireSpecialCharacter || p.PreventPasswordReuse || p.EnforcePasswordExpiration); Assert.Equal(1, p.MinimumLength); }
    private static void AssertCompanyDefaults(CurrencyResponse[] rows)
    { Assert.Equal(149, rows.Length); Assert.Equal("PLN", Assert.Single(rows, c => c.IsActive).Code); Assert.All(rows, c => Assert.False(c.DownloadA || c.DownloadB || c.DownloadC)); }
    private static void AssertRegistryDefaults(RegistryConfigurationResponse[] rows)
    {
        Assert.Equal(3, rows.Length);
        foreach (var row in rows)
        {
            Assert.Equal(row.Source == "GUS" ? "Test" : "Production", row.Environment);
            Assert.True(RegistrySettings.IsAllowedUrl(row.Source, "Production", row.ProductionUrl));
        }
        Assert.Equal(RegistrySettings.OfficialUrl("MF", "Production", true), rows.Single(x => x.Source == "MF").ProductionWl2Url);
    }
    private static SystemDbContext System(PersonalDatabaseTarget t) => new(new DbContextOptionsBuilder<SystemDbContext>().UseSqlServer(t.Connection(t.System)).Options);
    private static CompanyDbContext Company(PersonalDatabaseTarget t, Guid company) => new(new DbContextOptionsBuilder<CompanyDbContext>().UseSqlServer(t.Connection(t.Company)).Options, new CompanyContext(company));

    // Exercise the actual previously published 0.1.0 worker, through its inherited private channel.
    private static async Task CreateLegacyInterruptedBootstrap(string root)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "AGENTS.md"))) directory = directory.Parent;
        var api = Path.Combine(directory?.FullName ?? throw new IOException("Repository not found."), "artifacts", "personal-releases", "0.1.0-preview", "publish", "Server", "Vatowiec.Api.exe");
        if (!File.Exists(api)) throw new IOException("The preserved 0.1.0-preview publish is required to verify legacy bootstrap recovery.");
        using var input = new AnonymousPipeServerStream(PipeDirection.In, HandleInheritability.Inheritable);
        using var output = new AnonymousPipeServerStream(PipeDirection.Out, HandleInheritability.Inheritable);
        var start = new ProcessStartInfo(api) { UseShellExecute = false, CreateNoWindow = true };
        start.ArgumentList.Add("--personal-worker"); start.ArgumentList.Add(output.GetClientHandleAsString()); start.ArgumentList.Add(input.GetClientHandleAsString());
        using var worker = Process.Start(start) ?? throw new IOException("Legacy worker not started.");
        output.DisposeLocalCopyOfClientHandle(); input.DisposeLocalCopyOfClientHandle();
        using var writer = new StreamWriter(output) { AutoFlush = true }; using var reader = new StreamReader(input);
        try
        {
            await writer.WriteLineAsync(JsonSerializer.Serialize(root));
            await writer.WriteLineAsync(JsonSerializer.Serialize(PersonalSetupTests.Input()));
            while (true)
            {
                var status = JsonSerializer.Deserialize<PersonalSetupStatus>(await reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(120)) ?? throw new IOException("Legacy worker exited."))!;
                if (status.Stage == "administrator") { worker.Kill(); await worker.WaitForExitAsync(); break; }
                Assert.False(status.Final, status.Message);
            }
        }
        finally { if (!worker.HasExited) { writer.Dispose(); if (!worker.WaitForExit(60000)) worker.Kill(); } }
    }

    private sealed class BootstrapHttpProbe : EventListener
    {
        private static readonly AsyncLocal<BootstrapHttpProbe?> Current = new();
        public BootstrapHttpProbe() => Current.Value = this;
        public int Calls;
        protected override void OnEventSourceCreated(EventSource source) { if (source.Name == "System.Net.Http") EnableEvents(source, EventLevel.Informational); }
        protected override void OnEventWritten(EventWrittenEventArgs data) { if (data.EventName == "RequestStart" && ReferenceEquals(Current.Value, this)) Interlocked.Increment(ref Calls); }
        public override void Dispose() { if (ReferenceEquals(Current.Value, this)) Current.Value = null; base.Dispose(); }
    }
    private sealed class FailBootstrapAudit : DbCommandInterceptor
    {
        private static void Check(DbCommand command)
        { if (command.CommandText.Contains("INSERT INTO [audit].[RegistryConfigurationChanges]", StringComparison.Ordinal)) throw new DbUpdateException("Fikcyjna awaria audytu bootstrapu."); }
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData data, InterceptionResult<DbDataReader> result, CancellationToken ct = default)
        { Check(command); return ValueTask.FromResult(result); }
        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData data, InterceptionResult<int> result, CancellationToken ct = default)
        { Check(command); return ValueTask.FromResult(result); }
    }
}
