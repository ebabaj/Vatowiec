using System.Runtime.Versioning;
using System.Security.Principal;
using Microsoft.EntityFrameworkCore;
using Vatowiec.Core.Infrastructure.Persistence;
using Vatowiec.Core.Infrastructure.ServerTls;
using Vatowiec.SharedContracts.ServerTls;

namespace Vatowiec.Operations.ServerTls;

// ADR-047: lokalna komenda administracyjna na serwerze, bez zdalnej ścieżki obejścia.
//   Vatowiec.Operations tls status            — tryb, kod połączenia, terminy ważności (bez sekretów)
//   Vatowiec.Operations tls reset-mode-b      — przywraca tryb CA instalacji (wymaga administratora Windows), z audytem
internal static class ServerTlsCommand
{
    public static bool Matches(string[] args) => args is ["tls", ..];

    [SupportedOSPlatform("windows")]
    public static async Task<int> Run(string[] args, IConfiguration configuration)
    {
        var connection = configuration.GetConnectionString("VatowiecSystem");
        if (string.IsNullOrWhiteSpace(connection)) { Console.Error.WriteLine("Brak ConnectionStrings:VatowiecSystem."); return 2; }
        var options = new DbContextOptionsBuilder<SystemDbContext>().UseSqlServer(connection).Options;
        await using var db = new SystemDbContext(options);
        switch (args)
        {
            case ["tls", "status"]:
            {
                var settings = await db.ServerTlsConfigurations.AsNoTracking().SingleOrDefaultAsync();
                var state = await db.ServerTlsStates.AsNoTracking().SingleOrDefaultAsync();
                if (settings == null) { Console.Error.WriteLine("Baza System nie zawiera migracji ADR-047."); return 3; }
                Console.WriteLine("Tryb: " + (settings.Mode == ServerTlsModeNames.InstallationCa ? "CA instalacji (B)" : "certyfikat własny (A)"));
                Console.WriteLine("Katalog: " + ServerTlsPaths.From(configuration).Root);
                if (state?.CaSpkiSha256 is { } hash)
                {
                    Console.WriteLine("Kod połączenia: " + ConnectionCode.Format(Convert.FromHexString(hash)));
                    Console.WriteLine($"CA ważne do: {state.CaNotAfter:yyyy-MM-dd}, certyfikat serwera do: {state.ServerCertificateNotAfter:yyyy-MM-dd}");
                    Console.WriteLine("Nazwy certyfikatu serwera: " + string.Join(", ", state.ServerCertificateNameList));
                }
                else Console.WriteLine("CA instalacji jeszcze nie istnieje (utworzy je uruchomiona usługa).");
                if (state?.LastErrorCode is { } error) Console.WriteLine("Ostatni błąd: " + error);
                return 0;
            }
            case ["tls", "reset-mode-b"]:
            {
                using var identity = WindowsIdentity.GetCurrent();
                if (!new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator))
                {
                    Console.Error.WriteLine("Komenda wymaga uruchomienia jako administrator Windows (Uruchom jako administrator).");
                    return 4;
                }
                var changed = await ServerTlsService.LocalRecovery(db, "Lokalny administrator Windows: " + identity.Name, TimeProvider.System, CancellationToken.None);
                Console.WriteLine(changed ? "Przywrócono tryb CA instalacji. API przełączy certyfikat w ciągu 30 s." : "Tryb CA instalacji był już ustawiony.");
                return 0;
            }
            default:
                Console.Error.WriteLine("Użycie: Vatowiec.Operations tls status | tls reset-mode-b");
                return 1;
        }
    }
}
