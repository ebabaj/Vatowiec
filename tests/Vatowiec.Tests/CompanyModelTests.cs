using Microsoft.EntityFrameworkCore;
using Vatowiec.Core.Application;
using Vatowiec.Core.Domain;
using Vatowiec.Core.Infrastructure;
using Vatowiec.Core.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace Vatowiec.Tests;

public sealed class CompanyModelTests
{
    [Fact]
    public void Domain_RejectsEmptyIdentityNameAndInvalidCountry()
    {
        Assert.Throws<ArgumentException>(() => new Company(Guid.Empty, "Firma testowa"));
        Assert.Throws<ArgumentException>(() => new Company(Guid.NewGuid(), " "));
        Assert.Throws<ArgumentException>(() => new Company(Guid.NewGuid(), new string('X', 257)));
        Assert.Throws<ArgumentException>(() => new Contractor(Guid.Empty, Guid.NewGuid(), "A", "Test", "PL"));
        Assert.Throws<ArgumentException>(() => new Contractor(Guid.NewGuid(), Guid.NewGuid(), "A", "Test", "P1"));
        Assert.Throws<ArgumentException>(() => new Contractor(Guid.NewGuid(), Guid.NewGuid(), " ", "Test", "PL"));
        var contractor = new Contractor(Guid.NewGuid(), Guid.NewGuid(), " abc ", " Test ", "pl");
        Assert.Equal("ABC", contractor.Code);
        Assert.Equal("Test", contractor.Name);
        Assert.Equal("PL", contractor.CountryCode);
        Assert.Null(contractor.TaxIdentifier);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SaveChanges_RejectsMissingAndMismatchedCompanyBeforeConnecting(bool async)
    {
        var options = new DbContextOptionsBuilder<CoreDbContext>().UseSqlServer().Options;
        var companyId = Guid.NewGuid();
        foreach (var scope in new Guid?[] { null, Guid.Empty, Guid.NewGuid() })
        {
            await using var context = new CoreDbContext(options, new CompanyContext(scope));
            context.Companies.Add(new Company(companyId, "Firma testowa"));
            if (async)
            {
                await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());
            }
            else
            {
                Assert.Throws<InvalidOperationException>(() => context.SaveChanges());
            }
        }
    }

    [Theory]
    [InlineData("Server=localhost\\SQLEXPRESS;Database=Other;Integrated Security=True;Encrypt=True")]
    [InlineData("Server=remote;Database=Vatowiec_Dev;Integrated Security=True;Encrypt=True")]
    [InlineData("Server=localhost\\SQLEXPRESS;Database=Vatowiec_Dev;Integrated Security=False;Encrypt=True")]
    [InlineData("Server=localhost\\SQLEXPRESS;Database=Vatowiec_Dev;Integrated Security=True;Encrypt=False")]
    public void DevelopmentTools_RejectOtherDatabasesServersAndAuthentication(string connection) =>
        Assert.Throws<InvalidOperationException>(() => DevelopmentDatabase.Validate(connection));

    [Fact]
    public void CertificateTrust_IsLimitedToExactDevelopmentDatabase()
    {
        const string local = "Server=localhost\\SQLEXPRESS;Database=Vatowiec_Dev;Integrated Security=True;Encrypt=True;TrustServerCertificate=True";
        Assert.Throws<InvalidOperationException>(() => new ServiceCollection().AddInfrastructure(local));
        new ServiceCollection().AddInfrastructure(local, isDevelopment: true);
        Assert.Throws<InvalidOperationException>(() => new ServiceCollection().AddInfrastructure(
            local.Replace("Vatowiec_Dev", "Other", StringComparison.Ordinal), isDevelopment: true));
    }
}
