namespace Vatowiec.Core.Domain;

public sealed class ContractorChange : ITrackedEntity
{
    private ContractorChange() { }

    public ContractorChange(int? contractorId, Guid actorId, Guid actorIssuerId,
        string actorName, DateTimeOffset occurredAtUtc, string correlationId, string? beforeJson, string afterJson,
        string recordType = "Contractor", int? recordId = null, string? warningJson = null)
    {
        Id = Guid.NewGuid();
        if (contractorId is <= 0) throw new ArgumentException("Brak kontrahenta.", nameof(contractorId));
        ContractorId = contractorId;
        RecordType = recordType;
        RecordId = recordId ?? contractorId ?? throw new ArgumentException("Brak identyfikatora rekordu.");
        WarningJson = warningJson;
        ActorId = EntityText.Id(actorId, nameof(actorId));
        ActorIssuerId = EntityText.Id(actorIssuerId, nameof(actorIssuerId));
        ActorName = EntityText.Required(actorName, 256, nameof(actorName));
        OccurredAtUtc = occurredAtUtc;
        CorrelationId = EntityText.Required(correlationId, 128, nameof(correlationId));
        BeforeJson = beforeJson;
        AfterJson = afterJson;
        Operation = beforeJson is null ? "Create" : "Update";
    }

    public Guid Id { get; private set; }
    public int? ContractorId { get; private set; }
    public string RecordType { get; private set; } = "Contractor";
    public int RecordId { get; private set; }
    public string? WarningJson { get; private set; }
    public Guid ActorId { get; private set; }
    public Guid ActorIssuerId { get; private set; }
    public string ActorName { get; private set; } = null!;
    public DateTimeOffset OccurredAtUtc { get; private set; }
    public string CorrelationId { get; private set; } = null!;
    public string Operation { get; private set; } = null!;
    public int SnapshotVersion { get; private set; } = 2;
    public string? BeforeJson { get; private set; }
    public string AfterJson { get; private set; } = null!;
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }
    public byte[] RowVersion { get; private set; } = [];
}
