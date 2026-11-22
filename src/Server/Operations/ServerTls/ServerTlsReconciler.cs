using System.Net;
using System.Net.NetworkInformation;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Vatowiec.Core.Domain;
using Vatowiec.Core.Infrastructure.Persistence;
using Vatowiec.SharedContracts.ServerTls;

namespace Vatowiec.Operations.ServerTls;

// ADR-047: usługa Operations doprowadza stan do konfiguracji zapisanej przez administratora:
// tworzy CA instalacji (pierwsze uruchomienie albo zlecona wymiana), wystawia certyfikat serwera
// (brak, zmiana nazw, inne CA, mniej niż 30 dni ważności) i zapisuje plik oraz kod połączenia.
// CA powstaje wyłącznie, gdy stan nie zna żadnego CA albo zlecono wymianę; utrata plików znanego CA
// jest błędem wymagającym świadomej decyzji administratora (nowy kod dla wszystkich stacji).
[SupportedOSPlatform("windows")]
internal sealed class ServerTlsReconciler(SystemDbContext db, ServerTlsStore store, IConfiguration configuration, TimeProvider clock,
    ILogger<ServerTlsReconciler> logger)
{
    public const string ActorName = "VATowiec Operations";

    public async Task Run(CancellationToken ct)
    {
        var settings = await db.ServerTlsConfigurations.AsNoTracking().SingleOrDefaultAsync(ct);
        if (settings == null) return; // baza System bez migracji ADR-047
        var state = await db.ServerTlsStates.SingleOrDefaultAsync(ct);
        if (state == null) { state = ServerTlsState.Create(); db.ServerTlsStates.Add(state); }
        var now = clock.GetUtcNow();
        try
        {
            store.EnsureDirectories();
            var installation = await db.InstallationIdentities.AsNoTracking().Select(x => x.Id).SingleOrDefaultAsync(ct);
            if (installation == Guid.Empty) throw new ServerTlsFailure("tls_installation_missing");
            var changed = false;

            X509Certificate2 ca;
            var newCa = state.CaSpkiSha256 == null || settings.CaGeneration > state.CaGeneration;
            if (newCa)
            {
                var previous = state.CaThumbprint;
                ca = InstallationCertificateFactory.CreateCa(InstallationName, now);
                store.SaveCa(ca, installation, now);
                var hash = Convert.ToHexString(InstallationCertificateValidation.SpkiSha256(ca));
                state.CaCreated(settings.CaGeneration, hash, ca.Thumbprint, ca.NotAfter.ToUniversalTime(), ca.RawData, now);
                db.ServerTlsChanges.Add(new(ServerTlsChangeKinds.CaCreated, null, ActorName, now,
                    previous == null ? null : JsonSerializer.Serialize(new { caThumbprint = previous }),
                    JsonSerializer.Serialize(new { caThumbprint = ca.Thumbprint, caNotAfter = ca.NotAfter.ToUniversalTime(), generation = settings.CaGeneration })));
                changed = true;
            }
            else
            {
                ca = store.LoadCaWithKey(installation) ?? throw new ServerTlsFailure("tls_ca_missing");
                if (!string.Equals(Convert.ToHexString(InstallationCertificateValidation.SpkiSha256(ca)), state.CaSpkiSha256, StringComparison.OrdinalIgnoreCase))
                    throw new ServerTlsFailure("tls_ca_mismatch");
            }

            var (names, addresses) = DesiredNames(settings);
            var current = store.LoadServer();
            var reason = current is not { } existing ? "missing"
                : !SameKey(existing.Ca, ca) || !IssuedBy(existing.Server, ca) ? "ca_changed"
                : existing.Server.NotAfter.ToUniversalTime() - now < InstallationCertificateFactory.RenewBefore ? "renewal"
                : !SameNames(existing.Server, names, addresses) ? "names_changed" : null;
            if (reason != null)
            {
                using var server = InstallationCertificateFactory.CreateServer(ca, names, addresses, now);
                store.SaveServer(server, ca);
                string[] all = [.. names, .. addresses.Select(x => x.ToString())];
                state.ServerCertificateIssued(server.Thumbprint, server.NotAfter.ToUniversalTime(), all, now);
                db.ServerTlsChanges.Add(new(ServerTlsChangeKinds.ServerCertificateIssued, null, ActorName, now,
                    current is { } old ? JsonSerializer.Serialize(new { thumbprint = old.Server.Thumbprint, notAfter = old.Server.NotAfter.ToUniversalTime() }) : null,
                    JsonSerializer.Serialize(new { thumbprint = server.Thumbprint, notAfter = server.NotAfter.ToUniversalTime(), names = all, reason })));
                changed = true;
            }
            if (state.LastErrorCode != null) { state.Succeeded(now); changed = true; }
            if (changed) await db.SaveChangesAsync(ct);

            // Plik połączenia dopiero po zapisaniu stanu: kod na dysku zawsze dotyczy CA znanego bazie System.
            var port = Math.Clamp(configuration.GetValue("ServerTls:PublicPort", 7443), 1, 65535);
            var origins = names.Where(x => x != "localhost").Concat(addresses.Where(x => !IPAddress.IsLoopback(x))
                .Select(x => x.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6 ? "[" + x + "]" : x.ToString()))
                .Select(x => $"https://{x}:{port}/").DefaultIfEmpty($"https://localhost:{port}/");
            var content = ConnectionFile.Create(InstallationName, origins, ca);
            if (store.WriteConnection(ConnectionFile.Serialize(content), ConnectionCode.Format(Convert.FromHexString(content.CaSpkiSha256))))
                logger.LogInformation("Server TLS connection file updated");
        }
        catch (Exception error) when (error is ServerTlsFailure or IOException or UnauthorizedAccessException or CryptographicException)
        {
            var code = error is ServerTlsFailure failure ? failure.Code : error is UnauthorizedAccessException ? "tls_storage_access_denied" : "tls_storage_error";
            logger.LogWarning("Server TLS reconciliation failed ({Code})", code);
            db.ChangeTracker.Clear();
            var fresh = await db.ServerTlsStates.SingleOrDefaultAsync(ct);
            if (fresh == null) { fresh = ServerTlsState.Create(); db.ServerTlsStates.Add(fresh); }
            if (fresh.LastErrorCode != code) { fresh.Failed(code, now); await db.SaveChangesAsync(ct); }
        }
    }

    private static string InstallationName => Environment.MachineName;

    internal static (string[] Names, IPAddress[] Addresses) DesiredNames(ServerTlsConfiguration settings)
    {
        var names = settings.DnsNameList.Length > 0 ? settings.DnsNameList.ToList() : DefaultNames();
        if (!names.Contains("localhost")) names.Add("localhost");
        var addresses = settings.IpAddressList.Select(IPAddress.Parse).ToList();
        foreach (var loopback in new[] { IPAddress.Loopback, IPAddress.IPv6Loopback })
            if (!addresses.Contains(loopback)) addresses.Add(loopback);
        return ([.. names], [.. addresses]);
    }

    private static List<string> DefaultNames()
    {
        var host = Environment.MachineName.ToLowerInvariant();
        var result = new List<string> { host };
        var domain = IPGlobalProperties.GetIPGlobalProperties().DomainName?.Trim().TrimEnd('.').ToLowerInvariant();
        if (!string.IsNullOrEmpty(domain))
        {
            try { result.Add(ServerTlsConfiguration.NormalizeDnsNames([host + "." + domain])[0]); }
            catch (ArgumentException) { /* nietypowa nazwa domeny: zostaje sama nazwa komputera */ }
        }
        return result;
    }

    private static bool SameKey(X509Certificate2 a, X509Certificate2 b) =>
        InstallationCertificateValidation.SpkiSha256(a).AsSpan().SequenceEqual(InstallationCertificateValidation.SpkiSha256(b));

    private static bool IssuedBy(X509Certificate2 server, X509Certificate2 ca)
    {
        using var chain = new X509Chain();
        chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
        chain.ChainPolicy.VerificationFlags = X509VerificationFlags.AllowUnknownCertificateAuthority;
        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
        chain.ChainPolicy.DisableCertificateDownloads = true;
        chain.ChainPolicy.ExtraStore.Add(X509CertificateLoader.LoadCertificate(ca.RawData));
        return chain.Build(server) && chain.ChainElements.Count == 2 && SameKey(chain.ChainElements[1].Certificate, ca);
    }

    private static bool SameNames(X509Certificate2 server, string[] names, IPAddress[] addresses)
    {
        var extension = server.Extensions.OfType<X509SubjectAlternativeNameExtension>().FirstOrDefault();
        if (extension == null) return false;
        var dns = extension.EnumerateDnsNames().Select(x => x.ToLowerInvariant()).ToHashSet();
        var ips = extension.EnumerateIPAddresses().ToHashSet();
        return dns.SetEquals(names) && ips.SetEquals(addresses);
    }
}
