namespace Vatowiec.Core.Domain;

[Flags]
public enum CompanyPermission
{
    None = 0,
    ViewProfile = 1,
    ViewContractors = 2,
    EditContractors = 4,
    AcceptWarnings = 8,
    VerifyRegistries = 16
}

public sealed class CompanyAccess : ITrackedEntity
{
    private CompanyAccess() { }

    public CompanyAccess(Guid userId, Guid companyId, CompanyPermission permissions)
    {
        UserId = EntityText.Id(userId, nameof(userId));
        CompanyId = EntityText.Id(companyId, nameof(companyId));
        ChangePermissions(permissions);
    }

    public void ChangePermissions(CompanyPermission permissions)
    {
        if (permissions == CompanyPermission.None || (permissions & ~(CompanyPermission)31) != 0)
            throw new ArgumentException("Nieprawidłowe uprawnienia firmy.", nameof(permissions));
        Permissions = permissions;
    }

    public Guid UserId { get; private set; }
    public Guid CompanyId { get; private set; }
    public CompanyPermission Permissions { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }
    public byte[] RowVersion { get; private set; } = [];
}
