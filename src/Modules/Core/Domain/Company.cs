namespace Vatowiec.Core.Domain;

public sealed class Company : ICompanyEntity
{
    private Company() { }

    public Company(Guid id, string name, string? nip = null, string? regon = null)
    {
        Id = EntityText.Id(id, nameof(id));
        Rename(name);
        Nip = EntityText.Optional(nip, 32, nameof(nip));
        Regon = EntityText.Optional(regon, 14, nameof(regon));
    }

    public Guid Id { get; private set; }
    public Guid CompanyId => Id;
    public string Name { get; private set; } = null!;
    public string? Nip { get; private set; }
    public string? Regon { get; private set; }
    public bool IsActive { get; private set; } = true;
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    public void Rename(string name) => Name = EntityText.Required(name, 256, nameof(name));
}
