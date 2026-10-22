using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Vatowiec.SharedContracts;

namespace Vatowiec.Core.Infrastructure.Registries;

public sealed record RegistryReply(string Environment, string Outcome, string ServiceStatus, string? ErrorCode,
    string Message, string? Reference, RegistryData Data, string AdapterVersion, bool Executed = true);

public interface IRegistryAdapters
{
    string EnvironmentFor(string operation);
    Task<RegistryReply> Execute(string operation, string? nip, string? vat, string? account, DateOnly date, CancellationToken cancellationToken);
}

public sealed class RegistrySettings(IConfiguration configuration)
{
    public const string MfTest = "https://wykazpodatnikowvat-test.mf.gov.pl/api/";
    public const string MfProduction = "https://wykazpodatnikowvat.mf.gov.pl/api/";
    public const string GusTest = "https://wyszukiwarkaregontest.stat.gov.pl/wsBIR/UslugaBIRzewnPubl.svc";
    public const string GusProduction = "https://wyszukiwarkaregon.stat.gov.pl/wsBIR/UslugaBIRzewnPubl.svc";
    public const string ViesTest = "https://ec.europa.eu/taxation_customs/vies/test-services/checkVatTestService";
    public const string ViesProduction = "https://ec.europa.eu/taxation_customs/vies/services/checkVatService";
    public string Mode(string source)
    {
        var mode = configuration[$"Registries:{source}:Environment"] ?? "Test";
        if (mode is not ("Test" or "Production" or "Mock")) throw new InvalidOperationException("Nieznane środowisko adaptera.");
        if (mode == "Production" && !configuration.GetValue<bool>("Registries:AllowProduction"))
            throw new InvalidOperationException("Produkcja rejestrów wymaga jawnego AllowProduction.");
        return mode;
    }
    public Uri Endpoint(string source, bool wl2 = false)
    {
        var mode = Mode(source);
        var expected = source switch
        {
            "MF" => mode == "Production" ? MfProduction : MfTest,
            "GUS" => mode == "Production" ? GusProduction : GusTest,
            "VIES" => mode == "Production" ? ViesProduction : ViesTest,
            _ => throw new InvalidOperationException()
        };
        if (wl2) expected = expected.Replace("/api/", "/api2/");
        var configured = configuration[$"Registries:{source}:{(wl2 ? "Wl2Url" : "BaseUrl")}"] ?? expected;
        if (!Uri.TryCreate(configured, UriKind.Absolute, out var uri) || configured.TrimEnd('/') != expected.TrimEnd('/') || uri.AbsoluteUri.TrimEnd('/') != expected.TrimEnd('/'))
            throw new InvalidOperationException("Adres nie pasuje do oficjalnej listy i środowiska adaptera.");
        return uri;
    }
    public string GusKey => Mode("GUS") == "Test" ? "abcde12345abcde12345" : configuration["Registries:GUS:ApiKey"] ?? "";
}

// Globalna bramka na proces ogranicza także równoległe żądania różnych użytkowników.
// Brak automatycznych ponowień: użytkownik może świadomie zlecić kolejną próbę.
public sealed class RegistryThrottle
{
    private readonly object sync = new();
    private readonly Dictionary<string, (DateTimeOffset At, DateOnly Day, int Count)> entries = [];
    public bool TryEnter(string source, string environment, string operation, DateTimeOffset now)
    {
        lock (sync)
        {
            var prefix = source + "|" + environment + "|";
            var key = prefix + (source == "MF" ? operation : "");
            entries.TryGetValue(key, out var old);
            var day = DateOnly.FromDateTime(now.UtcDateTime);
            var count = old.Day == day ? old.Count : 0;
            var limit = source == "MF" ? operation == "MfTaxpayer" ? 100 : operation == "MfStatus" ? 50000 : 5000 : 10000;
            var last = entries.Where(x => x.Key.StartsWith(prefix, StringComparison.Ordinal)).Select(x => x.Value.At).DefaultIfEmpty(DateTimeOffset.MinValue).Max();
            if (now - last < TimeSpan.FromSeconds(2) || count >= limit) return false;
            entries[key] = (now, day, count + 1); return true;
        }
    }
}

public sealed class RegistryAdapters(IHttpClientFactory clients, RegistrySettings settings, RegistryThrottle throttle) : IRegistryAdapters
{
    public static string Source(string operation) => operation switch
    { "Vies" => "VIES", "Gus" => "GUS", "MfTaxpayer" or "MfAccount" or "MfStatus" => "MF", _ => throw new ArgumentException("Nieznana operacja.") };
    public string EnvironmentFor(string operation) => settings.Mode(Source(operation));
    public async Task<RegistryReply> Execute(string operation, string? nip, string? vat, string? account, DateOnly date, CancellationToken cancellationToken)
    {
        var source = Source(operation); var mode = EnvironmentFor(operation);
        var adapter = source switch { "MF" => "mf-1.6.1-v1", "GUS" => "bir1.2-v1", _ => "vies-soap-v1" };
        if (mode == "Mock") return new(mode, "Unknown", "Mock", null, "MOCK — lokalna atrapa, nie wykonano urzędowego sprawdzenia.", null,
            source == "GUS" ? new(Name: "Fikcyjny podmiot MOCK", Street: "Fikcyjna", BuildingNo: "1", City: "Miasto testowe", CountryCode: "PL") : new(), adapter, false);
        if (!throttle.TryEnter(source, mode, operation, DateTimeOffset.UtcNow))
            return new(mode, "Unknown", "RateLimited", "local_limit", "Ograniczenie częstotliwości; odczekaj przed kolejną próbą. Nie wykonano zapytania.", null, new(), adapter, false);
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(12));
            using var http = clients.CreateClient("registries");
            return source switch
            {
                "MF" => await Mf(http, mode, operation, nip!, account, date, adapter, timeout.Token),
                "GUS" => await Gus(http, mode, nip!, adapter, timeout.Token),
                _ => await Vies(http, mode, vat!, adapter, timeout.Token)
            };
        }
        catch (OperationCanceledException) { return Failure(mode, adapter, "Timeout", "timeout", "Usługa nie odpowiedziała w limicie czasu; nie jest to wynik negatywny."); }
        catch (HttpRequestException) { return Failure(mode, adapter, "Unavailable", "network", "Usługa zewnętrzna niedostępna; nie jest to wynik negatywny."); }
        catch (Exception error) when (error is XmlException or JsonException or InvalidDataException or InvalidOperationException or KeyNotFoundException)
        { return Failure(mode, adapter, "Error", "invalid_response", "Nie można bezpiecznie odczytać odpowiedzi usługi; nie jest to wynik negatywny."); }
    }
    private static RegistryReply Failure(string mode, string adapter, string status, string code, string message) => new(mode, "Unknown", status, code, message, null, new(), adapter);
    private async Task<RegistryReply> Mf(HttpClient http, string mode, string op, string nip, string? account, DateOnly date, string adapter, CancellationToken ct)
    {
        var endpoint = settings.Endpoint("MF", op == "MfStatus");
        var path = op switch { "MfAccount" => $"check/nip/{nip}/bank-account/{account}", "MfStatus" => $"status/nip/{nip}", _ => $"search/nip/{nip}" };
        var uri = new Uri(endpoint.AbsoluteUri.TrimEnd('/') + "/" + path + "?date=" + date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        using var response = await http.GetAsync(uri, ct);
        if (!response.IsSuccessStatusCode) return Failure(mode, adapter, (int)response.StatusCode == 429 ? "RateLimited" : (int)response.StatusCode >= 500 ? "Unavailable" : "Error", "mf_http_" + (int)response.StatusCode, "MF nie udzielił odpowiedzi merytorycznej. Nie jest to wynik NIE.");
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        var result = json.RootElement.GetProperty("result");
        if (op == "MfAccount")
        {
            var assigned = Text(result, "accountAssigned");
            if (assigned is not ("TAK" or "NIE")) throw new InvalidDataException();
            return new(mode, assigned == "TAK" ? "Positive" : "Negative", "Completed", null,
                "Przypisanie rachunku do NIP na wybrany dzień: " + assigned, Text(result, "requestId"), new(), adapter);
        }
        if (!result.TryGetProperty("subject", out var subject) || subject.ValueKind == JsonValueKind.Null)
            return new(mode, "Negative", "Completed", null, "Nie znaleziono podmiotu w odpowiedzi wykazu na wskazany dzień.", Text(result, "requestId"), new(), adapter);
        if (Text(subject, "nip") != nip) throw new InvalidDataException();
        var status = Text(subject, "statusVat") ?? throw new InvalidDataException();
        return new(mode, "Information", "Completed", null, "Status VAT: " + status + ". Nie potwierdza przypisania rachunku.",
            Text(result, "requestId") ?? Text(subject, "requestId"), new(Name: Text(subject, "name"),
            Address: Text(subject, "workingAddress") ?? Text(subject, "residenceAddress"), Regon: Text(subject, "regon"), VatStatus: status), adapter);
    }
    private async Task<RegistryReply> Vies(HttpClient http, string mode, string vat, string adapter, CancellationToken ct)
    {
        XNamespace ns = "urn:ec.europa.eu:taxud:vies:services:checkVat:types";
        var xml = await Soap(http, settings.Endpoint("VIES"), new XElement(ns + "checkVat", new XElement(ns + "countryCode", vat[..2]), new XElement(ns + "vatNumber", vat[2..])), false, "", null, ct);
        var fault = Value(xml, "faultstring");
        if (fault is not null) return Failure(mode, adapter, fault.Contains("TIMEOUT", StringComparison.Ordinal) ? "Timeout" : fault.Contains("UNAVAILABLE", StringComparison.Ordinal) ? "Unavailable" : "Error", SafeCode(fault), "VIES: " + SafeCode(fault) + ". Nie uzyskano odpowiedzi o ważności numeru.");
        if (Value(xml, "countryCode") != vat[..2] || Value(xml, "vatNumber") != vat[2..]) throw new InvalidDataException();
        var valid = Value(xml, "valid");
        if (valid is not ("true" or "false" or "1" or "0")) throw new InvalidDataException();
        var yes = valid is "true" or "1";
        return new(mode, yes ? "Positive" : "Negative", "Completed", null, yes ? "VIES: rejestracja VAT UE ważna w odpowiedzi usługi." : "VIES: rejestracja VAT UE nieważna w odpowiedzi usługi.",
            null, new(Name: Value(xml, "name"), Address: Value(xml, "address")), adapter);
    }
    private async Task<RegistryReply> Gus(HttpClient http, string mode, string nip, string adapter, CancellationToken ct)
    {
        var key = settings.GusKey;
        if (string.IsNullOrWhiteSpace(key)) return new(mode, "Unknown", "NotConfigured", "gus_key_missing", "Brak konfiguracji klucza GUS. Nie wykonano zapytania.", null, new(), adapter, false);
        XNamespace ns = "http://CIS/BIR/PUBL/2014/07";
        XNamespace data = "http://CIS/BIR/PUBL/2014/07/DataContract";
        var uri = settings.Endpoint("GUS");
        var login = await Soap(http, uri, new XElement(ns + "Zaloguj", new XElement(ns + "pKluczUzytkownika", key)), true, "Zaloguj", null, ct);
        var sid = Value(login, "ZalogujResult");
        if (string.IsNullOrEmpty(sid) || sid.Length > 128) return Failure(mode, adapter, "Unavailable", "gus_login_failed", "GUS nie przyjął sesji; nie uzyskano danych podmiotu.");
        try
        {
            var response = await Soap(http, uri, new XElement(ns + "DaneSzukajPodmioty", new XElement(ns + "pParametryWyszukiwania", new XElement(data + "Nip", nip))), true, "DaneSzukajPodmioty", sid, ct);
            var body = Value(response, "DaneSzukajPodmiotyResult");
            if (body is null) throw new InvalidDataException();
            var found = ParseXml(body).Descendants().Where(x => x.Name.LocalName == "dane").ToArray();
            if (found.Length == 0) return new(mode, "Negative", "Completed", null, "GUS: nie znaleziono danych dla NIP.", null, new(), adapter);
            if (found.Length > 1) return new(mode, "Unknown", "Ambiguous", "gus_multiple", "GUS zwrócił kilka podmiotów; nie wybrano automatycznie danych do przeniesienia.", null, new(), adapter);
            var row = found[0];
            if (Value(row, "ErrorCode") is not null) return Failure(mode, adapter, "Error", "gus_query_error", "GUS zgłosił błąd zapytania; nie jest to brak podmiotu.");
            if (Value(row, "Nip")?.Trim() != nip) throw new InvalidDataException();
            var result = new RegistryData(Name: Value(row, "Nazwa"), Regon: Value(row, "Regon"), Street: Value(row, "Ulica"),
                BuildingNo: Value(row, "NrNieruchomosci"), ApartmentNo: Value(row, "NrLokalu"), PostalCode: Value(row, "KodPocztowy"), City: Value(row, "Miejscowosc"), CountryCode: "PL");
            return new(mode, "Information", "Completed", null, "GUS: pobrano dane. TEST zawiera stare/częściowo zanonimizowane dane, nie aktualne dane przedsiębiorcy.", null, result, adapter);
        }
        finally
        {
            try { await Soap(http, uri, new XElement(ns + "Wyloguj", new XElement(ns + "pIdentyfikatorSesji", sid)), true, "Wyloguj", sid, ct); }
            catch (Exception error) when (error is HttpRequestException or OperationCanceledException or XmlException or InvalidDataException) { /* SID tylko w pamięci; wygasa po stronie GUS. */ }
        }
    }
    private static string SafeCode(string value) => Regex.IsMatch(value, "^[A-Z0-9_]{1,64}$") ? value : "service_fault";
    private static string? Text(JsonElement element, string name) => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var child) && child.ValueKind == JsonValueKind.String ? Limit(child.GetString(), 1000) : null;
    private static string? Value(XContainer container, string name) => Limit(container.Descendants().FirstOrDefault(x => x.Name.LocalName == name)?.Value, 2000);
    private static string? Limit(string? text, int length) => string.IsNullOrWhiteSpace(text) ? null : text[..Math.Min(text.Length, length)];
    public static XDocument ParseXml(string xml)
    {
        using var reader = XmlReader.Create(new StringReader(xml), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 2_000_000 });
        return XDocument.Load(reader);
    }
    private static async Task<XDocument> Soap(HttpClient http, Uri uri, XElement body, bool soap12, string action, string? sid, CancellationToken ct)
    {
        XNamespace ns = soap12 ? "http://www.w3.org/2003/05/soap-envelope" : "http://schemas.xmlsoap.org/soap/envelope/";
        XNamespace addressing = "http://www.w3.org/2005/08/addressing";
        var envelope = new XElement(ns + "Envelope", new XAttribute(XNamespace.Xmlns + "s", ns));
        if (soap12) envelope.Add(new XElement(ns + "Header", new XElement(addressing + "Action", "http://CIS/BIR/PUBL/2014/07/IUslugaBIRzewnPubl/" + action), new XElement(addressing + "To", uri.AbsoluteUri)));
        envelope.Add(new XElement(ns + "Body", body));
        using var request = new HttpRequestMessage(HttpMethod.Post, uri);
        request.Content = new StringContent(envelope.ToString(SaveOptions.DisableFormatting), Encoding.UTF8, soap12 ? "application/soap+xml" : "text/xml");
        if (!soap12) request.Headers.TryAddWithoutValidation("SOAPAction", "\"\"");
        if (sid is not null) request.Headers.Add("sid", sid);
        using var response = await http.SendAsync(request, ct);
        if ((int)response.StatusCode is >= 300 and < 400 || (int)response.StatusCode == 429 || (int)response.StatusCode == 503) throw new HttpRequestException();
        var text = await response.Content.ReadAsStringAsync(ct);
        if (response.Content.Headers.ContentType?.MediaType == "multipart/related")
        {
            var match = Regex.Match(text, @"<(?:\w+:)?Envelope\b[\s\S]*?</(?:\w+:)?Envelope>", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
            if (!match.Success) throw new InvalidDataException(); text = match.Value;
        }
        return ParseXml(text);
    }
}

public static class RegistryRegistration
{
    public static IServiceCollection AddRegistryAdapters(this IServiceCollection services)
    {
        services.AddSingleton<RegistrySettings>(); services.AddSingleton<RegistryThrottle>();
        services.AddScoped<IRegistryAdapters, RegistryAdapters>();
        services.AddHttpClient("registries", http => { http.Timeout = TimeSpan.FromSeconds(12); http.MaxResponseContentBufferSize = 2_000_000; })
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false })
            .RemoveAllLoggers(); // Nie logujemy kluczy, SID ani identyfikatorów w URL.
        return services;
    }
}
