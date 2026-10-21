namespace Vatowiec.Core.Domain;

public sealed class CompanyProfile : ICompanyEntity
{
    private CompanyProfile() { }

    public CompanyProfile(Guid companyId, string name, string? nip = null, string? regon = null)
    {
        CompanyId = EntityText.Id(companyId, nameof(companyId));
        Rename(name);
        Nip = EntityText.Optional(nip, 32, nameof(nip));
        Regon = EntityText.Optional(regon, 14, nameof(regon));
    }

    // PK i CHECK w SQL dopuszczają najwyżej jeden profil w fizycznej bazie.
    public byte SingletonKey { get; private set; } = 1;
    public Guid CompanyId { get; private set; }
    public string Name { get; private set; } = null!;
    public string? Nip { get; private set; }
    public string? Regon { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    public void Rename(string name) => Name = EntityText.Required(name, 256, nameof(name));
}
