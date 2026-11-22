using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Vatowiec.SharedContracts.ServerTls;

namespace Vatowiec.Desktop.Services;

// ADR-047: przypięte kotwice CA instalacji, każda dla dokładnie jednego adresu API (origin).
// Dane niesekretne (skrót klucza CA i certyfikat CA bez klucza) w LocalAppData, poza repozytorium.
// Nic nie trafia do systemowego magazynu certyfikatów Windows.
public interface IServerTrustStore
{
    InstallationTrustAnchor? Find(string apiAddress);
    void Save(string apiAddress, InstallationTrustAnchor anchor, string source);
    void Remove(string apiAddress);
}

public sealed record ServerTrustEntry(string Origin, string CaSpkiSha256, string? CaCertificate, string Source, DateTimeOffset PairedAtUtc);
public sealed record ServerTrustFile(int Version = 1, ServerTrustEntry[]? Entries = null);

public sealed class MemoryServerTrustStore : IServerTrustStore
{
    private readonly Dictionary<string, InstallationTrustAnchor> anchors = new(StringComparer.Ordinal);
    public InstallationTrustAnchor? Find(string apiAddress) => anchors.GetValueOrDefault(ConnectionFile.NormalizeOrigin(apiAddress));
    public void Save(string apiAddress, InstallationTrustAnchor anchor, string source) => anchors[ConnectionFile.NormalizeOrigin(apiAddress)] = anchor;
    public void Remove(string apiAddress) => anchors.Remove(ConnectionFile.NormalizeOrigin(apiAddress));
}

public sealed class FileServerTrustStore(string? filePath = null) : IServerTrustStore
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, WriteIndented = true };
    private readonly string path = filePath ?? DefaultPath();

    private static string DefaultPath()
    {
        var directory = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.IsPathFullyQualified(directory) ? Path.Combine(directory, "Vatowiec", "Desktop", "server-trust.json") : "";
    }

    public InstallationTrustAnchor? Find(string apiAddress)
    {
        var origin = ConnectionFile.NormalizeOrigin(apiAddress);
        var entry = Load().Entries?.FirstOrDefault(x => x.Origin == origin);
        if (entry == null) return null;
        try
        {
            return new InstallationTrustAnchor(Convert.FromHexString(entry.CaSpkiSha256),
                entry.CaCertificate == null ? null : Convert.FromBase64String(entry.CaCertificate));
        }
        catch (Exception e) when (e is FormatException or ArgumentException)
        {
            throw new LocalLoginStorageException("Zapisane zaufanie do serwera jest uszkodzone. Usuń je i wpisz ponownie kod połączenia.");
        }
    }

    public void Save(string apiAddress, InstallationTrustAnchor anchor, string source)
    {
        var origin = ConnectionFile.NormalizeOrigin(apiAddress);
        var entry = new ServerTrustEntry(origin, Convert.ToHexString(anchor.CaSpkiSha256),
            anchor.CaCertificate == null ? null : Convert.ToBase64String(anchor.CaCertificate), source, DateTimeOffset.UtcNow);
        var current = Load();
        Write(current with { Entries = [.. (current.Entries ?? []).Where(x => x.Origin != origin), entry] });
    }

    public void Remove(string apiAddress)
    {
        var origin = ConnectionFile.NormalizeOrigin(apiAddress);
        var current = Load();
        if (current.Entries?.Any(x => x.Origin == origin) != true) return;
        Write(current with { Entries = [.. current.Entries.Where(x => x.Origin != origin)] });
    }

    private static bool Expected(Exception e) => e is IOException or UnauthorizedAccessException or System.Security.SecurityException or JsonException or ArgumentException or NotSupportedException;

    // Odczyt z pamięci, dopóki plik się nie zmienił (status zaufania jest odświeżany razem z widokiem logowania).
    private (DateTime Stamp, long Length, ServerTrustFile Value)? cache;

    private ServerTrustFile Load()
    {
        try
        {
            if (!Path.IsPathFullyQualified(path)) throw new ArgumentException();
            var info = new FileInfo(path);
            if (!info.Exists) return new();
            if (info.Length > 1048576) throw new JsonException();
            if (cache is { } cached && cached.Stamp == info.LastWriteTimeUtc && cached.Length == info.Length) return cached.Value;
            var value = JsonSerializer.Deserialize<ServerTrustFile>(File.ReadAllText(path), Json) ?? throw new JsonException();
            if (value.Version != 1 || value.Entries?.Length > 200 || value.Entries?.Any(x => x?.Origin == null) == true) throw new JsonException();
            cache = (info.LastWriteTimeUtc, info.Length, value);
            return value;
        }
        catch (Exception e) when (Expected(e)) { throw new LocalLoginStorageException("Nie można odczytać zapisanego zaufania do serwerów VATowiec."); }
    }

    private void Write(ServerTrustFile value)
    {
        string? temporary = null;
        try
        {
            if (!Path.IsPathFullyQualified(path)) throw new ArgumentException();
            var directory = Path.GetDirectoryName(Path.GetFullPath(path))!;
            Directory.CreateDirectory(directory);
            temporary = Path.Combine(directory, "trust-" + Guid.NewGuid().ToString("N") + ".tmp");
            File.WriteAllText(temporary, JsonSerializer.Serialize(value, Json), Encoding.UTF8);
            File.Move(temporary, path, overwrite: true);
        }
        catch (Exception e) when (Expected(e)) { throw new LocalLoginStorageException("Nie można zapisać zaufania do serwera VATowiec."); }
        finally
        {
            if (temporary != null)
                try { File.Delete(temporary); } catch (Exception e) when (Expected(e)) { /* tylko plik tymczasowy z danymi publicznymi */ }
        }
    }
}
