namespace Vatowiec.SharedContracts;

public sealed record ContractorWriteRequest(int Id, string Code, string Name,
    string CountryCode, bool IsActive, string? RowVersion = null, Guid? OperationId = null, int? ParentId = null,
    string? TaxId = null, string? VatEuId = null, string? KsefIdWew = null, bool AcceptWarning = false, string? WarningHash = null,
    string? Regon = null, string? Krs = null, string? Bdo = null, string? Pesel = null)
{
    public override string ToString() => $"ContractorWriteRequest Id={Id} (dane formularza chronione)";
}

public sealed record ContractorSearchRequest(string? Query = null, string? Country = null, bool? Active = null,
    int? ParentId = null, int? ExcludeId = null, int Offset = 0, int Limit = 50, string[]? VisibleColumns = null)
{
    public override string ToString() => "ContractorSearchRequest (filtry chronione)";
}

public static class ContractorValidation
{
    public static Dictionary<string, string[]> Validate(ContractorWriteRequest value)
    {
        var errors = new Dictionary<string, string[]>();
        if (value.TaxId?.Length > 100) errors["TaxId"] = ["Najwyżej 100 znaków."];
        if (value.VatEuId?.Length > 100) errors["VatEuId"] = ["Najwyżej 100 znaków."];
        if (value.KsefIdWew?.Length > 100) errors["KsefIdWew"] = ["Najwyżej 100 znaków."];
        foreach (var (field, text) in new[] { ("Regon", value.Regon), ("Krs", value.Krs), ("Bdo", value.Bdo), ("Pesel", value.Pesel) })
            if (text?.Length > 100) errors[field] = ["Najwyżej 100 znaków."];
        if (value.Id < 0) errors["Id"] = ["Nieprawidłowy identyfikator pozycji."];
        if (value.ParentId is <= 0 || (value.Id > 0 && value.ParentId == value.Id))
            errors["ParentId"] = ["Nieprawidłowy rekord nadrzędny."];
        if (string.IsNullOrWhiteSpace(value.Code) || value.Code.Trim().Length > 64)
            errors["Code"] = ["Kod jest wymagany i może mieć najwyżej 64 znaki."];
        if (string.IsNullOrWhiteSpace(value.Name) || value.Name.Trim().Length > 256)
            errors["Name"] = ["Nazwa jest wymagana i może mieć najwyżej 256 znaków."];
        var country = value.CountryCode?.Trim().ToUpperInvariant();
        if (country is null || country.Length != 2 || country.Any(c => c is < 'A' or > 'Z'))
            errors["CountryCode"] = ["Kraj podaj jako dwuliterowy kod, np. PL."];
        return errors;
    }
}
