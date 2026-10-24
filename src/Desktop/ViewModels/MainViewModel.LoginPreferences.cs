using Vatowiec.Desktop.Services;

namespace Vatowiec.Desktop.ViewModels;

public sealed partial class MainViewModel
{
    private readonly ILoginPreferencesStore preferencesStore;
    private readonly ILoginCredentialStore credentialStore;
    private LoginPreferences loginPreferences = new();
    private LoginTarget? signedInLoginTarget;
    private bool loginPreferencesLoaded, loginInitialized, startingAutomaticLogin;
    private bool rememberCredentials, automaticLogin;
    private bool hasSavedPassword;
    private string apiAddress = "https://localhost:7243", userName = "";
    private readonly string? configuredApiAddress;

    public string ApiAddress
    {
        get => apiAddress;
        set { if (busy || IsPersonal || value == apiAddress) return; apiAddress = value; ApplyLoginFlags(); Changed(); }
    }
    public string UserName
    {
        get => userName;
        set { if (busy || value == userName) return; userName = value; ApplyLoginFlags(); Changed(); }
    }
    public bool RememberCredentials
    {
        get => rememberCredentials;
        set { if (busy) return; rememberCredentials = value; if (!value) automaticLogin = false; NotifyLoginFlags(); }
    }
    public bool AutomaticLogin
    {
        get => automaticLogin;
        set { if (busy) return; automaticLogin = value && rememberCredentials; NotifyLoginFlags(); }
    }
    public bool HasSavedPassword => hasSavedPassword;
    public string SavedPasswordMask => hasSavedPassword ? "••••••••" : "";
    public string SavedPasswordStatus => hasSavedPassword ? "Hasło zapisane na tym komputerze" : "";
    public void RefreshSavedPasswordState()
    {
        if (!busy && IsLogin && !disposed) UpdateSavedPasswordState();
    }
    private void UpdateSavedPasswordState()
    {
        hasSavedPassword = false;
        var target = FormLoginTarget();
        if (target?.UserId != null)
        {
            try { hasSavedPassword = credentialStore.Contains(target); }
            catch (LocalLoginStorageException e) { message = e.Message; Changed(nameof(Message)); }
        }
        Changed(nameof(HasSavedPassword)); Changed(nameof(SavedPasswordMask)); Changed(nameof(SavedPasswordStatus));
    }
    public bool CanSetDefaultCompany => IsCompanySelection && signedInLoginTarget != null && selectedCompany != null && companies.Contains(selectedCompany);
    public string DefaultCompanyLabel
    {
        get
        {
            var id = signedInLoginTarget == null ? null : loginPreferences.Find(signedInLoginTarget)?.DefaultCompanyId;
            return id == null ? "Firma domyślna: nie ustawiono." : "Firma domyślna: " +
                (companies.FirstOrDefault(x => x.CompanyId == id)?.Name ?? $"{id} — obecnie niedostępna");
        }
    }
    private LoginTarget? FormLoginTarget()
    {
        try { var target = new LoginTarget(ApiAddress, UserName); return new(ApiAddress, UserName, loginPreferences.Find(target)?.UserId); }
        catch (ArgumentException) { return null; }
    }
    private void NotifyLoginFlags() { Changed(nameof(RememberCredentials)); Changed(nameof(AutomaticLogin)); }
    private void ApplyLoginFlags()
    {
        var target = FormLoginTarget(); var row = target == null ? null : loginPreferences.Find(target);
        rememberCredentials = row?.UserId != null && row.RememberCredentials; automaticLogin = row?.UserId != null && row.AutomaticLogin && rememberCredentials;
        UpdateSavedPasswordState();
        NotifyLoginFlags();
    }
    private string? ReadLoginPreferences()
    {
        if (loginPreferencesLoaded) return null;
        loginPreferencesLoaded = true;
        try
        {
            loginPreferences = preferencesStore.Load();
            var legacy = loginPreferences.Profiles?.Where(x => (x.UserId == null || x.UserId == Guid.Empty) && (x.RememberCredentials || x.AutomaticLogin || x.DefaultCompanyId != null)).ToArray() ?? [];
            foreach (var profile in legacy)
            {
                try { credentialStore.Delete(profile.Target); } catch (LocalLoginStorageException) { }
                loginPreferences = loginPreferences.WithProfile(profile with { RememberCredentials = false, AutomaticLogin = false, DefaultCompanyId = null });
            }
            if (legacy.Length > 0) preferencesStore.Save(loginPreferences);
            return null;
        }
        catch (LocalLoginStorageException e) { return e.Message; }
    }
    private void SaveLoginProfile(LoginProfilePreferences profile, bool makeLast = false)
    {
        var updated = loginPreferences.WithProfile(profile, makeLast);
        preferencesStore.Save(updated); loginPreferences = updated;
    }
    private LoginProfilePreferences ProfileFor(LoginTarget target) => loginPreferences.Find(target) ?? new(target.ApiAddress, target.UserName, UserId: target.UserId);

    // Called once by the window. Expiry timers, company switches and form changes never call it.
    public async Task InitializeLogin()
    {
        if (loginInitialized || disposed) return;
        loginInitialized = true;
        if (busy || session != null) return;
        var warning = ReadLoginPreferences();
        if (warning != null) { message = warning; ChangedAll(); return; }
        var last = loginPreferences.Profiles?.FirstOrDefault(x => x.Target.Key == loginPreferences.LastTargetKey);
        if (last != null)
        {
            apiAddress = configuredApiAddress ?? last.Target.ApiAddress; userName = last.UserName;
        }
        ApplyLoginFlags(); ChangedAll();
        var target = FormLoginTarget();
        if (!AutomaticLogin || target?.UserId == null) return;
        string? password = null;
        try
        {
            password = credentialStore.Read(target);
            if (string.IsNullOrEmpty(password))
            { message = "Nie znaleziono zapamiętanego hasła. Zaloguj się ręcznie, aby zapisać aktualne poświadczenie."; ChangedAll(); return; }
            startingAutomaticLogin = true;
            await Login(password);
        }
        catch (LocalLoginStorageException e) { message = e.Message; }
        finally { password = null; startingAutomaticLogin = false; ChangedAll(); }
        if (session == null)
        {
            if (message.Length > 0) message = "Automatyczne logowanie nie powiodło się. " + message;
            ChangedAll(); return;
        }
    }

    // Runs inside Login's existing Run boundary, including the profile read. Manual
    // and startup login cannot interleave another actor/company operation here.
    private async Task OpenDefaultCompanyAfterLogin(LoginTarget target)
    {
        if (disposed || !IsCompanySelection || signedInLoginTarget?.Key != target.Key ||
            session == null || target.UserId != session.UserId || loginPreferences.Find(target)?.UserId != session.UserId || clock.GetUtcNow() >= session.ExpiresAtUtc) return;
        var companyId = loginPreferences.Find(target)?.DefaultCompanyId;
        if (companyId == null) return;
        var company = companies.FirstOrDefault(x => x.CompanyId == companyId);
        if (company == null)
        { message = "Firma domyślna nie jest obecnie dostępna. Wybierz firmę z aktualnej listy."; ChangedAll(); return; }
        SelectedCompany = company;
        await OpenCompanyCore();
    }

    private string? RememberSuccessfulLogin(LoginTarget target, string password, bool remember, bool automatic)
    {
        var previous = ProfileFor(target) with { UserId = target.UserId }; string? warning = null;
        try
        {
            if (remember) credentialStore.Write(target, password);
            else if (previous.RememberCredentials) credentialStore.Delete(target);
        }
        catch (LocalLoginStorageException e) { remember = automatic = false; warning = e.Message; }
        try { SaveLoginProfile(previous with { RememberCredentials = remember, AutomaticLogin = remember && automatic }, makeLast: true); }
        catch (LocalLoginStorageException e)
        {
            warning = e.Message;
            if (previous.AutomaticLogin && !automatic) warning += " " + RemoveCredentialAfterFailedOptOut(target);
        }
        ApplyLoginFlags(); return warning;
    }
    private string? RemoveCredentialAfterFailedOptOut(LoginTarget target)
    {
        try { credentialStore.Delete(target); UpdateSavedPasswordState(); return null; }
        catch (LocalLoginStorageException) { return "Nie udało się trwale wyłączyć autologowania. Użyj „Zapomnij dane logowania” lub usuń wpis VATowca w Windows Credential Manager."; }
    }
    private string? DisableAutomaticLogin()
    {
        var target = signedInLoginTarget ?? FormLoginTarget();
        automaticLogin = false;
        if (target == null) return null;
        var profile = ProfileFor(target);
        string? warning = null;
        try { SaveLoginProfile(profile with { AutomaticLogin = false }); }
        catch (LocalLoginStorageException)
        {
            // If the nonsecret preference cannot be saved, removing the credential
            // prevents a subsequent launch from automatically submitting it.
            warning = RemoveCredentialAfterFailedOptOut(target) ?? "Nie zapisano preferencji wylogowania. Usunięto zapamiętane poświadczenie, aby wyłączyć autologowanie.";
        }
        automaticLogin = false; NotifyLoginFlags();
        return warning;
    }
    public Task ForgetLoginCredentials() => Run(() =>
    {
        var target = signedInLoginTarget ?? FormLoginTarget();
        if (target == null) throw new ArgumentException("Wskaż poprawny adres HTTPS API i login, których dane chcesz zapomnieć.");
        var warning = ReadLoginPreferences();
        // Disable the preference even if Windows refuses deletion; never claim deletion succeeded.
        var previous = ProfileFor(target);
        try { SaveLoginProfile(previous with { AutomaticLogin = false }); }
        catch (LocalLoginStorageException e) { warning = e.Message; }
        automaticLogin = false;
        try { credentialStore.Delete(target); }
        catch (LocalLoginStorageException e) { message = e.Message; UpdateSavedPasswordState(); NotifyLoginFlags(); return Task.CompletedTask; }
        UpdateSavedPasswordState();
        rememberCredentials = false;
        try { SaveLoginProfile(previous with { RememberCredentials = false, AutomaticLogin = false }); }
        catch (LocalLoginStorageException e) { warning = e.Message; }
        message = "Usunięto zapamiętane poświadczenie i wyłączono autologowanie." + (warning == null ? "" : " " + warning);
        NotifyLoginFlags(); return Task.CompletedTask;
    });

    public Task SetDefaultCompany() => Run(() =>
    {
        if (!CanSetDefaultCompany || signedInLoginTarget == null) return Task.CompletedTask;
        try
        {
            SaveLoginProfile(ProfileFor(signedInLoginTarget) with { DefaultCompanyId = selectedCompany!.CompanyId });
            message = "Ustawiono firmę domyślną dla tego serwera i użytkownika.";
        }
        catch (LocalLoginStorageException e) { message = e.Message; }
        return Task.CompletedTask;
    });
    public Task ClearDefaultCompany() => Run(() =>
    {
        if (signedInLoginTarget == null || !IsCompanySelection) return Task.CompletedTask;
        try { SaveLoginProfile(ProfileFor(signedInLoginTarget) with { DefaultCompanyId = null }); message = "Usunięto firmę domyślną."; }
        catch (LocalLoginStorageException e) { message = e.Message; }
        return Task.CompletedTask;
    });
}
