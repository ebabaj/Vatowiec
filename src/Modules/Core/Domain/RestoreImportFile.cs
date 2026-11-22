namespace Vatowiec.Core.Domain;

// ADR-044 krok 3: plik kopii z manifestem VATowca znaleziony przez usługę w {BackupRoot}\_import.
// API nie ma dostępu do katalogu kopii — zna wyłącznie te metadane z manifestu.
public sealed class RestoreImportFile
{
    private RestoreImportFile() { }

    public RestoreImportFile(string fileName, Guid companyId, string companyName, Guid sourceInstallationId,
        DateTimeOffset createdAtUtc, long sizeBytes, string sha256, DateTimeOffset scannedAtUtc)
    {
        FileName = EntityText.Required(fileName, 260, nameof(fileName));
        Update(companyId, companyName, sourceInstallationId, createdAtUtc, sizeBytes, sha256, scannedAtUtc);
    }

    public string FileName { get; private set; } = null!;
    public Guid CompanyId { get; private set; }
    public string CompanyName { get; private set; } = null!;
    public Guid SourceInstallationId { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public long SizeBytes { get; private set; }
    public string Sha256 { get; private set; } = null!;
    public DateTimeOffset ScannedAtUtc { get; private set; }

    public void Update(Guid companyId, string companyName, Guid sourceInstallationId, DateTimeOffset createdAtUtc, long sizeBytes,
        string sha256, DateTimeOffset scannedAtUtc)
    {
        CompanyId = EntityText.Id(companyId, nameof(companyId));
        CompanyName = EntityText.Required(companyName, 256, nameof(companyName));
        SourceInstallationId = EntityText.Id(sourceInstallationId, nameof(sourceInstallationId));
        CreatedAtUtc = createdAtUtc;
        SizeBytes = sizeBytes > 0 ? sizeBytes : throw new ArgumentException("Pusta kopia.", nameof(sizeBytes));
        Sha256 = sha256.Length == 64 && sha256.All(Uri.IsHexDigit) ? sha256.ToUpperInvariant()
            : throw new ArgumentException("Nieprawidłowa suma SHA-256.", nameof(sha256));
        ScannedAtUtc = scannedAtUtc;
    }
}
