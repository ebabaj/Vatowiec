namespace Vatowiec.Core.Domain;

public sealed class ExternalVerification
{
    private ExternalVerification() { }
    public ExternalVerification(Guid operationId, int contractorId, int? bankAccountId, string source,
        string operation, string environment, string? taxId, string? vatEuId, string? accountNumber,
        DateOnly? stateDate, DateTimeOffset started, DateTimeOffset completed, string outcome,
        string serviceStatus, string? errorCode, string message, string? officeReference,
        string dataJson, string adapterVersion, string ruleVersion, bool executed,
        Guid actorId, Guid actorIssuerId, string actorName, string requestHash)
    {
        Id = Guid.NewGuid(); OperationId = EntityText.Id(operationId, nameof(operationId));
        if (contractorId <= 0) throw new ArgumentException("Brak kontrahenta.");
        ContractorId = contractorId; BankAccountId = bankAccountId;
        Source = source; Operation = operation; Environment = environment;
        TaxId = taxId; VatEuId = vatEuId; AccountNumber = accountNumber; StateDate = stateDate;
        StartedAtUtc = started; CompletedAtUtc = completed; Outcome = outcome;
        ServiceStatus = serviceStatus; ErrorCode = errorCode; Message = message;
        OfficeReference = officeReference; DataJson = dataJson; AdapterVersion = adapterVersion;
        RuleVersion = ruleVersion; Executed = executed;
        ActorId = EntityText.Id(actorId, nameof(actorId)); ActorIssuerId = EntityText.Id(actorIssuerId, nameof(actorIssuerId));
        ActorName = EntityText.Required(actorName, 256, nameof(actorName)); RequestHash = requestHash;
    }
    public Guid Id { get; private set; }
    public Guid OperationId { get; private set; }
    public int ContractorId { get; private set; }
    public int? BankAccountId { get; private set; }
    public string Source { get; private set; } = "";
    public string Operation { get; private set; } = "";
    public string Environment { get; private set; } = "";
    public string? TaxId { get; private set; }
    public string? VatEuId { get; private set; }
    public string? AccountNumber { get; private set; }
    public DateOnly? StateDate { get; private set; }
    public DateTimeOffset StartedAtUtc { get; private set; }
    public DateTimeOffset CompletedAtUtc { get; private set; }
    public string Outcome { get; private set; } = "";
    public string ServiceStatus { get; private set; } = "";
    public string? ErrorCode { get; private set; }
    public string Message { get; private set; } = "";
    public string? OfficeReference { get; private set; }
    public string DataJson { get; private set; } = "{}";
    public string AdapterVersion { get; private set; } = "";
    public string RuleVersion { get; private set; } = "";
    public bool Executed { get; private set; }
    public Guid ActorId { get; private set; }
    public Guid ActorIssuerId { get; private set; }
    public string ActorName { get; private set; } = "";
    public string RequestHash { get; private set; } = "";
}
