namespace Vatowiec.SharedContracts;

public sealed record ContractorWriteRequest(int Id, string Code, string Name,
    string CountryCode, bool IsActive, string? RowVersion = null, Guid? OperationId = null, int? ParentId = null);

public static class ContractorValidation
{
    public static Dictionary<string, string[]> Validate(ContractorWriteRequest value)
    {
        var errors = new Dictionary<string, string[]>();
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
