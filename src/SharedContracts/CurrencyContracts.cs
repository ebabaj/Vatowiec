using System.Globalization;
using System.Text.Json;

namespace Vatowiec.SharedContracts;

public sealed record CurrencyResponse(Guid CompanyId, string Code, string Name, bool IsActive,
    bool DownloadA, bool DownloadB, bool DownloadC, string RowVersion)
{
    public override string ToString() => $"{Code} — {Name}";
}
public sealed record CurrencyWriteRequest(Guid OperationId, string RowVersion, bool IsActive, bool DownloadA, bool DownloadB, bool DownloadC);
public sealed record RateImportRequest(Guid OperationId, DateOnly? From = null, DateOnly? To = null);
public sealed record RateValue(string TableNumber, DateOnly EffectiveDate, DateOnly? TradingDate, decimal? Mid, decimal? Bid, decimal? Ask);
public sealed record RateConflict(long RateId, string CurrencyCode, string TableType, string RowVersion, RateValue Existing, RateValue Incoming,
    DateTimeOffset? RetrievedAtUtc = null)
{
    public override string ToString() => $"{CurrencyCode} · {TableType} · {Existing.EffectiveDate:dd.MM.yyyy} — {Existing.TableNumber} → {Incoming.TableNumber}";
}
public sealed record RateImportResponse(long AuditId, Guid CompanyId, int Tables, int Added, int Skipped, RateConflict[] Conflicts,
    string[] Errors, string[] Notices, DateOnly? From, DateOnly? To, DateOnly? LastA, DateOnly? LastB, DateOnly? LastC, string[]? Selection = null);
public sealed record RateResolutionRequest(Guid OperationId, int ConflictIndex, bool AcceptIncoming, bool Confirm);
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
public sealed record RatePage(Guid CompanyId, RateResponse[] Items, DateOnly? LastA, DateOnly? LastB, DateOnly? LastC);
public sealed record CurrencyHistoryResponse(long Id, Guid CompanyId, string Kind, DateTimeOffset AtUtc, string ActorName, string Details)
{
    public override string ToString()
    {
        var kind = Kind switch { "Configuration" => "Ustawienia waluty", "Import" => "Ręczne pobranie NBP", "ResolveConflict" => "Rozstrzygnięcie konfliktu", _ => Kind };
        string info;
        try
        {
            using var json = JsonDocument.Parse(Details); var data = json.RootElement;
            static string Yes(JsonElement value) => value.GetBoolean() ? "tak" : "nie";
            info = Kind switch
            {
                "Configuration" => data.GetProperty("after").GetProperty("code").GetString() + " — " +
                    "aktywna: " + Yes(data.GetProperty("after").GetProperty("isActive")) + "; A/B/C: " + Yes(data.GetProperty("after").GetProperty("downloadA")) + "/" + Yes(data.GetProperty("after").GetProperty("downloadB")) + "/" + Yes(data.GetProperty("after").GetProperty("downloadC")),
                "Import" => $"Tabele: {data.GetProperty("tables")} · nowe: {data.GetProperty("added")} · pominięte: {data.GetProperty("skipped")} · konflikty: {data.GetProperty("conflicts").GetArrayLength()} · błędy: {data.GetProperty("errors").GetArrayLength()}",
                "ResolveConflict" => data.GetProperty("currencyCode").GetString() + " — " + data.GetProperty("decision").GetString(),
                _ => "Zdarzenie historii"
            };
        }
        catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException) { info = "Nie można odczytać opisu zdarzenia."; }
        return $"{AtUtc.ToLocalTime():dd.MM.yyyy HH:mm:ss} · {ActorName} · {kind}\n{info}";
    }
}
