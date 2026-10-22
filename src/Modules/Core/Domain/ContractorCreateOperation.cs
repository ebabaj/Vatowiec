namespace Vatowiec.Core.Domain;

// Wynik operacji jest trwały i niezmienny. GUID identyfikuje żądanie, nie kontrahenta.
public sealed class ContractorCreateOperation
{
    private ContractorCreateOperation() { }
    public ContractorCreateOperation(Guid operationId, Guid actorId, Guid actorIssuerId,
        string requestHash, int contractorId, string responseJson)
    {
        OperationId = EntityText.Id(operationId, nameof(operationId));
        ActorId = EntityText.Id(actorId, nameof(actorId));
        ActorIssuerId = EntityText.Id(actorIssuerId, nameof(actorIssuerId));
        RequestHash = requestHash;
        ContractorId = contractorId;
        ResponseJson = responseJson;
    }
    public Guid OperationId { get; private set; }
    public Guid ActorId { get; private set; }
    public Guid ActorIssuerId { get; private set; }
    public string RequestHash { get; private set; } = null!;
    public int ContractorId { get; private set; }
    public string ResponseJson { get; private set; } = null!;
}
