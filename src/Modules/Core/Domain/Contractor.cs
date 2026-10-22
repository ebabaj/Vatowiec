namespace Vatowiec.Core.Domain;

public sealed class Contractor : ICompanyEntity
{
    private Contractor() { }

    public Contractor(Guid companyId, Guid id, string code, string name,
        string countryCode, string? taxIdentifier = null)
    {
        CompanyId = EntityText.Id(companyId, nameof(companyId));
        Id = EntityText.Id(id, nameof(id));
        Code = EntityText.Required(code, 64, nameof(code)).ToUpperInvariant();
        Rename(name);
        CountryCode = EntityText.Required(countryCode, 2, nameof(countryCode)).ToUpperInvariant();
        if (CountryCode.Length != 2 || CountryCode.Any(c => c is < 'A' or > 'Z'))
        {
            throw new ArgumentException("Kod kraju musi zawierać dwie litery ASCII.", nameof(countryCode));
        }

        TaxIdentifier = EntityText.Optional(taxIdentifier, 32, nameof(taxIdentifier));
    }

    public Guid CompanyId { get; private set; }
    public Guid Id { get; private set; }
    public string Code { get; private set; } = null!;
    public string Name { get; private set; } = null!;
    public string CountryCode { get; private set; } = null!;
    public string? TaxIdentifier { get; private set; }
    public bool IsActive { get; private set; } = true;
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    public void Rename(string name) => Name = EntityText.Required(name, 256, nameof(name));

    public void Update(string code, string name, string? taxIdentifier, string countryCode, bool isActive)
    {
        // Waliduj całość przed zmianą stanu encji.
        var valid = new Contractor(CompanyId, Id, code, name, countryCode, taxIdentifier);
        Code = valid.Code;
        Name = valid.Name;
        TaxIdentifier = valid.TaxIdentifier;
        CountryCode = valid.CountryCode;
        IsActive = isActive;
    }
}
