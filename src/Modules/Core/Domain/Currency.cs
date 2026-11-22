namespace Vatowiec.Core.Domain;

// Słownik walut bazy firmy (ADR-024): cel kluczy obcych kartoteki i firmowa widoczność waluty.
// Od ADR-048 bez ustawień pobierania — kursy NBP są wspólne dla instalacji (Vatowiec_System).
public sealed class Currency
{
    public string Code { get; init; } = "";
    public string Name { get; set; } = "";
    public bool IsActive { get; set; } = true;
    public byte[] RowVersion { get; set; } = [];
}

// ADR-048: kurs NBP we wspólnej bazie System (ref.ExchangeRates), bez CompanyId.
public sealed class ExchangeRate
{
    public long Id { get; set; }
    public string CurrencyCode { get; set; } = "";
    public string Source { get; set; } = "NBP";
    public string TableType { get; set; } = "";
    public string TableNumber { get; set; } = "";
    public DateOnly EffectiveDate { get; set; }
    public DateOnly? TradingDate { get; set; }
    public decimal? Mid { get; set; }
    public decimal? Bid { get; set; }
    public decimal? Ask { get; set; }
    public DateTimeOffset RetrievedAtUtc { get; set; }
    public byte[] RowVersion { get; set; } = [];
}

// Niezmienna historia zmian walut bazy firmy (aktywność).
public sealed class CurrencyOperation
{
    public long Id { get; set; }
    public Guid OperationId { get; set; }
    public string RequestHash { get; set; } = "";
    public string Kind { get; set; } = "";
    public DateTimeOffset OccurredAtUtc { get; set; }
    public Guid ActorId { get; set; }
    public Guid ActorIssuerId { get; set; }
    public string ActorName { get; set; } = "";
    public string DetailsJson { get; set; } = "";
}

// ADR-048: wspólny słownik ISO 4217 w System (ref.Currencies), bez ustawień firm.
public sealed class ReferenceCurrency
{
    public string Code { get; init; } = "";
    public string Name { get; set; } = "";
}

// ADR-048: stan pobierania kursów NBP przez usługę Operations (ops.ExchangeRateSync, jeden wiersz).
// CheckedThrough* — ostatni dzień zamknięty dla tabeli; dzień bieżący nigdy nie jest zamykany.
public sealed class ExchangeRateSync
{
    public byte SingletonKey { get; set; } = 1;
    public DateOnly? HistoryStartDate { get; set; }
    public DateOnly? CheckedThroughA { get; set; }
    public DateOnly? CheckedThroughB { get; set; }
    public DateOnly? CheckedThroughC { get; set; }
    public DateTimeOffset? RunRequestedAtUtc { get; set; }
    public DateTimeOffset? LastAttemptUtc { get; set; }
    public DateTimeOffset? LastSuccessUtc { get; set; }
    public string? LastErrorCode { get; set; }
    public byte[] RowVersion { get; set; } = [];

    public DateOnly? CheckedThrough(string table) => table switch
    { "A" => CheckedThroughA, "B" => CheckedThroughB, "C" => CheckedThroughC, _ => throw new ArgumentOutOfRangeException(nameof(table)) };

    public void SetCheckedThrough(string table, DateOnly? value)
    {
        switch (table)
        {
            case "A": CheckedThroughA = value; break;
            case "B": CheckedThroughB = value; break;
            case "C": CheckedThroughC = value; break;
            default: throw new ArgumentOutOfRangeException(nameof(table));
        }
    }
}

// ADR-048: rozbieżność danych NBP z zapisanym kursem. Usługa nigdy nie nadpisuje kursu;
// rozstrzyga administrator instalacji. Ta sama propozycja (RateId + IncomingHash) jest zapisywana raz.
public sealed class ExchangeRateConflict
{
    public long Id { get; set; }
    public long RateId { get; set; }
    public string IncomingHash { get; set; } = "";
    public string IncomingTableNumber { get; set; } = "";
    public DateOnly? IncomingTradingDate { get; set; }
    public decimal? IncomingMid { get; set; }
    public decimal? IncomingBid { get; set; }
    public decimal? IncomingAsk { get; set; }
    public DateTimeOffset DetectedAtUtc { get; set; }
    public DateTimeOffset? ResolvedAtUtc { get; set; }
    public string? Decision { get; set; }
    public Guid? ResolvedById { get; set; }
    public string? ResolvedByName { get; set; }
    public byte[] RowVersion { get; set; } = [];
}

// ADR-048: niezmienna historia pobierania kursów i decyzji administratora (audit.ExchangeRateOperations).
// ActorId null = usługa Operations.
public sealed class ExchangeRateOperation
{
    public long Id { get; set; }
    public string Kind { get; set; } = "";
    public DateTimeOffset AtUtc { get; set; }
    public Guid? ActorId { get; set; }
    public string ActorName { get; set; } = "";
    public string DetailsJson { get; set; } = "";
}

public static class ExchangeRateOperationKinds
{
    public const string Download = "Download";
    public const string DownloadFailed = "DownloadFailed";
    public const string RunRequested = "RunRequested";
    public const string HistoryStartChanged = "HistoryStartChanged";
    public const string ResolveConflict = "ResolveConflict";
}
