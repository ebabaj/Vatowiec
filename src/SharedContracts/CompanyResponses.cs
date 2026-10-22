using System.Text.Json.Serialization;

namespace Vatowiec.SharedContracts;

public sealed record CompanyResponse(Guid CompanyId, string Name, string[] Permissions)
{
    [JsonIgnore]
    public bool CanViewContractors => Permissions.Contains("ViewContractors", StringComparer.Ordinal);
    [JsonIgnore]
    public bool CanEditContractors => CanViewContractors && Permissions.Contains("EditContractors", StringComparer.Ordinal);
    public override string ToString() => Name;
}

public sealed record CompanyProfileResponse(Guid CompanyId, string Name, string? Nip, string? Regon);
public sealed record ContractorResponse(int Id, Guid CompanyId, string Code, string Name, string CountryCode,
    string? TaxId = null, bool IsActive = true, string RowVersion = "", int? ParentId = null,
    string? VatEuId = null, string? KsefIdWew = null)
{
    public override string ToString() => $"{Code} — {Name} ({CountryCode})";
    [JsonIgnore]
    public string ActivityLabel => IsActive ? "Aktywny" : "Nieaktywny";
}
public sealed record SessionResponse(string UserName, DateTimeOffset ExpiresAtUtc);
public sealed record CsrfResponse(string Token, string HeaderName);
