using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Server.Kestrel.Https;
using Microsoft.EntityFrameworkCore;
using Vatowiec.Core.Domain;
using Vatowiec.Core.Infrastructure.Persistence;
using Vatowiec.Core.Infrastructure.ServerTls;
using Vatowiec.SharedContracts.ServerTls;

namespace Vatowiec.Api.Hosting;

// ADR-047: certyfikat podawany przez endpointy Kestrela wymienione w ServerTls:Endpoints.
// Tryb B: plik server\server-current.pfx od usługi Operations (certyfikat serwera + CA instalacji, wysyłane
// w uzgadnianiu TLS jako łańcuch). Tryb A: certyfikat z magazynu LocalMachine\My wybrany regułą.
// Konfiguracja jest odczytywana z bazy System co 30 s; odnowienie i zmiana trybu nie wymagają restartu.
// Proces API nie ma dostępu do klucza CA. Endpointy niewymienione (np. localhost z certyfikatem deweloperskim)
// zachowują certyfikat z konfiguracji Kestrela.
public sealed class ServerCertificateProvider(IServiceScopeFactory scopes, ServerTlsPaths paths, IServerCertificateSource source,
    IConfiguration configuration, TimeProvider clock, ILogger<ServerCertificateProvider> logger) : BackgroundService
{
    public static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(30);
    private sealed record Snapshot(SslStreamCertificateContext Context, X509Certificate2 Certificate, string Source, DateTime FileStampUtc);
    private volatile Snapshot? current;
    private volatile string? errorCode;
    private ServerTlsConfiguration? settings;

    public string[] Endpoints { get; } = EndpointNames(configuration);
    public bool Managed => Endpoints.Length > 0;
    public X509Certificate2? Certificate => current?.Certificate;
    public SslStreamCertificateContext? Context => current?.Context;

    public ServerTlsServedCertificate Status
    {
        get
        {
            var snapshot = current;
            return new(Managed, Endpoints, snapshot?.Source, snapshot?.Certificate.Thumbprint, snapshot?.Certificate.Subject,
                snapshot?.Certificate.NotAfter.ToUniversalTime(), errorCode);
        }
    }

    public static string[] EndpointNames(IConfiguration configuration) =>
        configuration.GetSection("ServerTls:Endpoints").Get<string[]>()?.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct().ToArray() ?? [];

    // Wywoływane dla każdego zarządzanego endpointu przy ładowaniu konfiguracji Kestrela.
    public static void Attach(HttpsConnectionAdapterOptions https, IServiceProvider services)
    {
        ServerCertificateProvider Provider() => services.GetRequiredService<ServerCertificateProvider>();
        https.ServerCertificateSelector = (_, _) => Provider().Certificate;
        https.OnAuthenticate = (_, ssl) =>
        {
            // Kontekst zawiera certyfikat CA instalacji jako certyfikat pośredni (tryb B) albo łańcuch firmowego CA (tryb A).
            if (Provider().Context is not { } context) return;
            ssl.ServerCertificateSelectionCallback = null;
            ssl.ServerCertificate = null;
            ssl.ServerCertificateContext = context;
        };
    }

    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        // Pierwsze wczytanie przed startem Kestrela, aby nasłuch od razu miał certyfikat.
        if (Managed) await Refresh(cancellationToken);
        await base.StartAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!Managed) return;
        using var timer = new PeriodicTimer(RefreshInterval, clock);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken)) await Refresh(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }

    public async Task Refresh(CancellationToken ct)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            settings = await scope.ServiceProvider.GetRequiredService<SystemDbContext>().ServerTlsConfigurations.AsNoTracking().SingleOrDefaultAsync(ct) ?? settings;
        }
        catch (Exception error) when (error is Microsoft.Data.SqlClient.SqlException or InvalidOperationException)
        {
            // Baza niedostępna: ostatnio znany tryb (przy starcie — CA instalacji z dysku); odnowiony plik nadal jest wczytywany.
            logger.LogWarning("Server TLS configuration unavailable ({ErrorType})", error.GetType().Name);
        }
        try
        {
            if (settings?.Mode == ServerTlsModes.OwnCertificate) SelectOwn(settings);
            else LoadInstallationCa();
        }
        catch (Exception error) when (error is CryptographicException or IOException or UnauthorizedAccessException)
        {
            Fail(error is UnauthorizedAccessException ? "tls_server_file_access_denied" : "tls_server_file_unreadable");
        }
    }

    private void SelectOwn(ServerTlsConfiguration settings)
    {
        var result = OwnCertificateSelection.Select(source.Candidates(), [.. settings.DnsNameList, .. settings.IpAddressList],
            settings.IssuerThumbprint, clock.GetUtcNow());
        if (result.Certificate is not { } certificate) { Fail(result.ErrorCode ?? "tls_own_not_found"); return; }
        if (current is { Source: ServerTlsModes.OwnCertificate } existing && existing.Certificate.Thumbprint == certificate.Thumbprint)
        { errorCode = null; return; }
        Publish(new(SslStreamCertificateContext.Create(certificate, null, offline: true), certificate, ServerTlsModes.OwnCertificate, default));
    }

    private void LoadInstallationCa()
    {
        if (!File.Exists(paths.ServerPfx)) { Fail("tls_server_file_missing"); return; }
        var stamp = File.GetLastWriteTimeUtc(paths.ServerPfx);
        if (current is { Source: ServerTlsModes.InstallationCa } existing && existing.FileStampUtc == stamp) { errorCode = null; return; }
        var collection = X509CertificateLoader.LoadPkcs12CollectionFromFile(paths.ServerPfx, null);
        var certificate = collection.FirstOrDefault(x => x.HasPrivateKey);
        var ca = collection.FirstOrDefault(x => !x.HasPrivateKey && InstallationCertificateValidation.IsCertificateAuthority(x));
        if (certificate == null || ca == null) { Fail("tls_server_file_invalid"); return; }
        var context = SslStreamCertificateContext.Create(certificate, [ca], offline: true);
        Publish(new(context, certificate, ServerTlsModes.InstallationCa, stamp));
    }

    private void Publish(Snapshot snapshot)
    {
        current = snapshot;
        errorCode = null;
        logger.LogInformation("Server TLS certificate loaded ({Source}, {Thumbprint}, valid to {NotAfter:yyyy-MM-dd})",
            snapshot.Source, snapshot.Certificate.Thumbprint, snapshot.Certificate.NotAfter);
    }

    // Błąd nie zabiera działającego certyfikatu: lepiej podawać dotychczasowy niż odciąć wszystkie stacje.
    private void Fail(string code)
    {
        if (errorCode != code) logger.LogWarning("Server TLS certificate unavailable ({Code})", code);
        errorCode = code;
    }
}
