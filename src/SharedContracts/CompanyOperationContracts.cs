namespace Vatowiec.SharedContracts;

// ADR-044: operacje techniczne firm (krok 1: tworzenie firmy). Bez ścieżek plików i connection stringów.
public sealed record CompanyOperationResponse(Guid Id, string Kind, Guid CompanyId, string CompanyName, string Stage,
    string Status, int Attempt, string? LastErrorCode, string RequestedByName, DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc, DateTimeOffset? CompletedAtUtc, string RowVersion, bool? CreateArchive = null);

// ADR-045: usuwanie firmy.
public sealed record DeleteCompanyRequest(Guid OperationKey, Guid CompanyId, bool CreateArchive, bool ConfirmWithoutArchive);

public sealed record InstallationCompanyResponse(Guid CompanyId, string Name, string DatabaseName, bool IsActive);

// ADR-044 krok 3: źródła odtworzenia (Source: Backup, Deleted, Import) i zlecenie.
public sealed record RestoreSourceResponse(string Source, long? BackupId, Guid CompanyId, string CompanyName, string? FileName,
    DateTimeOffset CreatedAtUtc, long SizeBytes, string Kind, bool CompanyExists);

public sealed record RestoreCompanyRequest(Guid OperationKey, string Source, long? BackupId, Guid? CompanyId, string? ImportFileName,
    bool ConfirmReplace, string? ConnectionProfileKey = null);

public sealed record DeletedCompanyResponse(Guid CompanyId, string CompanyName, string? Nip, string? Regon,
    DateTimeOffset DeletedAtUtc, string DeletedByName, bool HasArchive);

public sealed record ProvisionCompanyRequest(Guid OperationKey, string CompanyName, string ConnectionProfileKey);

public sealed record CompanyOperationRetryRequest(string RowVersion);

public sealed record ServerProfileResponse(string Key, string ServerName);
