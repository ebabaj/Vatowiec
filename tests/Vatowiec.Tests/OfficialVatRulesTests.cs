using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Vatowiec.Core.Application;
using Vatowiec.Core.Domain;
using Vatowiec.Core.Infrastructure.Persistence;

namespace Vatowiec.Tests;

public sealed class OfficialVatRulesTests
{
    internal static TaxIdentifierValidator Validator() => new([new PolishNipChecksum(), new FinnishVatChecksum(), new CroatianVatChecksum()]);
    private static readonly DateOnly Date = new(2026, 10, 23);

    // Fikcyjne numery z przykładu CSV Vero; nie dane podatników z rejestru.
    [Theory]
    [InlineData("12345671", true)]
    [InlineData("23456786", false)] // CSV ilustruje zapis; suma wymaga cyfry 0.
    [InlineData("34567892", false)] // CSV: reszta 1 oznacza numer niedopuszczalny.
    [InlineData("04567899", true)]
    [InlineData("12345672", false)]
    [InlineData("07654321", false)] // Ilustracja formatu Vero, nie poprawnej sumy.
    [InlineData("11111111", false)] // Reszta 1: numer niedopuszczalny.
    [InlineData("10000020", true)] // Syntetyczny przypadek reszty 0: 7+2*2=11.
    [InlineData("1234567", false)]
    [InlineData("1234567A", false)]
    [InlineData("１２３４５６７１", false)]
    public void FinnishChecksum_OfficialExamplesAndErrors(string value, bool valid) =>
        Assert.Equal(valid, new FinnishVatChecksum().IsValid(value));

    [Theory]
    [InlineData("69435151530", true)] // REGOS: obliczony przykład, końcowy remainder 1.
    [InlineData("11111111119", true)] // Wzorzec Porezna HR-BR-53.
    [InlineData("69435151531", false)]
    [InlineData("11111111118", false)]
    [InlineData("6943515153", false)]
    [InlineData("6943515153A", false)]
    [InlineData("６９４３５１５１５３０", false)]
    public void CroatianChecksum_OfficialExamplesAndErrors(string value, bool valid) =>
        Assert.Equal(valid, new CroatianVatChecksum().IsValid(value));

    [Theory]
    [InlineData("ATU12345678")]
    [InlineData("BE0123456789")]
    [InlineData("BE1123456789")]
    [InlineData("BG123456789")]
    [InlineData("CY12345678A")]
    [InlineData("CZ12345678")]
    [InlineData("DK12345678")]
    [InlineData("EE123456789")]
    [InlineData("FRAB123456789")]
    [InlineData("DE123456789")]
    [InlineData("EL123456789")]
    [InlineData("HU12345678")]
    [InlineData("IE1*23456A")]
    [InlineData("IT12345678901")]
    [InlineData("LV12345678901")]
    [InlineData("LT123456789")]
    [InlineData("LU12345678")]
    [InlineData("MT12345678")]
    [InlineData("NL123456789B01")]
    [InlineData("PT123456789")]
    [InlineData("RO12")]
    [InlineData("SE123456789001")]
    [InlineData("SK1234567890")]
    [InlineData("SI12345678")]
    [InlineData("ESA1234567Z")]
    [InlineData("XI123456789")]
    public void FormatOnly_IsAlwaysUnknownAndNotRegistrationConfirmation(string vat)
    {
        var check = Validator().Assess("VatEuId", vat, "PL", TaxSchemeSeed.Rules(), Date);
        Assert.Equal("Unknown", check.Status); Assert.True(check.FormatChecked); Assert.False(check.ChecksumChecked);
        Assert.Contains("Nie sprawdzono rejestracji", check.Message);
    }
    [Theory]
    [InlineData("FI12345671", "Valid")]
    [InlineData("FI12345672", "Invalid")]
    [InlineData("HR69435151530", "Valid")]
    [InlineData("HR69435151531", "Invalid")]
    [InlineData("PL3245174504", "Valid")]
    public void VatDispatch_UsesPrefixRegardlessOfContractorCountry(string number, string expected)
    {
        var check = Validator().Assess("VatEuId", number, "DE", TaxSchemeSeed.Rules(), Date);
        Assert.Equal(expected, check.Status); Assert.True(check.ChecksumChecked);
        if (expected == "Valid") Assert.Contains("Nie potwierdza rejestracji", check.Message);
    }
    [Theory]
    [InlineData("BE2123456789")]
    [InlineData("GR123456789")]
    [InlineData("GB123456789")]
    [InlineData("SE123456789002")]
    [InlineData("ES123456789")]
    [InlineData("FI1234567A")]
    public void FormatErrorsOrUnsupportedPrefixesAreNeverValid(string number)
    {
        var result = Validator().Assess("VatEuId", number, "PL", TaxSchemeSeed.Rules(), Date);
        Assert.NotEqual("Valid", result.Status); Assert.False(result.ChecksumChecked);
    }
    [Theory]
    [InlineData("BE2123456789", "Invalid")]
    [InlineData("FI12345671", "Valid")]
    [InlineData("HR69435151530", "Valid")]
    public void VersionBoundary_SelectsHistoryAndChangesFingerprint(string number, string current)
    {
        var before = Validator().Assess("VatEuId", number, "PL", TaxSchemeSeed.Rules(), Date.AddDays(-1));
        var after = Validator().Assess("VatEuId", number, "PL", TaxSchemeSeed.Rules(), Date);
        Assert.Equal("Unknown", before.Status); Assert.Equal(current, after.Status);
        Assert.Contains(TaxSchemeSeed.LegacyVersion, before.RuleVersion); Assert.Contains(TaxSchemeSeed.Version, after.RuleVersion);
        Assert.NotEqual(before.RuleVersion, after.RuleVersion);
    }
    [Fact]
    public void HistoricalRowsAndAll28PrefixesRemainAvailableWithoutOverlappingPeriods()
    {
        var rules = TaxSchemeSeed.Rules();
        Assert.Equal(32, rules.Length); Assert.Equal(29, rules.Count(x => x.Version == TaxSchemeSeed.LegacyVersion));
        Assert.Equal(3, rules.Count(x => x.Version == TaxSchemeSeed.Version));
        foreach (var day in new[] { Date.AddDays(-1), Date, Date.AddYears(1) })
        {
            var current = rules.Where(x => x.IdentifierType == "VatEuId" && x.IsActive && x.ValidFrom <= day && (x.ValidTo == null || x.ValidTo >= day)).ToArray();
            Assert.Equal(28, current.Length); Assert.Equal(28, current.Select(x => x.VatPrefix).Distinct().Count());
        }
        Assert.All(rules.Where(x => x.Version == TaxSchemeSeed.Version), row => Assert.StartsWith("https://", row.SourceUrl));
        Assert.Single(rules, x => x.IdentifierType == "TaxId" && x.CountryCode == "PL");
        Assert.DoesNotContain(rules, x => x.IdentifierType == "TaxId" && x.CountryCode != "PL");
    }
    [Fact]
    public void ForeignTaxIdNeverUsesNewVatAlgorithmsEvenWhenCustomRuleExists()
    {
        TaxIdentifierScheme[] custom = [new() { CountryCode = "FI", IdentifierType = "TaxId", FormatPattern = "^[0-9]{8}$",
            AlgorithmKey = "fi-vat-mod11-v1", Version = "test", ValidFrom = Date }];
        var result = Validator().Assess("TaxId", "12345671", "FI", custom, Date);
        Assert.Equal("Unknown", result.Status); Assert.False(result.FormatChecked); Assert.False(result.ChecksumChecked);
    }
    [Fact]
    public void MissingOrOverlappingRuleAndUnavailableAlgorithmFailAsUnknown()
    {
        var rules = TaxSchemeSeed.Rules();
        Assert.Equal("Unknown", Validator().Assess("VatEuId", "FI12345671", "FI", rules, new(2026, 10, 21)).Status);
        var current = rules.Single(x => x.VatPrefix == "FI" && x.Version == TaxSchemeSeed.Version);
        Assert.Equal("Unknown", Validator().Assess("VatEuId", "FI12345671", "FI", [.. rules, current], Date).Status);
        Assert.Equal("Unknown", new TaxIdentifierValidator([new PolishNipChecksum()]).Assess("VatEuId", "FI12345671", "FI", rules, Date).Status);
    }
    [Fact]
    public void MigrationOnlyVersionsRulesAndCannotDeleteHistoryOnDowngrade()
    {
        using var system = new SystemDbContextFactory().CreateDbContext([]);
        var migrations = system.Database.GetMigrations().ToArray();
        var index = Array.FindIndex(migrations, x => x.EndsWith("_OfficialVatEuRules", StringComparison.Ordinal));
        var migrator = system.GetService<IMigrator>();
        var script = migrator.GenerateScript(migrations[index - 1], migrations[index]);
        Assert.DoesNotContain("DELETE", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DROP", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("[security]", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("[ref].[TaxIdentifierSchemes]", script);
        Assert.Throws<NotSupportedException>(() => migrator.GenerateScript(migrations[index], migrations[index - 1]));
    }
}
