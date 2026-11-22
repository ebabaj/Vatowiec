namespace Vatowiec.SharedContracts;

// ADR-044 krok 2: kopie jednej firmy na serwerze. Bez ścieżek plików i bez pobierania kopii.
public sealed record CompanyBackupItem(long Id, DateTimeOffset CreatedAtUtc, long SizeBytes, bool IsAutomatic, string RequestedByName);

public sealed record CompanyBackupScheduleResponse(string Frequency, string Mode, int PreferredHour, string? RowVersion, bool IsDefault);

public sealed record CompanyBackupStatusResponse(Guid CompanyId, DateTimeOffset? LastSuccessAtUtc, CompanyBackupItem[] Backups,
    CompanyBackupScheduleResponse Schedule, bool CanRequest, bool CanManageSchedule, bool ReminderDue,
    CompanyOperationResponse? LatestOperation, int RetainedBackupsPerCompany);

public sealed record CompanyBackupRequest(Guid OperationKey);

public sealed record CompanyBackupScheduleRequest(string Frequency, string Mode, int PreferredHour, string? RowVersion);

public sealed record BackupPolicyResponse(bool UsersMayRequestBackups, int RetainedBackupsPerCompany, string RowVersion);

public sealed record BackupPolicyRequest(bool UsersMayRequestBackups, int RetainedBackupsPerCompany, string RowVersion);
