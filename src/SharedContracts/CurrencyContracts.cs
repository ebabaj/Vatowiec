using System.Globalization;
using System.Text.Json;

namespace Vatowiec.SharedContracts;

// Waluty bazy firmy: wyłącznie aktywność (widoczność w firmie). Kursy NBP są wspólne dla instalacji (ADR-048).
public sealed record CurrencyResponse(Guid CompanyId, string Code, string Name, bool IsActive, string RowVersion)
{
    public override string ToString() => $"{Code} — {Name}";
}
public sealed record CurrencyWriteRequest(Guid OperationId, string RowVersion, bool IsActive);
public sealed record CurrencyVersion(string Code, string RowVersion);
public sealed record CurrencyActivityRequest(Guid OperationId, bool IsActive, bool All, CurrencyVersion[] Currencies, bool Confirm);
public sealed record CurrencyActivityResponse(Guid CompanyId, bool IsActive, CurrencyResponse[] Changed, string[] Skipped);
public sealed record RateValue(string TableNumber, DateOnly EffectiveDate, DateOnly? TradingDate, decimal? Mid, decimal? Bid, decimal? Ask);
public sealed record RateResponse(long Id, Guid CompanyId, string CurrencyCode, string Source, string TableType,
    string TableNumber, DateOnly EffectiveDate, DateOnly? TradingDate, decimal? Mid, decimal? Bid, decimal? Ask,
    DateTimeOffset RetrievedAtUtc, string RowVersion)
{
    private static readonly CultureInfo Polish = CultureInfo.GetCultureInfo("pl-PL");
    private static string Number(decimal? value) => value?.ToString("0.0000########", Polish) ?? "—";
    public string DateLabel => EffectiveDate.ToString("dd.MM.yyyy", Polish);
    public string TradingDateLabel => TradingDate?.ToString("dd.MM.yyyy", Polish) ?? "—";
    public string Values => Mid != null ? "Średni: " + Number(Mid) : "Kupna: " + Number(Bid) + " · Sprzedaży: " + Number(Ask);
}

// ADR-048: stan pobierania kursów przez usługę Operations, widoczny w firmie i u administratora instalacji.
public sealed record ExchangeRateSyncStatus(DateTimeOffset? LastAttemptUtc, DateTimeOffset? LastSuccessUtc, string? LastErrorCode,
    DateOnly? LastA, DateOnly? LastB, DateOnly? LastC, bool RunRequested)
{
    private static readonly CultureInfo Polish = CultureInfo.GetCultureInfo("pl-PL");
    private static string Date(DateOnly? value) => value?.ToString("dd.MM.yyyy", Polish) ?? "brak";
    public string Summary =>
        $"Ostatnie tabele NBP: A {Date(LastA)} · B {Date(LastB)} · C {Date(LastC)}. " +
        (LastSuccessUtc is { } ok ? $"Ostatnie udane pobranie: {ok.ToLocalTime():dd.MM.yyyy HH:mm}. " : "Usługa VATowiec Operations jeszcze nie pobrała kursów. ") +
        (LastErrorCode != null && LastAttemptUtc is { } failed ? $"Ostatnia próba {failed.ToLocalTime():dd.MM.yyyy HH:mm} nieudana ({ErrorText(LastErrorCode)}); usługa ponowi ją automatycznie. " : "") +
        (RunRequested ? "Zlecono pobranie — czeka na usługę Operations." : "");
    public static string ErrorText(string code) => code switch
    {
        "nbp_unavailable" => "NBP niedostępny lub błędna odpowiedź",
        "history_start_changed" => "zmieniono datę początkową w trakcie pobierania",
        _ => code
    };
}
public sealed record RatePage(Guid CompanyId, RateResponse[] Items, DateOnly? LastA, DateOnly? LastB, DateOnly? LastC, ExchangeRateSyncStatus? Sync = null);

// ADR-048: Ustawienia instalacji → Kursy NBP (wyłącznie administrator instalacji).
public sealed record ExchangeRateConflictResponse(long Id, string CurrencyCode, string TableType, RateValue Existing, RateValue Incoming,
    DateTimeOffset DetectedAtUtc, string RowVersion, string RateRowVersion)
{
    public override string ToString() => $"{CurrencyCode} · {TableType} · {Existing.EffectiveDate:dd.MM.yyyy} — {Existing.TableNumber} → {Incoming.TableNumber}";
}
public sealed record ExchangeRateAdministrationResponse(DateOnly HistoryStartDate, bool HistoryStartIsDefault, ExchangeRateSyncStatus Status,
    ExchangeRateConflictResponse[] Conflicts, string RowVersion);
public sealed record ExchangeRateHistoryStartRequest(DateOnly HistoryStartDate, string RowVersion);
public sealed record ExchangeRateConflictResolution(bool AcceptIncoming, string RowVersion, string RateRowVersion, bool Confirm);
public sealed record ExchangeRateOperationResponse(long Id, string Kind, DateTimeOffset AtUtc, string ActorName, string Details)
{
    public override string ToString()
    {
        var kind = Kind switch
        {
            "Download" => "Pobranie kursów NBP",
            "DownloadFailed" => "Nieudane pobranie NBP",
            "RunRequested" => "Zlecenie pobrania",
            "HistoryStartChanged" => "Zmiana daty początkowej historii",
            "ResolveConflict" => "Rozstrzygnięcie konfliktu",
            _ => Kind
        };
        string info;
        try
        {
            using var json = JsonDocument.Parse(Details); var data = json.RootElement;
            info = Kind switch
            {
                "Download" => $"Tabela {data.GetProperty("table").GetString()} · {data.GetProperty("from").GetString()} – {data.GetProperty("to").GetString()} · " +
                    $"publikacje: {data.GetProperty("tables").GetInt32()} · nowe kursy: {data.GetProperty("added").GetInt32()} · pominięte: {data.GetProperty("skipped").GetInt32()} · " +
                    $"konflikty: {data.GetProperty("conflicts").GetInt32()}" + (data.GetProperty("unknownCodes").GetArrayLength() > 0 ? " · kody spoza słownika: " +
                    string.Join(", ", data.GetProperty("unknownCodes").EnumerateArray().Select(x => x.GetString())) : ""),
                "DownloadFailed" => $"Tabela {data.GetProperty("table").GetString()} · {data.GetProperty("from").GetString()} – {data.GetProperty("to").GetString()} · {data.GetProperty("error").GetString()}",
                "RunRequested" => "Pobranie przy najbliższym sprawdzeniu przez usługę Operations.",
                "HistoryStartChanged" => $"{data.GetProperty("before").GetString() ?? "domyślna"} → {data.GetProperty("after").GetString()}",
                "ResolveConflict" => $"{data.GetProperty("currencyCode").GetString()} · {data.GetProperty("tableType").GetString()} · {data.GetProperty("effectiveDate").GetString()} — " +
                    (data.GetProperty("decision").GetString() == "AcceptIncoming" ? "przyjęto dane NBP" : "zachowano zapisany kurs"),
                _ => "Zdarzenie historii"
            };
        }
        catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException) { info = "Nie można odczytać opisu zdarzenia."; }
        return $"{AtUtc.ToLocalTime():dd.MM.yyyy HH:mm:ss} · {ActorName} · {kind}\n{info}";
    }
}

public sealed record CurrencyHistoryResponse(long Id, Guid CompanyId, string Kind, DateTimeOffset AtUtc, string ActorName, string Details)
{
    public override string ToString()
    {
        var kind = Kind switch { "PersonalInitialized" => "Inicjalizacja walut Personal", "Configuration" => "Ustawienia waluty", "BulkActivity" => "Zbiorowa aktywność walut", _ => Kind };
        string info;
        try
        {
            using var json = JsonDocument.Parse(Details); var data = json.RootElement;
            static string Yes(JsonElement value) => value.GetBoolean() ? "tak" : "nie";
            info = Kind switch
            {
                "PersonalInitialized" => "Aktywna: PLN · waluty obce nieaktywne",
                "BulkActivity" => $"Aktywacja: {Yes(data.GetProperty("result").GetProperty("isActive"))} · zmienione: {data.GetProperty("result").GetProperty("changed").GetArrayLength()} · pominięte: {data.GetProperty("result").GetProperty("skipped").GetArrayLength()}",
                "Configuration" => data.GetProperty("after").GetProperty("code").GetString() + " — aktywna: " + Yes(data.GetProperty("after").GetProperty("isActive")),
                _ => "Zdarzenie historii"
            };
        }
        catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException) { info = "Nie można odczytać opisu zdarzenia."; }
        return $"{AtUtc.ToLocalTime():dd.MM.yyyy HH:mm:ss} · {ActorName} · {kind}\n{info}";
    }
}
