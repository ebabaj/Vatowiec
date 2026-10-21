using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Vatowiec.Core.Infrastructure;
using Vatowiec.Core.Infrastructure.Persistence;

namespace Vatowiec.Tests;

public sealed class InfrastructureConfigurationTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void MissingConnectionString_ConfiguresSqlServerWithoutConnecting(string? connectionString)
    {
        using var provider = new ServiceCollection()
            .AddInfrastructure(connectionString)
            .BuildServiceProvider(validateScopes: true);
        using var scope = provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<CoreDbContext>();

        Assert.Equal("Microsoft.EntityFrameworkCore.SqlServer", context.Database.ProviderName);
        Assert.Equal(ConnectionState.Closed, context.Database.GetDbConnection().State);
        Assert.Empty(context.Model.GetEntityTypes());
    }

    [Fact]
    public void SuppliedConnectionString_IsConfiguredWithoutConnecting()
    {
        // Nieistniejący adres testowy; test nie otwiera połączenia.
        const string connectionString =
            "Server=sql-not-used.invalid;Database=VatowiecTests;Integrated Security=True;Encrypt=True";
        using var provider = new ServiceCollection()
            .AddInfrastructure(connectionString)
            .BuildServiceProvider(validateScopes: true);
        using var scope = provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<CoreDbContext>();

        var connection = context.Database.GetDbConnection();
        Assert.Equal("sql-not-used.invalid", connection.DataSource);
        Assert.Equal("VatowiecTests", connection.Database);
        Assert.Equal(ConnectionState.Closed, connection.State);
    }

    [Fact]
    public void DbContext_IsSharedWithinScopeAndSeparateAcrossScopes()
    {
        using var provider = new ServiceCollection()
            .AddInfrastructure(null)
            .BuildServiceProvider(validateScopes: true);
        using var firstScope = provider.CreateScope();
        using var secondScope = provider.CreateScope();

        var first = firstScope.ServiceProvider.GetRequiredService<CoreDbContext>();
        var firstAgain = firstScope.ServiceProvider.GetRequiredService<CoreDbContext>();
        var second = secondScope.ServiceProvider.GetRequiredService<CoreDbContext>();

        Assert.Same(first, firstAgain);
        Assert.NotSame(first, second);
    }
}
