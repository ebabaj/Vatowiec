namespace Vatowiec.Core.Domain;

// Centralny katalog. Dane księgowe i identyfikatory podatkowe należą do CompanyProfile.
public sealed class Company : ITrackedEntity
{
    private Company() { }

    public Company(Guid id, string name)
    {
        Id = EntityText.Id(id, nameof(id));
        Rename(name);
    }

    public Guid Id { get; private set; }
    public string Name { get; private set; } = null!;
    public bool IsActive { get; private set; } = true;
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    public void Rename(string name) => Name = EntityText.Required(name, 256, nameof(name));
}
