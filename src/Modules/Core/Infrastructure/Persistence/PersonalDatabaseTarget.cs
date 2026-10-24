using Microsoft.Data.SqlClient;

namespace Vatowiec.Core.Infrastructure.Persistence;

// A target belongs to one installation, never a wildcard LocalDB/SQL Express profile.
public sealed class PersonalDatabaseTarget(Guid installationId)
{
    public const string Edition = "Personal";
    public Guid InstallationId { get; } = installationId != Guid.Empty ? installationId : throw new ArgumentException("Brak identyfikatora instalacji.");
    public string Instance => "VatowiecPersonal_" + InstallationId.ToString("N");
    public string Server => @"(localdb)\" + Instance;
    public string System => "Vatowiec_Personal_" + InstallationId.ToString("N") + "_System";
    public string Company => "Vatowiec_Personal_" + InstallationId.ToString("N") + "_Company";
    public void Validate(string? value, bool system)
    {
        var c = new SqlConnectionStringBuilder(value);
        if (!OperatingSystem.IsWindows() || !string.Equals(c.DataSource, Server, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(c.InitialCatalog, system ? System : Company, StringComparison.Ordinal) ||
            !c.IntegratedSecurity || c.UserID.Length != 0 || c.Password.Length != 0 || c.AttachDBFilename.Length != 0 ||
            c.Encrypt == SqlConnectionEncryptOption.Optional)
            throw new InvalidOperationException("Nieprawidłowy cel prywatnej instalacji Personal.");
    }
    public string Connection(string database)
    {
        if (database != System && database != Company && database != "master") throw new InvalidOperationException("Niedozwolona baza Personal.");
        return new SqlConnectionStringBuilder { DataSource = Server, InitialCatalog = database, IntegratedSecurity = true,
            Encrypt = SqlConnectionEncryptOption.Mandatory, TrustServerCertificate = true, Pooling = false, ConnectTimeout = 15 }.ConnectionString;
    }
}
