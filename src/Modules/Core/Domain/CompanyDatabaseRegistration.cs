namespace Vatowiec.Core.Domain;

// Wyłącznie lokalizacja i referencja do konfiguracji serwera, nigdy hasło/connection string.
public sealed class CompanyDatabaseRegistration : ITrackedEntity
{
    private CompanyDatabaseRegistration() { }

    public CompanyDatabaseRegistration(Guid companyId, string serverName, string databaseName,
        string connectionProfileKey)
    {
        CompanyId = EntityText.Id(companyId, nameof(companyId));
        ServerName = EntityText.Required(serverName, 128, nameof(serverName));
        DatabaseName = EntityText.Required(databaseName, 128, nameof(databaseName));
        ConnectionProfileKey = EntityText.Required(connectionProfileKey, 128, nameof(connectionProfileKey));
    }

    public Guid CompanyId { get; private set; }
    public string ServerName { get; private set; } = null!;
    public string DatabaseName { get; private set; } = null!;
    public string ConnectionProfileKey { get; private set; } = null!;
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }
    public byte[] RowVersion { get; private set; } = [];
}
