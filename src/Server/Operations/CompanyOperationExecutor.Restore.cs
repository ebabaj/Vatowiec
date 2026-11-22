using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Vatowiec.Core.Application;
using Vatowiec.Core.Domain;
using Vatowiec.Core.Infrastructure.Identity;
using Vatowiec.Core.Infrastructure.Persistence;
using Vatowiec.SharedContracts;

namespace Vatowiec.Operations;

// ADR-044 krok 3: Requested → DatabaseRestored → RestoreVerified → SchemaUpgraded → Switched → Ready.
// Plik kopii (SHA-256 z manifestu i rejestru) → RESTORE do bazy roboczej → CHECKDB, tożsamość firmy, historia
// migracji → brakujące migracje → przełączenie nazw. Istniejąca firma: kopia bezpieczeństwa i dotychczasowa
// baza zostaje jako …_przed_{data}. Usunięta lub przeniesiona: nowa rejestracja pod tym samym CompanyId.
public sealed partial class CompanyOperationExecutor
{
    internal const string RestoreMarker = "Vatowiec.RestoreOperationId";
    private const int RetainedPreRestoreBackups = 2;

    private async Task ExecuteRestore(CompanyOperation operation, CancellationToken ct)
    {
        var source = storage.FullPath(operation.BackupFileName ?? throw new OperationFailure("backup_path_invalid", reconcile: true));
        var working = operation.WorkingDatabaseName!;
        var target = operation.DatabaseName;

        if (!HasReached(operation, CompanyOperationStages.DatabaseRestored))
        {
            await VerifyRestoreSource(operation, source, ct);
            await Renew(operation, LongLease, ct);
            if (!await DatabaseExists(operation, working, ct))
                await RestoreToWorking(operation, source, working, ct);
            else if (await Marker(operation, working, RestoreMarker, ct) is { } marker && marker != operation.Id.ToString("D"))
                throw new OperationFailure("working_database_not_owned", reconcile: true);
            await Checkpoint(operation, CompanyOperationStages.DatabaseRestored, ct);
        }
        if (!HasReached(operation, CompanyOperationStages.RestoreVerified))
        {
            await Renew(operation, LongLease, ct);
            try { await ExecuteOn(operation, working, $"DBCC CHECKDB([{working}]) WITH NO_INFOMSGS, ALL_ERRORMSGS;", ct); }
            catch (SqlException) { throw new OperationFailure("restore_database_corrupt", reconcile: true); }
            await using (var company = CompanyContextFor(operation, working))
            {
                var profile = await company.CompanyProfiles.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(ct);
                if (profile is null || profile.CompanyId != operation.CompanyId) throw new OperationFailure("company_identity_mismatch", reconcile: true);
                var applied = (await company.Database.GetAppliedMigrationsAsync(ct)).ToArray();
                var known = company.Database.GetMigrations().ToArray();
                // Kopia nowsza niż program albo o obcej historii — odmowa (ADR-044 krok 3).
                if (applied.Length == 0 || !applied.SequenceEqual(known.Take(applied.Length)))
                    throw new OperationFailure("restore_schema_unknown", reconcile: true);
            }
            await Checkpoint(operation, CompanyOperationStages.RestoreVerified, ct);
        }
        if (!HasReached(operation, CompanyOperationStages.SchemaUpgraded))
        {
            // Starsza wersja programu: brakujące migracje w bazie roboczej, przed przełączeniem.
            await using (var company = CompanyContextFor(operation, working)) await company.Database.MigrateAsync(ct);
            await Checkpoint(operation, CompanyOperationStages.SchemaUpgraded, ct);
        }
        if (!HasReached(operation, CompanyOperationStages.Switched))
        {
            await Renew(operation, LongLease, ct);
            await Switch(operation, working, target, ct);
            await Checkpoint(operation, CompanyOperationStages.Switched, ct);
        }
        await PublishRestore(operation, ct);
    }

    private async Task VerifyRestoreSource(CompanyOperation operation, string source, CancellationToken ct)
    {
        var manifestPath = BackupStorage.ManifestPath(source);
        if (!File.Exists(source) || !File.Exists(manifestPath)) throw new OperationFailure("backup_file_missing", reconcile: true);
        BackupManifest? manifest;
        try { manifest = JsonSerializer.Deserialize<BackupManifest>(await File.ReadAllTextAsync(manifestPath, ct), ManifestJson); }
        catch (JsonException) { manifest = null; }
        if (manifest is null || manifest.Format != ManifestFormat) throw new OperationFailure("restore_manifest_invalid", reconcile: true);
        if (manifest.CompanyId != operation.CompanyId) throw new OperationFailure("restore_company_mismatch", reconcile: true);
        var (_, sha) = await Hash(source, ct);
        var registered = await system.CompanyBackups.AsNoTracking().Where(x => x.FileName == operation.BackupFileName).Select(x => x.Sha256).FirstOrDefaultAsync(ct)
            ?? await system.DeletedCompanies.AsNoTracking().Where(x => x.ArchiveFileName == operation.BackupFileName).Select(x => x.ArchiveSha256).FirstOrDefaultAsync(ct);
        if (!string.Equals(sha, manifest.Sha256, StringComparison.OrdinalIgnoreCase) ||
            (registered is not null && !string.Equals(sha, registered, StringComparison.OrdinalIgnoreCase)))
            throw new OperationFailure("backup_checksum_mismatch", reconcile: true);
        await ExecuteMaster(operation, "RESTORE VERIFYONLY FROM DISK = @path WITH CHECKSUM;", source, ct);
    }

    private async Task RestoreToWorking(CompanyOperation operation, string source, string working, CancellationToken ct)
    {
        if (!CompanyDatabaseNames.IsProjectDatabase(working)) throw new OperationFailure("database_name_invalid", reconcile: true);
        await using var master = new SqlConnection(connections.Master(operation));
        await master.OpenAsync(ct);
        var files = new List<(string Logical, string Type)>();
        await using (var list = new SqlCommand("RESTORE FILELISTONLY FROM DISK = @path;", master) { CommandTimeout = 600 })
        {
            list.Parameters.AddWithValue("@path", source);
            await using var reader = await list.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct)) files.Add((reader.GetString(reader.GetOrdinal("LogicalName")), reader.GetString(reader.GetOrdinal("Type"))));
        }
        string dataPath, logPath;
        await using (var paths = new SqlCommand("SELECT CONVERT(nvarchar(260), SERVERPROPERTY('InstanceDefaultDataPath')), CONVERT(nvarchar(260), SERVERPROPERTY('InstanceDefaultLogPath'));", master))
        await using (var reader = await paths.ExecuteReaderAsync(ct))
        {
            await reader.ReadAsync(ct);
            (dataPath, logPath) = (reader.GetString(0), reader.GetString(1));
        }
        // Pliki fizyczne bazy roboczej pod jej własną nazwą (WITH MOVE), bez nadpisywania istniejących.
        var moves = new List<string>();
        await using var restore = new SqlCommand { Connection = master, CommandTimeout = (int)LongLease.TotalSeconds };
        restore.Parameters.AddWithValue("@path", source);
        for (var i = 0; i < files.Count; i++)
        {
            var (logical, type) = files[i];
            var physical = type == "L" ? Path.Combine(logPath, $"{working}_log{(i == 0 ? "" : i)}.ldf")
                : Path.Combine(dataPath, $"{working}{(i == 0 ? "" : "_" + i)}.{(i == 0 ? "mdf" : "ndf")}");
            restore.Parameters.AddWithValue($"@l{i}", logical);
            restore.Parameters.AddWithValue($"@p{i}", physical);
            moves.Add($"MOVE @l{i} TO @p{i}");
        }
        restore.CommandText = $"RESTORE DATABASE [{working}] FROM DISK = @path WITH {string.Join(", ", moves)}, CHECKSUM, RECOVERY;";
        await restore.ExecuteNonQueryAsync(ct);
        // Kopia odtworzonej wcześniej bazy ma już znacznik poprzedniego odtworzenia — nadpisujemy go znacznikiem tej operacji.
        await using var mark = new SqlCommand($"""
            IF EXISTS (SELECT 1 FROM [{working}].sys.extended_properties WHERE class = 0 AND name = @marker)
                EXEC [{working}].sys.sp_updateextendedproperty @name = @marker, @value = @value;
            ELSE
                EXEC [{working}].sys.sp_addextendedproperty @name = @marker, @value = @value;
            """, master);
        mark.Parameters.AddWithValue("@marker", RestoreMarker);
        mark.Parameters.AddWithValue("@value", operation.Id.ToString("D"));
        await mark.ExecuteNonQueryAsync(ct);
        logger.LogInformation("Company operation {OperationId} restored the backup into its working database", operation.Id);
    }

    private async Task Switch(CompanyOperation operation, string working, string target, CancellationToken ct)
    {
        var mine = operation.Id.ToString("D");
        var targetExists = await DatabaseExists(operation, target, ct);
        var targetIsRestored = targetExists && await Marker(operation, target, RestoreMarker, ct) == mine;
        if (operation.RestoreMode == RestoreModes.Existing)
        {
            var company = await system.Companies.SingleOrDefaultAsync(x => x.Id == operation.CompanyId, ct)
                ?? throw new OperationFailure("company_missing", reconcile: true);
            company.Deactivate(); // Na czas przełączenia firma jest niedostępna; Ready przywraca dostęp.
            await system.SaveChangesAsync(ct);
            if (targetExists && !targetIsRestored)
            {
                await PreRestoreBackup(operation, target, ct);
                var previous = operation.PreviousDatabaseName!;
                if (await DatabaseExists(operation, previous, ct)) throw new OperationFailure("previous_database_name_taken", reconcile: true);
                await Rename(operation, target, previous, ct);
                targetExists = false;
            }
        }
        else if (targetExists && !targetIsRestored) throw new OperationFailure("database_name_taken", reconcile: true);
        if (!targetIsRestored)
        {
            if (!await DatabaseExists(operation, working, ct)) throw new OperationFailure("working_database_missing", reconcile: true);
            await Rename(operation, working, target, ct);
        }
    }

    // Kopia bezpieczeństwa dotychczasowej bazy przed przełączeniem (rodzaj PreRestore, zachowywane 2 ostatnie).
    private async Task PreRestoreBackup(CompanyOperation operation, string database, CancellationToken ct)
    {
        if (await system.CompanyBackups.AnyAsync(x => x.OperationId == operation.Id, ct)) return;
        var relative = "_prerestore/" + Vatowiec.Core.Infrastructure.Operations.CompanyBackupService.BackupFileName(database, clock.GetUtcNow(), operation.Id);
        var path = storage.FullPath(relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await ExecuteMasterFor(database, operation, $"BACKUP DATABASE [{database}] TO DISK = @path WITH COPY_ONLY, CHECKSUM, INIT, FORMAT;", path, ct);
        await ExecuteMasterFor(database, operation, "RESTORE VERIFYONLY FROM DISK = @path WITH CHECKSUM;", path, ct);
        var (size, sha) = await Hash(path, ct);
        string[] migrations;
        await using (var company = CompanyContextFor(operation, database)) migrations = (await company.Database.GetAppliedMigrationsAsync(ct)).ToArray();
        var createdAt = clock.GetUtcNow();
        var manifest = new BackupManifest(ManifestFormat, CompanyBackup.PreRestoreKind, operation.ActorIssuerId, operation.CompanyId, operation.CompanyName,
            database, operation.Id, createdAt, operation.RequestedByName, false, size, sha, migrations, "pre-restore");
        await File.WriteAllTextAsync(BackupStorage.ManifestPath(path), JsonSerializer.Serialize(manifest, ManifestJson), ct);
        system.CompanyBackups.Add(new CompanyBackup(operation.CompanyId, operation.Id, relative, size, sha, createdAt, false,
            operation.RequestedByName, CompanyBackup.PreRestoreKind));
        await system.SaveChangesAsync(ct);
    }

    private async Task PublishRestore(CompanyOperation operation, CancellationToken ct)
    {
        var target = operation.DatabaseName;
        if (await Marker(operation, target, RestoreMarker, ct) != operation.Id.ToString("D"))
            throw new OperationFailure("restore_switch_incomplete", reconcile: true);
        await using var transaction = await system.Database.BeginTransactionAsync(ct);
        await UserAdministrationService.Lock(system, ct);
        var now = clock.GetUtcNow();
        if (operation.RestoreMode == RestoreModes.Existing)
            (await system.Companies.SingleAsync(x => x.Id == operation.CompanyId, ct)).Activate();
        else
        {
            if (await system.Companies.AnyAsync(x => x.Id == operation.CompanyId, ct) ||
                await system.CompanyDatabases.AnyAsync(x => x.ServerName == operation.ServerName && x.DatabaseName == target, ct))
                throw new OperationFailure("catalog_conflict", reconcile: true);
            system.Companies.Add(new Company(operation.CompanyId, operation.CompanyName));
            system.CompanyDatabases.Add(new CompanyDatabaseRegistration(operation.CompanyId, operation.ServerName, target, operation.ConnectionProfileKey));
            var restorer = operation.RequestedById!.Value;
            var user = await system.Users.AsNoTracking().Where(x => x.Id == restorer).Select(x => new { x.UserName, x.IsArchived }).SingleOrDefaultAsync(ct);
            if (user is { IsArchived: false, UserName: not null })
            {
                // Decyzja właściciela: przywracający dostaje pełne prawa (jak twórca); innym nadaje się je od nowa.
                system.CompanyAccesses.Add(new CompanyAccess(restorer, operation.CompanyId, CompanyPermissionCatalog.All));
                system.UserAdministrationChanges.Add(new UserAdministrationChange(restorer, operation.ActorIssuerId, operation.RequestedByName,
                    restorer, user.UserName, now, "CompanyRestorerAccessGranted", operation.CompanyId, CompanyPermission.None, CompanyPermissionCatalog.All));
            }
            await system.DeletedCompanies.Where(x => x.CompanyId == operation.CompanyId).ExecuteDeleteAsync(ct);
        }
        operation.Reach(CompanyOperationStages.Ready, now, Lease);
        operation.Complete(now);
        system.CompanyOperationEvents.Add(Event(operation, "Completed"));
        await system.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        await RotatePreRestore(operation.CompanyId, operation.ActorIssuerId, ct);
    }

    private async Task RotatePreRestore(Guid companyId, Guid installationId, CancellationToken ct)
    {
        try
        {
            var expired = await system.CompanyBackups.Where(x => x.CompanyId == companyId && x.RemovedAtUtc == null && x.Kind == CompanyBackup.PreRestoreKind)
                .OrderByDescending(x => x.CreatedAtUtc).ThenByDescending(x => x.Id).Skip(RetainedPreRestoreBackups).ToListAsync(ct);
            foreach (var backup in expired)
                if (await DeleteOwnedBackupFiles(backup, installationId, ct) is BackupFiles.Deleted or BackupFiles.Missing)
                    backup.MarkRemoved(clock.GetUtcNow(), "Rotation");
            await system.SaveChangesAsync(ct);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or OperationFailure or DbUpdateException)
        {
            logger.LogWarning("Pre-restore backup rotation for company {CompanyId} incomplete ({ErrorType})", companyId, error.GetType().Name);
            system.ChangeTracker.Clear();
        }
    }

    private async Task<bool> DatabaseExists(CompanyOperation operation, string database, CancellationToken ct)
    {
        await using var master = new SqlConnection(connections.Master(operation));
        await master.OpenAsync(ct);
        await using var command = new SqlCommand("SELECT COUNT(*) FROM sys.databases WHERE name = @name", master);
        command.Parameters.AddWithValue("@name", database);
        return (int)(await command.ExecuteScalarAsync(ct))! > 0;
    }

    private async Task<string?> Marker(CompanyOperation operation, string database, string marker, CancellationToken ct)
    {
        await using var connection = new SqlConnection(connections.Database(operation, database));
        await connection.OpenAsync(ct);
        await using var command = new SqlCommand("SELECT CONVERT(nvarchar(128), value) FROM sys.extended_properties WHERE class = 0 AND name = @marker", connection);
        command.Parameters.AddWithValue("@marker", marker);
        return await command.ExecuteScalarAsync(ct) as string;
    }

    private async Task Rename(CompanyOperation operation, string from, string to, CancellationToken ct)
    {
        if (!CompanyDatabaseNames.IsSafeIdentifier(from) || !CompanyDatabaseNames.IsSafeIdentifier(to))
            throw new OperationFailure("database_name_invalid", reconcile: true);
        SqlConnection.ClearAllPools();
        await using var master = new SqlConnection(connections.Master(operation));
        await master.OpenAsync(ct);
        await using var rename = new SqlCommand(
            $"ALTER DATABASE [{from}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; ALTER DATABASE [{from}] MODIFY NAME = [{to}]; ALTER DATABASE [{to}] SET MULTI_USER;",
            master)
        { CommandTimeout = 300 };
        await rename.ExecuteNonQueryAsync(ct);
        logger.LogInformation("Company operation {OperationId} renamed a database", operation.Id);
    }

    private async Task ExecuteOn(CompanyOperation operation, string database, string sql, CancellationToken ct)
    {
        await using var connection = new SqlConnection(connections.Database(operation, database));
        await connection.OpenAsync(ct);
        await using var command = new SqlCommand(sql, connection) { CommandTimeout = (int)LongLease.TotalSeconds };
        await command.ExecuteNonQueryAsync(ct);
    }

    private async Task ExecuteMasterFor(string database, CompanyOperation operation, string sql, string path, CancellationToken ct)
    {
        if (!CompanyDatabaseNames.IsSafeIdentifier(database)) throw new OperationFailure("database_name_invalid", reconcile: true);
        await using var master = new SqlConnection(connections.Master(operation));
        await master.OpenAsync(ct);
        await using var command = new SqlCommand(sql, master) { CommandTimeout = (int)LongLease.TotalSeconds };
        command.Parameters.AddWithValue("@path", path);
        await command.ExecuteNonQueryAsync(ct);
    }

    private CompanyDbContext CompanyContextFor(CompanyOperation operation, string database) => new(
        new DbContextOptionsBuilder<CompanyDbContext>().UseSqlServer(connections.Database(operation, database)).Options,
        new CompanyContext(operation.CompanyId));
}
