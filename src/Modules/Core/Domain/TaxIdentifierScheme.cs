namespace Vatowiec.Core.Domain;

// Wspólny słownik reguł, bez danych podatników. Zmiana reguły tworzy nową wersję.
public sealed class TaxIdentifierScheme
{
    public int Id { get; set; }
    public string CountryCode { get; set; } = "";
    public string IdentifierType { get; set; } = "";
    public string? VatPrefix { get; set; }
    public string FormatPattern { get; set; } = "";
    public string AlgorithmKey { get; set; } = "";
    public string Version { get; set; } = "";
    public DateOnly ValidFrom { get; set; }
    public DateOnly? ValidTo { get; set; }
    public bool IsActive { get; set; } = true;
    public string SourceUrl { get; set; } = "";
}
