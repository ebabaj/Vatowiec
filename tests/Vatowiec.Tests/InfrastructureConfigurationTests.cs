using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Vatowiec.Core.Application;
using Vatowiec.Core.Infrastructure;
using Vatowiec.Core.Infrastructure.Persistence;

namespace Vatowiec.Tests;

public sealed class InfrastructureConfigurationTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void MissingSystemConnection_ConfiguresProviderWithoutConnecting(string? connectionString)
    {
        using var provider = new ServiceCollection().AddInfrastructure(connectionString).BuildServiceProvider(true);
        using var scope = provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<SystemDbContext>();
        Assert.Equal("Microsoft.EntityFrameworkCore.SqlServer", context.Database.ProviderName);
        Assert.Equal(ConnectionState.Closed, context.Database.GetDbConnection().State);
        Assert.True(string.IsNullOrWhiteSpace(context.Database.GetConnectionString()));
        Assert.Throws<InvalidOperationException>(() => scope.ServiceProvider.GetRequiredService<CompanyDbContext>());
    }

    [Fact]
    public void LegacyConfiguration_IsIgnored()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Vatowiec"] = "Server=legacy.invalid;Database=Vatowiec_Dev;Integrated Security=True"
        }).Build();
        using var provider = new ServiceCollection().AddInfrastructure(configuration, false).BuildServiceProvider(true);
        using var scope = provider.CreateScope();
        Assert.True(string.IsNullOrEmpty(scope.ServiceProvider.GetRequiredService<SystemDbContext>()
            .Database.GetConnectionString()));
        Assert.Throws<InvalidOperationException>(() => scope.ServiceProvider.GetRequiredService<CompanyDbContext>());
    }

    [Fact]
    public void CompanyDatabase_RequiresMatchingContextAndSelection()
    {
        var id = Guid.NewGuid();
        foreach (var selectedId in new[] { id, Guid.NewGuid() })
        {
            var services = new ServiceCollection().AddInfrastructure(null);
            services.AddScoped<ICompanyContext>(_ => new CompanyContext(id));
            services.AddScoped(_ => new CompanyDatabaseSelection(selectedId,
                "Server=company.invalid;Database=CompanyA;Integrated Security=True;Encrypt=True"));
            using var provider = services.BuildServiceProvider(true);
            using var scope = provider.CreateScope();
            if (selectedId != id)
            {
                Assert.Throws<InvalidOperationException>(() => scope.ServiceProvider.GetRequiredService<CompanyDbContext>());
                continue;
            }

            var context = scope.ServiceProvider.GetRequiredService<CompanyDbContext>();
            Assert.Equal(id, context.CurrentCompanyId);
            Assert.Equal("CompanyA", context.Database.GetDbConnection().Database);
            Assert.Equal(ConnectionState.Closed, context.Database.GetDbConnection().State);
        }
    }

    [Fact]
    public void CompanySelectionAlone_DoesNotGrantCompanyContext()
    {
        var services = new ServiceCollection().AddInfrastructure(null);
        services.AddScoped(_ => new CompanyDatabaseSelection(Guid.NewGuid(),
            "Server=company.invalid;Database=CompanyA;Integrated Security=True;Encrypt=True"));
        using var provider = services.BuildServiceProvider(true);
        using var scope = provider.CreateScope();
        Assert.Throws<InvalidOperationException>(() => scope.ServiceProvider.GetRequiredService<CompanyDbContext>());
    }

    [Fact]
    public void ScopedSelections_KeepDatabasesAndChangeTrackersSeparate()
    {
        var services = new ServiceCollection().AddInfrastructure(
            "Server=system.invalid;Database=Vatowiec_System;Integrated Security=True;Encrypt=True");
        services.AddScoped<ICompanyContext>(_ => new CompanyContext(Guid.NewGuid()));
        services.AddScoped(provider => new CompanyDatabaseSelection(
            provider.GetRequiredService<ICompanyContext>().CompanyId!.Value,
            $"Server=company.invalid;Database=Company_{provider.GetRequiredService<ICompanyContext>().CompanyId:N};Integrated Security=True;Encrypt=True"));
        using var provider = services.BuildServiceProvider(true);
        using var firstScope = provider.CreateScope();
        using var secondScope = provider.CreateScope();
        var first = firstScope.ServiceProvider.GetRequiredService<CompanyDbContext>();
        var second = secondScope.ServiceProvider.GetRequiredService<CompanyDbContext>();
        Assert.Same(first, firstScope.ServiceProvider.GetRequiredService<CompanyDbContext>());
        Assert.NotSame(first, second);
        Assert.NotEqual(first.Database.GetDbConnection().Database, second.Database.GetDbConnection().Database);
        var system = firstScope.ServiceProvider.GetRequiredService<SystemDbContext>();
        Assert.Same(system, firstScope.ServiceProvider.GetRequiredService<SystemDbContext>());
        Assert.NotSame(system, secondScope.ServiceProvider.GetRequiredService<SystemDbContext>());
        Assert.Equal("Vatowiec_System", system.Database.GetDbConnection().Database);
    }

    [Fact]
    public void Selection_DoesNotExposeSecretInDiagnosticText()
    {
        var selection = new CompanyDatabaseSelection(Guid.NewGuid(),
            "Server=secret.invalid;Database=CompanyA;User ID=example;Password=fictional-test-secret;Encrypt=True");
        Assert.DoesNotContain("fictional-test-secret", selection.ToString());
        Assert.DoesNotContain("secret.invalid", selection.ToString());
    }
}
