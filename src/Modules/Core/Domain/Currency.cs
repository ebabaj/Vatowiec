namespace Vatowiec.Core.Domain;

public sealed class Currency
{
    public string Code { get; init; } = "";
    public string Name { get; set; } = "";
    public bool IsActive { get; set; } = true;
    public bool DownloadA { get; set; }
    public bool DownloadB { get; set; }
    public bool DownloadC { get; set; }
    public byte[] RowVersion { get; set; } = [];
    public bool Downloads(string table) => IsActive && Code != "PLN" && table switch
    { "A" => DownloadA, "B" => DownloadB, "C" => DownloadC, _ => false };
}

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
