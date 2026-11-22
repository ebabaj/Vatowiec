using Microsoft.Data.SqlClient;
using Vatowiec.Core.Domain;
using Vatowiec.Core.Infrastructure.Persistence;
using Vatowiec.SharedContracts;

namespace Vatowiec.Operations;

// Błąd operacji z kodem bez treści SQL i sekretów. Reconcile = stan fizyczny niezgodny z planem.
public sealed class OperationFailure(string code, bool reconcile) : Exception(code)
{
    public string Code { get; } = code;
    public bool Reconcile { get; } = reconcile;
}

// Połączenia procesu technicznego: osobne profile OperationsConnections:{klucz} z tożsamością DDL.
// Konto zwykłego API (CompanyConnections) nie ma tych uprawnień.
public sealed class OperationsConnections(IConfiguration configuration, IHostEnvironment environment)
{
    public string Master(CompanyOperation operation)
    {
        var connection = Build(operation);
        connection.InitialCatalog = "master";
        return connection.ConnectionString;
    }

    public string Company(CompanyOperation operation)
    {
        if (!CompanyDatabaseNames.IsCompanyDatabase(operation.DatabaseName))
            throw new OperationFailure("database_name_invalid", reconcile: true);
        var connection = Build(operation);
        connection.InitialCatalog = operation.DatabaseName;
        try { DatabaseConnectionPolicy.ValidateCompany(connection.ConnectionString, environment.IsDevelopment()); }
        catch (InvalidOperationException) { throw new OperationFailure("server_profile_policy", reconcile: false); }
        return connection.ConnectionString;
    }

    private SqlConnectionStringBuilder Build(CompanyOperation operation)
    {
        var raw = configuration[$"OperationsConnections:{operation.ConnectionProfileKey}"];
        if (string.IsNullOrWhiteSpace(raw)) throw new OperationFailure("server_profile_missing", reconcile: false);
        SqlConnectionStringBuilder connection;
        try { connection = new SqlConnectionStringBuilder(raw); }
        catch (ArgumentException) { throw new OperationFailure("server_profile_invalid", reconcile: false); }
        if (!string.Equals(connection.DataSource, operation.ServerName, StringComparison.OrdinalIgnoreCase))
            throw new OperationFailure("server_profile_mismatch", reconcile: false);
        if (connection.Encrypt == SqlConnectionEncryptOption.Optional || connection.AttachDBFilename.Length != 0)
            throw new OperationFailure("server_profile_policy", reconcile: false);
        return connection;
    }
}
