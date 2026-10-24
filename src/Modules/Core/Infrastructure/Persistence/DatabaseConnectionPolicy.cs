using Microsoft.Data.SqlClient;

namespace Vatowiec.Core.Infrastructure.Persistence;

public static class DatabaseConnectionPolicy
{
    public static void ValidateSystem(string? connectionString, bool isDevelopment, bool isPersonalPrototype = false)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return; // Status/health i generowanie modelu nadal działają bez SQL.
        }

        var connection = Parse(connectionString, isDevelopment, isPersonalPrototype);
        if (isPersonalPrototype ? !PersonalPrototypeDatabase.IsSystem(connection.InitialCatalog) : !IsSystemDatabase(connection.InitialCatalog))
        {
            throw new InvalidOperationException("Połączenie systemowe musi wskazywać Vatowiec_System lub Vatowiec_System_Test.");
        }
    }

    public static void ValidateCompany(string connectionString, bool isDevelopment, bool isPersonalPrototype = false)
    {
        var connection = Parse(connectionString, isDevelopment, isPersonalPrototype);
        if (IsSystemDatabase(connection.InitialCatalog) || PersonalPrototypeDatabase.IsSystem(connection.InitialCatalog) ||
            new[] { "Vatowiec_Dev", "master", "model", "msdb", "tempdb" }
                .Contains(connection.InitialCatalog, StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Połączenie firmowe nie może wskazywać bazy systemowej ani historycznej Vatowiec_Dev.");
        }
    }

    private static bool IsSystemDatabase(string database) =>
        string.Equals(database, "Vatowiec_System", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(database, "Vatowiec_System_Test", StringComparison.OrdinalIgnoreCase);

    private static SqlConnectionStringBuilder Parse(string connectionString, bool isDevelopment, bool isPersonalPrototype)
    {
        SqlConnectionStringBuilder connection;
        try
        {
            connection = new SqlConnectionStringBuilder(connectionString);
        }
        catch (ArgumentException)
        {
            throw new InvalidOperationException("Nieprawidłowa konfiguracja połączenia SQL.");
        }

        if (string.IsNullOrWhiteSpace(connection.DataSource) ||
            string.IsNullOrWhiteSpace(connection.InitialCatalog) || connection.AttachDBFilename.Length != 0 ||
            connection.InitialCatalog != connection.InitialCatalog.Trim() ||
            connection.Encrypt == SqlConnectionEncryptOption.Optional)
        {
            throw new InvalidOperationException("Wymagany jawny serwer, baza i szyfrowanie SQL; dołączanie plików jest zabronione.");
        }

        if (isPersonalPrototype)
        {
            PersonalPrototypeDatabase.Validate(connection);
            return connection; // Private LocalDB IPC only; never changes Network SQL/TLS policy.
        }

        if (connection.TrustServerCertificate)
        {
            var localCatalogs = new[] { "Vatowiec_System", "Vatowiec_System_Test",
                "Vatowiec_Company_A_Dev", "Vatowiec_Company_B_Dev",
                "Vatowiec_Company_A_Test", "Vatowiec_Company_B_Test" };
            if (!isDevelopment ||
                !string.Equals(connection.DataSource, @"localhost\SQLEXPRESS", StringComparison.OrdinalIgnoreCase) ||
                !connection.IntegratedSecurity || connection.UserID.Length != 0 || connection.Password.Length != 0 ||
                !localCatalogs.Contains(connection.InitialCatalog, StringComparer.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Zaufanie certyfikatowi SQL dozwolone tylko dla jawnych lokalnych baz Development z Windows Authentication.");
            }
        }

        return connection;
    }
}
