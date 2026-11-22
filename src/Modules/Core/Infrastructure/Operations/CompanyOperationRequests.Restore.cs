using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Vatowiec.Core.Domain;
using Vatowiec.SharedContracts;

namespace Vatowiec.Core.Infrastructure.Operations;

// ADR-044 krok 3: zlecenie odtworzenia (administrator instalacji). Plik i jego treść sprawdza usługa Operations.
public sealed partial class CompanyOperationRequests
{
    public const string SourceBackup = "Backup";
    public const string SourceDeleted = "Deleted";
    public const string SourceImport = "Import";

    public async Task<RestoreSourceResponse[]> RestoreSources(CancellationToken ct)
    {
        var companies = await db.Companies.AsNoTracking().Where(x => x.IsActive).ToDictionaryAsync(x => x.Id, x => x.Name, ct);
        var backups = await db.CompanyBackups.AsNoTracking().Where(x => x.RemovedAtUtc == null).OrderByDescending(x => x.CreatedAtUtc).Take(500).ToArrayAsync(ct);
        var archives = await db.DeletedCompanies.AsNoTracking().Where(x => x.ArchiveFileName != null).OrderByDescending(x => x.DeletedAtUtc).ToArrayAsync(ct);
        var imports = await db.RestoreImportFiles.AsNoTracking().OrderBy(x => x.FileName).ToArrayAsync(ct);
        var known = await db.Companies.AsNoTracking().Select(x => x.Id).ToHashSetAsync(ct);
        return
        [
            .. backups.Where(x => companies.ContainsKey(x.CompanyId)).Select(x => new RestoreSourceResponse(SourceBackup, x.Id, x.CompanyId,
                companies[x.CompanyId], null, x.CreatedAtUtc, x.SizeBytes, x.Kind, true)),
            .. archives.Select(x => new RestoreSourceResponse(SourceDeleted, null, x.CompanyId, x.CompanyName, null, x.DeletedAtUtc,
                x.ArchiveSizeBytes ?? 0, "Archive", false)),
            .. imports.Select(x => new RestoreSourceResponse(SourceImport, null, x.CompanyId, x.CompanyName, x.FileName, x.CreatedAtUtc,
                x.SizeBytes, "Import", known.Contains(x.CompanyId)))
        ];
    }

    public async Task<(CompanyOperation Operation, bool Created)> RequestRestore(OperationRequester requester, RestoreCompanyRequest value,
        string? defaultServerName, string? defaultProfileKey, CancellationToken ct, Func<string, CancellationToken, Task<bool>>? databaseExists = null)
    {
        if (value.OperationKey == Guid.Empty)
            throw new CompanyOperationException(400, "operation_key_missing", "Brak klucza operacji. Odśwież formularz i spróbuj ponownie.");
        var sourceKey = value.Source switch
        {
            SourceBackup when value.BackupId is { } backup => backup.ToString(),
            SourceDeleted when value.CompanyId is { } deletedId => deletedId.ToString("D"),
            SourceImport when !string.IsNullOrWhiteSpace(value.ImportFileName) => value.ImportFileName,
            _ => throw new CompanyOperationException(400, "restore_source_invalid", "Wybierz kopię do odtworzenia z listy.")
        };
        var hash = Hash(CompanyOperationKinds.Restore, value.Source + ":" + sourceKey, value.ConfirmReplace ? "replace" : "");
        var previous = await Find(requester.UserId, value.OperationKey, ct);
        if (previous is not null) return (Replay(previous, hash), false);

        // Rozpoznanie źródła: plik, firma, tryb.
        string file; Guid companyId; string? deletedName = null; string? importName = null;
        switch (value.Source)
        {
            case SourceBackup:
                var backup = await db.CompanyBackups.AsNoTracking().SingleOrDefaultAsync(x => x.Id == value.BackupId && x.RemovedAtUtc == null, ct)
                    ?? throw new CompanyOperationException(404, "restore_source_missing", "Nie znaleziono kopii. Odśwież listę.");
                (file, companyId) = (backup.FileName, backup.CompanyId);
                break;
            case SourceDeleted:
                var deleted = await db.DeletedCompanies.AsNoTracking().SingleOrDefaultAsync(x => x.CompanyId == value.CompanyId && x.ArchiveFileName != null, ct)
                    ?? throw new CompanyOperationException(404, "restore_source_missing", "Nie znaleziono archiwum usuniętej firmy. Odśwież listę.");
                (file, companyId, deletedName) = (deleted.ArchiveFileName!, deleted.CompanyId, deleted.CompanyName);
                break;
            default:
                var import = await db.RestoreImportFiles.AsNoTracking().SingleOrDefaultAsync(x => x.FileName == value.ImportFileName, ct)
                    ?? throw new CompanyOperationException(404, "restore_source_missing", "Nie znaleziono pliku w katalogu importu. Odśwież listę.");
                (file, companyId, importName) = (import.FileName, import.CompanyId, import.CompanyName);
                deletedName = (await db.DeletedCompanies.AsNoTracking().SingleOrDefaultAsync(x => x.CompanyId == companyId, ct))?.CompanyName;
                break;
        }

        if (await db.CompanyOperations.AnyAsync(x => x.CompanyId == companyId && (x.Status == CompanyOperationStatuses.Pending ||
                x.Status == CompanyOperationStatuses.Running || x.Status == CompanyOperationStatuses.Failed ||
                x.Status == CompanyOperationStatuses.NeedsReconciliation), ct))
            throw new CompanyOperationException(409, "company_operation_active",
                "Dla tej firmy trwa lub czeka inna operacja. Poczekaj na jej zakończenie albo ją wyjaśnij.");
        var issuer = await db.InstallationIdentities.AsNoTracking().Select(x => x.Id).SingleOrDefaultAsync(ct);
        if (issuer == Guid.Empty || string.IsNullOrWhiteSpace(requester.Name))
            throw new CompanyOperationException(503, "audit_identity_unavailable", "Brak trwałej identyfikacji instalacji lub wykonawcy. Nie przyjęto zlecenia.");

        var id = Guid.NewGuid();
        var company = await db.Companies.AsNoTracking().SingleOrDefaultAsync(x => x.Id == companyId, ct);
        CompanyOperation operation;
        if (company is not null)
        {
            // Zastąpienie danych istniejącej firmy — wymaga świadomego potwierdzenia.
            if (!value.ConfirmReplace)
                throw new CompanyOperationException(400, "restore_confirmation_required",
                    "Odtworzenie zastąpi bieżące dane firmy stanem z kopii. Potwierdź świadomie.");
            if (!company.IsActive) throw new CompanyOperationException(409, "company_inactive", "Firma jest nieaktywna. Odśwież listę.");
            var registration = await db.CompanyDatabases.AsNoTracking().SingleOrDefaultAsync(x => x.CompanyId == companyId, ct)
                ?? throw new CompanyOperationException(503, "company_unavailable", "Baza firmy nie jest zarejestrowana w instalacji.");
            operation = CompanyOperation.Restore(id, value.OperationKey, companyId, company.Name, requester.UserId, requester.Name, issuer, hash,
                registration.ServerName, registration.ConnectionProfileKey, registration.DatabaseName, file, RestoreModes.Existing,
                CompanyDatabaseNames.Working(registration.DatabaseName, id),
                CompanyDatabaseNames.Previous(registration.DatabaseName, TimeZoneInfo.ConvertTime(clock.GetUtcNow(), clock.LocalTimeZone).DateTime));
        }
        else
        {
            // Usunięta (rejestr usuniętych) albo przeniesiona z innej instalacji: nowa rejestracja pod tym samym CompanyId.
            if (defaultServerName is null || defaultProfileKey is null)
                throw new CompanyOperationException(400, "server_profile_invalid", "Wybierz serwer baz danych z listy.");
            var name = deletedName ?? importName!;
            var target = CompanyDatabaseNames.FromCompanyName(name)
                ?? throw new CompanyOperationException(400, "company_name_invalid", "Nazwa firmy z kopii nie zawiera liter ani cyfr.");
            if (!CompanyDatabaseNames.IsCompanyDatabase(target) ||
                await db.CompanyDatabases.AnyAsync(x => x.ServerName == defaultServerName && x.DatabaseName == target, ct) ||
                await db.CompanyOperations.AnyAsync(x => x.ServerName == defaultServerName && x.DatabaseName == target &&
                    x.Status != CompanyOperationStatuses.Completed && x.Status != CompanyOperationStatuses.Cancelled, ct) ||
                (databaseExists is not null && await databaseExists(target, ct)))
                throw NameTaken(target);
            operation = CompanyOperation.Restore(id, value.OperationKey, companyId, name, requester.UserId, requester.Name, issuer, hash,
                defaultServerName, defaultProfileKey, target, file, deletedName is not null ? RestoreModes.Deleted : RestoreModes.Import,
                CompanyDatabaseNames.Working(target, id), null);
        }
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
            var winner = await Find(requester.UserId, value.OperationKey, ct);
            if (winner is null) throw new CompanyOperationException(409, "company_operation_active", "Odtworzenie tej firmy zostało już zlecone. Odśwież listę.");
            return (Replay(winner, hash), false);
        }
    }
}
