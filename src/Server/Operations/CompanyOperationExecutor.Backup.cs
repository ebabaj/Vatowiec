using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Vatowiec.Core.Domain;
using Vatowiec.SharedContracts;

namespace Vatowiec.Operations;

// ADR-044 krok 2: Requested → BackupWritten → BackupVerified → Ready, potem rotacja kopii firmy.
// BACKUP COPY_ONLY + CHECKSUM, RESTORE VERIFYONLY, manifest z SHA-256. Rotacja usuwa wyłącznie pliki
// zarejestrowane w ops.CompanyBackups i potwierdzone manifestem tej instalacji i firmy.
public sealed partial class CompanyOperationExecutor
{
    public const string ManifestFormat = "vatowiec-company-backup-v1";

    internal sealed record BackupManifest(string Format, Guid InstallationId, Guid CompanyId, string CompanyName, string DatabaseName,
        Guid OperationId, DateTimeOffset CreatedAtUtc, string RequestedBy, bool Automatic, long SizeBytes, string Sha256,
        string[] Migrations, string Application);

    private static readonly JsonSerializerOptions ManifestJson = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    private async Task ExecuteBackup(CompanyOperation operation, CancellationToken ct)
    {
        var path = storage.FullPath(operation.BackupFileName ?? throw new OperationFailure("backup_path_invalid", reconcile: true));
        var migrations = await VerifyBackupSource(operation, ct);
        await Renew(operation, LongLease, ct);
        if (!HasReached(operation, CompanyOperationStages.BackupVerified))
        {
            // Plik z planu tej operacji: brak pliku albo brak checkpointu = zapis od nowa (INIT, FORMAT).
            if (!HasReached(operation, CompanyOperationStages.BackupWritten) || !File.Exists(path))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                // Nazwa bazy z rejestracji, sprawdzona ścisłym wzorcem identyfikatora.
                await ExecuteMaster(operation, $"BACKUP DATABASE [{operation.DatabaseName}] TO DISK = @path WITH COPY_ONLY, CHECKSUM, INIT, FORMAT, NAME = @name;",
                    path, ct, ("@name", ("VATowiec " + operation.CompanyName)[..Math.Min(128, 9 + operation.CompanyName.Length)]));
            }
            await Checkpoint(operation, CompanyOperationStages.BackupWritten, ct);
            await ExecuteMaster(operation, "RESTORE VERIFYONLY FROM DISK = @path WITH CHECKSUM;", path, ct);
        }
        else if (!File.Exists(path)) throw new OperationFailure("backup_file_missing", reconcile: true);
        var (size, sha) = await Hash(path, ct);
        var createdAt = clock.GetUtcNow();
        var manifest = new BackupManifest(ManifestFormat, operation.ActorIssuerId, operation.CompanyId, operation.CompanyName,
            operation.DatabaseName, operation.Id, createdAt, operation.RequestedByName, operation.RequestedById is null, size, sha, migrations,
            typeof(CompanyOperationExecutor).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown");
        await File.WriteAllTextAsync(BackupStorage.ManifestPath(path), JsonSerializer.Serialize(manifest, ManifestJson), ct);
        await Checkpoint(operation, CompanyOperationStages.BackupVerified, ct);
        await PublishBackup(operation, size, sha, createdAt, ct);
        await Rotate(operation.CompanyId, operation.ActorIssuerId, ct);
    }

    // Źródło kopii musi nadal być zarejestrowaną bazą tej firmy, z jej własnym profilem.
    private async Task<string[]> VerifyBackupSource(CompanyOperation operation, CancellationToken ct)
    {
        var registration = await system.CompanyDatabases.AsNoTracking().SingleOrDefaultAsync(x => x.CompanyId == operation.CompanyId, ct);
        if (registration is null || !string.Equals(registration.DatabaseName, operation.DatabaseName, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(registration.ServerName, operation.ServerName, StringComparison.OrdinalIgnoreCase))
            throw new OperationFailure("company_database_changed", reconcile: true);
        await using var company = CompanyContext(operation);
        var profile = await company.CompanyProfiles.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(ct);
        if (profile is null || profile.CompanyId != operation.CompanyId) throw new OperationFailure("company_identity_mismatch", reconcile: true);
        return (await company.Database.GetAppliedMigrationsAsync(ct)).ToArray();
    }

    private async Task ExecuteMaster(CompanyOperation operation, string sql, string path, CancellationToken ct, params (string Name, string Value)[] extra)
    {
        if (!CompanyDatabaseNames.IsSafeIdentifier(operation.DatabaseName)) throw new OperationFailure("database_name_invalid", reconcile: true);
        await using var master = new SqlConnection(connections.Master(operation));
        await master.OpenAsync(ct);
        await using var command = new SqlCommand(sql, master) { CommandTimeout = (int)LongLease.TotalSeconds };
        command.Parameters.AddWithValue("@path", path);
        foreach (var (name, value) in extra) command.Parameters.AddWithValue(name, value);
        await command.ExecuteNonQueryAsync(ct);
    }

    private static async Task<(long Size, string Sha)> Hash(string path, CancellationToken ct)
    {
        await using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20, useAsync: true);
        var sha = Convert.ToHexString(await SHA256.HashDataAsync(file, ct));
        return (file.Length, sha);
    }

    private async Task PublishBackup(CompanyOperation operation, long size, string sha, DateTimeOffset createdAt, CancellationToken ct)
    {
        await using var transaction = await system.Database.BeginTransactionAsync(ct);
        if (!await system.CompanyBackups.AnyAsync(x => x.OperationId == operation.Id, ct))
            system.CompanyBackups.Add(new CompanyBackup(operation.CompanyId, operation.Id, operation.BackupFileName!, size, sha, createdAt,
                operation.RequestedById is null, operation.RequestedByName));
        var now = clock.GetUtcNow();
        operation.Reach(CompanyOperationStages.Ready, now, Lease);
        operation.Complete(now);
        system.CompanyOperationEvents.Add(Event(operation, "Completed"));
        await system.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }

    // Rotacja po udanej kopii. Błąd rotacji nie unieważnia kopii; zostaje w logu i ponowi się przy następnej kopii.
    internal async Task Rotate(Guid companyId, Guid installationId, CancellationToken ct)
    {
        try
        {
            var retained = (await system.BackupPolicies.AsNoTracking().SingleAsync(ct)).RetainedBackupsPerCompany;
            var expired = await system.CompanyBackups.Where(x => x.CompanyId == companyId && x.RemovedAtUtc == null && x.Kind == CompanyBackup.RegularKind)
                .OrderByDescending(x => x.CreatedAtUtc).ThenByDescending(x => x.Id).Skip(retained).ToListAsync(ct);
            foreach (var backup in expired)
            {
                var path = storage.FullPath(backup.FileName);
                var manifestPath = BackupStorage.ManifestPath(path);
                if (!File.Exists(path) && !File.Exists(manifestPath)) { backup.MarkRemoved(clock.GetUtcNow(), "Missing"); continue; }
                var manifest = File.Exists(manifestPath)
                    ? JsonSerializer.Deserialize<BackupManifest>(await File.ReadAllTextAsync(manifestPath, ct), ManifestJson) : null;
                if (manifest is null || manifest.Format != ManifestFormat || manifest.InstallationId != installationId ||
                    manifest.CompanyId != backup.CompanyId || manifest.OperationId != backup.OperationId)
                {
                    logger.LogWarning("Backup {BackupId} kept: manifest does not confirm ownership", backup.Id);
                    continue;
                }
                if (File.Exists(path)) File.Delete(path);
                File.Delete(manifestPath);
                backup.MarkRemoved(clock.GetUtcNow(), "Rotation");
            }
            await system.SaveChangesAsync(ct);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or OperationFailure or DbUpdateException)
        {
            logger.LogWarning("Backup rotation for company {CompanyId} incomplete ({ErrorType})", companyId, error.GetType().Name);
            system.ChangeTracker.Clear();
        }
    }
}
