using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Vatowiec.SharedContracts;

namespace Vatowiec.Desktop.Services;

public interface IVatowiecApiClient : IDisposable
{
    Task<SessionResponse> Login(string userName, string password);
    Task<CompanyResponse[]> Companies();
    Task<CompanyProfileResponse> Profile(Guid companyId);
    Task<ContractorResponse[]> Contractors(Guid companyId, string? search = null, string? country = null, bool? active = null, int offset = 0, int limit = 50);
    Task<ContractorResponse> Contractor(Guid companyId, int id);
    Task<ContractorResponse> SaveContractor(Guid companyId, ContractorWriteRequest value, bool isNew);
    Task<IdentifierPreview> ValidateIdentifiers(Guid companyId, ContractorWriteRequest value) => Task.FromResult(new IdentifierPreview([], [], "", false));
    Task<RegistryEnvironmentResponse> RegistryEnvironments(Guid companyId) => Task.FromResult(new RegistryEnvironmentResponse("Test", "Test", "Test"));
    Task<VerificationResponse[]> Verifications(Guid companyId, int id, int offset = 0) => Task.FromResult<VerificationResponse[]>([]);
    Task<VerificationResponse> Verify(Guid companyId, int id, VerificationRequest value) => throw new NotSupportedException();
    Task<DetailResponse[]> Details(Guid companyId, int contractorId, string kind, int offset = 0, int limit = 50) => Task.FromResult<DetailResponse[]>([]);
    Task<DetailResponse> Detail(Guid companyId, int contractorId, string kind, int id) => throw new NotSupportedException();
    Task<DetailResponse> SaveDetail(Guid companyId, int contractorId, string kind, DetailWriteRequest value, bool isNew) => throw new NotSupportedException();
    Task<BankAccountValidationResponse> ValidateBankAccount(Guid companyId, int contractorId, string number) => throw new NotSupportedException();
    Task<AddressTypeResponse[]> AddressTypes(Guid companyId) => Task.FromResult<AddressTypeResponse[]>([]);
    Task<AddressTypeResponse> AddAddressType(Guid companyId, AddressTypeWriteRequest value) => throw new NotSupportedException();
    Task<ContractorResponse[]> ParentCandidates(Guid companyId, string search, int excludeId, int offset = 0) => Task.FromResult<ContractorResponse[]>([]);
    Task<ContractorResponse[]> Children(Guid companyId, int parentId, int offset = 0) => Task.FromResult<ContractorResponse[]>([]);
    Task Logout();
    Task<GusConfigurationResponse> GusConfiguration() => throw new NotSupportedException();
    Task<RegistryConfigurationResponse[]> RegistryConfigurations() => Task.FromResult<RegistryConfigurationResponse[]>([]);
    Task<RegistryConfigurationResponse> SaveRegistryConfiguration(string source, RegistryConfigurationRequest value) => throw new NotSupportedException();
    Task<GusConfigurationResponse> SaveGusKey(SaveGusKeyRequest value) => throw new NotSupportedException();
    Task<GusConfigurationResponse> DeleteGusKey(DeleteGusKeyRequest value) => throw new NotSupportedException();
    Task<CurrencyResponse[]> Currencies(Guid companyId) => throw new NotSupportedException();
    Task<CurrencyResponse> SaveCurrency(Guid companyId, string currencyCode, CurrencyWriteRequest value) => throw new NotSupportedException();
    Task<RatePage> Rates(Guid companyId, string? code, string? table, DateOnly? from, DateOnly? to, int offset, bool descending) => throw new NotSupportedException();
    Task<RateImportResponse> ImportRates(Guid companyId, RateImportRequest value) => throw new NotSupportedException();
    Task<CurrencyHistoryResponse> ResolveRate(Guid companyId, long auditId, RateResolutionRequest value) => throw new NotSupportedException();
    Task<CurrencyHistoryResponse[]> CurrencyHistory(Guid companyId, int offset) => throw new NotSupportedException();
    Task<DataPortConfigurationResponse> DataPortConfiguration(Guid companyId) => throw new NotSupportedException();
    Task<DataPortConfigurationResponse> SaveDataPortKey(Guid companyId, SaveDataPortKeyRequest value) => throw new NotSupportedException();
    Task<DataPortConfigurationResponse> DeleteDataPortKey(Guid companyId, DeleteDataPortKeyRequest value) => throw new NotSupportedException();
}

public sealed class ApiException(int statusCode, string message) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
    public string? Code { get; init; }
    public ContractorResponse? Current { get; init; }
    public DetailResponse? DetailCurrent { get; init; }
    public string? WarningHash { get; init; }
    public BankAccountValidationResponse? BankValidation { get; init; }
    public Dictionary<string, string[]>? Errors { get; init; }
}

public sealed class VatowiecApiClient : IVatowiecApiClient
{
    private readonly HttpClient http;

    public static Uri ValidateAddress(string address)
    {
        if (!Uri.TryCreate(address, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps ||
            string.IsNullOrEmpty(uri.Host) || uri.UserInfo.Length != 0 || uri.Query.Length != 0 ||
            uri.Fragment.Length != 0 || uri.AbsolutePath != "/")
            throw new ArgumentException("Podaj adres HTTPS API, np. https://localhost:7243 (bez ścieżki).");
        return uri;
    }

    public static VatowiecApiClient Connect(string address)
    {
        var uri = ValidateAddress(address);
        // Standardowa walidacja certyfikatu HTTPS. Cookie tylko w pamięci procesu.
        var handler = new HttpClientHandler
        {
            CookieContainer = new CookieContainer(),
            UseCookies = true,
            AllowAutoRedirect = false
        };
        return new VatowiecApiClient(new HttpClient(handler)
        {
            BaseAddress = uri,
            Timeout = TimeSpan.FromSeconds(20)
        });
    }

    // HttpClient jest własnością adaptera; w testach może wskazywać TestServer.
    public VatowiecApiClient(HttpClient http)
    {
        ValidateAddress(http.BaseAddress?.AbsoluteUri ?? "");
        this.http = http;
    }

    public async Task<SessionResponse> Login(string userName, string password)
    {
        var csrf = await Get<CsrfResponse>("api/v1/auth/csrf");
        using var request = new HttpRequestMessage(HttpMethod.Post, "api/v1/auth/login");
        request.Headers.Add("X-CSRF-TOKEN", csrf.Token);
        request.Content = JsonContent.Create(new { userName, password });
        return await Send<SessionResponse>(request, login: true);
    }

    public Task<CompanyResponse[]> Companies() => Get<CompanyResponse[]>("api/v1/companies");
    public Task<GusConfigurationResponse> GusConfiguration() => Get<GusConfigurationResponse>("api/v1/installation/integrations/gus");
    public Task<RegistryConfigurationResponse[]> RegistryConfigurations() => Get<RegistryConfigurationResponse[]>("api/v1/installation/integrations/registries");
    public Task<RegistryConfigurationResponse> SaveRegistryConfiguration(string source, RegistryConfigurationRequest value) =>
        Write<RegistryConfigurationResponse>($"api/v1/installation/integrations/registries/{Uri.EscapeDataString(source)}", value, true);
    public Task<GusConfigurationResponse> SaveGusKey(SaveGusKeyRequest value) => GusWrite(value, HttpMethod.Put);
    public Task<GusConfigurationResponse> DeleteGusKey(DeleteGusKeyRequest value) => GusWrite(value, HttpMethod.Delete);
    private async Task<GusConfigurationResponse> GusWrite(object value, HttpMethod method)
    {
        var csrf = await Get<CsrfResponse>("api/v1/auth/csrf");
        using var request = new HttpRequestMessage(method, "api/v1/installation/integrations/gus") { Content = JsonContent.Create(value) };
        request.Headers.Add("X-CSRF-TOKEN", csrf.Token);
        return await Send<GusConfigurationResponse>(request);
    }
    public Task<CurrencyResponse[]> Currencies(Guid companyId) => Get<CurrencyResponse[]>($"api/v1/companies/{companyId}/currencies");
    public Task<CurrencyResponse> SaveCurrency(Guid companyId, string currencyCode, CurrencyWriteRequest value) => Write<CurrencyResponse>($"api/v1/companies/{companyId}/currencies/{Uri.EscapeDataString(currencyCode)}", value, true);
    public Task<RatePage> Rates(Guid companyId, string? code, string? table, DateOnly? from, DateOnly? to, int offset, bool descending)
    {
        var query = $"offset={offset}&limit=51&descending={descending.ToString().ToLowerInvariant()}";
        if (code != null) query += "&code=" + Uri.EscapeDataString(code);
        if (table != null) query += "&table=" + Uri.EscapeDataString(table);
        if (from != null) query += "&from=" + from.Value.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        if (to != null) query += "&to=" + to.Value.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        return Get<RatePage>($"api/v1/companies/{companyId}/exchange-rates?{query}");
    }
    public Task<RateImportResponse> ImportRates(Guid companyId, RateImportRequest value) => Post<RateImportResponse>($"api/v1/companies/{companyId}/exchange-rates/imports", value);
    public Task<CurrencyHistoryResponse> ResolveRate(Guid companyId, long auditId, RateResolutionRequest value) => Post<CurrencyHistoryResponse>($"api/v1/companies/{companyId}/exchange-rates/imports/{auditId}/resolve", value);
    public Task<CurrencyHistoryResponse[]> CurrencyHistory(Guid companyId, int offset) => Get<CurrencyHistoryResponse[]>($"api/v1/companies/{companyId}/exchange-rates/history?offset={offset}");
    public Task<DataPortConfigurationResponse> DataPortConfiguration(Guid companyId) => Get<DataPortConfigurationResponse>($"api/v1/companies/{companyId}/integrations/dataport");
    public Task<DataPortConfigurationResponse> SaveDataPortKey(Guid companyId, SaveDataPortKeyRequest value) => DataPortWrite(companyId, value, HttpMethod.Put);
    public Task<DataPortConfigurationResponse> DeleteDataPortKey(Guid companyId, DeleteDataPortKeyRequest value) => DataPortWrite(companyId, value, HttpMethod.Delete);
    private async Task<DataPortConfigurationResponse> DataPortWrite(Guid companyId, object value, HttpMethod method)
    {
        var csrf = await Get<CsrfResponse>("api/v1/auth/csrf");
        using var request = new HttpRequestMessage(method, $"api/v1/companies/{companyId}/integrations/dataport") { Content = JsonContent.Create(value) };
        request.Headers.Add("X-CSRF-TOKEN", csrf.Token);
        return await Send<DataPortConfigurationResponse>(request);
    }
    public Task<IdentifierPreview> ValidateIdentifiers(Guid companyId, ContractorWriteRequest value) => Post<IdentifierPreview>($"api/v1/companies/{companyId}/contractors/identifiers/validate", value);
    public Task<RegistryEnvironmentResponse> RegistryEnvironments(Guid companyId) => Get<RegistryEnvironmentResponse>($"api/v1/companies/{companyId}/registry-environments");
    public Task<VerificationResponse[]> Verifications(Guid companyId, int id, int offset = 0) => Get<VerificationResponse[]>($"api/v1/companies/{companyId}/contractors/{id}/verifications?offset={offset}&limit=51");
    public Task<VerificationResponse> Verify(Guid companyId, int id, VerificationRequest value) => Post<VerificationResponse>($"api/v1/companies/{companyId}/contractors/{id}/verifications", value);
    private async Task<T> Post<T>(string path, object value)
    {
        var csrf = await Get<CsrfResponse>("api/v1/auth/csrf");
        using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(value) };
        request.Headers.Add("X-CSRF-TOKEN", csrf.Token);
        return await Send<T>(request);
    }
    public Task<CompanyProfileResponse> Profile(Guid companyId) => Get<CompanyProfileResponse>($"api/v1/companies/{companyId:D}/profile");
    public Task<ContractorResponse[]> Contractors(Guid companyId, string? search = null, string? country = null, bool? active = null, int offset = 0, int limit = 50)
        => Post<ContractorResponse[]>($"api/v1/companies/{companyId:D}/contractors/search",
            new ContractorSearchRequest(search?.Trim(), country?.Trim(), active, Offset: offset, Limit: limit));

    public Task<ContractorResponse> Contractor(Guid companyId, int id) => Get<ContractorResponse>($"api/v1/companies/{companyId:D}/contractors/{id:D}");

    public async Task<ContractorResponse> SaveContractor(Guid companyId, ContractorWriteRequest value, bool isNew)
    {
        var csrf = await Get<CsrfResponse>("api/v1/auth/csrf");
        using var request = new HttpRequestMessage(isNew ? HttpMethod.Post : HttpMethod.Put,
            $"api/v1/companies/{companyId:D}/contractors" + (isNew ? "" : $"/{value.Id:D}"));
        request.Headers.Add("X-CSRF-TOKEN", csrf.Token);
        request.Content = JsonContent.Create(value);
        return await Send<ContractorResponse>(request);
    }

    public Task<DetailResponse[]> Details(Guid companyId, int contractorId, string kind, int offset = 0, int limit = 50) =>
        Get<DetailResponse[]>($"api/v1/companies/{companyId}/contractors/{contractorId}/{kind}?offset={offset}&limit={limit}");
    public Task<DetailResponse> Detail(Guid companyId, int contractorId, string kind, int id) =>
        Get<DetailResponse>($"api/v1/companies/{companyId}/contractors/{contractorId}/{kind}/{id}");
    public Task<DetailResponse> SaveDetail(Guid companyId, int contractorId, string kind, DetailWriteRequest value, bool isNew) =>
        Write<DetailResponse>($"api/v1/companies/{companyId}/contractors/{contractorId}/{kind}" + (isNew ? "" : $"/{value.Id}"), value, !isNew);
    public Task<BankAccountValidationResponse> ValidateBankAccount(Guid companyId, int contractorId, string number) =>
        Write<BankAccountValidationResponse>($"api/v1/companies/{companyId}/contractors/{contractorId}/bank-accounts/validate", new BankAccountValidationRequest(number), false);
    public Task<AddressTypeResponse[]> AddressTypes(Guid companyId) => Get<AddressTypeResponse[]>($"api/v1/companies/{companyId}/address-types");
    public Task<AddressTypeResponse> AddAddressType(Guid companyId, AddressTypeWriteRequest value) => Write<AddressTypeResponse>($"api/v1/companies/{companyId}/address-types", value, false);
    public Task<ContractorResponse[]> ParentCandidates(Guid companyId, string search, int excludeId, int offset = 0) =>
        Post<ContractorResponse[]>($"api/v1/companies/{companyId}/contractors/search", new ContractorSearchRequest(search, ExcludeId: excludeId, Offset: offset, Limit: 51));
    public Task<ContractorResponse[]> Children(Guid companyId, int parentId, int offset = 0) =>
        Get<ContractorResponse[]>($"api/v1/companies/{companyId}/contractors?parentId={parentId}&limit=51&offset={offset}");
    private async Task<T> Write<T>(string path, object value, bool update)
    {
        var csrf = await Get<CsrfResponse>("api/v1/auth/csrf");
        using var request = new HttpRequestMessage(update ? HttpMethod.Put : HttpMethod.Post, path);
        request.Headers.Add("X-CSRF-TOKEN", csrf.Token);
        request.Content = JsonContent.Create(value);
        return await Send<T>(request);
    }

    public async Task Logout()
    {
        // Token anonimowy sprzed login nie jest tokenem uwierzytelnionego użytkownika.
        var csrf = await Get<CsrfResponse>("api/v1/auth/csrf");
        using var request = new HttpRequestMessage(HttpMethod.Post, "api/v1/auth/logout");
        request.Headers.Add("X-CSRF-TOKEN", csrf.Token);
        using var response = await http.SendAsync(request);
        await EnsureSuccess(response, false);
    }

    private async Task<T> Get<T>(string path)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        return await Send<T>(request);
    }

    private async Task<T> Send<T>(HttpRequestMessage request, bool login = false)
    {
        using var response = await http.SendAsync(request);
        await EnsureSuccess(response, login);
        try
        {
            return await response.Content.ReadFromJsonAsync<T>() ?? throw new JsonException();
        }
        catch (JsonException)
        {
            throw new ApiException(502, "API zwróciło nieprawidłową odpowiedź. Spróbuj ponownie.");
        }
    }

    private static async Task EnsureSuccess(HttpResponseMessage response, bool login)
    {
        if (response.IsSuccessStatusCode) return;
        ApiError? error = null;
        try { error = await response.Content.ReadFromJsonAsync<ApiError>(); }
        catch (JsonException) { }
        var message = response.StatusCode switch
        {
            _ when error?.Code?.StartsWith("currency_", StringComparison.Ordinal) == true => error.Message ?? "Sprawdź ustawienia, daty i aktualną wersję danych walutowych.",
            HttpStatusCode.Conflict when error?.Code == "configuration_stale" => error.Message ?? "Ustawienia zmieniły się. Odśwież stan i ponownie sprawdź wartości.",
            HttpStatusCode.BadRequest when error?.Code is "dataport_key_invalid" or "confirmation_required" => error.Message ?? "Sprawdź konfigurację DataPort.",
            HttpStatusCode.BadRequest when error?.Code is "verification_unsupported" or "verification_invalid" or "verification_date" => error.Message ?? "Sprawdź dane wejściowe weryfikacji.",
            HttpStatusCode.Conflict when error?.Code == "verification_stale" => "Dane zmieniły się. Otwórz aktualną wersję przed sprawdzeniem rejestru.",
            HttpStatusCode.ServiceUnavailable when error?.Code == "verification_history_failed" => "Nie zapisano historii odpowiedzi. Wynik nie został potwierdzony w VATowcu.",
            HttpStatusCode.ServiceUnavailable when error?.Code == "key_protection_unavailable" => "Serwer nie może trwale zabezpieczyć klucza. Sprawdź konfigurację kryptograficzną serwera.",
            HttpStatusCode.BadRequest when error?.Code == "production_confirmation_required" => error.Message ?? "Potwierdź świadome włączenie PRODUKCJI.",
            HttpStatusCode.ServiceUnavailable when error?.Code is "configuration_audit_failed" or "registry_configuration_missing" => error.Message ?? "Nie zapisano konfiguracji rejestru.",
            HttpStatusCode.Unauthorized when login => "Nieprawidłowy login lub hasło albo konto jest zablokowane.",
            HttpStatusCode.Unauthorized => "Sesja wygasła lub została unieważniona. Zaloguj się ponownie.",
            HttpStatusCode.Forbidden when error?.Code == "pesel_permission_denied" => "Brak uprawnienia do odczytu i edycji PESEL w tej firmie.",
            HttpStatusCode.Forbidden => "Brak uprawnień do firmy lub kartoteki kontrahentów.",
            HttpStatusCode.BadRequest when error?.Code is "hierarchy_cycle" or "invalid_parent" or "invalid_address_type" => error.Message ?? "Sprawdź wybrane powiązanie.",
            HttpStatusCode.BadRequest => "API odrzuciło żądanie. Spróbuj ponownie; sprawdź wersję aplikacji i API.",
            HttpStatusCode.TooManyRequests when login => "Zbyt wiele prób logowania. Odczekaj minutę i spróbuj ponownie.",
            HttpStatusCode.TooManyRequests => "Zbyt wiele żądań do API. Odczekaj minutę przed kolejną próbą; to ograniczenie API VATowca, a nie saldo konta DataPort.",
            HttpStatusCode.ServiceUnavailable => "Baza firmy jest niedostępna lub niezgodna. Spróbuj później lub skontaktuj się z administratorem.",
            HttpStatusCode.NotFound when error?.Code == "detail_not_found" => "Nie znaleziono pozycji u tego kontrahenta. Odśwież listę.",
            HttpStatusCode.NotFound => "Nie znaleziono kontrahenta w tej firmie. Odśwież listę.",
            HttpStatusCode.Conflict when error?.Code == "contractor_conflict" => "Inna osoba zmieniła kontrahenta. Porównaj wersje i wybierz sposób rozwiązania konfliktu.",
            HttpStatusCode.Conflict when error?.Code == "contractor_code_exists" => "Ten kod jest już zajęty w tej firmie. Wpisz inny kod.",
            HttpStatusCode.Conflict when error?.Code == "operation_payload_mismatch" => "Ta operacja została już zapisana z innymi danymi. Anuluj formularz, wyszukaj kontrahenta i otwórz jego edycję.",
            HttpStatusCode.UnprocessableEntity when error?.Code == "bank_warning" => error.Message ?? "Numer wymaga świadomego potwierdzenia ostrzeżenia.",
            HttpStatusCode.Conflict when error?.Code == "detail_conflict" => "Inna osoba zmieniła pozycję. Porównaj wersje i świadomie rozstrzygnij konflikt.",
            HttpStatusCode.Conflict when error?.Code == "address_type_exists" => "Taki rodzaj adresu już istnieje. Wybierz go z listy.",
            HttpStatusCode.Conflict when error?.Code == "detail_default_conflict" => "Inna osoba zmieniła preferencję. Odśwież pozycję przed ponownym zapisem.",
            HttpStatusCode.Conflict => "Pozycja już istnieje. Odśwież listę przed ponowieniem.",
            _ => "API nie może wykonać operacji. Spróbuj ponownie."
        };
        if (response.StatusCode == HttpStatusCode.BadRequest && error?.Code == "validation_failed" && error.Errors is not null)
            message = string.Join(Environment.NewLine, error.Errors.Values.SelectMany(x => x));
        throw new ApiException((int)response.StatusCode, message)
        { Code = error?.Code, Current = error?.Current, DetailCurrent = error?.DetailCurrent, WarningHash = error?.WarningHash, BankValidation = error?.BankValidation, Errors = error?.Errors };
    }

    private sealed record ApiError(string? Code, Dictionary<string, string[]>? Errors, ContractorResponse? Current,
        DetailResponse? DetailCurrent, string? WarningHash, string? Message, BankAccountValidationResponse? BankValidation);

    public void Dispose() => http.Dispose();
}
