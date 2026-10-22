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
            Assert.Equal($"/api/v1/companies/{A:D}/contractors", path);
            Assert.False(request.Headers.Contains("X-Company-Id"));
            return Json(Array.Empty<ContractorResponse>());
        }))
        { BaseAddress = new Uri("https://localhost") });
        await client.Login("fictional", "temporary-fictional-password");
        Assert.Empty(await client.Contractors(A));
        await client.Logout();
        Assert.Equal(new[] { "/api/v1/auth/csrf", "/api/v1/auth/login", $"/api/v1/companies/{A:D}/contractors",
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
        pending.SetResult([new(Guid.NewGuid(), A, "x", "old", "PL")]);
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
    private sealed class FakeApi : IVatowiecApiClient
    {
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
        public (string? Search, string? Country, bool? Active, int Offset, int Limit) LastQuery { get; private set; }
        public Task<SessionResponse> Login(string userName, string password) => Task.FromResult(new SessionResponse(userName, Expires));
        public Task<CompanyResponse[]> Companies() => CompanyListError ? throw new HttpRequestException() : Task.FromResult<CompanyResponse[]>(
            [new(A, "Firma A", Permissions), new(B, "Firma B", Permissions)]);
        public Task<CompanyProfileResponse> Profile(Guid id) => Task.FromResult(new CompanyProfileResponse(WrongCompany ? B : id, $"Firma {id}", null, null));
        public Task<ContractorResponse> Contractor(Guid companyId, Guid id) => Task.FromResult(new ContractorResponse(id, companyId, "DEMO", "Kontrahent", "PL", RowVersion: "AAAAAAAAAAE="));
        public Task<ContractorResponse> SaveContractor(Guid companyId, ContractorWriteRequest value, bool isNew)
        {
            Writes.Add(value);
            if (SaveError is not null) throw SaveError;
            return Task.FromResult(new ContractorResponse(value.Id, companyId, value.Code, value.Name, value.CountryCode, value.TaxIdentifier, value.IsActive, "AAAAAAAAAAI="));
        }
        public Task<ContractorResponse[]> Contractors(Guid id, string? search = null, string? country = null, bool? active = null, int offset = 0, int limit = 50)
        {
            ContractorCalls++;
            LastQuery = (search, country, active, offset, limit);
            if (ContractorError is not null) return Task.FromException<ContractorResponse[]>(ContractorError);
            return Pending ?? Task.FromResult<ContractorResponse[]>([new(Guid.NewGuid(), WrongCompany ? B : id, "DEMO", "Kontrahent", "PL")]);
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
