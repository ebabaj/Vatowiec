using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using Vatowiec.Core.Domain;
using Vatowiec.Core.Infrastructure.ServerTls;
using Vatowiec.Operations.ServerTls;
using Vatowiec.SharedContracts.ServerTls;
using static Vatowiec.Tests.CompanyProvisioningSqlTests;

namespace Vatowiec.Tests;

// ADR-047 na developerskiej Vatowiec_System w wycofywanej transakcji. Pliki CA i certyfikatu powstają
// w katalogu tymczasowym testu i są po teście usuwane.
[Collection("LocalSql")]
[Trait("Category", "SqlIntegration")]
public sealed class ServerTlsSqlTests
{
    private const string Route = "/api/v1/installation/server-certificate";

    [ProvisioningSqlFact]
    public async Task ApiAndOperations_ManageInstallationCaOwnCertificateAuditAndRecovery()
    {
        if (!OperatingSystem.IsWindows()) return;
        var root = Path.Combine(Path.GetTempPath(), "vatowiec-tls-sql-" + Guid.NewGuid().ToString("N"));
        var clock = new MutableClock(DateTimeOffset.UtcNow);
        using var ownCa = InstallationCertificateFactory.CreateCa("FIRMA", clock.GetUtcNow());
        using var own = InstallationCertificateFactory.CreateServer(ownCa, ["api.firma.local"], [], clock.GetUtcNow());
        try
        {
            await using var tx = await PreservedSystemTransaction.Open();
            using var app = App(tx).WithWebHostBuilder(b => b.ConfigureServices(s =>
            {
                s.RemoveAll<IServerCertificateSource>(); s.AddSingleton<IServerCertificateSource>(new FixedSource([own]));
            }));
            using var scope = app.Services.CreateScope();
            var password = "Fixture!1a-" + Guid.NewGuid().ToString("N");
            var admin = await Account(scope.ServiceProvider, true, password);
            var plain = await Account(scope.ServiceProvider, false, password);
            using var adminClient = Client(app); using var user = Client(app); using var noCsrf = Client(app);
            await GusConfigurationSqlTests.Login(adminClient, admin.UserName!, password);
            await GusConfigurationSqlTests.Login(user, plain.UserName!, password);
            await GusConfigurationSqlTests.Login(noCsrf, admin.UserName!, password); noCsrf.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");

            // Wyłącznie administrator instalacji.
            Assert.Equal(HttpStatusCode.Forbidden, (await user.GetAsync(Route)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await user.GetAsync(Route + "/connection-file")).StatusCode);
            var initial = (await adminClient.GetFromJsonAsync<ServerTlsResponse>(Route))!;
            Assert.Equal(ServerTlsModeNames.InstallationCa, initial.Mode);
            Assert.False(initial.Served.Managed);

            // CSRF, wersja, walidacja nazw i reguła certyfikatu własnego sprawdzana w procesie API.
            var ownRequest = new ServerTlsRequest(ServerTlsModeNames.OwnCertificate, ["api.firma.local"], [], null, initial.RowVersion);
            await Expect(noCsrf.PutAsJsonAsync(Route, ownRequest), HttpStatusCode.BadRequest, "csrf_invalid");
            await Expect(adminClient.PutAsJsonAsync(Route, ownRequest with { RowVersion = Convert.ToBase64String(new byte[8]) }), HttpStatusCode.Conflict, "server_tls_stale");
            await Expect(adminClient.PutAsJsonAsync(Route, ownRequest with { DnsNames = ["*.firma.local"] }), HttpStatusCode.BadRequest, "server_tls_invalid");
            await Expect(adminClient.PutAsJsonAsync(Route, ownRequest with { DnsNames = ["inny.firma.local"] }), HttpStatusCode.Conflict, "tls_own_not_found");
            await Expect(adminClient.PostAsJsonAsync(Route + "/ca-replacement", new ServerTlsCaReplacementRequest(initial.RowVersion, true)), HttpStatusCode.Conflict, "server_tls_ca_missing");
            await Expect(adminClient.GetAsync(Route + "/connection-file"), HttpStatusCode.NotFound, "server_tls_ca_missing");
            var saved = await Read(adminClient.PutAsJsonAsync(Route, ownRequest));
            Assert.Equal(ServerTlsModeNames.OwnCertificate, saved.Mode);
            Assert.Equal(["api.firma.local"], saved.DnsNames);
            var change = Assert.Single(saved.History, x => x.Kind == ServerTlsChangeKinds.ConfigurationChanged);
            Assert.Equal(admin.UserName, change.ActorName);
            Assert.Contains("OwnCertificate", change.AfterJson);

            // Usługa Operations: pierwsze uruchomienie tworzy CA, certyfikat serwera i plik połączenia.
            var paths = new ServerTlsPaths(root);
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["ServerTls:PublicPort"] = "7443" }).Build();
            var store = new ServerTlsStore(paths, configuration);
            async Task Reconcile() { await using var db = tx.Context(); await new ServerTlsReconciler(db, store, configuration, clock, NullLogger<ServerTlsReconciler>.Instance).Run(CancellationToken.None); }
            async Task<ServerTlsState> State() { await using var db = tx.Context(); return await db.ServerTlsStates.AsNoTracking().SingleAsync(); }
            async Task<int> AuditCount() { await using var db = tx.Context(); return await db.ServerTlsChanges.CountAsync(); }

            await Reconcile();
            var state = await State();
            Assert.Null(state.LastErrorCode);
            Assert.NotNull(state.CaSpkiSha256);
            Assert.True(File.Exists(paths.CaCertificate) && File.Exists(paths.CaProtectedKey) && File.Exists(paths.ServerPfx));
            Assert.DoesNotContain("PRIVATE KEY", File.ReadAllText(paths.ConnectionFile));
            Assert.True(ConnectionFile.TryRead(File.ReadAllText(paths.ConnectionFile), out _, out var fileAnchor, out _));
            Assert.Contains(ConnectionCode.Format(Convert.FromHexString(state.CaSpkiSha256!)), File.ReadAllText(paths.ConnectionCodeFile));
            var server = store.LoadServer()!.Value;
            Assert.Equal(InstallationTrustResult.TrustedByInstallationCa, InstallationCertificateValidation.Validate(
                X509CertificateLoader.LoadCertificate(server.Server.RawData), [], "localhost", System.Net.Security.SslPolicyErrors.RemoteCertificateChainErrors, fileAnchor!));
            var afterFirst = await AuditCount();
            await Reconcile();
            Assert.Equal(afterFirst, await AuditCount()); // bez zmian: brak nowego certyfikatu i audytu

            // Plik połączenia z API: ta sama kotwica co na dysku serwera.
            var fromApi = (await adminClient.GetFromJsonAsync<ConnectionFileContent>(Route + "/connection-file"))!;
            Assert.Equal(state.CaSpkiSha256, fromApi.CaSpkiSha256);
            var status = (await adminClient.GetFromJsonAsync<ServerTlsResponse>(Route))!;
            Assert.Equal(ConnectionCode.Format(Convert.FromHexString(state.CaSpkiSha256!)), status.InstallationCa.ConnectionCode);

            // Zmiana nazw w trybie CA instalacji → nowy certyfikat z tym samym CA.
            var named = await Read(adminClient.PutAsJsonAsync(Route, new ServerTlsRequest(ServerTlsModeNames.InstallationCa, ["Celina.tenjin.local"], ["10.0.0.147"], null, status.RowVersion)));
            await Reconcile();
            var renamed = await State();
            Assert.Equal(state.CaSpkiSha256, renamed.CaSpkiSha256);
            Assert.NotEqual(state.ServerCertificateThumbprint, renamed.ServerCertificateThumbprint);
            Assert.Contains("celina.tenjin.local", renamed.ServerCertificateNameList);
            Assert.Contains("10.0.0.147", renamed.ServerCertificateNameList);

            // Odnowienie 30 dni przed końcem ważności, bez zmiany CA i kodu.
            clock.Advance(TimeSpan.FromDays(340));
            await Reconcile();
            var renewed = await State();
            Assert.Equal(state.CaSpkiSha256, renewed.CaSpkiSha256);
            Assert.NotEqual(renamed.ServerCertificateThumbprint, renewed.ServerCertificateThumbprint);
            Assert.True(renewed.ServerCertificateNotAfter > renamed.ServerCertificateNotAfter);

            // Utrata plików znanego CA: błąd, bez cichego utworzenia nowego CA.
            File.Move(paths.CaProtectedKey, paths.CaProtectedKey + ".hidden");
            await Reconcile();
            var lost = await State();
            Assert.Equal("tls_ca_missing", lost.LastErrorCode);
            Assert.Equal(state.CaSpkiSha256, lost.CaSpkiSha256);
            File.Move(paths.CaProtectedKey + ".hidden", paths.CaProtectedKey);

            // Awaryjna wymiana CA: potwierdzenie, jedno oczekujące zlecenie, nowy kod po wykonaniu.
            var current = (await adminClient.GetFromJsonAsync<ServerTlsResponse>(Route))!;
            await Expect(adminClient.PostAsJsonAsync(Route + "/ca-replacement", new ServerTlsCaReplacementRequest(current.RowVersion, false)), HttpStatusCode.BadRequest, "server_tls_confirmation_required");
            var pending = await Read(adminClient.PostAsJsonAsync(Route + "/ca-replacement", new ServerTlsCaReplacementRequest(current.RowVersion, true)));
            Assert.True(pending.InstallationCa.ReplacementPending);
            await Expect(adminClient.PostAsJsonAsync(Route + "/ca-replacement", new ServerTlsCaReplacementRequest(pending.RowVersion, true)), HttpStatusCode.Conflict, "server_tls_replacement_pending");
            await Reconcile();
            var replaced = await State();
            Assert.NotEqual(state.CaSpkiSha256, replaced.CaSpkiSha256);
            Assert.True(Directory.Exists(Path.Combine(paths.CaDirectory, "retired")));
            Assert.Equal(InstallationTrustResult.Rejected, InstallationCertificateValidation.Validate(
                X509CertificateLoader.LoadCertificate(store.LoadServer()!.Value.Server.RawData), [], "localhost",
                System.Net.Security.SslPolicyErrors.RemoteCertificateChainErrors, fileAnchor!)); // stara kotwica nie działa

            // Lokalne odzyskiwanie z trybu certyfikatu własnego (komenda administratora Windows na serwerze).
            var latest = (await adminClient.GetFromJsonAsync<ServerTlsResponse>(Route))!;
            await Read(adminClient.PutAsJsonAsync(Route, ownRequest with { RowVersion = latest.RowVersion }));
            await using (var db = tx.Context())
                Assert.True(await ServerTlsService.LocalRecovery(db, "Lokalny administrator Windows: TEST", clock, CancellationToken.None));
            var recovered = (await adminClient.GetFromJsonAsync<ServerTlsResponse>(Route))!;
            Assert.Equal(ServerTlsModeNames.InstallationCa, recovered.Mode);
            Assert.Contains(recovered.History, x => x.Kind == ServerTlsChangeKinds.LocalRecovery);
            await using (var db = tx.Context())
            {
                Assert.All(await db.ServerTlsChanges.AsNoTracking().ToListAsync(), x =>
                    Assert.DoesNotContain("PRIVATE", (x.BeforeJson ?? "") + x.AfterJson));
                Assert.Equal(1, await db.Database.SqlQueryRaw<int>(
                    "SELECT COUNT(*) AS [Value] FROM sys.triggers WHERE name = N'TR_ServerTlsChanges_Immutable'").SingleAsync());
            }
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }

    private static async Task Expect(Task<HttpResponseMessage> call, HttpStatusCode status, string code)
    {
        using var response = await call;
        Assert.Equal(status, response.StatusCode);
        Assert.Contains(code, await response.Content.ReadAsStringAsync());
    }

    private static async Task<ServerTlsResponse> Read(Task<HttpResponseMessage> call)
    {
        using var response = await call;
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ServerTlsResponse>())!;
    }

    private sealed class FixedSource(X509Certificate2[] certificates) : IServerCertificateSource
    {
        public IReadOnlyList<X509Certificate2> Candidates() => certificates;
    }
}
