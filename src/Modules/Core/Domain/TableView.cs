namespace Vatowiec.Core.Domain;

public sealed class TableView
{
    public long Id { get; set; }
    public Guid InstallationId { get; set; }
    // Empty means installation default; all other values are immutable Identity UserIds.
    public Guid OwnerId { get; set; }
    public string TableId { get; set; } = "";
    public string? ColumnsJson { get; set; }
    public int? PageSize { get; set; }
    public byte[] RowVersion { get; set; } = [];
}

public sealed class TableViewChange
{
    public long Id { get; set; }
    public Guid InstallationId { get; set; }
    public string TableId { get; set; } = "";
    public Guid ActorId { get; set; }
    public string ActorName { get; set; } = "";
    public DateTimeOffset AtUtc { get; set; }
    public string? BeforeJson { get; set; }
    public string? AfterJson { get; set; }
}
