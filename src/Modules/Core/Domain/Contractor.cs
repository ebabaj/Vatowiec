namespace Vatowiec.Core.Domain;

public sealed class Contractor : ITrackedEntity
{
    private Contractor() { }

    public Contractor(string code, string name, string countryCode, string? taxId = null,
        string? vatEuId = null, string? ksefIdWew = null, int? parentId = null)
    {
        Code = EntityText.Required(code, 64, nameof(code)).ToUpperInvariant();
        Rename(name);
        CountryCode = EntityText.Required(countryCode, 2, nameof(countryCode)).ToUpperInvariant();
        if (CountryCode.Length != 2 || CountryCode.Any(c => c is < 'A' or > 'Z'))
        {
            throw new ArgumentException("Kod kraju musi zawierać dwie litery ASCII.", nameof(countryCode));
        }

        TaxId = EntityText.Optional(taxId, 100, nameof(taxId));
        VatEuId = EntityText.Optional(vatEuId, 100, nameof(vatEuId));
        KsefIdWew = EntityText.Optional(ksefIdWew, 100, nameof(ksefIdWew));
        SetParent(parentId);
    }

    public int Id { get; private set; }
    public int? ParentId { get; private set; }
    public string Code { get; private set; } = null!;
    public string Name { get; private set; } = null!;
    public string CountryCode { get; private set; } = null!;
    public string? TaxId { get; private set; }
    public string? VatEuId { get; private set; }
    public string? KsefIdWew { get; private set; }
    public string? Regon { get; private set; }
    public string? Krs { get; private set; }
    public string? Bdo { get; private set; }
    public string? Pesel { get; private set; }
    public bool IsActive { get; private set; } = true;
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    public void Rename(string name) => Name = EntityText.Required(name, 256, nameof(name));

    public void SetParent(int? parentId)
    {
        if (parentId is <= 0 || (Id > 0 && parentId == Id))
            throw new ArgumentException("Nieprawidłowy rekord nadrzędny.", nameof(parentId));
        ParentId = parentId;
    }

    public void SetIdentifiers(string? taxId, string? vatEuId, string? ksefIdWew)
    {
        var tax = EntityText.Optional(taxId, 100, nameof(taxId));
        var vat = EntityText.Optional(vatEuId, 100, nameof(vatEuId));
        var ksef = EntityText.Optional(ksefIdWew, 100, nameof(ksefIdWew));
        TaxId = tax; VatEuId = vat; KsefIdWew = ksef;
    }

    public void SetStandardIdentifiers(string? regon, string? krs, string? bdo, string? pesel)
    {
        var r = EntityText.Optional(regon, 100, nameof(regon));
        var k = EntityText.Optional(krs, 100, nameof(krs));
        var b = EntityText.Optional(bdo, 100, nameof(bdo));
        var p = EntityText.Optional(pesel, 100, nameof(pesel));
        Regon = r; Krs = k; Bdo = b; Pesel = p;
    }

    public void Update(string code, string name, string countryCode, bool isActive)
    {
        // Waliduj całość przed zmianą stanu encji.
        var valid = new Contractor(code, name, countryCode);
        Code = valid.Code;
        Name = valid.Name;
        CountryCode = valid.CountryCode;
        IsActive = isActive;
    }
}
