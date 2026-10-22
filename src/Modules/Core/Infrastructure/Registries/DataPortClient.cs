using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using Vatowiec.Core.Application;
using Vatowiec.SharedContracts;

namespace Vatowiec.Core.Infrastructure.Registries;

public interface IDataPortClient
{
    Task<RegistryReply> Lookup(string nip, string apiKey, CancellationToken cancellationToken);
}

public sealed class DataPortClient(IHttpClientFactory clients) : IDataPortClient
{
    private const string Adapter = "dataport-rest-full-v1";
    private static RegistryReply Fail(string status, string code, string message, bool executed = true) =>
        new("Production", "Unknown", status, code, message, null, new(), Adapter, executed);

    public async Task<RegistryReply> Lookup(string nip, string apiKey, CancellationToken cancellationToken)
    {
        if (!new PolishNipChecksum().IsValid(nip)) return Fail("Error", "invalid_nip", "Wymagany poprawny polski NIP. Nie wykonano zapytania.", false);
        if (string.IsNullOrWhiteSpace(apiKey) || apiKey.Any(c => c < 33 || c > 126))
            return Fail("NotConfigured", "dataport_key_missing", "Wprowadź klucz w ustawieniach firmy → Integracje zewnętrzne → DataPort.", false);
        try
        {
            using var http = clients.CreateClient("dataport");
            using var request = new HttpRequestMessage(HttpMethod.Get, "https://dataport.pl/api/v1/company/" + nip + "?format=full");
            request.Headers.Add("X-API-Key", apiKey);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(12));
            using var response = await http.SendAsync(request, timeout.Token);
            var status = (int)response.StatusCode;
            if (status == 401) return InvalidKey();
            if (status == 429) return Limit();
            if (status == 404) return new("Production", "Negative", "Completed", "not_found", "DataPort nie znalazł firmy. Nie jest to bezpośrednie potwierdzenie GUS.", null, new(), Adapter);
            if (status == 400) return Fail("Error", "bad_request", "DataPort odrzucił żądanie (400).");
            if (!response.IsSuccessStatusCode) return Fail("Unavailable", "dataport_http_" + status, "DataPort nie udzielił odpowiedzi merytorycznej. Nie jest to wynik negatywny.");
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(timeout.Token));
            var json = document.RootElement;
            if (json.TryGetProperty("error", out var error))
            {
                if (error.GetString() == "INVALID_API_KEY") return InvalidKey();
                if (error.GetString() == "RATE_LIMIT_EXCEEDED") return Limit();
            }
            string? Text(string field, int max = 256)
            {
                if (!json.TryGetProperty(field, out var value) || value.ValueKind == JsonValueKind.Null) return null;
                var text = value.GetString();
                if (text?.Length > max || text?.Contains(apiKey, StringComparison.Ordinal) == true) throw new InvalidDataException();
                return text;
            }
            if (!json.GetProperty("success").GetBoolean() || Text("nip") != nip || string.IsNullOrWhiteSpace(Text("nazwa"))) throw new InvalidDataException();
            var end = Text("data_zakonczenia_dzialalnosci");
            DateOnly? date = string.IsNullOrWhiteSpace(end) ? null : DateOnly.ParseExact(end, "yyyy-MM-dd", CultureInfo.InvariantCulture);
            return new("Production", "Information", "Completed", null,
                "DataPort (dane GUS) — rzeczywista usługa pośrednika; dane mogą pochodzić z cache. To nie jest bezpośrednia odpowiedź GUS ani urzędowe potwierdzenie.", null,
                new(Name: Text("nazwa"), Regon: Text("regon"), Street: Text("ulica", 200), BuildingNo: Text("numer_budynku", 20),
                    ApartmentNo: Text("numer_lokalu", 20), PostalCode: Text("kod_pocztowy", 20), City: Text("miasto", 150), CountryCode: "PL", ActivityEndDate: date), Adapter);
        }
        catch (OperationCanceledException) { return Fail("Timeout", "timeout", "DataPort nie odpowiedział w limicie czasu. Nie jest to wynik negatywny."); }
        catch (HttpRequestException) { return Fail("Unavailable", "network", "Nie można połączyć się z DataPort. Nie jest to wynik negatywny."); }
        catch (Exception e) when (e is JsonException or InvalidDataException or InvalidOperationException or KeyNotFoundException or FormatException)
        { return Fail("Error", "invalid_response", "Nie można bezpiecznie odczytać odpowiedzi DataPort. Nie jest to wynik negatywny."); }
    }
    private static RegistryReply InvalidKey() => Fail("NotConfigured", "INVALID_API_KEY", "Nieprawidłowy klucz DataPort. Zmień go w ustawieniach firmy.");
    private static RegistryReply Limit() => Fail("RateLimited", "RATE_LIMIT_EXCEEDED", "Przekroczono dzienny limit całego konta DataPort, wspólny dla wszystkich jego kluczy. Reset o 00:00 UTC.");
}
