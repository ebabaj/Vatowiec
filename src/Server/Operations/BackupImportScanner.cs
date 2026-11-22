using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Vatowiec.Core.Domain;
using Vatowiec.Core.Infrastructure.Persistence;

namespace Vatowiec.Operations;

// ADR-044 krok 3: pliki .bak z manifestem VATowca w {BackupRoot}\_import (np. z innej instalacji).
// Usługa przepisuje metadane z manifestu do ops.RestoreImportFiles; API nie ma dostępu do katalogu kopii.
// SHA-256 pliku jest sprawdzana dopiero przy odtwarzaniu.
public sealed class BackupImportScanner(SystemDbContext system, BackupStorage storage, TimeProvider clock, ILogger<BackupImportScanner> logger)
{
    public const string ImportFolder = "_import";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<int> Scan(CancellationToken ct)
    {
        string directory;
        try { directory = Path.Combine(storage.Root, ImportFolder); }
        catch (OperationFailure) { return 0; } // Brak katalogu kopii w konfiguracji.
        var found = new Dictionary<string, CompanyOperationExecutor.BackupManifest>(StringComparer.OrdinalIgnoreCase);
        if (Directory.Exists(directory))
            foreach (var manifestPath in Directory.EnumerateFiles(directory, "*.manifest.json", SearchOption.TopDirectoryOnly))
            {
                var backup = manifestPath[..^".manifest.json".Length] + ".bak";
                try
                {
                    var manifest = JsonSerializer.Deserialize<CompanyOperationExecutor.BackupManifest>(await File.ReadAllTextAsync(manifestPath, ct), Json);
                    if (manifest is null || manifest.Format != CompanyOperationExecutor.ManifestFormat || !File.Exists(backup) ||
                        manifest.CompanyId == Guid.Empty || string.IsNullOrWhiteSpace(manifest.CompanyName)) continue;
                    found[$"{ImportFolder}/{Path.GetFileName(backup)}"] = manifest;
                }
                catch (Exception error) when (error is JsonException or IOException or UnauthorizedAccessException)
                {
                    logger.LogWarning("Import manifest skipped ({ErrorType})", error.GetType().Name);
                }
            }
        var rows = await system.RestoreImportFiles.ToListAsync(ct);
        foreach (var row in rows.Where(x => !found.ContainsKey(x.FileName))) system.RestoreImportFiles.Remove(row);
        var now = clock.GetUtcNow();
        foreach (var (file, manifest) in found)
        {
            var size = new FileInfo(storage.FullPath(file)).Length;
            var row = rows.SingleOrDefault(x => string.Equals(x.FileName, file, StringComparison.OrdinalIgnoreCase));
            try
            {
                if (row is null) system.RestoreImportFiles.Add(new RestoreImportFile(file, manifest.CompanyId, manifest.CompanyName,
                    manifest.InstallationId, manifest.CreatedAtUtc, size, manifest.Sha256, now));
                else row.Update(manifest.CompanyId, manifest.CompanyName, manifest.InstallationId, manifest.CreatedAtUtc, size, manifest.Sha256, now);
            }
            catch (ArgumentException) { logger.LogWarning("Import manifest skipped (invalid values)"); }
        }
        await system.SaveChangesAsync(ct);
        return found.Count;
    }
}
