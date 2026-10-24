using System.Net;
using System.Net.Http.Json;
using Vatowiec.Desktop.Services;
using Vatowiec.Desktop.ViewModels;
using Vatowiec.SharedContracts;

namespace Vatowiec.Tests;

public sealed partial class DesktopTests
{
    private static readonly Guid A = Guid.NewGuid();
    private static readonly Guid B = Guid.NewGuid();

    [Theory]
    [InlineData("http://localhost:7243")]
    [InlineData("https://user:password@localhost:7243")]
    [InlineData("https://localhost:7243/api")]
    [InlineData("https://localhost:7243?companyId=1")]
    [InlineData("https://localhost:7243/#secret")]
    public void ApiAddress_RejectsUnsafeOrAmbiguousValues(string address) =>
        Assert.Throws<ArgumentException>(() => VatowiecApiClient.ValidateAddress(address));

    [Fact]
    public async Task Transport_UsesFreshCsrfForLoginAndLogout_AndRouteCompanyId()
    {
        var requests = new List<string>();
        using var client = new VatowiecApiClient(new HttpClient(new Handler(async request =>
        {
            var path = request.RequestUri!.PathAndQuery;
            requests.Add(path);
            if (path.EndsWith("/csrf", StringComparison.Ordinal))
                return Json(new CsrfResponse(requests.Count == 1 ? "anonymous" : "authenticated", "X-CSRF-TOKEN"));
            if (path.EndsWith("/login", StringComparison.Ordinal))
            {
                Assert.Equal("anonymous", request.Headers.GetValues("X-CSRF-TOKEN").Single());
                var body = await request.Content!.ReadFromJsonAsync<Dictionary<string, string>>();
                Assert.Equal("fictional", body!["userName"]);
                return Json(new SessionResponse("fictional", DateTimeOffset.UtcNow.AddMinutes(30)));
            }
            if (path.EndsWith("/logout", StringComparison.Ordinal))
            {
                Assert.Equal("authenticated", request.Headers.GetValues("X-CSRF-TOKEN").Single());
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            }
            Assert.Equal($"/api/v1/companies/{A:D}/contractors/search", path);
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("authenticated", request.Headers.GetValues("X-CSRF-TOKEN").Single());
            Assert.False(request.Headers.Contains("X-Company-Id"));
            return Json(Array.Empty<ContractorResponse>());
        }))
        { BaseAddress = new Uri("https://localhost") });
        await client.Login("fictional", "temporary-fictional-password");
        Assert.Empty(await client.Contractors(A));
        await client.Logout();
        Assert.Equal(new[] { "/api/v1/auth/csrf", "/api/v1/auth/login", "/api/v1/auth/csrf", $"/api/v1/companies/{A:D}/contractors/search",
            "/api/v1/auth/csrf", "/api/v1/auth/logout" }, requests);
    }

    [Theory]
    [InlineData(401)]
    [InlineData(403)]
    [InlineData(429)]
    [InlineData(503)]
    [InlineData(302)]
    public async Task Transport_MapsErrorsWithoutExposingServerContent(int status)
    {
        using var client = new VatowiecApiClient(new HttpClient(new Handler(_ => Task.FromResult(
            new HttpResponseMessage((HttpStatusCode)status) { Content = new StringContent("secret SQL details") })))
        { BaseAddress = new Uri("https://localhost") });
        var error = await Assert.ThrowsAsync<ApiException>(() => client.Companies());
        Assert.Equal(status, error.StatusCode);
        Assert.DoesNotContain("secret", error.Message);
    }

    [Fact]
    public async Task Transport_RejectsMalformedJson()
    {
        using var client = new VatowiecApiClient(new HttpClient(new Handler(_ => Task.FromResult(
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("broken") })))
        { BaseAddress = new Uri("https://localhost") });
        Assert.Equal(502, (await Assert.ThrowsAsync<ApiException>(() => client.Companies())).StatusCode);
    }

    [Fact]
    public async Task Workflow_LoginSwitchCompaniesAndLogout_ClearPreviousData()
    {
        var api = new FakeApi();
        using var model = new MainViewModel(_ => api) { UserName = "operator" };
        await model.Login("temporary");
        Assert.True(model.IsCompanySelection);
        Assert.Equal("operator", model.CurrentUser);
        model.SelectedCompany = model.Companies[0];
        await model.OpenCompany();
        await model.LoadContractors();
        Assert.Equal(A, Assert.Single(model.Contractors).CompanyId);
        await model.ChangeCompany();
        Assert.Empty(model.Contractors);
        Assert.Empty(model.CurrentCompany);
        Assert.Null(model.SelectedCompany);
        model.SelectedCompany = model.Companies[1];
        await model.OpenCompany();
        await model.LoadContractors();
        Assert.Equal(B, Assert.Single(model.Contractors).CompanyId);
        await model.Logout();
        Assert.True(model.IsLogin);
        Assert.Empty(model.Companies);
        Assert.Empty(model.Contractors);
        Assert.True(api.LoggedOut);
        Assert.True(api.Disposed);
    }

    [Fact]
    public async Task Reader_CannotRequestContractors()
    {
        var api = new FakeApi { Permissions = ["ViewProfile"] };
        using var model = await Open(api);
        Assert.False(model.CanViewContractors);
        await model.LoadContractors();
        Assert.Equal(0, api.ContractorCalls);
        Assert.Empty(model.Contractors);
        Assert.Contains("Brak uprawnień", model.Message);
    }

    [Theory]
    [InlineData(401)]
    [InlineData(403)]
    [InlineData(503)]
    public async Task RefreshFailure_NeverLeavesOldContractorsVisible(int status)
    {
        var api = new FakeApi();
        using var model = await Open(api);
        await model.LoadContractors();
        api.ContractorError = new ApiException(status, "odmowa");
        await model.LoadContractors();
        Assert.Empty(model.Contractors);
        Assert.Equal(status == 401, model.IsLogin);
        if (status == 403) Assert.True(model.IsCompanySelection);
    }

    [Fact]
    public async Task OfflineRefresh_ClearsDataAndAllowsRetry()
    {
        var api = new FakeApi();
        using var model = await Open(api);
        await model.LoadContractors();
        api.ContractorError = new HttpRequestException();
        await model.LoadContractors();
        Assert.Empty(model.Contractors);
        Assert.Contains("połączyć", model.Message);
        api.ContractorError = null;
        await model.LoadContractors();
        Assert.Single(model.Contractors);
    }

    [Fact]
    public async Task ResponseFromDifferentCompany_IsNeverDisplayed()
    {
        var api = new FakeApi { WrongCompany = true };
        using var model = new MainViewModel(_ => api) { UserName = "operator" };
        await model.Login("temporary");
        model.SelectedCompany = model.Companies[0];
        await model.OpenCompany();
        Assert.True(model.IsCompanySelection);
        Assert.Contains("innej firmy", model.Message);
        api.WrongCompany = false;
        await model.OpenCompany();
        api.WrongCompany = true;
        await model.LoadContractors();
        Assert.Empty(model.Contractors);
        Assert.Contains("innej firmy", model.Message);
    }

    [Fact]
    public async Task ExpiryWhileIdle_ClearsSessionAndCompanyData()
    {
        var clock = new Clock();
        var api = new FakeApi { Expires = clock.GetUtcNow().AddMinutes(1) };
        using var model = new MainViewModel(_ => api, clock) { UserName = "operator" };
        await model.Login("temporary");
        model.SelectedCompany = model.Companies[0];
        await model.OpenCompany();
        await model.LoadContractors();
        clock.Now += TimeSpan.FromMinutes(2);
        model.CheckExpiry();
        Assert.True(model.IsLogin);
        Assert.Empty(model.Contractors);
        Assert.Empty(model.Companies);
        Assert.Contains("wygasła", model.Message);
        Assert.True(api.Disposed);
    }

    [Fact]
    public async Task SlowRequest_BlocksSwitch_AndDiscardsResultIfSessionExpired()
    {
        var clock = new Clock();
        var pending = new TaskCompletionSource<ContractorResponse[]>();
        var api = new FakeApi { Expires = clock.Now.AddMinutes(1), Pending = pending.Task };
        using var model = new MainViewModel(_ => api, clock) { UserName = "operator" };
        await model.Login("temporary");
        model.SelectedCompany = model.Companies[0];
        await model.OpenCompany();
        var load = model.LoadContractors();
        Assert.True(model.IsBusy);
        await model.ChangeCompany();
        Assert.True(model.IsWorkspace);
        clock.Now += TimeSpan.FromMinutes(2);
        pending.SetResult([new(1, A, "x", "old", "PL")]);
        await load;
        Assert.True(model.IsLogin);
        Assert.Empty(model.Contractors);
    }

    [Fact]
    public async Task OfflineLogout_DiscardsLocalSessionAndExplainsServerExpiry()
    {
        var api = new FakeApi { LogoutError = true };
        using var model = await Open(api);
        await model.LoadContractors();
        await model.Logout();
        Assert.True(model.IsLogin);
        Assert.Empty(model.Contractors);
        Assert.True(api.Disposed);
        Assert.Contains("Nie potwierdzono", model.Message);
    }

    [Fact]
    public async Task CompanyListFailureAfterLogin_CanBeRetriedWithoutPassword()
    {
        var api = new FakeApi { CompanyListError = true };
        using var model = new MainViewModel(_ => api) { UserName = "operator" };
        await model.Login("temporary");
        Assert.True(model.IsCompanySelection);
        Assert.Empty(model.Companies);
        api.CompanyListError = false;
        await model.RefreshCompanies();
        Assert.Equal(2, model.Companies.Length);
    }

    private static async Task<MainViewModel> Open(FakeApi api)
    {
        var model = new MainViewModel(_ => api) { UserName = "operator" };
        await model.Login("temporary");
        model.SelectedCompany = model.Companies[0];
        await model.OpenCompany();
        return model;
    }

    private static HttpResponseMessage Json<T>(T value) => new(HttpStatusCode.OK) { Content = JsonContent.Create(value) };
    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request);
    }
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => Now;
    }
    private sealed partial class FakeApi : IVatowiecApiClient
    {
        public BankAccountValidationResponse? BankCheck { get; set; }
        public Task<BankAccountValidationResponse>? PendingBankCheck { get; set; }
        public Task<BankAccountValidationResponse> ValidateBankAccount(Guid companyId, int contractorId, string number) =>
            PendingBankCheck ?? Task.FromResult(BankCheck!);
        public List<CurrencyWriteRequest> CurrencyWrites { get; } = [];
        public int NbpImports { get; private set; }
        public CurrencyHistoryResponse[] CurrencyHistoryData { get; set; } = [];
        public CurrencyResponse[]? CurrencyData { get; set; }
        public CurrencyResponse[] CurrenciesWithRatesData { get; set; } = [];
        public int ArchiveCurrencyReads { get; private set; }
        public Task<CurrencyResponse[]> CurrenciesWithRates(Guid companyId)
        { ArchiveCurrencyReads++; return Task.FromResult(CurrenciesWithRatesData); }
        public RateResponse[] StoredRates { get; set; } = [];
        public (string? Code, string? Table, DateOnly? From, DateOnly? To, int Offset, bool Descending) LastRateQuery { get; private set; }
        public Task<CurrencyResponse[]> Currencies(Guid companyId) => Task.FromResult<CurrencyResponse[]>(CurrencyData ?? [
            new(companyId, "PLN", "złoty", true, false, false, false, "AAAAAAAAAAE="),
            new(companyId, "EUR", "euro", true, true, false, false, "AAAAAAAAAAE=")]);
        public Task<CurrencyResponse> SaveCurrency(Guid companyId, string currencyCode, CurrencyWriteRequest value)
        { CurrencyWrites.Add(value); return Task.FromResult(new CurrencyResponse(companyId, currencyCode, "euro", value.IsActive, value.DownloadA, value.DownloadB, value.DownloadC, "AAAAAAAAAAI=")); }
        public List<CurrencyActivityRequest> ActivityWrites { get; } = [];
        public Exception? ActivityError { get; set; }
        public Task<CurrencyActivityResponse> ChangeCurrencyActivity(Guid companyId, CurrencyActivityRequest value)
        {
            ActivityWrites.Add(value);
            if (ActivityError != null) throw ActivityError;
            var rows = CurrencyData!;
            var changed = rows.Where(x => value.Currencies.Any(v => v.Code == x.Code) && x.IsActive != value.IsActive && (value.IsActive || x.Code != "PLN"))
                .Select(x => x with { IsActive = value.IsActive, DownloadA = value.IsActive && x.DownloadA, DownloadB = value.IsActive && x.DownloadB, DownloadC = value.IsActive && x.DownloadC, RowVersion = "AAAAAAAAAAI=" }).ToArray();
            CurrencyData = rows.Select(x => changed.FirstOrDefault(c => c.Code == x.Code) ?? x).ToArray();
            return Task.FromResult(new CurrencyActivityResponse(companyId, value.IsActive, changed, value.Currencies.Where(x => changed.All(c => c.Code != x.Code)).Select(x => x.Code).ToArray()));
        }
        public Task<RatePage> Rates(Guid companyId, string? code, string? table, DateOnly? from, DateOnly? to, int offset, bool descending)
        {
            LastRateQuery = (code, table, from, to, offset, descending);
            return Task.FromResult(new RatePage(companyId, StoredRates.Where(x => (code == null || x.CurrencyCode == code) &&
                (table == null || x.TableType == table) && (from == null || x.EffectiveDate >= from) && (to == null || x.EffectiveDate <= to)).Skip(offset).Take(51).ToArray(), null, null, null));
        }
        public Task<RateImportResponse> ImportRates(Guid companyId, RateImportRequest value)
        { NbpImports++; return Task.FromResult(new RateImportResponse(1, companyId, 0, 0, 0, [], [], [], value.From, value.To, null, null, null)); }
        public Task<CurrencyHistoryResponse[]> CurrencyHistory(Guid companyId, int offset) => Task.FromResult(CurrencyHistoryData);
        private bool keyConfigured;
        private int keyVersion;
        public int KeySaves { get; private set; }
        public int KeyDeletes { get; private set; }
        public Task<DataPortConfigurationResponse> DataPortConfiguration(Guid companyId) => Task.FromResult(new DataPortConfigurationResponse(companyId, keyConfigured, keyVersion.ToString()));
        public Task<DataPortConfigurationResponse> SaveDataPortKey(Guid companyId, SaveDataPortKeyRequest value)
        { KeySaves++; keyVersion++; keyConfigured = true; return DataPortConfiguration(companyId); }
        public Task<DataPortConfigurationResponse> DeleteDataPortKey(Guid companyId, DeleteDataPortKeyRequest value)
        { KeyDeletes++; keyVersion++; keyConfigured = false; return DataPortConfiguration(companyId); }
        public string[] Permissions { get; set; } = ["ViewProfile", "ViewContractors"];
        public DateTimeOffset Expires { get; set; } = DateTimeOffset.UtcNow.AddMinutes(30);
        public Exception? ContractorError { get; set; }
        public bool WrongCompany { get; set; }
        public bool CompanyListError { get; set; }
        public bool LogoutError { get; set; }
        public bool LoggedOut { get; private set; }
        public bool Disposed { get; private set; }
        public int ContractorCalls { get; private set; }
        public Task<ContractorResponse[]>? Pending { get; set; }
        public Exception? SaveError { get; set; }
        public List<ContractorWriteRequest> Writes { get; } = [];
        public List<DetailWriteRequest> DetailWrites { get; } = [];
        public DetailResponse[] DetailData { get; set; } = [];
        public VerificationResponse[] VerificationData { get; set; } = [];
        public VerificationResponse? VerificationResult { get; set; }
        public Task<VerificationResponse[]> Verifications(Guid companyId, int id, int offset = 0) => Task.FromResult(VerificationData);
        public Task<VerificationResponse> Verify(Guid companyId, int id, VerificationRequest value)
        {
            var result = VerificationResult ?? throw new InvalidOperationException();
            VerificationData = [result, .. VerificationData]; return Task.FromResult(result);
        }
        public Task<DetailResponse[]> Details(Guid companyId, int contractorId, string kind, int offset = 0, int limit = 50) => Task.FromResult(DetailData);
        public Task<DetailResponse> Detail(Guid companyId, int contractorId, string kind, int id) => Task.FromResult(DetailData.Single(x => x.Id == id));
        public Task<DetailResponse> SaveDetail(Guid companyId, int contractorId, string kind, DetailWriteRequest value, bool isNew)
        {
            DetailWrites.Add(value);
            if (SaveError is not null) throw SaveError;
            return Task.FromResult(new DetailResponse(value.Id == 0 ? 1 : value.Id, companyId, contractorId, kind, value.Data, "AAAAAAAAAAE="));
        }
        public (string? Search, string? Country, bool? Active, int Offset, int Limit) LastQuery { get; private set; }
        public Task<SessionResponse> Login(string userName, string password) => Task.FromResult(new SessionResponse(userName, Expires, InstallationAdmin));
        public Task<CompanyResponse[]> Companies() => CompanyListError ? throw new HttpRequestException() : Task.FromResult<CompanyResponse[]>(
            [new(A, "Firma A", Permissions), new(B, "Firma B", Permissions)]);
        public List<Guid> ProfileCalls { get; } = [];
        public Task<CompanyProfileResponse>? PendingProfile { get; set; }
        public Exception? ProfileError { get; set; }
        public Task<CompanyProfileResponse> Profile(Guid id)
        { ProfileCalls.Add(id); if (ProfileError != null) throw ProfileError; return PendingProfile ?? Task.FromResult(new CompanyProfileResponse(WrongCompany ? B : id, $"Firma {id}", null, null)); }
        public Task<ContractorResponse> Contractor(Guid companyId, int id) => Task.FromResult(new ContractorResponse(id, companyId, "DEMO", "Kontrahent", "PL", RowVersion: "AAAAAAAAAAE="));
        public Task<ContractorResponse> SaveContractor(Guid companyId, ContractorWriteRequest value, bool isNew)
        {
            Writes.Add(value);
            if (SaveError is not null) throw SaveError;
            return Task.FromResult(new ContractorResponse(value.Id, companyId, value.Code, value.Name, value.CountryCode, null, value.IsActive, "AAAAAAAAAAI="));
        }
        public Task<ContractorResponse[]> Contractors(Guid id, string? search = null, string? country = null, bool? active = null, int offset = 0, int limit = 50)
        {
            ContractorCalls++;
            LastQuery = (search, country, active, offset, limit);
            if (ContractorError is not null) return Task.FromException<ContractorResponse[]>(ContractorError);
            return Pending ?? Task.FromResult<ContractorResponse[]>([new(1, WrongCompany ? B : id, "DEMO", "Kontrahent", "PL")]);
        }
        public Task Logout()
        {
            if (LogoutError) throw new HttpRequestException();
            LoggedOut = true;
            return Task.CompletedTask;
        }
        public void Dispose() => Disposed = true;
    }
}
