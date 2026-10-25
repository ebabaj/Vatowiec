using Vatowiec.SharedContracts;
using Vatowiec.Desktop.Services;
using System.Collections.ObjectModel;

namespace Vatowiec.Desktop.ViewModels;

public sealed partial class MainViewModel
{
    private bool usersSettings, ownPasswordForm;
    private bool showArchivedUsers;
    public bool ShowArchivedUsers
    {
        get => showArchivedUsers;
        set
        {
            if (busy || value == showArchivedUsers) return;
            void Apply() { showArchivedUsers = value; NewUserName = ""; _ = Run(() => ReadAdminUsers(0)); }
            if (HasAdminChanges) { RequestDiscard(Apply); Changed(nameof(ShowArchivedUsers)); } else Apply();
        }
    }
    public bool IsArchivedAdminUser => adminUser?.User.IsArchived == true;
    public bool CanChangeSelectedAccount => CanManageUsers && adminUser != null && !IsArchivedAdminUser;
    public bool CanArchiveUser => CanChangeSelectedAccount && !string.Equals(adminUser!.User.UserName, CurrentUser, StringComparison.OrdinalIgnoreCase);
    public bool CanRestoreAdminUser => CanManageUsers && IsArchivedAdminUser && adminUser?.User.IsLoginReleased == false;
    public string ArchivedUserInfo => !IsArchivedAdminUser ? "" : $"Konto usunięte: {adminUser!.User.ArchivedAtUtc?.ToLocalTime():g}; administrator: {adminUser.User.ArchivedByName}. " +
        (adminUser.User.IsLoginReleased ? "Login przekazano nowej osobie; tego konta nie można już przywrócić." : "Login pozostaje zarezerwowany. Wybierz świadomie tę samą albo nową osobę.");
    private readonly ObservableCollection<UserSummary> adminUsers = [];
    private UserSummary? selectedAdminUser;
    private UserAdministrationDetail? adminUser;
    private bool administratorRole;
    private bool adminRoleUnconfirmed;
    public bool AdministratorRole { get => administratorRole; set { if (busy) return; administratorRole = value; Changed(); Changed(nameof(CanSaveAdministratorRole)); Changed(nameof(AdminRoleStatus)); Changed(nameof(AdminDirtyStatus)); Changed(nameof(HasUnsavedChanges)); } }
    public bool HasRoleChanges => adminUser != null && administratorRole != adminUser.User.IsInstallationAdministrator;
    public bool CanEditAdministratorRole => CanChangeSelectedAccount && !adminRoleUnconfirmed && !string.Equals(adminUser!.User.UserName, CurrentUser, StringComparison.OrdinalIgnoreCase);
    public bool CanSaveAdministratorRole => CanEditAdministratorRole && HasRoleChanges;
    public string AdminRoleStatus => adminUser == null ? "" : adminRoleUnconfirmed
        ? "Aktualny stan roli nie został potwierdzony. Checkbox pokazuje ostatni odczytany stan. Odśwież szczegóły użytkownika przed kolejną zmianą."
        : "Stan odczytany z serwera: " + (adminUser.User.IsInstallationAdministrator ? "administrator instalacji." : "bez roli administratora instalacji.") +
            (HasRoleChanges ? " Checkbox zawiera niezapisaną propozycję zmiany." : "");
    private UserAdministrationLookups? adminLookups;
    private CompanyAccessEditor[] adminCompanyAccess = [];
    private int adminOffset, adminHistoryOffset;
    private string loadedAdminSearch = "";
    private UserAdministrationHistoryRow[] adminHistory = [];
    public string AdminSearch { get; set; } = "";
    private string newUserName = "";
    public string NewUserName { get => newUserName; set { newUserName = value; Changed(); Changed(nameof(AdminDirtyStatus)); Changed(nameof(HasUnsavedChanges)); } }
    public bool IsUsersSettings => session != null && usersSettings;
    public bool IsOwnPasswordChange => session != null && (session.MustChangePassword || ownPasswordForm);
    public bool CanCancelPasswordChange => session != null && !session.MustChangePassword;
    public bool CanOpenOwnPasswordChange => session != null && !busy && !IsOwnPasswordChange;
    public bool CanManageUsers => IsUsersSettings && IsInstallationAdministrator && !busy;
    public bool HasAdminUser => IsUsersSettings && adminUser != null;
    public bool HasAdminChanges => HasRoleChanges || adminCompanyAccess.Any(x => x.IsDirty) || !string.IsNullOrWhiteSpace(NewUserName);
    public bool CanSaveUserAccess => CanChangeSelectedAccount && adminCompanyAccess.Any(x => x.IsDirty);
    public ObservableCollection<UserSummary> AdminUsers => adminUsers;
    public CompanyAccessEditor[] AdminCompanyAccess => adminCompanyAccess;
    public UserAdministrationHistoryRow[] AdminHistory => adminHistory;
    public bool AdminHasNext { get; private set; }
    public bool AdminHasPrevious => adminOffset > 0;
    public string AdminPage => $"Strona {adminOffset / 50 + 1}";
    public bool AdminHistoryHasNext { get; private set; }
    public bool AdminHistoryHasPrevious => adminHistoryOffset > 0;
    public string AdminAccountName => adminUser?.User.DisplayName ?? "Wybierz konto lub utwórz nowe";
    public string AdminAccountStatus => adminUser == null ? "" :
        (adminUser.User.IsArchived ? "Konto usunięte" : adminUser.User.IsBlocked ? "Konto zablokowane" : "Konto aktywne") +
        (adminUser.User.MustChangePassword ? " · wymagana zmiana hasła" : "") +
        (adminUser.User.IsInstallationAdministrator ? " · administrator instalacji (bez automatycznych praw do firm)" : "");
    public string AdminBlockAction => adminUser?.User.IsBlocked == true ? "Aktywuj konto…" : "Zablokuj konto…";
    public string AdminDirtyStatus => HasAdminChanges ? "Niezapisane zmiany" : "Brak niezapisanych zmian";
    public UserSummary? SelectedAdminUser
    {
        get => selectedAdminUser;
        set
        {
            if (busy || value == null || value.Id == selectedAdminUser?.Id || !adminUsers.Any(x => x.Id == value.Id)) return;
            void Select() { selectedAdminUser = value; _ = Run(ReadAdminUser); }
            if (HasAdminChanges) { RequestDiscard(() => { NewUserName = ""; Select(); }); Changed(nameof(SelectedAdminUser)); }
            else Select();
        }
    }
    public Task OpenUsersSettings()
    {
        if (!IsInstallationAdministrator || busy || IsOwnPasswordChange) return Task.CompletedTask;
        return Run(async () =>
        {
            usersSettings = true; gusSettings = false;
            adminLookups = await api!.UserLookups(); await ReadAdminUsers(0);
        });
    }
    private async Task ReadAdminUsers(int offset)
    {
        if (api == null || !IsUsersSettings) return;
        var text = AdminSearch.Trim(); if (text != loadedAdminSearch) offset = 0;
        var rows = await api.Users(text, offset, showArchivedUsers); loadedAdminSearch = text; adminOffset = offset;
        adminUsers.Clear(); foreach (var row in rows.Take(50)) adminUsers.Add(row); AdminHasNext = rows.Length > 50;
        selectedAdminUser = null; adminUser = null; adminCompanyAccess = []; adminHistory = [];
    }
    public Task SearchAdminUsers(int direction = 0)
    {
        void Search() { NewUserName = ""; _ = Run(() => ReadAdminUsers(Math.Max(0, adminOffset + direction * 50))); }
        if (busy || !IsUsersSettings) return Task.CompletedTask;
        if (HasAdminChanges) { RequestDiscard(Search); return Task.CompletedTask; }
        return Run(() => ReadAdminUsers(Math.Max(0, adminOffset + direction * 50)));
    }
    private async Task ReadAdminUser()
    {
        adminUser = null; adminCompanyAccess = []; adminHistory = [];
        if (api == null || selectedAdminUser == null || !IsUsersSettings) return;
        var detail = await api.UserDetail(selectedAdminUser.Id); AcceptAdminUser(detail); await ReadAdminHistory(0);
    }
    private void AcceptAdminUser(UserAdministrationDetail detail)
    {
        if (adminLookups == null || (selectedAdminUser != null && detail.User.Id != selectedAdminUser.Id))
            throw new ApiException(502, "Odpowiedź dotyczy innego użytkownika. Odśwież listę.");
        adminUser = detail; selectedAdminUser = detail.User; adminRoleUnconfirmed = false;
        administratorRole = detail.User.IsInstallationAdministrator;
        var existing = adminUsers.FirstOrDefault(x => x.Id == detail.User.Id);
        if (existing != null) adminUsers[adminUsers.IndexOf(existing)] = detail.User;
        else { adminUsers.Insert(0, detail.User); if (adminUsers.Count > 50) adminUsers.RemoveAt(50); }
        adminCompanyAccess = adminLookups.Companies.Select(c => new CompanyAccessEditor(c, adminLookups.Permissions, detail.Access.SingleOrDefault(x => x.CompanyId == c.CompanyId))).ToArray();
        foreach (var company in adminCompanyAccess) company.PropertyChanged += (_, _) => { Changed(nameof(CanSaveUserAccess)); Changed(nameof(AdminDirtyStatus)); Changed(nameof(HasUnsavedChanges)); };
    }
    public Task SaveAdminUserAccess() => Run(async () =>
    {
        if (api == null || adminUser == null || !IsUsersSettings || IsArchivedAdminUser) return;
        if (HasRoleChanges) throw new ArgumentException("Zapisz lub porzuć zmianę roli systemowej przed zapisem praw firmowych.");
        var result = await api.SaveUserAccess(adminUser.User.Id, new(adminUser.ConcurrencyStamp, adminCompanyAccess.Select(x => x.Request()).ToArray()));
        AcceptAdminUser(result); await ReadAdminHistory(0);
        message = "Zapisano prawa. Dotychczasowe sesje użytkownika zostały unieważnione.";
    });
    public Task RefreshAdminUser()
    {
        if (busy) return Task.CompletedTask;
        void Refresh() { NewUserName = ""; _ = Run(ReadAdminUser); }
        if (HasAdminChanges) { RequestDiscard(Refresh); return Task.CompletedTask; }
        return Run(ReadAdminUser);
    }
    public async Task<TemporaryPasswordResponse?> CreateAdminUser(string? temporaryPassword = null)
    {
        TemporaryPasswordResponse? result = null;
        if (!CanManageUsers || HasRoleChanges || adminCompanyAccess.Any(x => x.IsDirty)) { message = "Zapisz lub porzuć szkic uprawnień przed utworzeniem konta."; ChangedAll(); return null; }
        await Run(async () =>
        {
            result = await api!.CreateUser(new(NewUserName, temporaryPassword)); NewUserName = ""; selectedAdminUser = null;
            AcceptAdminUser(result.User); await ReadAdminHistory(0); message = "Utworzono konto. Przekaż hasło tymczasowe bezpiecznym kanałem.";
        });
        return result;
    }
    public async Task<TemporaryPasswordResponse?> ResetAdminPassword(string? temporaryPassword = null)
    {
        TemporaryPasswordResponse? result = null;
        if (!CanChangeSelectedAccount || adminUser == null) return null;
        if (HasAdminChanges) { message = "Zapisz lub porzuć niezapisane zmiany przed resetem hasła."; ChangedAll(); return null; }
        await Run(async () =>
        {
            var response = await api!.ResetUserPassword(adminUser.User.Id, new(adminUser.ConcurrencyStamp, true, temporaryPassword));
            AcceptAdminUser(response.User); result = response;
            // No further API call here: resetting our own password revokes this session too.
            message = temporaryPassword == null && result.IsGenerated
                ? "Zresetowano hasło i unieważniono sesje. Wymagana zmiana hasła przy logowaniu."
                : "Hasło użytkownika zostało zmienione. Przy następnym logowaniu użytkownik będzie musiał ustawić własne hasło";
        });
        return result;
    }
    public Task ToggleAdminBlock() => Run(async () =>
    {
        if (api == null || adminUser == null || !IsUsersSettings || IsArchivedAdminUser) return;
        if (HasAdminChanges) throw new ArgumentException("Zapisz lub porzuć niezapisane zmiany przed zmianą stanu konta.");
        AcceptAdminUser(await api.BlockUser(adminUser.User.Id, new(adminUser.ConcurrencyStamp, !adminUser.User.IsBlocked, true)));
        await ReadAdminHistory(0); message = "Zapisano stan konta i unieważniono dotychczasowe sesje.";
    });
    public Task SaveAdministratorRole() => Run(async () =>
    {
        if (api == null || adminUser == null || !IsUsersSettings || IsArchivedAdminUser) return;
        if (adminCompanyAccess.Any(x => x.IsDirty) || !string.IsNullOrWhiteSpace(NewUserName))
            throw new ArgumentException("Zapisz lub porzuć pozostałe zmiany przed zmianą roli systemowej.");
        if (!IsInstallationAdministrator || adminRoleUnconfirmed || string.Equals(adminUser.User.UserName, CurrentUser, StringComparison.OrdinalIgnoreCase)) return;
        var id = adminUser.User.Id; var proposed = administratorRole;
        var failureContext = "Nie potwierdzono zapisu roli administratora instalacji.";
        var detailsConfirmed = false;
        try
        {
            AcceptAdminUser(await api.SaveAdministratorRole(id, new(adminUser.ConcurrencyStamp, proposed, true)));
            failureContext = "API potwierdziło zapis roli, ale nie udało się ponownie odczytać stanu użytkownika.";
            AcceptAdminUser(await api.UserDetail(id)); detailsConfirmed = true;
            failureContext = "Rola została zapisana i odczytana z serwera, ale nie udało się odświeżyć historii.";
            await ReadAdminHistory(0);
            message = administratorRole == proposed
                ? "Zapisano rolę systemową i odczytano aktualny stan z serwera. Sesje użytkownika unieważniono. Dostęp do firm pozostaje bez zmian."
                : "API potwierdziło zapis, ale aktualny stan roli różni się od wybranej propozycji. Sprawdź stan odczytany z serwera.";
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            // A lost response may follow a committed write. Do not present the draft as a saved role.
            administratorRole = adminUser?.User.IsInstallationAdministrator ?? false;
            adminRoleUnconfirmed = !detailsConfirmed;
            throw ex is ApiException apiError
                ? new ApiException(apiError.StatusCode, failureContext + " " + apiError.Message) { Code = apiError.Code }
                : new ApiException(0, failureContext + " Nie można połączyć się z API. Sprawdź adres HTTPS i połączenie, a następnie odśwież szczegóły użytkownika.");
        }
    });
    private async Task ReadAdminHistory(int offset)
    {
        if (api == null || !IsUsersSettings) return;
        var rows = await api.UserHistory(adminUser?.User.Id, offset);
        adminHistory = rows.Take(50).Select(x => new UserAdministrationHistoryRow(x, adminLookups!)).ToArray(); AdminHistoryHasNext = rows.Length > 50; adminHistoryOffset = offset;
    }
    public Task LoadAdminHistory(int direction = 0) => Run(() => ReadAdminHistory(Math.Max(0, adminHistoryOffset + direction * 50)));
    public void CloseUsersSettings()
    {
        if (busy) return;
        void Close() { ResetUsersSettings(); gusSettings = true; ChangedAll(); }
        if (HasAdminChanges) RequestDiscard(Close); else Close();
    }
    private void ResetUsersSettings()
    {
        usersSettings = false; showArchivedUsers = false; adminUsers.Clear(); selectedAdminUser = null; adminUser = null; adminLookups = null; administratorRole = false; adminRoleUnconfirmed = false;
        adminCompanyAccess = []; adminHistory = []; adminOffset = adminHistoryOffset = 0; NewUserName = ""; AdminSearch = loadedAdminSearch = "";
    }
    public void OpenOwnPasswordChange()
    {
        if (!CanOpenOwnPasswordChange) return;
        void Open() { CancelContractorLoading(); ClearNavigationDrafts(); ownPasswordForm = true; ChangedAll(); }
        if (HasUnsavedChanges) RequestDiscard(Open); else Open();
    }
    public void CancelOwnPasswordChange() { if (!busy && CanCancelPasswordChange) { ownPasswordForm = false; ChangedAll(); _ = LoadContractors(); } }
    public void ReportOwnPasswordMismatch() { message = "Nowe hasła muszą być identyczne. Popraw pola formularza."; Changed(nameof(Message)); }
    public Task ChangeOwnPassword(string current, string replacement) => Run(async () =>
    {
        if (api == null || !IsOwnPasswordChange) return;
        await api.ChangeOwnPassword(new(current, replacement));
        ResetSession(); message = "Hasło zmieniono. Zaloguj się nowym hasłem; stare zapisane poświadczenie nie będzie działać.";
    });
}
