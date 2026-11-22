namespace Vatowiec.Core.Domain;

// ADR-045: jedyny ślad usuniętej firmy. CompanyId pozostaje zablokowany (poza przywróceniem z archiwum).
// Bez danych księgowych, kontrahentów i historii — te są usuwane razem z firmą.
public sealed class DeletedCompany
{
    private DeletedCompany() { }

    public DeletedCompany(Guid companyId, string companyName, string? nip, string? regon, DateTimeOffset deletedAtUtc,
        Guid deletedById, string deletedByName, string? archiveFileName, long? archiveSizeBytes, string? archiveSha256)
    {
        CompanyId = EntityText.Id(companyId, nameof(companyId));
        CompanyName = EntityText.Required(companyName, 256, nameof(companyName));
        Nip = EntityText.Optional(nip, 32, nameof(nip));
        Regon = EntityText.Optional(regon, 14, nameof(regon));
        DeletedAtUtc = deletedAtUtc;
        DeletedById = EntityText.Id(deletedById, nameof(deletedById));
        DeletedByName = EntityText.Required(deletedByName, 256, nameof(deletedByName));
        ArchiveFileName = EntityText.Optional(archiveFileName, 260, nameof(archiveFileName));
        ArchiveSizeBytes = archiveSizeBytes;
        ArchiveSha256 = archiveSha256;
    }

    public Guid CompanyId { get; private set; }
    public string CompanyName { get; private set; } = null!;
    public string? Nip { get; private set; }
    public string? Regon { get; private set; }
    public DateTimeOffset DeletedAtUtc { get; private set; }
    public Guid DeletedById { get; private set; }
    public string DeletedByName { get; private set; } = null!;
    public string? ArchiveFileName { get; private set; }
    public long? ArchiveSizeBytes { get; private set; }
    public string? ArchiveSha256 { get; private set; }
}
