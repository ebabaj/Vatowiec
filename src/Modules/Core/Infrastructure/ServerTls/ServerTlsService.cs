using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Vatowiec.Core.Domain;
using Vatowiec.Core.Infrastructure.Operations;
using Vatowiec.Core.Infrastructure.Persistence;
using Vatowiec.SharedContracts.ServerTls;

namespace Vatowiec.Core.Infrastructure.ServerTls;

public sealed class ServerTlsException(int status, string code, string message) : Exception(message)
{
    public int Status { get; } = status;
    public string Code { get; } = code;
}

// ADR-047: ustawienia certyfikatu serwera. Wywoływane wyłącznie z endpointów z polityką administratora
// instalacji (świeża kontrola roli w DB) i CSRF. API zapisuje konfigurację i audyt; klucze, CA i certyfikat
// trybu CA instalacji tworzy wyłącznie usługa Operations.
public sealed class ServerTlsService(SystemDbContext db, TimeProvider clock)
{
    public const int HistoryLimit = 30;
    private const string Stale = "Ustawienia certyfikatu serwera zmieniły się. Odśwież widok przed zapisem.";

    public async Task<ServerTlsResponse> Read(ServerTlsServedCertificate served, CancellationToken ct)
    {
        var configuration = await Configuration(ct);
        var state = await db.ServerTlsStates.AsNoTracking().SingleOrDefaultAsync(ct);
        var history = await db.ServerTlsChanges.AsNoTracking().OrderByDescending(x => x.Id).Take(HistoryLimit)
            .Select(x => new ServerTlsHistory(x.Id, x.AtUtc, x.Kind, x.ActorName, x.BeforeJson, x.AfterJson)).ToArrayAsync(ct);
        return Response(configuration, state, served, history, clock.GetUtcNow());
    }

    // ownCertificateCheck: wybór certyfikatu własnego w procesie API (konto, które musi mieć dostęp do klucza).
    public async Task<ServerTlsResponse> Save(OperationRequester requester, ServerTlsRequest value,
        Func<IReadOnlyCollection<string>, string?, OwnCertificateSelectionResult> ownCertificateCheck, ServerTlsServedCertificate served, CancellationToken ct)
    {
        var configuration = await Configuration(ct);
        if (value.RowVersion != Convert.ToBase64String(configuration.RowVersion)) throw new ServerTlsException(409, "server_tls_stale", Stale);
        var before = Json(configuration);
        try { configuration.Change(value.Mode, value.DnsNames ?? [], value.IpAddresses ?? [], value.IssuerThumbprint); }
        catch (ArgumentException error) { throw new ServerTlsException(400, "server_tls_invalid", error.Message); }
        if (configuration.Mode == ServerTlsModes.OwnCertificate)
        {
            var check = ownCertificateCheck([.. configuration.DnsNameList, .. configuration.IpAddressList], configuration.IssuerThumbprint);
            if (check.Certificate == null) throw new ServerTlsException(409, check.ErrorCode ?? "tls_own_not_found", check.Message);
        }
        var after = Json(configuration);
        if (after == before) return await Read(served, ct);
        db.ServerTlsChanges.Add(new(ServerTlsChangeKinds.ConfigurationChanged, requester.UserId, requester.Name, clock.GetUtcNow(), before, after));
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { throw new ServerTlsException(409, "server_tls_stale", Stale); }
        return await Read(served, ct);
    }

    // Awaryjna wymiana CA: po wykonaniu przez Operations każda stacja potrzebuje nowego kodu lub pliku połączenia.
    public async Task<ServerTlsResponse> RequestCaReplacement(OperationRequester requester, ServerTlsCaReplacementRequest value,
        ServerTlsServedCertificate served, CancellationToken ct)
    {
        if (!value.Confirm) throw new ServerTlsException(400, "server_tls_confirmation_required", "Potwierdź wymianę CA instalacji.");
        var configuration = await Configuration(ct);
        if (value.RowVersion != Convert.ToBase64String(configuration.RowVersion)) throw new ServerTlsException(409, "server_tls_stale", Stale);
        var state = await db.ServerTlsStates.AsNoTracking().SingleOrDefaultAsync(ct);
        if (state?.CaSpkiSha256 == null)
            throw new ServerTlsException(409, "server_tls_ca_missing", "CA instalacji jeszcze nie istnieje; usługa VATowiec Operations utworzy je po uruchomieniu.");
        if (configuration.CaGeneration > state.CaGeneration)
            throw new ServerTlsException(409, "server_tls_replacement_pending", "Wymiana CA instalacji została już zlecona i czeka na usługę VATowiec Operations.");
        var before = JsonSerializer.Serialize(new { caGeneration = configuration.CaGeneration, caThumbprint = state?.CaThumbprint });
        configuration.RequestCaReplacement();
        db.ServerTlsChanges.Add(new(ServerTlsChangeKinds.CaReplacementRequested, requester.UserId, requester.Name, clock.GetUtcNow(), before,
            JsonSerializer.Serialize(new { caGeneration = configuration.CaGeneration })));
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { throw new ServerTlsException(409, "server_tls_stale", Stale); }
        return await Read(served, ct);
    }

    // Plik połączenia dla stacji: adres, pod którym administrator jest połączony, oraz skonfigurowane nazwy z tym samym portem.
    public async Task<ConnectionFileContent> ConnectionFile(Uri requestOrigin, string installationName, CancellationToken ct)
    {
        var state = await db.ServerTlsStates.AsNoTracking().SingleOrDefaultAsync(ct);
        if (state?.CaCertificate == null)
            throw new ServerTlsException(404, "server_tls_ca_missing", "CA instalacji nie zostało jeszcze utworzone przez usługę VATowiec Operations.");
        var ca = X509CertificateLoader.LoadCertificate(state.CaCertificate);
        var port = requestOrigin.IsDefaultPort ? "" : ":" + requestOrigin.Port;
        var names = state.ServerCertificateNameList.Where(x => x is not ("localhost" or "127.0.0.1" or "::1"))
            .Select(x => System.Net.IPAddress.TryParse(x, out var ip) && ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6 ? "[" + x + "]" : x);
        string[] addresses = [requestOrigin.GetLeftPart(UriPartial.Authority) + "/", .. names.Select(x => "https://" + x + port + "/")];
        return SharedContracts.ServerTls.ConnectionFile.Create(installationName, addresses, ca);
    }

    // Lokalna komenda odzyskiwania (usługa Operations, administrator Windows): przywraca tryb CA instalacji.
    public static async Task<bool> LocalRecovery(SystemDbContext db, string actorName, TimeProvider clock, CancellationToken ct)
    {
        var configuration = await db.ServerTlsConfigurations.SingleOrDefaultAsync(ct)
            ?? throw new InvalidOperationException("Brak konfiguracji certyfikatu serwera. Wykonaj migrację bazy System.");
        if (configuration.Mode == ServerTlsModes.InstallationCa) return false;
        var before = Json(configuration);
        configuration.Change(ServerTlsModes.InstallationCa, configuration.DnsNameList, configuration.IpAddressList, null);
        db.ServerTlsChanges.Add(new(ServerTlsChangeKinds.LocalRecovery, null, actorName, clock.GetUtcNow(), before, Json(configuration)));
        await db.SaveChangesAsync(ct);
        return true;
    }

    private async Task<ServerTlsConfiguration> Configuration(CancellationToken ct) =>
        await db.ServerTlsConfigurations.SingleOrDefaultAsync(ct)
        ?? throw new ServerTlsException(503, "server_tls_not_migrated", "Baza systemowa nie zawiera ustawień certyfikatu serwera. Wymagana jest migracja bazy System.");

    private static string Json(ServerTlsConfiguration x) => JsonSerializer.Serialize(new
    {
        mode = x.Mode,
        dnsNames = x.DnsNameList,
        ipAddresses = x.IpAddressList,
        issuerThumbprint = x.IssuerThumbprint
    });

    public static ServerTlsResponse Response(ServerTlsConfiguration configuration, ServerTlsState? state, ServerTlsServedCertificate served,
        ServerTlsHistory[] history, DateTimeOffset now)
    {
        var replacementPending = state?.CaSpkiSha256 != null && configuration.CaGeneration > state.CaGeneration;
        var ca = new ServerTlsInstallationCaState(state?.CaSpkiSha256 != null, replacementPending,
            state?.CaSpkiSha256 is { } hash ? ConnectionCode.Format(Convert.FromHexString(hash)) : null,
            state?.CaThumbprint, state?.CaNotAfter, state?.ServerCertificateThumbprint, state?.ServerCertificateNotAfter,
            state?.ServerCertificateNameList ?? [], state?.LastErrorCode, state?.LastErrorAtUtc, state?.UpdatedAtUtc);
        return new(configuration.Mode, configuration.DnsNameList, configuration.IpAddressList, configuration.IssuerThumbprint,
            Convert.ToBase64String(configuration.RowVersion), ca, served, Warnings(configuration, state, served, now), history);
    }

    private static string[] Warnings(ServerTlsConfiguration configuration, ServerTlsState? state, ServerTlsServedCertificate served, DateTimeOffset now)
    {
        var warnings = new List<string>();
        if (state?.CaSpkiSha256 == null)
            warnings.Add("CA instalacji jeszcze nie istnieje. Tworzy je usługa VATowiec Operations po uruchomieniu na serwerze.");
        if (state?.CaNotAfter is { } caEnd && caEnd - now < TimeSpan.FromDays(180))
            warnings.Add($"CA instalacji wygasa {caEnd:yyyy-MM-dd}. Zleć wymianę CA i przekaż stacjom nowy kod połączenia przed tym terminem.");
        if (state?.LastErrorCode is { } error)
            warnings.Add("Ostatnia operacja usługi VATowiec Operations zakończyła się błędem: " + error + ".");
        if (!served.Managed)
            warnings.Add("Ten proces API nie podaje certyfikatu zarządzanego przez VATowiec (brak ServerTls:Endpoints). Certyfikat pochodzi z konfiguracji Kestrela.");
        else if (served.ErrorCode is { } servedError)
            warnings.Add("API nie może użyć certyfikatu: " + servedError + ".");
        else if (configuration.Mode == ServerTlsModes.OwnCertificate && served.Source != ServerTlsModes.OwnCertificate)
            warnings.Add("API jeszcze nie przełączyło się na certyfikat własny (odświeżenie co 30 s).");
        return [.. warnings];
    }
}
