using Vatowiec.Core.Infrastructure.Persistence;

namespace Vatowiec.Tests;

public sealed class DatabaseConnectionPolicyTests
{
    [Theory]
    [InlineData("Vatowiec_Dev")]
    [InlineData("Vatowiec_System")]
    [InlineData("vatowiec_system_test")]
    [InlineData("master")]
    [InlineData("model")]
    [InlineData("msdb")]
    [InlineData("tempdb")]
    public void CompanyConnection_RejectsLegacyAndSystemDatabases(string database) =>
        Assert.Throws<InvalidOperationException>(() => DatabaseConnectionPolicy.ValidateCompany(
            $"Server=unused.invalid;Database={database};Integrated Security=True;Encrypt=True", false));

    [Theory]
    [InlineData("Vatowiec_Dev")]
    [InlineData("CompanyA")]
    [InlineData("master")]
    public void SystemConnection_RejectsOtherDatabases(string database) =>
        Assert.Throws<InvalidOperationException>(() => DatabaseConnectionPolicy.ValidateSystem(
            $"Server=unused.invalid;Database={database};Integrated Security=True;Encrypt=True", false));

    [Theory]
    [InlineData("Vatowiec_System", true)]
    [InlineData("Vatowiec_Company_A_Dev", false)]
    public void CertificateTrust_IsLimitedToExplicitLocalDevelopmentDatabases(string database, bool system)
    {
        var connection = $"Server=localhost\\SQLEXPRESS;Database={database};Integrated Security=True;Encrypt=True;TrustServerCertificate=True";
        void Validate(string value, bool development)
        {
            if (system) DatabaseConnectionPolicy.ValidateSystem(value, development);
            else DatabaseConnectionPolicy.ValidateCompany(value, development);
        }
        Validate(connection, true);
        Assert.Throws<InvalidOperationException>(() => Validate(connection, false));
        Assert.Throws<InvalidOperationException>(() => Validate(connection.Replace("localhost\\SQLEXPRESS", "remote.invalid"), true));
    }

    [Theory]
    [InlineData("Server=sql.invalid;Database=CompanyA;Encrypt=False")]
    [InlineData("Server=sql.invalid;Encrypt=True")]
    [InlineData("Database=CompanyA;Encrypt=True")]
    [InlineData("Server=sql.invalid;Database=CompanyA;Encrypt=True;AttachDBFilename=data.mdf")]
    [InlineData("Server=sql.invalid;Database=\"Vatowiec_Dev \";Encrypt=True")]
    public void UnsafeOrImplicitConnection_IsRejected(string connection) =>
        Assert.Throws<InvalidOperationException>(() => DatabaseConnectionPolicy.ValidateCompany(connection, false));

    [Fact]
    public void CompanyDatabase_CanResideOnAnotherInstance() =>
        DatabaseConnectionPolicy.ValidateCompany(
            "Server=another-instance.invalid;Database=CompanyB;Integrated Security=True;Encrypt=True;TrustServerCertificate=False", false);
}
