namespace Vatowiec.Core.Domain;

public sealed class UserAdministrationChange
{
    private UserAdministrationChange() { }
    public UserAdministrationChange(Guid actorId, Guid issuerId, string actorName, Guid targetId, string targetName,
        DateTimeOffset at, string operation, Guid? companyId = null, CompanyPermission? before = null,
        CompanyPermission? after = null, bool? blockedBefore = null, bool? blockedAfter = null)
    {
        ActorId = EntityText.Id(actorId, nameof(actorId)); ActorIssuerId = EntityText.Id(issuerId, nameof(issuerId));
        ActorName = actorName; TargetId = EntityText.Id(targetId, nameof(targetId)); TargetName = targetName;
        OccurredAtUtc = at; Operation = operation; CompanyId = companyId; PermissionsBefore = before;
        PermissionsAfter = after; BlockedBefore = blockedBefore; BlockedAfter = blockedAfter;
    }
    public long Id { get; private set; }
    public Guid ActorId { get; private set; }
    public Guid ActorIssuerId { get; private set; }
    public string ActorName { get; private set; } = "";
    public Guid TargetId { get; private set; }
    public string TargetName { get; private set; } = "";
    public Guid? CompanyId { get; private set; }
    public DateTimeOffset OccurredAtUtc { get; private set; }
    public string Operation { get; private set; } = "";
    public CompanyPermission? PermissionsBefore { get; private set; }
    public CompanyPermission? PermissionsAfter { get; private set; }
    public bool? BlockedBefore { get; private set; }
    public bool? BlockedAfter { get; private set; }
}
