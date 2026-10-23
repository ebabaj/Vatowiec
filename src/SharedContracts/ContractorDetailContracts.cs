namespace Vatowiec.SharedContracts;

public sealed record DetailValues
{
    public int AddressTypeId { get; init; } = 1;
    public string? Name { get; init; }
    public string? Street { get; init; }
    public string? BuildingNo { get; init; }
    public string? ApartmentNo { get; init; }
    public string? PostalCode { get; init; }
    public string? City { get; init; }
    public string? Region { get; init; }
    public string? CountryCode { get; init; }
    public bool IsDefault { get; init; }
    public bool IsActive { get; init; } = true;
    public string? Position { get; init; }
    public string? Email { get; init; }
    public string? Phone { get; init; }
    public string? Notes { get; init; }
    public string? AccountNumber { get; init; }
    public string? BankName { get; init; }
    public string? Bic { get; init; }
    public string? CurrencyCode { get; init; }
    public string? AccountHolderName { get; init; }
}

public sealed record DetailWriteRequest(int Id, DetailValues Data, string? RowVersion = null,
    Guid? OperationId = null, bool AcceptWarning = false, string? WarningHash = null);
public sealed record DetailResponse(int Id, Guid CompanyId, int ContractorId, string Kind, DetailValues Data, string RowVersion)
{
    public BankAccountValidationResponse? BankValidation { get; init; }
    [System.Text.Json.Serialization.JsonIgnore]
    public string? AddressTypeName { get; init; }
    public override string ToString() => Kind switch
    {
        "addresses" => $"{AddressTypeName ?? "Adres"} · {Data.Name ?? Data.Street} · {Data.City} · {(Data.IsDefault ? "preferowany" : "")} · {(Data.IsActive ? "aktywny" : "nieaktywny")}",
        "contacts" => $"{Data.Name} · {Data.Email} · {Data.Phone} · {Data.City}",
        _ => $"{Data.Name ?? Data.BankName ?? "Rachunek"} · {Data.AccountNumber} · {Data.CurrencyCode ?? "bez waluty"} · {(Data.IsDefault ? "domyślny" : "")} · {(Data.IsActive ? "aktywny" : "nieaktywny")} · {BankValidation?.Message}",
    };
}
public sealed record AddressTypeResponse(int Id, string Name)
{
    public override string ToString() => Name;
}
public sealed record AddressTypeWriteRequest(Guid OperationId, string Name);

public sealed record BankAccountValidationRequest(string AccountNumber);
public sealed record BankAccountValidationResponse(string Status, string Code, string Message,
    string NormalizedNumber, bool IsPolishNrb, string RuleVersion)
{
    public BankLookupResponse? BankLookup { get; init; }
}
public sealed record BankLookupResponse(string Status, string Message, string ClearingNumber,
    string? BankName, string[] Bics);

public static class DetailValidation
{
    public static Dictionary<string, string[]> Validate(string kind, DetailValues value)
    {
        var errors = new Dictionary<string, string[]>();
        void Length(string key, string? text, int max, string label, bool required = false)
        {
            if ((required && string.IsNullOrWhiteSpace(text)) || text?.Trim().Length > max)
                errors[key] = [$"{label}: {(required ? "pole wymagane; " : "")}najwyżej {max} znaków."];
        }
        Length("Name", value.Name, kind == "contacts" ? 256 : 100, "Nazwa", kind == "contacts");
        Length("Street", value.Street, 200, "Ulica");
        Length("BuildingNo", value.BuildingNo, 20, "Numer budynku");
        Length("ApartmentNo", value.ApartmentNo, 20, "Numer lokalu");
        Length("PostalCode", value.PostalCode, 20, "Kod pocztowy");
        Length("City", value.City, 150, "Miejscowość");
        Length("Region", value.Region, 100, "Region");
        var country = value.CountryCode?.Trim();
        if (!string.IsNullOrEmpty(country) && (country.Length != 2 || country.Any(c => !char.IsAsciiLetter(c))))
            errors["CountryCode"] = ["Kraj: dwie litery albo puste pole."];
        if (kind == "addresses" && value.AddressTypeId <= 0) errors["AddressTypeId"] = ["Wybierz rodzaj adresu."];
        if (kind == "contacts")
        {
            Length("Position", value.Position, 100, "Stanowisko/funkcja");
            Length("Email", value.Email, 256, "E-mail");
            Length("Phone", value.Phone, 100, "Telefon");
        }
        if (kind == "bank-accounts")
        {
            Length("AccountNumber", value.AccountNumber, 100, "Numer rachunku", true);
            Length("BankName", value.BankName, 200, "Nazwa banku");
            Length("Bic", value.Bic, 11, "BIC/SWIFT");
            if (value.Bic?.Any(c => c > 127) == true) errors["Bic"] = ["BIC/SWIFT: użyj znaków ASCII."];
            Length("AccountHolderName", value.AccountHolderName, 256, "Właściciel rachunku");
            var currency = value.CurrencyCode?.Trim();
            if (!string.IsNullOrEmpty(currency) && (currency.Length != 3 || currency.Any(c => !char.IsAsciiLetter(c))))
                errors["CurrencyCode"] = ["Waluta: trzy litery albo puste pole (osobna grupa bez waluty)."];
        }
        return errors;
    }
}
