using Vatowiec.Core.Infrastructure.Personal;
using System.Runtime.Versioning;

namespace Vatowiec.Tests;

[SupportedOSPlatform("windows")]
public sealed class PersonalDistributionTests
{
    [Theory]
    [InlineData("001,002", "001,002")]
    [InlineData("002,001", "001,002")]
    public void SameSchemaIsAcceptedWithoutMigration(string available, string applied) =>
        PersonalInstallation.EnsureCompatibleSchema(available.Split(','), applied.Split(','));

    [Theory]
    [InlineData("001,002", "001")]
    [InlineData("001", "001,002")]
    [InlineData("001,002", "001,003")]
    public void UpgradeDowngradeAndDivergentSchemaRequireSeparateBackupProcedure(string available, string applied)
    {
        var error = Assert.Throws<PersonalSetupException>(() => PersonalInstallation.EnsureCompatibleSchema(available.Split(','), applied.Split(',')));
        Assert.Contains("kopię", error.Message);
        Assert.Contains("Nie zmieniono schematu", error.Message);
    }
}
