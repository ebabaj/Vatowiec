using Microsoft.Data.SqlClient;

namespace Vatowiec.Core.Infrastructure.Persistence;

public static class DevelopmentDatabase
{
    public static string Validate(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("Ustaw ConnectionStrings__Vatowiec dla lokalnej bazy Development.");
        }

        var connection = new SqlConnectionStringBuilder(connectionString);
        if (!string.Equals(connection.DataSource, @"localhost\SQLEXPRESS", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(connection.InitialCatalog, "Vatowiec_Dev", StringComparison.Ordinal) ||
            !connection.IntegratedSecurity || !string.IsNullOrEmpty(connection.UserID) ||
            !string.IsNullOrEmpty(connection.Password) || connection.AttachDBFilename.Length != 0 ||
            connection.Encrypt == SqlConnectionEncryptOption.Optional)
        {
            throw new InvalidOperationException(
                "Dozwolone tylko localhost\\SQLEXPRESS / Vatowiec_Dev / Windows Authentication / Encrypt=True.");
        }

        return connection.ConnectionString;
    }

    public static void ValidateRuntime(string? connectionString, bool isDevelopment)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return;
        }

        var connection = new SqlConnectionStringBuilder(connectionString);
        if (connection.TrustServerCertificate)
        {
            if (!isDevelopment)
            {
                throw new InvalidOperationException("TrustServerCertificate=True dozwolone tylko w Development.");
            }

            Validate(connectionString);
        }
    }
}
