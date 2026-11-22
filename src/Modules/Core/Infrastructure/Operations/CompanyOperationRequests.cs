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
        // ADR-045: identyfikator usuniętej firmy jest zablokowany (poza przywróceniem z archiwum).
        while (await db.DeletedCompanies.AnyAsync(x => x.CompanyId == companyId, ct)) companyId = Guid.NewGuid();
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

    // ADR-045: usunięcie firmy (administrator instalacji). Bez kopii archiwalnej wymagane jest jawne potwierdzenie.
    public async Task<(CompanyOperation Operation, bool Created)> RequestDelete(OperationRequester requester, Guid operationKey,
        Guid companyId, bool createArchive, bool confirmWithoutArchive, CancellationToken ct)
    {
        if (operationKey == Guid.Empty)
            throw new CompanyOperationException(400, "operation_key_missing", "Brak klucza operacji. Odśwież formularz i spróbuj ponownie.");
        var hash = Hash(CompanyOperationKinds.Delete, companyId.ToString("D"), createArchive ? "archive" : "no-archive");
        var previous = await Find(requester.UserId, operationKey, ct);
        if (previous is not null) return (Replay(previous, hash), false);
        if (!createArchive && !confirmWithoutArchive)
            throw new CompanyOperationException(400, "archive_confirmation_required",
                "Usunięcie bez kopii archiwalnej jest nieodwracalne. Potwierdź je świadomie albo wybierz kopię archiwalną.");
        var company = await db.Companies.SingleOrDefaultAsync(x => x.Id == companyId, ct)
            ?? throw new CompanyOperationException(404, "company_not_found", "Nie znaleziono firmy. Odśwież listę.");
        if (await db.CompanyOperations.AnyAsync(x => x.CompanyId == companyId && (x.Status == CompanyOperationStatuses.Pending ||
                x.Status == CompanyOperationStatuses.Running || x.Status == CompanyOperationStatuses.Failed ||
                x.Status == CompanyOperationStatuses.NeedsReconciliation), ct))
            throw new CompanyOperationException(409, "company_operation_active",
                "Dla tej firmy trwa lub czeka inna operacja (np. kopia lub usuwanie). Poczekaj na jej zakończenie albo ją wyjaśnij.");
        if (!company.IsActive)
            throw new CompanyOperationException(409, "company_inactive", "Firma jest nieaktywna. Odśwież listę.");
        var registration = await db.CompanyDatabases.AsNoTracking().SingleOrDefaultAsync(x => x.CompanyId == companyId, ct)
            ?? throw new CompanyOperationException(503, "company_unavailable", "Baza firmy nie jest zarejestrowana w instalacji.");
        var issuer = await db.InstallationIdentities.AsNoTracking().Select(x => x.Id).SingleOrDefaultAsync(ct);
        if (issuer == Guid.Empty || string.IsNullOrWhiteSpace(requester.Name))
            throw new CompanyOperationException(503, "audit_identity_unavailable", "Brak trwałej identyfikacji instalacji lub wykonawcy. Nie przyjęto zlecenia.");
        var id = Guid.NewGuid();
        var now = clock.GetUtcNow();
        // Decyzja właściciela (ADR-045): bez kopii archiwalnej wykonanie startuje najwcześniej po 10 minutach,
        // co daje czas na „Anuluj usuwanie”. Celowo nie jest opisywane w instrukcji użytkownika ani w interfejsie.
        var operation = CompanyOperation.Delete(id, operationKey, companyId, company.Name, requester.UserId, requester.Name, issuer, hash,
            registration.ServerName, registration.ConnectionProfileKey, registration.DatabaseName,
            createArchive ? ArchivePrefix + CompanyBackupService.BackupFileName(registration.DatabaseName, now, id) : null,
            createArchive ? null : now + DeleteWithoutArchiveGrace);
        // Firma przestaje być dostępna od razu po przyjęciu zlecenia; anulowanie przywraca dostęp.
        company.Deactivate();
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
            db.ChangeTracker.Clear();
            var winner = await Find(requester.UserId, operationKey, ct);
            if (winner is null) throw new CompanyOperationException(409, "company_operation_active", "Usuwanie tej firmy zostało już zlecone. Odśwież listę.");
            return (Replay(winner, hash), false);
        }
    }

    // Kopie archiwalne w osobnym katalogu, poza rotacją zwykłych kopii.
    public const string ArchivePrefix = "_archive/";
    public static readonly TimeSpan DeleteWithoutArchiveGrace = TimeSpan.FromMinutes(10);

    public async Task<CompanyOperation> Cancel(OperationRequester requester, Guid id, string? rowVersion, CancellationToken ct)
    {
        var operation = await db.CompanyOperations.SingleOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new CompanyOperationException(404, "operation_not_found", "Nie znaleziono operacji.");
        if (rowVersion != Convert.ToBase64String(operation.RowVersion))
            throw new CompanyOperationException(409, "operation_stale", "Stan operacji zmienił się. Odśwież listę przed anulowaniem.");
        if (!operation.CanCancel)
            throw new CompanyOperationException(409, "operation_not_cancellable", "Anulować można wyłącznie usunięcie firmy, które nie jest wykonywane i nie usunęło jeszcze bazy.");
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        operation.Cancel();
        var company = await db.Companies.SingleOrDefaultAsync(x => x.Id == operation.CompanyId, ct);
        company?.Activate();
        db.CompanyOperationEvents.Add(new CompanyOperationEvent(operation, clock.GetUtcNow(), CompanyOperationEvent.UserActor,
            requester.UserId, requester.Name, "Cancelled"));
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException)
        {
            throw new CompanyOperationException(409, "operation_stale", "Stan operacji zmienił się. Odśwież listę przed anulowaniem.");
        }
        await transaction.CommitAsync(ct);
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
