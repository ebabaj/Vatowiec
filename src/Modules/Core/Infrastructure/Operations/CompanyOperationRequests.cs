using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Vatowiec.Core.Domain;
using Vatowiec.Core.Infrastructure.Persistence;
using Vatowiec.SharedContracts;

namespace Vatowiec.Core.Infrastructure.Operations;

public sealed class CompanyOperationException(int status, string code, string message) : Exception(message)
{
    public int Status { get; } = status;
    public string Code { get; } = code;
}

public sealed record OperationRequester(Guid UserId, string Name);

// ADR-044: API tylko zapisuje zlecenie i jego audyt. Bazę tworzy osobny proces z tożsamością DDL.
public sealed class CompanyOperationRequests(SystemDbContext db, TimeProvider clock)
{
    // databaseExists: odczyt sys.databases na serwerze docelowym (bez DDL). Null = bez kontroli fizycznej.
    public async Task<(CompanyOperation Operation, bool Created)> RequestProvision(OperationRequester requester,
        Guid operationKey, string? companyName, string? profileKey, string serverName, CancellationToken ct,
        Func<string, CancellationToken, Task<bool>>? databaseExists = null)
    {
        if (operationKey == Guid.Empty)
            throw new CompanyOperationException(400, "operation_key_missing", "Brak klucza operacji. Odśwież formularz i spróbuj ponownie.");
        var name = companyName?.Trim() ?? "";
        if (name.Length is 0 or > 256 || name.Any(char.IsControl))
            throw new CompanyOperationException(400, "company_name_invalid", "Podaj nazwę firmy (1–256 znaków, bez znaków sterujących).");
        var databaseName = CompanyDatabaseNames.FromCompanyName(name);
        if (databaseName is null)
            throw new CompanyOperationException(400, "company_name_invalid", "Nazwa firmy musi zawierać litery lub cyfry.");
        var profile = profileKey?.Trim() ?? "";
        if (profile.Length is 0 or > 128)
            throw new CompanyOperationException(400, "server_profile_invalid", "Wybierz serwer baz danych z listy.");
        var hash = Hash(CompanyOperationKinds.Provision, name, profile);

        var previous = await Find(requester.UserId, operationKey, ct);
        if (previous is not null) return (Replay(previous, hash), false);

        // ADR-045: zajęta nazwa bazy = komunikat dla administratora, bez automatycznych sufiksów.
        if (!CompanyDatabaseNames.IsCompanyDatabase(databaseName) ||
            await db.CompanyDatabases.AnyAsync(x => x.ServerName == serverName && x.DatabaseName == databaseName, ct) ||
            await db.CompanyOperations.AnyAsync(x => x.ServerName == serverName && x.DatabaseName == databaseName, ct) ||
            (databaseExists is not null && await databaseExists(databaseName, ct)))
            throw NameTaken(databaseName);

        var issuer = await db.InstallationIdentities.AsNoTracking().Select(x => x.Id).SingleOrDefaultAsync(ct);
        if (issuer == Guid.Empty || string.IsNullOrWhiteSpace(requester.Name))
            throw new CompanyOperationException(503, "audit_identity_unavailable", "Brak trwałej identyfikacji instalacji lub wykonawcy. Nie przyjęto zlecenia.");

        var companyId = Guid.NewGuid();
        var operation = CompanyOperation.Provision(Guid.NewGuid(), operationKey, companyId, name, requester.UserId, requester.Name,
            issuer, hash, serverName, profile, databaseName);
        db.CompanyOperations.Add(operation);
        db.CompanyOperationEvents.Add(new CompanyOperationEvent(operation, clock.GetUtcNow(), CompanyOperationEvent.UserActor,
            requester.UserId, requester.Name, "Requested"));
        try
        {
            await db.SaveChangesAsync(ct);
            return (operation, true);
        }
        catch (DbUpdateException error) when (error.InnerException is SqlException { Number: 2601 or 2627 })
        {
            // Równoległe ponowienie tego samego żądania: zwracamy zapisane zlecenie zamiast drugiego.
            db.ChangeTracker.Clear();
            var winner = await Find(requester.UserId, operationKey, ct);
            // Inny klucz z tą samą nazwą bazy zapisany równolegle — nazwa jest zajęta.
            if (winner is null) throw NameTaken(databaseName);
            return (Replay(winner, hash), false);
        }
    }

    private static CompanyOperationException NameTaken(string databaseName) => new(409, "database_name_taken",
        $"Nazwa zajęta — baza {databaseName} już istnieje albo jest zastrzeżona. Podaj inną nazwę firmy.");

    public async Task<CompanyOperation> Retry(OperationRequester requester, Guid id, string? rowVersion, CancellationToken ct)
    {
        var operation = await db.CompanyOperations.SingleOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new CompanyOperationException(404, "operation_not_found", "Nie znaleziono operacji.");
        if (rowVersion != Convert.ToBase64String(operation.RowVersion))
            throw new CompanyOperationException(409, "operation_stale", "Stan operacji zmienił się. Odśwież listę przed ponowieniem.");
        if (!operation.CanRetry)
            throw new CompanyOperationException(409, "operation_not_retryable", "Ponowić można wyłącznie operację zakończoną błędem lub wymagającą uzgodnienia.");
        operation.Retry(clock.GetUtcNow());
        db.CompanyOperationEvents.Add(new CompanyOperationEvent(operation, clock.GetUtcNow(), CompanyOperationEvent.UserActor,
            requester.UserId, requester.Name, "RetryRequested"));
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException)
        {
            throw new CompanyOperationException(409, "operation_stale", "Stan operacji zmienił się. Odśwież listę przed ponowieniem.");
        }
        return operation;
    }

    private Task<CompanyOperation?> Find(Guid requester, Guid operationKey, CancellationToken ct) =>
        db.CompanyOperations.AsNoTracking().SingleOrDefaultAsync(x => x.RequestedById == requester && x.OperationKey == operationKey, ct);

    private static CompanyOperation Replay(CompanyOperation previous, string hash) => previous.RequestHash == hash
        ? previous
        : throw new CompanyOperationException(409, "operation_payload_mismatch",
            "Ten klucz operacji użyto już z innymi danymi. Odśwież formularz i zleć utworzenie ponownie.");

    public static string Hash(string kind, string name, string profile) => Convert.ToHexString(SHA256.HashData(
        Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { kind, name, profile }))));
}
