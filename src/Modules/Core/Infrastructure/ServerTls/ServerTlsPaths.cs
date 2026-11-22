using Microsoft.Extensions.Configuration;

namespace Vatowiec.Core.Infrastructure.ServerTls;

// ADR-047: katalog certyfikatu serwera na komputerze serwera (ServerTls:Root; domyślnie %ProgramData%\Vatowiec\Server\Tls).
// ca\         — certyfikat CA (publiczny) i klucz CA chroniony Data Protection + DPAPI konta usługi Operations;
// server\     — bieżący certyfikat serwera z kluczem (odczyt dla konta API);
// connection\ — plik i kod połączenia (dane publiczne).
public sealed class ServerTlsPaths
{
    public ServerTlsPaths(string root)
    {
        if (string.IsNullOrWhiteSpace(root) || !Path.IsPathFullyQualified(root))
            throw new InvalidOperationException("ServerTls:Root musi być bezwzględną ścieżką lokalną.");
        Root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
    }

    public static ServerTlsPaths From(IConfiguration configuration) => new(configuration["ServerTls:Root"] is { Length: > 0 } root ? root
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Vatowiec", "Server", "Tls"));

    public string Root { get; }
    public string CaDirectory => Path.Combine(Root, "ca");
    public string CaCertificate => Path.Combine(CaDirectory, "installation-ca.cer");
    public string CaProtectedKey => Path.Combine(CaDirectory, "installation-ca.key.protected");
    public string KeyRing => Path.Combine(CaDirectory, "keyring");
    public string ServerDirectory => Path.Combine(Root, "server");
    public string ServerPfx => Path.Combine(ServerDirectory, "server-current.pfx");
    public string ConnectionDirectory => Path.Combine(Root, "connection");
    public string ConnectionFile => Path.Combine(ConnectionDirectory, "VATowiec" + SharedContracts.ServerTls.ConnectionFile.Extension);
    public string ConnectionCodeFile => Path.Combine(ConnectionDirectory, "kod-polaczenia.txt");
}
