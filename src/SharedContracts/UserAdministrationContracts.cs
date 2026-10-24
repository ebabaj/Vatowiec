namespace Vatowiec.SharedContracts;

public sealed record PermissionDefinition(string Code, string Name, string Description, string[] Requires);
public sealed record AdministrationCompany(Guid CompanyId, string Name);
public sealed record UserSummary(Guid Id, string UserName, bool IsBlocked, bool MustChangePassword, bool IsInstallationAdministrator,
    bool IsArchived = false, string? HistoricalUserName = null, DateTimeOffset? ArchivedAtUtc = null, string? ArchivedByName = null, bool IsLoginReleased = false)
{
    [System.Text.Json.Serialization.JsonIgnore] public string DisplayName => IsArchived ? HistoricalUserName ?? UserName : UserName;
}
public sealed record UserCompanyAccess(Guid CompanyId, string[] Permissions, string RowVersion);
public sealed record UserAdministrationDetail(UserSummary User, string ConcurrencyStamp, UserCompanyAccess[] Access);
public sealed record UserAdministrationLookups(PermissionDefinition[] Permissions, AdministrationCompany[] Companies);
public sealed record UserCreateRequest(string UserName, string? TemporaryPassword = null)
{
    public override string ToString() => "UserCreateRequest [redacted]";
}
public sealed record UserVersionRequest(string ConcurrencyStamp, bool Confirm, string? TemporaryPassword = null)
{
    public override string ToString() => "UserVersionRequest [redacted]";
}
public sealed record UserBlockRequest(string ConcurrencyStamp, bool IsBlocked, bool Confirm);
public sealed record UserAccessItem(Guid CompanyId, string[] Permissions, string? RowVersion);
public sealed record UserAccessRequest(string ConcurrencyStamp, UserAccessItem[] Companies);
public sealed record TemporaryPasswordResponse(UserAdministrationDetail User, string TemporaryPassword, bool IsGenerated = true)
{
    public override string ToString() => "TemporaryPasswordResponse [redacted]";
}
public sealed record OwnPasswordRequest(string CurrentPassword, string NewPassword)
{
    public override string ToString() => "OwnPasswordRequest [redacted]";
}
public sealed record UserAdministrationHistory(long Id, Guid ActorId, string ActorName, Guid TargetId, string TargetName,
    Guid? CompanyId, DateTimeOffset OccurredAtUtc, string Operation, string[]? PermissionsBefore, string[]? PermissionsAfter,
    bool? BlockedBefore, bool? BlockedAfter, Guid? RelatedUserId = null);
