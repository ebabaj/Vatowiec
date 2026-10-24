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

    public MainViewModel(Func<string, IVatowiecApiClient>? connect = null, TimeProvider? clock = null,
        ILoginPreferencesStore? preferencesStore = null, ILoginCredentialStore? credentialStore = null)
    {
        this.connect = connect ?? VatowiecApiClient.Connect;
        this.clock = clock ?? TimeProvider.System;
        this.preferencesStore = preferencesStore ?? new MemoryLoginPreferencesStore();
        this.credentialStore = credentialStore ?? new UnavailableLoginCredentialStore();
        configuredApiAddress = Environment.GetEnvironmentVariable("VATOWIEC_API_URL");
        apiAddress = configuredApiAddress ?? "https://localhost:7243";
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public bool IsBusy => busy;
    public bool CanInteract => !busy;
    public bool IsLogin => session is null;
    public bool IsCompanySelection => session is not null && profile is null && !gusSettings;
    public bool IsWorkspace => session is not null && profile is not null;
    public string CurrentUser => session?.UserName ?? "";
    public string CurrentCompany => profile?.Name ?? "";
    public string SessionExpiry => session is null ? "" : $"Sesja do {session.ExpiresAtUtc.ToLocalTime():HH:mm}";
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
    public string ContractorStatus => !contractorsLoaded ? "Wybierz „Pokaż kontrahentów”, aby pobrać aktualne dane."
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
            if (string.IsNullOrEmpty(password) && credentialStore.Contains(target))
                password = credentialStore.Read(target) ?? "";
            if (string.IsNullOrEmpty(password)) throw new ArgumentException("Wpisz hasło. Nie znaleziono zapamiętanego poświadczenia dla tego serwera i loginu.");
            ResetSession();
            api = connect(target.ApiAddress);
            session = await api.Login(target.UserName, password);
            signedInLoginTarget = target;
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
        var id = selectedCompany.CompanyId;
        ClearCompanyData();
        var result = await api.Profile(id);
        if (result.CompanyId != id) throw new ApiException(502, "API zwróciło dane innej firmy. Odmowa wyświetlenia danych.");
        profile = result;
    }

    public Task OpenClickedCompany(CompanyResponse company)
    {
        if (busy || !IsCompanySelection || !companies.Contains(company)) return Task.CompletedTask;
        SelectedCompany = company;
        return OpenCompany();
    }

    public Task LoadContractors() => Run(() => LoadPage(0));

    private async Task LoadPage(int offset)
    {
        contractors = [];
        contractorsLoaded = false;
        ChangedAll();
        if (api is null || profile is null || !CanViewContractors)
            throw new ApiException(403, "Brak uprawnień do kartoteki kontrahentów.");
        var filter = (SearchText.Trim(), FilterCountry.Trim(), FilterActivity);
        if (filter != loadedFilter) offset = 0;
        var rows = await api.Contractors(profile.CompanyId, filter.Item1, filter.Item2,
            FilterActivity == 0 ? null : FilterActivity == 1, offset, 51);
        if (rows.Any(x => x.CompanyId != profile.CompanyId))
            throw new ApiException(502, "API zwróciło dane innej firmy. Odmowa wyświetlenia danych.");
        contractors = rows.Take(50).ToArray();
        hasNextPage = rows.Length > 50;
        pageOffset = offset;
        loadedFilter = filter;
        selectedContractor = null;
        contractorsLoaded = true;
    }

    public Task ChangeCompany() => Run(async () =>
    {
        ClearCompanyData();
        selectedCompany = null;
        companies = [];
        ChangedAll();
        if (api is not null) companies = await api.Companies();
    });

    public Task Logout()
    {
        if (busy || disposed) return Task.CompletedTask;
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
        if (busy || disposed) return;
        var wasAuthenticated = session is not null;
        CheckExpiry();
        if (wasAuthenticated && session is null) return;
        busy = true;
        message = "";
        ChangedAll();
        try { await action(); }
        catch (ApiException ex)
        {
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
            message = "Nie można połączyć się z API. Sprawdź adres HTTPS, zaufanie certyfikatu i połączenie. Spróbuj ponownie.";
        }
        finally
        {
            busy = false;
            if (disposed) ResetSession();
            CheckExpiry();
            ChangedAll();
        }
    }

    private static bool IsExpected(Exception ex) => ex is ApiException or HttpRequestException or TaskCanceledException or JsonException;
    private void ClearCompanyData()
    {
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
        if (!busy) ResetSession();
    }
}
