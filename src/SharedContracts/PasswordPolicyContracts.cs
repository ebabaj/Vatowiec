namespace Vatowiec.SharedContracts;

public sealed record PasswordPolicyResponse(bool IsEnabled, int MinimumLength, bool RequireUppercase, bool RequireLowercase,
    bool RequireDigit, bool RequireSpecialCharacter, string RowVersion, bool PreventPasswordReuse = false, int PreviousPasswordCount = 5,
    bool EnforcePasswordExpiration = false, int PasswordValidityDays = 90, DateTimeOffset? PasswordExpirationEnabledAtUtc = null);
public sealed record PasswordPolicyRequest(bool IsEnabled, int MinimumLength, bool RequireUppercase, bool RequireLowercase,
    bool RequireDigit, bool RequireSpecialCharacter, string RowVersion, bool ConfirmDisable,
    [property: System.Text.Json.Serialization.JsonRequired] bool PreventPasswordReuse = false,
    [property: System.Text.Json.Serialization.JsonRequired] int PreviousPasswordCount = 5,
    [property: System.Text.Json.Serialization.JsonRequired] bool EnforcePasswordExpiration = false,
    [property: System.Text.Json.Serialization.JsonRequired] int PasswordValidityDays = 90);
public sealed record PasswordPolicyHistory(long Id, string ActorName, DateTimeOffset OccurredAtUtc, PasswordPolicyResponse Before, PasswordPolicyResponse After);
public sealed record AdministratorRoleRequest(string ConcurrencyStamp, bool IsAdministrator, bool Confirm);
