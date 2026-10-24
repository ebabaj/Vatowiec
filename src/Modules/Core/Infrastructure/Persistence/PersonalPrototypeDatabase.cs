using Microsoft.Data.SqlClient;

namespace Vatowiec.Core.Infrastructure.Persistence;

// Experimental allowlist, never a fallback for Network or arbitrary LocalDB instances.
public static class PersonalPrototypeDatabase
{
    public const string Edition = "PersonalPrototype";
    public const string Instance = "VatowiecPersonal_Prototype";
    public const string Server = @"(localdb)\" + Instance;
    public const string System = "Vatowiec_Personal_Prototype_System";
    public const string CompanyA = "Vatowiec_Personal_Prototype_Company_A";
    public const string CompanyB = "Vatowiec_Personal_Prototype_Company_B";
    public static string[] Databases => [System, CompanyA, CompanyB];
    public static string Root
    {
        get
        {
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (!Path.IsPathFullyQualified(local)) throw new InvalidOperationException("Brak lokalnego katalogu danych użytkownika Windows.");
            return Path.Combine(local, "Vatowiec", "PersonalPrototype");
        }
    }

    public static bool IsSystem(string name) => string.Equals(name, System, StringComparison.OrdinalIgnoreCase);

    public static void Validate(SqlConnectionStringBuilder connection)
    {
        if (!OperatingSystem.IsWindows() ||
            !string.Equals(connection.DataSource, Server, StringComparison.OrdinalIgnoreCase) ||
            !Databases.Contains(connection.InitialCatalog, StringComparer.OrdinalIgnoreCase) ||
            !connection.IntegratedSecurity || connection.UserID.Length != 0 || connection.Password.Length != 0 ||
            connection.AttachDBFilename.Length != 0 || connection.Encrypt == SqlConnectionEncryptOption.Optional)
            throw new InvalidOperationException("Profil PersonalPrototype wymaga jawnej odizolowanej instancji LocalDB i zatwierdzonej bazy z Windows Authentication.");
    }
}
