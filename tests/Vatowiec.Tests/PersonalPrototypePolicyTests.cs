using Microsoft.Data.SqlClient;
using Vatowiec.Core.Infrastructure.Persistence;
using P = Vatowiec.Core.Infrastructure.Persistence.PersonalPrototypeDatabase;

namespace Vatowiec.Tests;

public sealed class PersonalPrototypePolicyTests
{
    private static string Connection(string database) => new SqlConnectionStringBuilder
    { DataSource=P.Server, InitialCatalog=database, IntegratedSecurity=true, Encrypt=SqlConnectionEncryptOption.Mandatory, TrustServerCertificate=true }.ConnectionString;

    [Fact]
    public void LocalDbExceptionRequiresExplicitPrototypeModeAndCorrectDatabaseRole()
    {
        Assert.Throws<InvalidOperationException>(()=>DatabaseConnectionPolicy.ValidateSystem(Connection(P.System),true));
        Assert.Throws<InvalidOperationException>(()=>DatabaseConnectionPolicy.ValidateCompany(Connection(P.CompanyA),true));
        Assert.Throws<InvalidOperationException>(()=>DatabaseConnectionPolicy.ValidateCompany(Connection(P.System),false,true));
        Assert.Throws<InvalidOperationException>(()=>DatabaseConnectionPolicy.ValidateSystem(Connection(P.CompanyA),false,true));
        if(OperatingSystem.IsWindows())
        {
            DatabaseConnectionPolicy.ValidateSystem(Connection(P.System),false,true);
            DatabaseConnectionPolicy.ValidateCompany(Connection(P.CompanyA),false,true);
            DatabaseConnectionPolicy.ValidateCompany(Connection(P.CompanyB),false,true);
        }
    }

    [Theory]
    [InlineData("DataSource",@"localhost\SQLEXPRESS")]
    [InlineData("DataSource",@"(localdb)\MSSQLLocalDB")]
    [InlineData("DataSource","remote.invalid")]
    [InlineData("InitialCatalog","Vatowiec_System")]
    [InlineData("InitialCatalog","Vatowiec_Company_A_Dev")]
    [InlineData("InitialCatalog","master")]
    [InlineData("InitialCatalog","unrelated")]
    [InlineData("UserID","unwanted")]
    [InlineData("Password","fictional")]
    [InlineData("AttachDBFilename","unwanted.mdf")]
    [InlineData("Encrypt","False")]
    [InlineData("IntegratedSecurity","False")]
    public void PrototypeCannotTargetNetworkExistingLocalDbOtherCatalogsOrUnsafeAuthentication(string property,string value)
    {
        var builder=new SqlConnectionStringBuilder(Connection(P.System));
        // SqlClient accepts aliases through its indexer, not CLR property names.
        var key=property switch {"DataSource"=>"Data Source","InitialCatalog"=>"Initial Catalog","UserID"=>"User ID","IntegratedSecurity"=>"Integrated Security",_=>property};
        builder[key]=value;
        Assert.Throws<InvalidOperationException>(()=>DatabaseConnectionPolicy.ValidateSystem(builder.ConnectionString,false,true));
    }

    [Fact]
    public void PrototypeNamesNeverOverlapNetworkOrDefaultLocalDb()
    {
        Assert.DoesNotContain("MSSQLLocalDB",P.Server,StringComparison.OrdinalIgnoreCase);
        Assert.Equal(3,P.Databases.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(P.Databases,name=>Assert.StartsWith("Vatowiec_Personal_Prototype_",name));
    }
}
