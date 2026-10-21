using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Configuration;

namespace Vatowiec.DatabaseTools;

internal static class DemoAuthenticationProbe
{
    public static async Task Run(string address)
    {
        var uri = new Uri(address);
        if (uri.Scheme != "https" || uri.Host != "localhost" || uri.UserInfo.Length != 0)
            throw new InvalidOperationException("Próba demonstracyjna wysyła dane logowania tylko przez HTTPS do localhost.");
        var config = new ConfigurationBuilder().AddUserSecrets("vatowiec-api-development").AddEnvironmentVariables().Build();
        using var op = new HttpClient(new HttpClientHandler { CookieContainer = new CookieContainer(), AllowAutoRedirect = false }) { BaseAddress = uri };
        using var reader = new HttpClient(new HttpClientHandler { CookieContainer = new CookieContainer(), AllowAutoRedirect = false }) { BaseAddress = uri };
        const string a = "aaaaaaaa-0000-4000-8000-000000000001";
        const string b = "bbbbbbbb-0000-4000-8000-000000000002";
        foreach (var (client, kind) in new[] { (op, "Operator"), (reader, "Reader") })
        {
            var name = config[$"DemoUsers:{kind}:UserName"];
            var password = config[$"DemoUsers:{kind}:Password"];
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(password))
                throw new InvalidOperationException($"Brak lokalnych danych konta {kind}.");
            await Csrf(client);
            using var login = await client.PostAsJsonAsync("/api/v1/auth/login", new { userName = name, password });
            Check(login, 200, $"{name} logowanie");
            using var list = await client.GetAsync("/api/v1/companies");
            Check(list, 200, $"{name} lista firm");
            if ((await list.Content.ReadFromJsonAsync<JsonElement>()).GetArrayLength() != (kind == "Operator" ? 2 : 1))
                throw new InvalidOperationException("Niezgodna lista dostępnych firm.");
            using var profileA = await client.GetAsync($"/api/v1/companies/{a}/profile");
            Check(profileA, 200, $"{name} profil A");
            using var profileB = await client.GetAsync($"/api/v1/companies/{b}/profile");
            Check(profileB, kind == "Operator" ? 200 : 403, $"{name} profil B");
            using var contractors = await client.GetAsync($"/api/v1/companies/{a}/contractors");
            Check(contractors, kind == "Operator" ? 200 : 403, $"{name} kontrahenci A");
            using var manipulation = await client.GetAsync($"/api/v1/companies/{a}/profile?companyId={b}");
            Check(manipulation, 400, $"{name} manipulacja CompanyId");
        }
        await Csrf(op);
        using var logout = await op.PostAsync("/api/v1/auth/logout", null);
        Check(logout, 204, "Wylogowanie operatora");
        using var rejected = await op.GetAsync("/api/v1/companies");
        Check(rejected, 401, "Operator po wylogowaniu");
        using var stillValid = await reader.GetAsync($"/api/v1/companies/{a}/profile");
        Check(stillValid, 200, "Czytelnik nadal w firmie A");
        await Csrf(reader);
        using var readerLogout = await reader.PostAsync("/api/v1/auth/logout", null);
        Check(readerLogout, 204, "Wylogowanie czytelnika");
        Console.WriteLine("Próba dwóch użytkowników zakończona poprawnie; hasła, tokeny i cookie nie zostały wypisane.");
    }

    private static async Task Csrf(HttpClient client)
    {
        using var response = await client.GetAsync("/api/v1/auth/csrf");
        response.EnsureSuccessStatusCode();
        var token = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString();
        client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", token);
    }

    private static void Check(HttpResponseMessage response, int expected, string operation)
    {
        if ((int)response.StatusCode != expected)
            throw new InvalidOperationException($"{operation}: HTTP {(int)response.StatusCode}, oczekiwano {expected}.");
        Console.WriteLine($"{operation}: HTTP {expected}.");
    }
}
