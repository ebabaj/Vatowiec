namespace Vatowiec.SharedContracts;

public sealed record ContractorWriteRequest(Guid Id, string Code, string Name, string? TaxIdentifier,
    string CountryCode, bool IsActive, string? RowVersion = null);

public static class ContractorValidation
{
    public static Dictionary<string, string[]> Validate(ContractorWriteRequest value)
    {
        var errors = new Dictionary<string, string[]>();
        if (value.Id == Guid.Empty) errors["Id"] = ["Brak identyfikatora pozycji. Otwórz formularz ponownie."];
        if (string.IsNullOrWhiteSpace(value.Code) || value.Code.Trim().Length > 64)
            errors["Code"] = ["Kod jest wymagany i może mieć najwyżej 64 znaki."];
        if (string.IsNullOrWhiteSpace(value.Name) || value.Name.Trim().Length > 256)
            errors["Name"] = ["Nazwa jest wymagana i może mieć najwyżej 256 znaków."];
        if (value.TaxIdentifier?.Trim().Length > 32)
            errors["TaxIdentifier"] = ["Identyfikator podatkowy może mieć najwyżej 32 znaki."];
        var country = value.CountryCode?.Trim().ToUpperInvariant();
        if (country is null || country.Length != 2 || country.Any(c => c is < 'A' or > 'Z'))
            errors["CountryCode"] = ["Kraj podaj jako dwuliterowy kod, np. PL."];
        return errors;
    }
}
