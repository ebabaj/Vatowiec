namespace Vatowiec.SharedContracts;

// ADR-044: operacje techniczne firm (krok 1: tworzenie firmy). Bez ścieżek plików i connection stringów.
public sealed record CompanyOperationResponse(Guid Id, string Kind, Guid CompanyId, string CompanyName, string Stage,
    string Status, int Attempt, string? LastErrorCode, string RequestedByName, DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc, DateTimeOffset? CompletedAtUtc, string RowVersion);

public sealed record ProvisionCompanyRequest(Guid OperationKey, string CompanyName, string ConnectionProfileKey);

public sealed record CompanyOperationRetryRequest(string RowVersion);

public sealed record ServerProfileResponse(string Key, string ServerName);
