namespace Vatowiec.SharedContracts;

public sealed record PasswordPolicyResponse(bool IsEnabled, int MinimumLength, bool RequireUppercase, bool RequireLowercase,
    bool RequireDigit, bool RequireSpecialCharacter, string RowVersion);
public sealed record PasswordPolicyRequest(bool IsEnabled, int MinimumLength, bool RequireUppercase, bool RequireLowercase,
    bool RequireDigit, bool RequireSpecialCharacter, string RowVersion, bool ConfirmDisable);
public sealed record PasswordPolicyHistory(long Id, string ActorName, DateTimeOffset OccurredAtUtc, PasswordPolicyResponse Before, PasswordPolicyResponse After);
public sealed record AdministratorRoleRequest(string ConcurrencyStamp, bool IsAdministrator, bool Confirm);
