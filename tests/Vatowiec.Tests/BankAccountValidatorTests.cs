using Vatowiec.Core.Application;
using Vatowiec.Core.Infrastructure;

namespace Vatowiec.Tests;

public sealed class BankAccountValidatorTests
{
    private readonly IBankAccountValidator validator = new IbanNetBankAccountValidator();

    [Theory]
    [InlineData("70506405335016096312945164", true)]
    [InlineData("PL70506405335016096312945164", false)]
    [InlineData("DE89370400440532013000", false)]
    [InlineData("GB82WEST12345698765432", false)]
    [InlineData("FR1420041010050500013M02606", false)]
    public void ValidatesCountryStructureAndChecksum(string number, bool nrb)
    {
        var result = validator.Validate(number);
        Assert.Equal(BankAccountStatus.ValidIban, result.Status);
        Assert.False(result.RequiresWarning);
        Assert.Equal(nrb, result.IsPolishNrb);
        Assert.Equal(number, result.NormalizedNumber);
        Assert.Contains("IbanNet-6.2.0", result.RuleVersion);
        Assert.Contains("bank-iban-v2", result.RuleVersion);
    }

    [Theory]
    [InlineData("71506405335016096312945164", "invalid_checksum")]
    [InlineData("PL71506405335016096312945164", "invalid_checksum")]
    [InlineData("DE88370400440532013000", "invalid_checksum")]
    [InlineData("00000000000000000000000001", "invalid_checksum")]
    [InlineData("PL01506405335016096312945164", "invalid_checksum")]
    [InlineData("PL99506405335016096312945164", "invalid_checksum")]
    [InlineData("PL7050640533501609631294516", "invalid_length")]
    [InlineData("PL", "invalid_length")]
    [InlineData("PL7", "invalid_length")]
    [InlineData("ZZ89370400440532013000", "unknown_country")]
    [InlineData("DE8937040044053201300?", "illegal_characters")]
    [InlineData("GB82WEST1234569876543?", "illegal_characters")]
    public void ExplainsInvalidIbanInPolish(string number, string code)
    {
        var result = validator.Validate(number);
        Assert.Equal(BankAccountStatus.InvalidIban, result.Status);
        Assert.True(result.RequiresWarning);
        Assert.Equal(code, result.Code);
        Assert.Contains("IBAN", result.Message);
    }

    [Theory]
    [InlineData("123456789")]
    [InlineData("LOCAL-ACCOUNT/123")]
    [InlineData("DEMO-RACHUNEK-NIE-DO-PLATNOSCI")]
    [InlineData("7150640533501609631294516")]
    public void NonIbanIsUnverifiedAndMayBeAccepted(string number)
    {
        var result = validator.Validate(number);
        Assert.Equal(BankAccountStatus.NonIban, result.Status);
        Assert.True(result.RequiresWarning);
        Assert.Contains("niezweryfikowany", result.Message);
    }

    [Theory]
    [InlineData("  pl70 5064\t0533 5016\n0963 1294\u00a05164 ", "PL70506405335016096312945164")]
    [InlineData("70 5064 0533 5016 0963 1294 5164", "70506405335016096312945164")]
    [InlineData("gb82 west 1234 5698 7654 32", "GB82WEST12345698765432")]
    public void NormalizesWhitespaceAndCaseWithoutConvertingStoredNrb(string input, string expected)
    {
        var result = validator.Validate(input);
        Assert.Equal(expected, result.NormalizedNumber);
        Assert.Equal(BankAccountStatus.ValidIban, result.Status);
    }
}
