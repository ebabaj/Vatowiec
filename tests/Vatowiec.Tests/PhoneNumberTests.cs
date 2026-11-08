using Vatowiec.Desktop.ViewModels;
using Vatowiec.SharedContracts;

namespace Vatowiec.Tests;

public sealed class PhoneNumberTests
{
    [Theory]
    [InlineData("501234567", "+48501234567", "501234567")]
    [InlineData("+48501234567", "+48501234567", "501234567")]
    [InlineData("+48 501 234 567", "+48501234567", "501234567")]
    [InlineData("0048501234567", "+48501234567", "501234567")]
    [InlineData("+420601123456", "+420601123456", "+420601123456")]
    [InlineData("22 123 45 67", "+48221234567", "221234567")]
    [InlineData("+48 (22) 123-45-67", "+48221234567", "221234567")]
    [InlineData("+49 30 123456", "+4930123456", "+4930123456")]
    [InlineData("+49 1512 3456789", "+4915123456789", "+4915123456789")]
    [InlineData("+1 484 555 0123", "+14845550123", "+14845550123")]
    [InlineData("+39 02 36618 300", "+390236618300", "+390236618300")]
    [InlineData("0039 02 36618 300", "+390236618300", "+390236618300")]
    public void MetadataValidationStorageDisplayAndRepeatedNormalization(string input, string sql, string display)
    {
        var result = PhoneNumberService.Validate(input);
        Assert.Equal(PhoneNumberStatus.Valid, result.Status);
        Assert.Null(result.Message);
        Assert.Equal(sql, PhoneNumberService.NormalizeForStorage(input));
        Assert.Equal(sql, PhoneNumberService.NormalizeForStorage(sql));
        Assert.Equal(display, PhoneNumberService.FormatForDisplay(sql));
        Assert.Equal(sql, PhoneNumberService.NormalizeForStorage(display));
        Assert.Equal(sql, PhoneNumberService.NormalizeForUpdate(display, sql));
        Assert.False(DetailValidation.Validate("contacts", new() { Name = "Kontakt testowy", Phone = input }).ContainsKey("Phone"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void OptionalEmptyPhoneIsAccepted(string? input)
    {
        Assert.Equal(PhoneNumberStatus.Empty, PhoneNumberService.Validate(input).Status);
        Assert.Null(PhoneNumberService.NormalizeForStorage(input));
        Assert.Empty(DetailValidation.Validate("contacts", new() { Name = "Kontakt", Phone = input }));
    }

    [Theory]
    [InlineData("50123456", PhoneNumberStatus.Invalid)]
    [InlineData("5012345678", PhoneNumberStatus.Invalid)]
    [InlineData("+49501234567", PhoneNumberStatus.Invalid)]
    [InlineData("+48 000 000 000", PhoneNumberStatus.Invalid)]
    [InlineData("501abc567", PhoneNumberStatus.Invalid)]
    [InlineData("501@234567", PhoneNumberStatus.Invalid)]
    [InlineData("++48501234567", PhoneNumberStatus.Invalid)]
    [InlineData("501+234567", PhoneNumberStatus.Invalid)]
    [InlineData("+999501234567", PhoneNumberStatus.Ambiguous)]
    [InlineData("501234567 / 601234567", PhoneNumberStatus.Ambiguous)]
    [InlineData("501234567;601234567", PhoneNumberStatus.Ambiguous)]
    [InlineData("501234567\n601234567", PhoneNumberStatus.Ambiguous)]
    [InlineData("501234567#12", PhoneNumberStatus.Ambiguous)]
    public void InvalidAndAmbiguousInputIsNeverSilentlyChanged(string input, PhoneNumberStatus status)
    {
        var check = PhoneNumberService.Validate(input);
        Assert.Equal(status, check.Status);
        Assert.False(check.CanSave);
        Assert.Null(check.E164);
        Assert.StartsWith("Telefon:", check.Message);
        Assert.Throws<FormatException>(() => PhoneNumberService.NormalizeForStorage(input));
        Assert.Equal(input, PhoneNumberService.FormatForDisplay(input));
        Assert.Contains("Phone", DetailValidation.Validate("contacts", new() { Name = "Kontakt", Phone = input }).Keys);
        var editor = new DetailEditor("contacts") { Name = "Kontakt", Phone = input };
        Assert.Contains("Phone", editor.Validate().Keys);
        Assert.Equal(input, editor.Phone);
    }

    [Theory]
    [InlineData("+48501234567", "501234567")]
    [InlineData("+420601123456", "+420601123456")]
    [InlineData("+390236618300", "+390236618300")]
    [InlineData("501 234 567", "501234567")]
    [InlineData("TEST", "TEST")]
    [InlineData("+49501234567", "+49501234567")]
    [InlineData(null, null)]
    [InlineData("", "")]
    public void SqlValueToEditorToRequest_PreservesUnchangedPhoneIncludingLegacy(string? sql, string? display)
    {
        var row = new DetailResponse(1, Guid.NewGuid(), 1, "contacts", new() { Name = "Kontakt", Phone = sql }, "AAAAAAAAAAE=");
        var editor = new DetailEditor(row);
        Assert.Equal(display, editor.Phone);
        Assert.False(editor.IsDirty);
        Assert.Empty(editor.Validate());
        Assert.Equal(sql, editor.Request().Data.Phone);
        editor.Notes = "Inna zmiana formularza";
        Assert.Equal(sql, editor.Request().Data.Phone);
        Assert.Contains(display ?? "", row.ToString());
        editor.Phone = "601 234 567";
        Assert.Equal("+48601234567", editor.Request().Data.Phone);
        editor.Phone = "NOWY BŁĘDNY";
        Assert.Contains("Phone", editor.Validate().Keys);
    }

    [Fact]
    public void ForeignPhoneNeverUsesAddressCountryOrPolishLengthRule()
    {
        var editor = new DetailEditor("contacts") { Name = "Kontakt", CountryCode = "PL", Phone = "+390236618300" };
        Assert.Empty(editor.Validate());
        Assert.Equal("+390236618300", editor.Request().Data.Phone);
        editor.CountryCode = "DE"; editor.Phone = "501234567";
        Assert.Equal("+48501234567", editor.Request().Data.Phone);
    }
}
