using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Vatowiec.Core.Application;
using Vatowiec.Core.Domain;
using Vatowiec.Core.Infrastructure.Persistence;
using Vatowiec.Core.Infrastructure.Identity;

namespace Vatowiec.Tests;

public sealed class CompanyModelTests
{
    [Fact]
    public void Domain_RejectsEmptyIdentityNameAndInvalidCountry()
    {
        Assert.Throws<ArgumentException>(() => new Company(Guid.Empty, "Firma testowa"));
        Assert.Throws<ArgumentException>(() => new Company(Guid.NewGuid(), " "));
        Assert.Throws<ArgumentException>(() => new CompanyProfile(Guid.Empty, "Firma testowa"));
        Assert.Throws<ArgumentException>(() => new CompanyProfile(Guid.NewGuid(), new string('X', 257)));
        Assert.Throws<ArgumentException>(() => new CompanyDatabaseRegistration(Guid.Empty, "sql", "db", "profile"));
        Assert.Throws<ArgumentException>(() => new CompanyDatabaseRegistration(Guid.NewGuid(), " ", "db", "profile"));
        Assert.Throws<ArgumentException>(() => new Contractor(Guid.Empty, Guid.NewGuid(), "A", "Test", "PL"));
        Assert.Throws<ArgumentException>(() => new Contractor(Guid.NewGuid(), Guid.NewGuid(), "A", "Test", "P1"));
        var contractor = new Contractor(Guid.NewGuid(), Guid.NewGuid(), " abc ", " Test ", "pl");
        Assert.Equal("ABC", contractor.Code);
        Assert.Equal("Test", contractor.Name);
        Assert.Equal("PL", contractor.CountryCode);
        Assert.Null(contractor.TaxIdentifier);
    }

    [Fact]
    public void Models_AreDisjointAndCompanyForeignKeysStayLocal()
    {
        using var system = new SystemDbContextFactory().CreateDbContext([]);
        using var company = new CompanyDbContextFactory().CreateDbContext([]);
        Assert.NotNull(system.Model.FindEntityType(typeof(ApplicationUser)));
        Assert.NotNull(system.Model.FindEntityType(typeof(CompanyAccess)));
        Assert.NotNull(system.Model.FindEntityType(typeof(UserSession)));
        Assert.Null(system.Model.FindEntityType(typeof(Contractor)));
        Assert.Null(system.Model.FindEntityType(typeof(CompanyProfile)));
        Assert.Null(system.Model.FindEntityType(typeof(ContractorChange)));
        Assert.Equal(new[] { typeof(CompanyProfile), typeof(Contractor), typeof(ContractorChange) },
            company.Model.GetEntityTypes().Select(x => x.ClrType).OrderBy(x => x.Name));
        var contractor = company.Model.FindEntityType(typeof(Contractor))!;
        Assert.Equal(typeof(CompanyProfile), Assert.Single(contractor.GetForeignKeys()).PrincipalEntityType.ClrType);
        Assert.Equal(new[] { "CompanyId", "Id" }, contractor.FindPrimaryKey()!.Properties.Select(x => x.Name));
        Assert.True(contractor.FindProperty("RowVersion")!.IsConcurrencyToken);
        var profile = company.Model.FindEntityType(typeof(CompanyProfile))!;
        Assert.Equal("SingletonKey", Assert.Single(profile.FindPrimaryKey()!.Properties).Name);
        Assert.Contains(profile.GetKeys(), x => x.Properties.Select(p => p.Name).SequenceEqual(new[] { "CompanyId" }));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SaveChanges_RejectsMissingAndMismatchedCompanyBeforeConnecting(bool async)
    {
        var options = new DbContextOptionsBuilder<CompanyDbContext>().UseSqlServer().Options;
        var id = Guid.NewGuid();
        foreach (var scope in new Guid?[] { null, Guid.Empty, Guid.NewGuid() })
        {
            await using var context = new CompanyDbContext(options, new CompanyContext(scope));
            context.CompanyProfiles.Add(new CompanyProfile(id, "Firma testowa"));
            context.Contractors.Add(new Contractor(id, Guid.NewGuid(), "A", "Test", "PL"));
            if (async)
                await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());
            else
                Assert.Throws<InvalidOperationException>(() => context.SaveChanges());
        }
    }

    [Fact]
    public void QueryFilters_AreParameterizedPerContext()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var options = new DbContextOptionsBuilder<CompanyDbContext>().UseSqlServer().Options;
        using var first = new CompanyDbContext(options, new CompanyContext(a));
        using var second = new CompanyDbContext(options, new CompanyContext(b));
        var firstSql = first.Contractors.ToQueryString();
        var secondSql = second.Contractors.ToQueryString();
        Assert.Contains(a.ToString(), firstSql);
        Assert.DoesNotContain(b.ToString(), firstSql);
        Assert.Contains(b.ToString(), secondSql);
    }

    [Fact]
    public void MigrationSets_ContainOnlyTheirOwnTablesAndMatchSnapshots()
    {
        using var system = new SystemDbContextFactory().CreateDbContext([]);
        using var company = new CompanyDbContextFactory().CreateDbContext([]);
        Assert.Equal(3, system.Database.GetMigrations().Count());
        Assert.EndsWith("_InstallationIdentity", system.Database.GetMigrations().Last());
        Assert.Equal(2, company.Database.GetMigrations().Count());
        Assert.EndsWith("_ContractorAudit", company.Database.GetMigrations().Last());
        Assert.False(system.Database.HasPendingModelChanges());
        Assert.False(company.Database.HasPendingModelChanges());
        Assert.NotEqual(system.GetService<IMigrationsAssembly>().ModelSnapshot!.GetType(),
            company.GetService<IMigrationsAssembly>().ModelSnapshot!.GetType());
        var systemSql = system.GetService<IMigrator>().GenerateScript();
        var companySql = company.GetService<IMigrator>().GenerateScript();
        Assert.Contains("[core].[Companies]", systemSql);
        Assert.Contains("[core].[CompanyDatabases]", systemSql);
        Assert.Contains("[security].[Users]", systemSql);
        Assert.Contains("[security].[CompanyAccess]", systemSql);
        Assert.Contains("[security].[UserSessions]", systemSql);
        Assert.DoesNotContain("[core].[Contractors]", systemSql);
        Assert.DoesNotContain("[core].[CompanyProfile]", systemSql);
        Assert.Contains("[core].[Contractors]", companySql);
        Assert.Contains("[core].[CompanyProfile]", companySql);
        Assert.Contains("[SingletonKey] = 1", companySql);
        Assert.DoesNotContain("[core].[Companies]", companySql);
        Assert.DoesNotContain("[core].[CompanyDatabases]", companySql);
        Assert.True(string.IsNullOrEmpty(system.Database.GetConnectionString()));
        Assert.True(string.IsNullOrEmpty(company.Database.GetConnectionString()));
    }
}
