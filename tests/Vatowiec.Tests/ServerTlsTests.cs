using System.Net;
using System.Net.Http;
using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Vatowiec.Api.Hosting;
using Vatowiec.Core.Domain;
using Vatowiec.Core.Infrastructure.ServerTls;
using Vatowiec.Desktop.Services;
using Vatowiec.Desktop.ViewModels;
using Vatowiec.Operations.ServerTls;
using Vatowiec.SharedContracts.ServerTls;

namespace Vatowiec.Tests;

// ADR-047: kod i plik połączenia, walidacja z kotwicą CA instalacji (kryteria akceptacji klienta),
// wybór certyfikatu własnego oraz rzeczywiste uzgadnianie TLS z Kestrelem. Certyfikaty powstają w teście.
public sealed class ServerTlsTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    private static X509Certificate2 Ca() => InstallationCertificateFactory.CreateCa("TEST", Now);
    private static X509Certificate2 Server(X509Certificate2 ca, string[]? names = null, IPAddress[]? addresses = null) =>
        InstallationCertificateFactory.CreateServer(ca, names ?? ["localhost"], addresses ?? [], Now);
    private static X509Certificate2 Public(X509Certificate2 certificate) => X509CertificateLoader.LoadCertificate(certificate.RawData);
    private static InstallationTrustAnchor CodeAnchor(X509Certificate2 ca)
    {
        Assert.True(ConnectionCode.TryParse(ConnectionCode.Format(InstallationCertificateValidation.SpkiSha256(ca)), out var pin, out _));
        return new(pin);
    }
    private static InstallationTrustResult Check(X509Certificate2 server, X509Certificate2[] presented, InstallationTrustAnchor anchor,
        string host = "localhost", SslPolicyErrors errors = SslPolicyErrors.RemoteCertificateChainErrors, DateTime? at = null) =>
        InstallationCertificateValidation.Validate(Public(server), presented.Select(Public), host, errors, anchor, at);

    [Fact]
    public void ConnectionCode_RoundTripsNormalizesAndDetectsTypos()
    {
        for (var i = 0; i < 200; i++)
        {
            var hash = RandomNumberGenerator.GetBytes(32);
            var code = ConnectionCode.Format(hash);
            Assert.Matches("^VTW1(-[0-9A-Z*~$=]{1,5}){6}$", code);
            Assert.True(ConnectionCode.TryParse(code, out var pin, out _));
            Assert.Equal(hash[..16], pin);
            // Małe litery, spacje zamiast myślników i mylone znaki O/I/L są normalizowane.
            var relaxed = code.ToLowerInvariant().Replace('-', ' ').Replace('0', 'o').Replace('1', 'l');
            Assert.True(ConnectionCode.TryParse(relaxed.Replace("vtwl", "vtw1"), out var again, out _));
            Assert.Equal(pin, again);
        }
        var valid = ConnectionCode.Format(RandomNumberGenerator.GetBytes(32));
        var detected = 0;
        for (var position = 5; position < valid.Length - 1; position++)
        {
            if (valid[position] == '-') continue;
            var swapped = valid[position] == 'A' ? 'B' : 'A';
            var typo = valid[..position] + swapped + valid[(position + 1)..];
            if (!ConnectionCode.TryParse(typo, out _, out var error)) { detected++; Assert.NotEmpty(error); }
        }
        Assert.Equal(26, detected); // każda pojedyncza zmiana znaku danych jest wykryta przed połączeniem
        Assert.False(ConnectionCode.TryParse(valid[..^3], out _, out _));
        Assert.False(ConnectionCode.TryParse("ABC1" + valid[4..], out _, out _));
        Assert.False(ConnectionCode.TryParse("", out _, out _));
    }

    [Fact]
    public void ConnectionFile_RoundTripsAndRejectsInconsistentContent()
    {
        using var ca = Ca();
        var content = ConnectionFile.Create("SERWER", ["https://Celina.tenjin.local:7443", "https://10.0.0.5:7443/"], ca);
        var text = ConnectionFile.Serialize(content);
        Assert.DoesNotContain("PRIVATE", text);
        Assert.True(ConnectionFile.TryRead(text, out var read, out var anchor, out _));
        Assert.Equal(["https://celina.tenjin.local:7443/", "https://10.0.0.5:7443/"], read!.ApiAddresses);
        Assert.Equal(InstallationCertificateValidation.SpkiSha256(ca), anchor!.CaSpkiSha256);
        Assert.Equal(ca.RawData, anchor.CaCertificate);

        using var other = Ca();
        Assert.False(ConnectionFile.TryRead(ConnectionFile.Serialize(content with { CaSpkiSha256 = Convert.ToHexString(InstallationCertificateValidation.SpkiSha256(other)) }), out _, out _, out var mismatch));
        Assert.Contains("niespójny", mismatch);
        using var server = Server(ca);
        Assert.False(ConnectionFile.TryRead(ConnectionFile.Serialize(content with
        { CaCertificate = server.ExportCertificatePem(), CaSpkiSha256 = Convert.ToHexString(InstallationCertificateValidation.SpkiSha256(server)) }), out _, out _, out _));
        Assert.False(ConnectionFile.TryRead(ConnectionFile.Serialize(content with { ApiAddresses = ["http://celina:7443"] }), out _, out _, out _));
        Assert.False(ConnectionFile.TryRead(text.Replace("\"version\": 1", "\"version\": 1, \"extra\": 1"), out _, out _, out _));
        Assert.False(ConnectionFile.TryRead(new string(' ', ConnectionFile.MaxBytes + 1), out _, out _, out _));
    }

    [Fact]
    public void Factory_IssuesCaSentAsIntermediateAndShortLivedServerCertificate()
    {
        using var ca = Ca();
        Assert.True(InstallationCertificateValidation.IsCertificateAuthority(ca));
        Assert.NotEqual(ca.SubjectName.Name, ca.IssuerName.Name); // nie samopodpisany: stos TLS wysyła go jako certyfikat pośredni
        Assert.InRange((ca.NotAfter.ToUniversalTime() - Now.UtcDateTime).TotalDays, 3640, 3660);
        Assert.NotNull(ca.GetECDsaPublicKey());
        using var server = Server(ca, ["celina", "celina.tenjin.local", "localhost"], [IPAddress.Parse("10.0.0.5"), IPAddress.Loopback]);
        Assert.InRange((server.NotAfter.ToUniversalTime() - Now.UtcDateTime).TotalDays, 364, 366);
        Assert.True(InstallationCertificateValidation.HasServerAuthentication(server));
        Assert.False(InstallationCertificateValidation.IsCertificateAuthority(server));
        foreach (var host in new[] { "celina", "celina.tenjin.local", "localhost", "10.0.0.5", "127.0.0.1" })
            Assert.True(server.MatchesHostname(host, allowWildcards: false, allowCommonName: false), host);
    }

    [Fact]
    public void Validation_AcceptsOnlyPinnedInstallationCaWithStrictChecks()
    {
        using var ca = Ca(); using var server = Server(ca);
        var code = CodeAnchor(ca);
        var file = new InstallationTrustAnchor(InstallationCertificateValidation.SpkiSha256(ca), ca.RawData);

        Assert.Equal(InstallationTrustResult.TrustedByInstallationCa, Check(server, [ca], code));          // kod + CA z uzgadniania TLS
        Assert.Equal(InstallationTrustResult.TrustedByInstallationCa, Check(server, [], file));            // plik: CA z kotwicy
        Assert.Equal(InstallationTrustResult.TrustedBySystem, Check(server, [], code, errors: SslPolicyErrors.None));

        using var other = Ca();
        Assert.Equal(InstallationTrustResult.Rejected, Check(server, [ca], CodeAnchor(other)));            // inny skrót
        // Certyfikat serwera od obcego CA z identycznym Subject.
        using var impostorCa = InstallationCertificateFactory.CreateCa("TEST", Now);
        Assert.Equal(ca.Subject, impostorCa.Subject);
        using var impostor = Server(impostorCa);
        Assert.Equal(InstallationTrustResult.Rejected, Check(impostor, [impostorCa], code));
        Assert.Equal(InstallationTrustResult.Rejected, Check(impostor, [impostorCa], file));
        Assert.Equal(InstallationTrustResult.Rejected, Check(impostor, [ca], code));                       // podpis nie od przypiętego CA
        // Nazwa spoza SAN: zarówno zgłoszona przez stos TLS, jak i wykryta lokalnie.
        Assert.Equal(InstallationTrustResult.Rejected, Check(server, [ca], code, errors: SslPolicyErrors.RemoteCertificateChainErrors | SslPolicyErrors.RemoteCertificateNameMismatch));
        Assert.Equal(InstallationTrustResult.Rejected, Check(server, [ca], code, host: "celina"));
        Assert.Equal(InstallationTrustResult.Rejected, Check(server, [ca], code, errors: SslPolicyErrors.RemoteCertificateNotAvailable));
        // Terminy ważności.
        Assert.Equal(InstallationTrustResult.Rejected, Check(server, [ca], code, at: server.NotAfter.ToUniversalTime().AddDays(1)));
        Assert.Equal(InstallationTrustResult.Rejected, Check(server, [ca], code, at: server.NotBefore.ToUniversalTime().AddDays(-1)));
        // Brak EKU Server Authentication.
        using var noEku = WithoutEku(ca);
        Assert.Equal(InstallationTrustResult.Rejected, Check(noEku, [ca], code));
        // Sam certyfikat CA podany jako certyfikat serwera.
        Assert.Equal(InstallationTrustResult.Rejected, Check(ca, [], file));
        // Odnowiony certyfikat od tego samego CA: bez czynności na stacji.
        using var renewed = Server(ca);
        Assert.NotEqual(server.Thumbprint, renewed.Thumbprint);
        Assert.Equal(InstallationTrustResult.TrustedByInstallationCa, Check(renewed, [ca], code));
    }

    private static X509Certificate2 WithoutEku(X509Certificate2 ca)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest("CN=localhost", key, HashAlgorithmName.SHA256);
        var san = new SubjectAlternativeNameBuilder(); san.AddDnsName("localhost"); request.CertificateExtensions.Add(san.Build());
        request.CertificateExtensions.Add(X509AuthorityKeyIdentifierExtension.CreateFromCertificate(ca, true, false));
        return request.Create(ca, Now.AddHours(-1), Now.AddDays(30), RandomNumberGenerator.GetBytes(16));
    }

    [Fact]
    public void OwnCertificateSelection_RequiresNamesEkuKeyAndPicksNewest()
    {
        using var ca = Ca();
        using var older = InstallationCertificateFactory.CreateServer(ca, ["api.firma.local"], [], Now.AddDays(-10));
        using var newer = InstallationCertificateFactory.CreateServer(ca, ["api.firma.local"], [], Now.AddDays(-1));
        using var otherName = InstallationCertificateFactory.CreateServer(ca, ["inny.firma.local"], [], Now);
        using var publicOnly = Public(InstallationCertificateFactory.CreateServer(ca, ["api.firma.local"], [], Now));
        using var noEku = WithoutEku(ca);
        var result = OwnCertificateSelection.Select([older, otherName, newer, publicOnly], ["api.firma.local"], null, Now);
        Assert.Equal(newer.Thumbprint, result.Certificate?.Thumbprint);
        Assert.Equal("tls_own_not_found", OwnCertificateSelection.Select([otherName], ["api.firma.local"], null, Now).ErrorCode);
        Assert.Equal("tls_own_key_inaccessible", OwnCertificateSelection.Select([publicOnly], ["api.firma.local"], null, Now).ErrorCode);
        Assert.Equal("tls_own_eku_missing", OwnCertificateSelection.Select([noEku], ["localhost"], null, Now).ErrorCode);
        Assert.Equal("tls_own_expired", OwnCertificateSelection.Select([newer], ["api.firma.local"], null, Now.AddYears(2)).ErrorCode);
        Assert.Equal("tls_own_names_missing", OwnCertificateSelection.Select([newer], [], null, Now).ErrorCode);
    }

    [Fact]
    public void Configuration_ValidatesNamesAddressesAndOwnModeRequirements()
    {
        Assert.Equal(["celina.tenjin.local", "celina"], ServerTlsConfiguration.NormalizeDnsNames(["Celina.Tenjin.Local.", "celina", "CELINA"]));
        Assert.Throws<ArgumentException>(() => ServerTlsConfiguration.NormalizeDnsNames(["*.firma.local"]));
        Assert.Throws<ArgumentException>(() => ServerTlsConfiguration.NormalizeDnsNames(["serwer_1"]));
        Assert.Throws<ArgumentException>(() => ServerTlsConfiguration.NormalizeDnsNames(["https://celina"]));
        Assert.Equal(["10.0.0.5", "fe80::1"], ServerTlsConfiguration.NormalizeIpAddresses(["10.0.0.5", " fe80::1 "]));
        Assert.Throws<ArgumentException>(() => ServerTlsConfiguration.NormalizeIpAddresses(["0.0.0.0"]));
        Assert.Throws<ArgumentException>(() => ServerTlsConfiguration.NormalizeIpAddresses(["10.0.0.300"]));
        Assert.Throws<ArgumentException>(() => ServerTlsConfiguration.NormalizeThumbprint("ABC"));
        Assert.Equal(new string('A', 40), ServerTlsConfiguration.NormalizeThumbprint(string.Join(':', Enumerable.Repeat("aa", 20))));
    }

    [Fact]
    public void LoginScreen_StoresAnchorOnlyForExactAddressAndImportsFile()
    {
        using var ca = Ca();
        var store = new MemoryServerTrustStore();
        using var model = new MainViewModel(_ => throw new InvalidOperationException(), trustStore: store, apiAddressOverride: "https://celina.tenjin.local:7443");
        model.ConnectionCodeInput = "VTW1-XXXXX";
        model.ApplyConnectionCode();
        Assert.Null(store.Find("https://celina.tenjin.local:7443"));
        Assert.Contains("długość", model.Message);

        model.ConnectionCodeInput = ConnectionCode.Format(InstallationCertificateValidation.SpkiSha256(ca));
        model.ApplyConnectionCode();
        Assert.NotNull(store.Find("https://celina.tenjin.local:7443/"));
        Assert.Null(store.Find("https://celina.tenjin.local:7444")); // inny port
        Assert.Null(store.Find("https://celina:7443"));               // inna nazwa
        Assert.True(model.HasServerTrust);
        Assert.Contains("CA instalacji", model.ServerTrustStatus);
        model.ForgetServerTrust();
        Assert.False(model.HasServerTrust);

        using var fileModel = new MainViewModel(_ => throw new InvalidOperationException(), trustStore: store);
        fileModel.ImportConnectionFile(ConnectionFile.Serialize(ConnectionFile.Create("SERWER", ["https://celina:7443", "https://10.0.0.5:7443"], ca)));
        Assert.Equal("https://celina:7443/", fileModel.ApiAddress);
        Assert.Equal(ca.RawData, store.Find("https://10.0.0.5:7443")?.CaCertificate);
    }

    [Fact]
    public void FileTrustStore_PersistsPerOriginWithoutSecrets()
    {
        var directory = Path.Combine(Path.GetTempPath(), "vatowiec-trust-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var ca = Ca();
            var path = Path.Combine(directory, "server-trust.json");
            new FileServerTrustStore(path).Save("https://Celina:7443", new(InstallationCertificateValidation.SpkiSha256(ca), ca.RawData), "File");
            var reloaded = new FileServerTrustStore(path);
            Assert.Equal(ca.RawData, reloaded.Find("https://celina:7443/")?.CaCertificate);
            Assert.DoesNotContain("PRIVATE", File.ReadAllText(path));
            reloaded.Remove("https://celina:7443");
            Assert.Null(new FileServerTrustStore(path).Find("https://celina:7443"));
            File.WriteAllText(path, "{\"version\":2}");
            Assert.Throws<LocalLoginStorageException>(() => new FileServerTrustStore(path).Find("https://celina:7443"));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
    }

    // Rzeczywiste uzgadnianie TLS: Kestrel z ServerCertificateProvider (tryb CA instalacji z pliku od Operations)
    // i klient VatowiecApiClient z kotwicą z kodu połączenia. Obejmuje odnowienie bez restartu i odmowy.
    [Fact]
    public async Task Tls_EndToEnd_KestrelProviderAndPinnedClient()
    {
        if (!OperatingSystem.IsWindows()) return;
        var root = Path.Combine(Path.GetTempPath(), "vatowiec-tls-" + Guid.NewGuid().ToString("N"));
        try
        {
            var paths = new ServerTlsPaths(root);
            var settings = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["ServerTls:Endpoints:0"] = "Test" }).Build();
            var store = new ServerTlsStore(paths, settings);
            store.EnsureDirectories();
            using var ca = Ca();
            using var first = Server(ca);
            store.SaveServer(first, ca);

            var builder = WebApplication.CreateSlimBuilder();
            builder.Logging.ClearProviders();
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["ServerTls:Endpoints:0"] = "Test" });
            builder.Services.AddSingleton(paths);
            builder.Services.AddSingleton<IServerCertificateSource>(new EmptySource());
            builder.Services.AddSingleton(TimeProvider.System);
            builder.Services.AddSingleton<ServerCertificateProvider>();
            builder.WebHost.UseKestrel(o => o.Listen(IPAddress.Loopback, 0, l => l.UseHttps(h => ServerCertificateProvider.Attach(h, o.ApplicationServices))));
            await using var app = builder.Build();
            app.MapGet("/api/v1/companies", () => Array.Empty<object>());
            var provider = app.Services.GetRequiredService<ServerCertificateProvider>();
            await provider.Refresh(CancellationToken.None);
            Assert.Equal(first.Thumbprint, provider.Status.Thumbprint);
            await app.StartAsync();
            var port = new Uri(app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First()).Port;
            var address = $"https://localhost:{port}";

            using (var paired = VatowiecApiClient.Connect(address, CodeAnchor(ca))) Assert.Empty(await paired.Companies());
            // Bez kotwicy obowiązuje standardowa walidacja Windows: CA instalacji nie jest zaufane systemowo.
            using (var standard = VatowiecApiClient.Connect(address))
                Assert.Equal(HttpRequestError.SecureConnectionError, (await Assert.ThrowsAsync<HttpRequestException>(() => standard.Companies())).HttpRequestError);
            using var other = Ca();
            using (var wrong = VatowiecApiClient.Connect(address, CodeAnchor(other)))
                await Assert.ThrowsAsync<HttpRequestException>(() => wrong.Companies());
            // Ten sam serwer pod adresem spoza SAN (127.0.0.1) — odmowa mimo poprawnej kotwicy.
            using (var byIp = VatowiecApiClient.Connect($"https://127.0.0.1:{port}", CodeAnchor(ca)))
                await Assert.ThrowsAsync<HttpRequestException>(() => byIp.Companies());

            // Odnowienie przez Operations: nowy plik, ten sam CA; API przełącza się bez restartu, stacja bez zmian.
            using var renewed = Server(ca);
            store.SaveServer(renewed, ca);
            await provider.Refresh(CancellationToken.None);
            Assert.Equal(renewed.Thumbprint, provider.Status.Thumbprint);
            using (var paired = VatowiecApiClient.Connect(address, CodeAnchor(ca))) Assert.Empty(await paired.Companies());
            await app.StopAsync();
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }

    private sealed class EmptySource : IServerCertificateSource
    {
        public IReadOnlyList<X509Certificate2> Candidates() => [];
    }
}
