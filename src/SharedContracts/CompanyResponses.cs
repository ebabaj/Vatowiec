using System.Text.Json.Serialization;

namespace Vatowiec.SharedContracts;

public sealed record CompanyResponse(Guid CompanyId, string Name, string[] Permissions)
{
    [JsonIgnore]
    public bool CanViewContractors => Permissions.Contains("ViewContractors", StringComparer.Ordinal);
    public override string ToString() => Name;
}

public sealed record CompanyProfileResponse(Guid CompanyId, string Name, string? Nip, string? Regon);
public sealed record ContractorResponse(Guid Id, Guid CompanyId, string Code, string Name, string CountryCode)
{
    public override string ToString() => $"{Code} — {Name} ({CountryCode})";
}
public sealed record SessionResponse(string UserName, DateTimeOffset ExpiresAtUtc);
public sealed record CsrfResponse(string Token, string HeaderName);
