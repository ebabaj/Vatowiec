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
    Task<ContractorResponse[]> Contractors(Guid companyId);
    Task Logout();
}

public sealed class ApiException(int statusCode, string message) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
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
    public Task<CompanyProfileResponse> Profile(Guid companyId) => Get<CompanyProfileResponse>($"api/v1/companies/{companyId:D}/profile");
    public Task<ContractorResponse[]> Contractors(Guid companyId) => Get<ContractorResponse[]>($"api/v1/companies/{companyId:D}/contractors");

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

    private static Task EnsureSuccess(HttpResponseMessage response, bool login)
    {
        if (response.IsSuccessStatusCode) return Task.CompletedTask;
        throw new ApiException((int)response.StatusCode, response.StatusCode switch
        {
            HttpStatusCode.Unauthorized when login => "Nieprawidłowy login lub hasło albo konto jest zablokowane.",
            HttpStatusCode.Unauthorized => "Sesja wygasła lub została unieważniona. Zaloguj się ponownie.",
            HttpStatusCode.Forbidden => "Brak uprawnień do firmy lub kartoteki kontrahentów.",
            HttpStatusCode.BadRequest => "API odrzuciło żądanie. Spróbuj ponownie; sprawdź wersję aplikacji i API.",
            HttpStatusCode.TooManyRequests => "Zbyt wiele prób logowania. Odczekaj minutę i spróbuj ponownie.",
            HttpStatusCode.ServiceUnavailable => "Baza firmy jest niedostępna lub niezgodna. Spróbuj później lub skontaktuj się z administratorem.",
            _ => "API nie może wykonać operacji. Spróbuj ponownie."
        });
    }

    public void Dispose() => http.Dispose();
}
