namespace Vatowiec.Operations;

// Katalog kopii na serwerze (Operations:BackupRoot). Musi być dostępny do zapisu dla konta usługi SQL Server
// (BACKUP) oraz do odczytu i usuwania dla usługi VATowiec Operations (SHA-256, manifest, rotacja).
public sealed class BackupStorage(IConfiguration configuration)
{
    public string Root
    {
        get
        {
            var root = configuration["Operations:BackupRoot"];
            if (string.IsNullOrWhiteSpace(root) || !Path.IsPathFullyQualified(root))
                throw new OperationFailure("backup_root_missing", reconcile: false);
            return Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
    }

    // Ścieżka względna pochodzi z planu operacji; nigdy nie wychodzi poza katalog kopii.
    public string FullPath(string relative)
    {
        if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative) || relative.Contains("..", StringComparison.Ordinal))
            throw new OperationFailure("backup_path_invalid", reconcile: true);
        var root = Root;
        var full = Path.GetFullPath(Path.Combine(root, relative));
        if (!full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new OperationFailure("backup_path_invalid", reconcile: true);
        return full;
    }

    public static string ManifestPath(string backupPath) => Path.ChangeExtension(backupPath, ".manifest.json");
}
