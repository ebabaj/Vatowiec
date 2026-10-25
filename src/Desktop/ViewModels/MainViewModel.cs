using System.ComponentModel;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Vatowiec.Desktop.Services;
using Vatowiec.SharedContracts;

namespace Vatowiec.Desktop.ViewModels;

public sealed partial class MainViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly Func<string, IVatowiecApiClient> connect;
    private readonly TimeProvider clock;
    private IVatowiecApiClient? api;
    private SessionResponse? session;
    private bool busy;
    private string message = "";
    private CompanyResponse[] companies = [];
    private CompanyResponse? selectedCompany;
    private CompanyProfileResponse? profile;
    private ContractorResponse[] contractors = [];
    private bool contractorsLoaded;
    private bool disposed;
    private string? personalFailure;
    public bool IsPersonal => PersonalApiIdentity.TryParse(configuredApiAddress ?? "", out _);
    public bool IsPersonalUnavailable => personalFailure != null;
    public void NotifyPersonalFailure(string error)
    {
        personalFailure = error; message = error;
        if (!busy) ResetSession();
        ChangedAll();
    }

    public MainViewModel(Func<string, IVatowiecApiClient>? connect = null, TimeProvider? clock = null,
        ILoginPreferencesStore? preferencesStore = null, ILoginCredentialStore? credentialStore = null, string? apiAddressOverride = null)
    {
        this.connect = connect ?? VatowiecApiClient.Connect;
        this.clock = clock ?? TimeProvider.System;
        this.preferencesStore = preferencesStore ?? new MemoryLoginPreferencesStore();
        this.credentialStore = credentialStore ?? new UnavailableLoginCredentialStore();
        configuredApiAddress = apiAddressOverride ?? Environment.GetEnvironmentVariable("VATOWIEC_API_URL");
        apiAddress = configuredApiAddress ?? "https://localhost:7243";
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public bool IsBusy => busy;
    public bool CanInteract => !busy && personalFailure == null;
    public bool IsLogin => session is null;
    public bool IsCompanySelection => session is not null && profile is null && !gusSettings && !usersSettings && !IsOwnPasswordChange && !passwordPolicyScreen;
    public bool IsWorkspace => session is not null && profile is not null;
    public string CurrentUser => session?.UserName ?? "";
    public string CurrentCompany => profile?.Name ?? "";
    public string SessionExpiry => session is null ? "" : $"Sesja do: {session.ExpiresAtUtc.ToLocalTime():HH:mm}";
    public string Message => message;
    public CompanyResponse[] Companies => companies;
    public CompanyResponse? SelectedCompany
    {
        get => selectedCompany;
        set { selectedCompany = value; Changed(); Changed(nameof(CanSetDefaultCompany)); }
    }
    public ContractorResponse[] Contractors => contractors;
    public bool CanViewContractors => IsWorkspace && selectedCompany?.CanViewContractors == true;
    public bool CannotViewContractors => IsWorkspace && !CanViewContractors;
    public string ContractorStatus => IsContractorLoading ? "Ładowanie kontrahentów…" : !contractorsLoaded ? "Nie wczytano danych. Wybierz Odśwież, aby ponowić."
        : contractors.Length == 0 ? "Brak kontrahentów spełniających filtry." : $"Pozycje na tej stronie: {contractors.Length}";

    public async Task Login(string password)
    {
        if (busy || disposed || !IsLogin) return;
        await Run(async () =>
        {
            if (string.IsNullOrWhiteSpace(UserName)) throw new ArgumentException("Wpisz login i hasło.");
            var target = new LoginTarget(ApiAddress, UserName);
            var remember = RememberCredentials; var automatic = AutomaticLogin;
            var warning = ReadLoginPreferences();
            target = new(target.ApiAddress, target.UserName, loginPreferences.Find(target)?.UserId);
            if (string.IsNullOrEmpty(password) && target.UserId != null && credentialStore.Contains(target))
                password = credentialStore.Read(target) ?? "";
            if (string.IsNullOrEmpty(password)) throw new ArgumentException("Wpisz hasło. Nie znaleziono zapamiętanego poświadczenia dla tego serwera i loginu.");
            ResetSession();
            api = connect(target.ApiAddress);
            session = await api.Login(target.UserName, password);
            if (session.UserId == Guid.Empty) { ResetSession(); throw new ApiException(502, "API nie zwróciło identyfikatora użytkownika. Wymagana jest aktualna wersja serwera."); }
            if (target.UserId != null && target.UserId != session.UserId)
            {
                var newId = session.UserId;
                try { await api.Logout(); } catch (Exception ex) when (IsExpected(ex)) { }
                try { credentialStore.Delete(target); } catch (LocalLoginStorageException) { }
                ResetSession(); rememberCredentials = automaticLogin = false;
                var clean = new LoginProfilePreferences(target.ApiAddress, target.UserName, UserId: newId);
                loginPreferences = loginPreferences.WithProfile(clean, makeLast: true);
                try { preferencesStore.Save(loginPreferences); } catch (LocalLoginStorageException) { }
                message = "Ten login należy do innej tożsamości użytkownika. Odrzucono zapamiętane dane, autologowanie i firmę domyślną. Zaloguj się ponownie ręcznie.";
                password = ""; return;
            }
            target = new(target.ApiAddress, target.UserName, session.UserId);
            signedInLoginTarget = target;
            if (session.MustChangePassword)
            {
                password = "";
                message = "Przed dalszą pracą ustaw nowe własne hasło. Poświadczenie nie zostanie zapamiętane przed zakończeniem zmiany.";
                ChangedAll(); return;
            }
            if (!startingAutomaticLogin) warning = RememberSuccessfulLogin(target, password, remember, automatic) ?? warning;
            password = "";
            ChangedAll();
            companies = await api.Companies();
            if (warning != null) message = warning;
            await OpenDefaultCompanyAfterLogin(target);
            ChangedAll();
        });
        if (session is null) ResetSession();
        password = "";
    }

    public Task RefreshCompanies() => Run(async () =>
    {
        if (api is null || session is null) return;
        companies = [];
        selectedCompany = null;
        companies = await api.Companies();
    });

    public Task OpenCompany() => Run(OpenCompanyCore);

    private async Task OpenCompanyCore()
    {
        if (api is null || session is null || selectedCompany is null)
            throw new ArgumentException("Wybierz firmę z listy.");
        if (signedInLoginTarget?.UserId != session.UserId || session.UserId == Guid.Empty)
            throw new ApiException(401, "Nie potwierdzono tożsamości użytkownika. Zaloguj się ponownie.");
        var id = selectedCompany.CompanyId;
        ClearCompanyData();
        var result = await api.Profile(id);
        if (result.CompanyId != id) throw new ApiException(502, "API zwróciło dane innej firmy. Odmowa wyświetlenia danych.");
        profile = result;
        await ReadTableViews();
        if (CanViewContractors) _ = LoadPage(0);
    }

    public Task OpenClickedCompany(CompanyResponse company)
    {
        if (busy || !IsCompanySelection || !companies.Contains(company)) return Task.CompletedTask;
        SelectedCompany = company;
        return OpenCompany();
    }

    public Task LoadContractors() => LoadPage(0);
    private CancellationTokenSource? contractorLoad;
    private long contractorLoadGeneration;
    public bool IsContractorLoading { get; private set; }
    private void CancelContractorLoading()
    {
        contractorLoadGeneration++;
        contractorLoad?.Cancel(); contractorLoad?.Dispose(); contractorLoad = null;
        IsContractorLoading = false;
    }
    private async Task LoadPage(int offset)
    {
        CancelContractorLoading();
        if (disposed || api == null || profile == null) return;
        if (!CanViewContractors) { message = "Brak uprawnień do kartoteki kontrahentów."; ChangedAll(); return; }
        if (!CanBrowse) return;
        CheckExpiry();
        if (api == null || profile == null) return;
        var generation = contractorLoadGeneration;
        var client = api; var companyId = profile.CompanyId; var identity = session?.UserId;
        contractorLoad = new(); var token = contractorLoad.Token;
        var filter = (SearchText.Trim(), FilterCountry.Trim(), FilterActivity);
        if (filter != loadedFilter) offset = 0;
        contractors = []; selectedContractor = null; contractorsLoaded = false; IsContractorLoading = true;
        ChangedAll();
        try
        {
            var rows = await client.SearchContractors(companyId, new(filter.Item1, Active: FilterActivity == 0 ? null : FilterActivity == 1,
                Offset: offset, Limit: PageSize + 1, VisibleColumns: ContractorTable.VisibleColumns), token);
            if (token.IsCancellationRequested || generation != contractorLoadGeneration || profile?.CompanyId != companyId || session?.UserId != identity) return;
            if (rows.Any(x => x.CompanyId != companyId)) throw new ApiException(502, "API zwróciło dane innej firmy. Odmowa wyświetlenia danych.");
            contractors = rows.Take(PageSize).ToArray();
            hasNextPage = rows.Length > PageSize; pageOffset = offset; loadedFilter = filter; contractorsLoaded = true;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception ex) when (IsExpected(ex))
        {
            if (generation != contractorLoadGeneration) return;
            if (ex is ApiException { StatusCode: 401 }) ResetSession();
            else if (ex is ApiException { StatusCode: 403, Code: "user_password_change_required" })
            {
                ClearCompanyData(); ResetUsersSettings(); ResetGusSettings(); ResetPasswordPolicy();
                if (session != null) session = session with { MustChangePassword = true };
            }
            else if (ex is ApiException { StatusCode: 403 }) ClearCompanyData();
            message = ex is ApiException error ? error.Message : "Nie można połączyć się z API i wczytać kartoteki. Sprawdź połączenie i wybierz Odśwież.";
            ChangedAll();
        }
        finally { if (generation == contractorLoadGeneration) { IsContractorLoading = false; ChangedAll(); } }
    }

    public Task ChangeCompany() => Run(async () =>
    {
        if (HasUnsavedChanges) { RequestDiscard(() => { ClearNavigationDrafts(); _ = ChangeCompany(); }); return; }
        ClearCompanyData();
        selectedCompany = null;
        companies = [];
        ChangedAll();
        if (api is not null) companies = await api.Companies();
    });

    public Task Logout()
    {
        if (busy || disposed) return Task.CompletedTask;
        if (HasUnsavedChanges) { RequestDiscard(() => { ClearNavigationDrafts(); _ = Logout(); }); return Task.CompletedTask; }
        // An explicit logout must also disable startup login if CheckExpiry in Run
        // discovers that the server session has just expired.
        var preferenceWarning = DisableAutomaticLogin();
        return Run(async () =>
        {
            try { if (api is not null) await api.Logout(); }
            catch (Exception ex) when (IsExpected(ex))
            {
                if (ex is not ApiException { StatusCode: 401 })
                    message = "Wylogowano lokalnie. Nie potwierdzono wylogowania na serwerze; sesja wygaśnie zgodnie z terminem ważności.";
            }
            finally
            {
                ResetSession();
                if (preferenceWarning != null) message = preferenceWarning + " " + message;
            }
        });
    }

    // Nie usuwamy sesji podczas aktywnego żądania. Run ponownie sprawdzi termin po odpowiedzi.
    public void CheckExpiry()
    {
        if (!busy && session is not null && clock.GetUtcNow() >= session.ExpiresAtUtc)
        {
            ResetSession();
            message = "Sesja wygasła. Zaloguj się ponownie.";
            ChangedAll();
        }
    }

    private async Task Run(Func<Task> action)
    {
        if (busy || disposed || personalFailure != null) return;
        var wasAuthenticated = session is not null;
        CheckExpiry();
        if (wasAuthenticated && session is null) return;
        busy = true;
        message = "";
        ChangedAll();
        try { await action(); }
        catch (ApiException ex)
        {
            if (ex.StatusCode == 403 && ex.Code == "user_password_change_required")
            {
                ClearCompanyData(); ResetUsersSettings(); ResetGusSettings(); ResetPasswordPolicy();
                if (session != null) session = session with { MustChangePassword = true };
                message = "Hasło wymaga zmiany. Ustaw własne hasło przed dalszą pracą.";
                return;
            }
            if (ex.StatusCode == 403 && (IsUsersSettings || IsPasswordPolicyScreen))
            {
                ResetUsersSettings(); ResetGusSettings(); ResetPasswordPolicy();
                if (session != null) session = session with { IsInstallationAdministrator = false };
            }
            if (ex.StatusCode == 401) ResetSession();
            if (ex.StatusCode == 403) ClearCompanyData();
            if (ex.StatusCode == 403 && gusSettings)
            {
                if (session != null) session = session with { IsInstallationAdministrator = false };
                ResetGusSettings();
            }
            message = ex.Message;
        }
        catch (LocalLoginStorageException ex) { message = ex.Message; }
        catch (ArgumentException ex) { message = ex.Message; }
        catch (Exception ex) when (IsExpected(ex))
        {
            message = IsPersonal ? ex switch
            {
                TaskCanceledException => "Przekroczono czas oczekiwania na lokalne API Personal. Sprawdź ostatni zapis; zamknij program i uruchom ponownie.",
                JsonException => "Lokalny serwer Personal zwrócił nieprawidłową odpowiedź. Sprawdź zgodność wersji klienta i API.",
                _ => "Nie można potwierdzić bezpiecznego kanału Windows do lokalnego API Personal. Zamknij program i uruchom ponownie. Zapisane dane pozostają zachowane."
            } : ex switch
            {
                HttpRequestException { HttpRequestError: HttpRequestError.SecureConnectionError } =>
                    "Nie można potwierdzić bezpiecznego połączenia HTTPS z API. Sprawdź certyfikat serwera: zaufanie, ważność i zgodność nazwy z adresem. Skontaktuj się z administratorem.",
                TaskCanceledException =>
                    "Przekroczono czas oczekiwania na API. Sprawdź adres serwera, połączenie LAN lub VPN i dostępność API. Spróbuj ponownie.",
                JsonException =>
                    "Serwer nie zwrócił prawidłowej odpowiedzi API VATowca. Sprawdź adres serwera i zgodność wersji klienta oraz API.",
                _ => "Nie można połączyć się z API. Sprawdź adres HTTPS serwera, połączenie LAN lub VPN i dostępność API. localhost oznacza ten komputer. Spróbuj ponownie."
            };
        }
        finally
        {
            busy = false;
            if (disposed) ResetSession();
            if (personalFailure != null) { ResetSession(); message = personalFailure; }
            CheckExpiry();
            ChangedAll();
        }
    }

    private static bool IsExpected(Exception ex) => ex is ApiException or HttpRequestException or TaskCanceledException or JsonException;
    private void ClearCompanyData()
    {
        CancelContractorLoading();
        ResetTableViews();
        profile = null;
        contractors = [];
        contractorsLoaded = false;
        ClearContractorState();
        ResetDataPortSettings();
        ResetCurrencyState();
    }
    private void ResetSession()
    {
        api?.Dispose();
        api = null;
        session = null;
        signedInLoginTarget = null;
        ownPasswordForm = false; ResetUsersSettings();
        ResetPasswordPolicy();
        ResetGusSettings();
        companies = [];
        selectedCompany = null;
        ClearCompanyData();
    }
    private void Changed([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    private void ChangedAll() => Changed(string.Empty);
    public void Dispose()
    {
        disposed = true;
        CancelContractorLoading();
        if (!busy) ResetSession();
    }
}
