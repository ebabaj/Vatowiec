using Vatowiec.SharedContracts;

namespace Vatowiec.Desktop.ViewModels;

public sealed class UserAdministrationHistoryRow(UserAdministrationHistory value, UserAdministrationLookups lookups)
{
    public string Time => value.OccurredAtUtc.ToLocalTime().ToString("g");
    public string Operation => value.Operation switch
    {
        "CreateUser" => "Utworzenie konta", "ResetPassword" => "Administracyjny reset hasła",
        "ChangeOwnPassword" => "Zmiana własnego hasła", "BlockUser" => "Zablokowanie konta",
        "GrantInstallationAdministrator" => "Nadanie roli administratora instalacji", "RevokeInstallationAdministrator" => "Odebranie roli administratora instalacji",
        "ActivateUser" => "Aktywowanie konta", "ChangeCompanyAccess" => "Zmiana dostępu do firmy",
        "ArchiveUser" => "Usunięcie logiczne użytkownika", "RestoreSameUser" => "Przywrócenie tej samej osoby",
        "ReleaseArchivedLogin" => "Zwolnienie loginu dla nowej osoby", "CreateFromArchivedLogin" => "Utworzenie nowej osoby z loginem archiwalnym", _ => value.Operation
    };
    public string Actor => "Wykonawca: " + value.ActorName;
    public string Target => "Konto: " + value.TargetName;
    public string Company => value.CompanyId == null ? "" : "Firma: " + (lookups.Companies.FirstOrDefault(x => x.CompanyId == value.CompanyId)?.Name ?? value.CompanyId.ToString());
    private string Rights(string[]? codes) => codes is not { Length: > 0 } ? "brak" : string.Join(", ", codes.Select(code => lookups.Permissions.FirstOrDefault(x => x.Code == code)?.Name ?? code));
    public string Before => value.PermissionsBefore == null ? "" : "Prawa przed: " + Rights(value.PermissionsBefore);
    public string After => value.PermissionsAfter == null ? "" : "Prawa po: " + Rights(value.PermissionsAfter);
    public string State => value.BlockedAfter == null ? "" : $"Stan: {(value.BlockedBefore == true ? "zablokowane" : "aktywne")} → {(value.BlockedAfter == true ? "zablokowane" : "aktywne")}";
}
