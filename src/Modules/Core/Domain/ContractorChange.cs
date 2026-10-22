namespace Vatowiec.Core.Domain;

public sealed class ContractorChange : ICompanyEntity
{
    private ContractorChange() { }

    public ContractorChange(Guid companyId, Guid contractorId, Guid actorId, Guid actorIssuerId,
        string actorName, DateTimeOffset occurredAtUtc, string correlationId, string? beforeJson, string afterJson)
    {
        Id = Guid.NewGuid();
        CompanyId = EntityText.Id(companyId, nameof(companyId));
        ContractorId = EntityText.Id(contractorId, nameof(contractorId));
        ActorId = EntityText.Id(actorId, nameof(actorId));
        ActorIssuerId = EntityText.Id(actorIssuerId, nameof(actorIssuerId));
        ActorName = EntityText.Required(actorName, 256, nameof(actorName));
        OccurredAtUtc = occurredAtUtc;
        CorrelationId = EntityText.Required(correlationId, 128, nameof(correlationId));
        BeforeJson = beforeJson;
        AfterJson = afterJson;
        Operation = beforeJson is null ? "Create" : "Update";
    }

    public Guid CompanyId { get; private set; }
    public Guid Id { get; private set; }
    public Guid ContractorId { get; private set; }
    public Guid ActorId { get; private set; }
    public Guid ActorIssuerId { get; private set; }
    public string ActorName { get; private set; } = null!;
    public DateTimeOffset OccurredAtUtc { get; private set; }
    public string CorrelationId { get; private set; } = null!;
    public string Operation { get; private set; } = null!;
    public int SnapshotVersion { get; private set; } = 1;
    public string? BeforeJson { get; private set; }
    public string AfterJson { get; private set; } = null!;
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }
    public byte[] RowVersion { get; private set; } = [];
}
