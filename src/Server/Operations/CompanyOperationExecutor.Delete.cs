using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Vatowiec.Core.Domain;
using Vatowiec.Core.Infrastructure.Identity;
using Vatowiec.SharedContracts;

namespace Vatowiec.Operations;

// ADR-045: Requested → CompanyDeactivated → ArchiveWritten → ArchiveVerified → DatabaseDropped → usunięcie danych z System.
// Baza jest usuwana dopiero po zweryfikowanej kopii archiwalnej (o ile zlecona) i tylko gdy jej profil potwierdza firmę.
// Na końcu jedna transakcja System czyści katalog, prawa, kopie, harmonogram, historię operacji i audyt firmy,
// zostawiając wyłącznie wpis w ops.DeletedCompanies. Wyjątek od niezmienności historii otwiera SESSION_CONTEXT
// ustawiony wyłącznie na czas tej transakcji (triggery dopuszczają DELETE tylko wierszy tej firmy).
public sealed partial class CompanyOperationExecutor
{
    internal const string PurgeContextKey = "Vatowiec.PurgeCompanyId";

    private async Task ExecuteDelete(CompanyOperation operation, CancellationToken ct)
    {
        var registration = await system.CompanyDatabases.AsNoTracking().SingleOrDefaultAsync(x => x.CompanyId == operation.CompanyId, ct);
        if (registration is null || !string.Equals(registration.DatabaseName, operation.DatabaseName, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(registration.ServerName, operation.ServerName, StringComparison.OrdinalIgnoreCase))
            throw new OperationFailure("company_database_changed", reconcile: true);

        if (!HasReached(operation, CompanyOperationStages.CompanyDeactivated))
            (await system.Companies.SingleOrDefaultAsync(x => x.Id == operation.CompanyId, ct)
                ?? throw new OperationFailure("company_missing", reconcile: true)).Deactivate();
        await Checkpoint(operation, CompanyOperationStages.CompanyDeactivated, ct);

        string? nip = null, regon = null;
        var databaseExists = await DatabaseExists(operation, ct);
        string[] migrations = [];
        if (databaseExists)
        {
            await using var company = CompanyContext(operation);
            var profile = await company.CompanyProfiles.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(ct);
            if (profile is null || profile.CompanyId != operation.CompanyId) throw new OperationFailure("company_identity_mismatch", reconcile: true);
            (nip, regon) = (profile.Nip, profile.Regon);
            migrations = (await company.Database.GetAppliedMigrationsAsync(ct)).ToArray();
        }
        else if (!HasReached(operation, CompanyOperationStages.ArchiveVerified))
            throw new OperationFailure("database_missing", reconcile: true); // Bez bazy nie da się wykonać archiwum.

        string? archivePath = null;
        if (operation.CreateArchive == true)
        {
            archivePath = storage.FullPath(operation.BackupFileName ?? throw new OperationFailure("backup_path_invalid", reconcile: true));
            if (databaseExists || !HasReached(operation, CompanyOperationStages.ArchiveVerified))
            {
                await Renew(operation, LongLease, ct);
                await WriteVerifiedBackup(operation, archivePath, CompanyOperationStages.ArchiveWritten,
                    CompanyOperationStages.ArchiveVerified, "Archive", migrations, ct);
            }
            else if (!File.Exists(archivePath)) throw new OperationFailure("archive_file_missing", reconcile: true);
        }
        else
        {
            await Checkpoint(operation, CompanyOperationStages.ArchiveWritten, ct);
            await Checkpoint(operation, CompanyOperationStages.ArchiveVerified, ct);
        }

        if (databaseExists)
        {
            if (!CompanyDatabaseNames.IsSafeIdentifier(operation.DatabaseName)) throw new OperationFailure("database_name_invalid", reconcile: true);
            SqlConnection.ClearAllPools();
            await using var master = new SqlConnection(connections.Master(operation));
            await master.OpenAsync(ct);
            await using var drop = new SqlCommand(
                $"ALTER DATABASE [{operation.DatabaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{operation.DatabaseName}];", master)
            { CommandTimeout = 300 };
            await drop.ExecuteNonQueryAsync(ct);
            logger.LogInformation("Company operation {OperationId} dropped the company database", operation.Id);
        }
        await Checkpoint(operation, CompanyOperationStages.DatabaseDropped, ct);

        // Zwykłe kopie firmy są danymi klienta: usuwamy pliki potwierdzone manifestem. Archiwum zostaje.
        foreach (var backup in await system.CompanyBackups.AsNoTracking().Where(x => x.CompanyId == operation.CompanyId && x.RemovedAtUtc == null).ToArrayAsync(ct))
            if (await DeleteOwnedBackupFiles(backup, operation.ActorIssuerId, ct) == BackupFiles.NotOwned)
                logger.LogWarning("Company operation {OperationId}: backup {BackupId} file kept (not confirmed)", operation.Id, backup.Id);

        long? archiveSize = null; string? archiveSha = null;
        if (archivePath is not null) (archiveSize, archiveSha) = await Hash(archivePath, ct);
        await Renew(operation, Lease, ct); // Potwierdza dzierżawę tuż przed czyszczeniem.
        await Purge(operation, nip, regon, archivePath, archiveSize, archiveSha, ct);
    }

    public const string HistoryFormat = "vatowiec-company-history-v1";
    public static string HistoryPath(string archivePath) => Path.ChangeExtension(archivePath, ".history.json");

    // ADR-045: archiwum usuwanej historii z bazy System (historia z bazy firmy jest w samej kopii .bak).
    private async Task ArchiveHistory(CompanyOperation operation, string archivePath, CancellationToken ct)
    {
        var id = operation.CompanyId;
        var operations = await system.CompanyOperations.AsNoTracking().Where(x => x.CompanyId == id).OrderBy(x => x.CreatedAtUtc).ToArrayAsync(ct);
        var operationIds = operations.Select(x => x.Id).ToArray();
        var history = new
        {
            Format = HistoryFormat,
            InstallationId = operation.ActorIssuerId,
            CompanyId = id,
            operation.CompanyName,
            operation.DatabaseName,
            DeletionOperationId = operation.Id,
            ExportedAtUtc = clock.GetUtcNow(),
            Operations = operations.Select(x => new
            {
                x.Id,
                x.Kind,
                x.Stage,
                x.Status,
                x.Attempt,
                x.LastErrorCode,
                x.RequestedById,
                x.RequestedByName,
                x.DatabaseName,
                x.BackupFileName,
                x.CreatedAtUtc,
                x.UpdatedAtUtc,
                x.CompletedAtUtc
            }),
            OperationEvents = await system.CompanyOperationEvents.AsNoTracking().Where(x => operationIds.Contains(x.OperationId))
                .OrderBy(x => x.Id).ToArrayAsync(ct),
            UserAdministrationChanges = await system.UserAdministrationChanges.AsNoTracking().Where(x => x.CompanyId == id)
                .OrderBy(x => x.Id).ToArrayAsync(ct),
            CompanyAccess = await system.CompanyAccesses.AsNoTracking().Where(x => x.CompanyId == id)
                .Select(x => new { x.UserId, x.Permissions, x.CreatedAtUtc, x.UpdatedAtUtc }).ToArrayAsync(ct),
            Backups = await system.CompanyBackups.AsNoTracking().Where(x => x.CompanyId == id).OrderBy(x => x.Id).ToArrayAsync(ct),
            BackupSchedule = await system.CompanyBackupSchedules.AsNoTracking().Where(x => x.CompanyId == id)
                .Select(x => new { x.Frequency, x.Mode, x.PreferredHour, x.UpdatedAtUtc }).SingleOrDefaultAsync(ct),
            BackupSettingChanges = await system.BackupSettingChanges.AsNoTracking().Where(x => x.CompanyId == id)
                .OrderBy(x => x.Id).ToArrayAsync(ct)
        };
        Directory.CreateDirectory(Path.GetDirectoryName(archivePath)!);
        await File.WriteAllTextAsync(HistoryPath(archivePath), System.Text.Json.JsonSerializer.Serialize(history, ManifestJson), ct);
    }

    private async Task<bool> DatabaseExists(CompanyOperation operation, CancellationToken ct)
    {
        await using var master = new SqlConnection(connections.Master(operation));
        await master.OpenAsync(ct);
        await using var command = new SqlCommand("SELECT COUNT(*) FROM sys.databases WHERE name = @name", master);
        command.Parameters.AddWithValue("@name", operation.DatabaseName);
        return (int)(await command.ExecuteScalarAsync(ct))! > 0;
    }

    private async Task Purge(CompanyOperation operation, string? nip, string? regon, string? archivePath, long? archiveSize, string? archiveSha, CancellationToken ct)
    {
        var id = operation.CompanyId;
        await using var transaction = await system.Database.BeginTransactionAsync(ct);
        await UserAdministrationService.Lock(system, ct);
        await system.Database.ExecuteSqlInterpolatedAsync($"EXEC sys.sp_set_session_context @key = {PurgeContextKey}, @value = {id};", ct);
        try
        {
            // Przy usuwaniu z archiwum: usuwana historia z System trafia obok kopii bazy (stan z tej samej transakcji).
            if (archivePath is not null) await ArchiveHistory(operation, archivePath, ct);
            await system.BackupReminderSnoozes.Where(x => x.CompanyId == id).ExecuteDeleteAsync(ct);
            await system.CompanyBackupSchedules.Where(x => x.CompanyId == id).ExecuteDeleteAsync(ct);
            await system.BackupSettingChanges.Where(x => x.CompanyId == id).ExecuteDeleteAsync(ct);
            await system.UserAdministrationChanges.Where(x => x.CompanyId == id).ExecuteDeleteAsync(ct);
            await system.CompanyAccesses.Where(x => x.CompanyId == id).ExecuteDeleteAsync(ct);
            await system.CompanyBackups.Where(x => x.CompanyId == id).ExecuteDeleteAsync(ct);
            var operations = system.CompanyOperations.Where(x => x.CompanyId == id).Select(x => x.Id);
            await system.CompanyOperationEvents.Where(x => operations.Contains(x.OperationId)).ExecuteDeleteAsync(ct);
            await system.CompanyOperations.Where(x => x.CompanyId == id).ExecuteDeleteAsync(ct);
            await system.CompanyDatabases.Where(x => x.CompanyId == id).ExecuteDeleteAsync(ct);
            await system.Companies.Where(x => x.Id == id).ExecuteDeleteAsync(ct);
            if (!await system.DeletedCompanies.AnyAsync(x => x.CompanyId == id, ct))
                system.DeletedCompanies.Add(new DeletedCompany(id, operation.CompanyName, nip, regon, clock.GetUtcNow(),
                    operation.RequestedById!.Value, operation.RequestedByName, archiveSize is null ? null : operation.BackupFileName, archiveSize, archiveSha));
            system.Entry(operation).State = EntityState.Detached; // Wiersz zlecenia usunięto powyżej.
            await system.SaveChangesAsync(ct);
        }
        finally
        {
            // Po błędzie transakcja może być już wycofana — nie przesłaniamy pierwotnego wyjątku.
            try { await system.Database.ExecuteSqlRawAsync("EXEC sys.sp_set_session_context @key = N'Vatowiec.PurgeCompanyId', @value = NULL;", CancellationToken.None); }
            catch (SqlException) { }
        }
        await transaction.CommitAsync(ct);
        logger.LogInformation("Company {CompanyId} deleted and purged", id);
    }
}
