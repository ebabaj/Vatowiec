using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Vatowiec.Core.Application;
using Vatowiec.Core.Domain;
using Vatowiec.Core.Infrastructure.Identity;
using Vatowiec.Core.Infrastructure.Persistence;
using Vatowiec.Core.Infrastructure.Registries;
using Vatowiec.SharedContracts;

namespace Vatowiec.Tests;

[Collection("LocalSql")]
[Trait("Category", "SqlIntegration")]
public sealed class GusConfigurationSqlTests
{
    private const string Route = "/api/v1/installation/integrations/gus";
    private static readonly Guid A = Guid.Parse("aaaaaaaa-0000-4000-8000-000000000001");
    private static readonly Guid B = Guid.Parse("bbbbbbbb-0000-4000-8000-000000000002");

    [PreservedVatSqlFact]
    public async Task CentralConfiguration_ApiRoleCsrfEncryptionRowversionAndClear_AllWritesRolledBack()
    {
        await using var transaction = await PreservedSystemTransaction.Open();
        using var app = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development"); builder.ConfigureLogging(logging => logging.ClearProviders());
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Registries:AllowProduction"] = "true",
                ["Registries:GUS:Environment"] = "Production",
                ["Registries:GUS:BaseUrl"] = RegistrySettings.GusProduction,
                ["Registries:MF:Environment"] = "Mock",
                ["Registries:VIES:Environment"] = "Mock",
                ["CompanyConnections:LocalWindows"] = "Server=localhost\\SQLEXPRESS;Integrated Security=True;Encrypt=True;TrustServerCertificate=True"
            }));
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<SystemDbContext>(); services.AddScoped(_ => transaction.Context());
                services.AddScoped(_ => ProductionRegistryTests.Settings()); // Explicit mock-transport contract test, independent of installation state.
                services.AddDataProtection().UseEphemeralDataProtectionProvider();
                services.AddSingleton<IGusKeyProtector>(new GusKeyProtector(new EphemeralDataProtectionProvider()));
                // Any accidental request remains local and fails. No production traffic in tests.
                services.AddHttpClient("registries").ConfigurePrimaryHttpMessageHandler(() => new DataPortTests.Stub(_ => throw new InvalidOperationException("Unexpected HTTP")));
            });
        });
        using var scope = app.Services.CreateScope(); var system = scope.ServiceProvider.GetRequiredService<SystemDbContext>();
        Assert.Empty(await system.Database.GetPendingMigrationsAsync());
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
        var password = "Fake!a1-" + Guid.NewGuid().ToString("N");
        var admin = new ApplicationUser { Id = Guid.NewGuid(), UserName = "fixture-gus-admin-" + Guid.NewGuid().ToString("N") };
        var companyAdmin = new ApplicationUser { Id = Guid.NewGuid(), UserName = "fixture-company-admin-" + Guid.NewGuid().ToString("N") };
        Assert.True((await users.CreateAsync(admin, password)).Succeeded); Assert.True((await users.CreateAsync(companyAdmin, password)).Succeeded);
        if (!await roles.RoleExistsAsync(InstallationRoles.Administrator))
            Assert.True((await roles.CreateAsync(new IdentityRole<Guid>(InstallationRoles.Administrator) { Id = Guid.NewGuid() })).Succeeded);
        Assert.True((await users.AddToRoleAsync(admin, InstallationRoles.Administrator)).Succeeded);
        foreach (var id in new[] { A, B })
            system.CompanyAccesses.Add(new CompanyAccess(companyAdmin.Id, id, CompanyPermission.ViewProfile | CompanyPermission.ViewContractors | CompanyPermission.ManageCompanyIntegrations | CompanyPermission.VerifyRegistries));
        await system.SaveChangesAsync();
        using var client = app.CreateClient(new() { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
        using var ordinary = app.CreateClient(new() { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(Route)).StatusCode);
        Assert.True((await Login(client, admin.UserName!, password)).IsInstallationAdministrator);
        Assert.False((await Login(ordinary, companyAdmin.UserName!, password)).IsInstallationAdministrator);
        Assert.Equal(HttpStatusCode.Forbidden, (await ordinary.GetAsync(Route)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await ordinary.PutAsJsonAsync(Route, new SaveGusKeyRequest { ApiKey = "fake-key" })).StatusCode);
        using (var forbiddenDelete = new HttpRequestMessage(HttpMethod.Delete, Route) { Content = JsonContent.Create(new DeleteGusKeyRequest(null, true)) })
            Assert.Equal(HttpStatusCode.Forbidden, (await ordinary.SendAsync(forbiddenDelete)).StatusCode);
        var initial = (await client.GetFromJsonAsync<GusConfigurationResponse>(Route))!;
        client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync(Route, new SaveGusKeyRequest { ApiKey = "fake-key", RowVersion = initial.RowVersion })).StatusCode);
        await Csrf(client);
        var key = "fake-" + Guid.NewGuid().ToString("N");
        using var saved = await client.PutAsJsonAsync(Route, new SaveGusKeyRequest { ApiKey = key, RowVersion = initial.RowVersion });
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        var status = (await saved.Content.ReadFromJsonAsync<GusConfigurationResponse>())!;
        Assert.True(status.IsConfigured); Assert.Equal("Production", status.Environment); Assert.NotEqual(initial.RowVersion, status.RowVersion);
        system.ChangeTracker.Clear(); var encrypted = (await system.GusConfigurations.AsNoTracking().SingleAsync()).ProtectedApiKey!;
        Assert.DoesNotContain(key, encrypted);
        var responseText = await (await client.GetAsync(Route)).Content.ReadAsStringAsync();
        Assert.DoesNotContain(key, responseText); Assert.DoesNotContain(encrypted, responseText); Assert.DoesNotContain("protectedApiKey", responseText);
        using var stale = await client.PutAsJsonAsync(Route, new SaveGusKeyRequest { ApiKey = "fake-other-key", RowVersion = initial.RowVersion });
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync(Route, new SaveGusKeyRequest { ApiKey = RegistrySettings.GusTestApiKey, RowVersion = status.RowVersion })).StatusCode);
        key = "fake-replacement-" + Guid.NewGuid().ToString("N");
        using var replacement = await client.PutAsJsonAsync(Route, new SaveGusKeyRequest { ApiKey = key, RowVersion = status.RowVersion });
        Assert.Equal(HttpStatusCode.OK, replacement.StatusCode);
        var replaced = (await replacement.Content.ReadFromJsonAsync<GusConfigurationResponse>())!;
        Assert.NotEqual(status.RowVersion, replaced.RowVersion); status = replaced;

        // Same central provider/secret from two company-authorized requests/scopes.
        foreach (var id in new[] { A, B })
        {
            await using var access = await scope.ServiceProvider.GetRequiredService<CompanyDatabaseResolver>().Resolve(companyAdmin.Id, id,
                CompanyPermission.ViewContractors | CompanyPermission.VerifyRegistries, true);
            using var requestScope = app.Services.CreateScope();
            var provider = requestScope.ServiceProvider.GetRequiredService<IGusApiKeyProvider>();
            Assert.Equal(key, await provider.Read(default));
            await VerifyHistoryWithRollback(access.Context, system, provider, companyAdmin.Id);
        }
        Assert.Equal(1, await system.GusConfigurations.CountAsync());
        var resolver = scope.ServiceProvider.GetRequiredService<CompanyDatabaseResolver>();
        Assert.True((await Assert.ThrowsAsync<CompanyResolutionException>(() => resolver.Resolve(admin.Id, A,
            CompanyPermission.ViewContractors | CompanyPermission.VerifyRegistries, true))).Forbidden);
        var accessB = await system.CompanyAccesses.SingleAsync(x => x.UserId == companyAdmin.Id && x.CompanyId == B);
        accessB.ChangePermissions(CompanyPermission.ViewContractors | CompanyPermission.ManageCompanyIntegrations);
        await system.SaveChangesAsync();
        Assert.True((await Assert.ThrowsAsync<CompanyResolutionException>(() => resolver.Resolve(companyAdmin.Id, B,
            CompanyPermission.ViewContractors | CompanyPermission.VerifyRegistries, true))).Forbidden);
        admin = (await users.FindByIdAsync(admin.Id.ToString()))!;
        Assert.True((await users.RemoveFromRoleAsync(admin, InstallationRoles.Administrator)).Succeeded);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(Route)).StatusCode); // Existing cookie is not enough.
        Assert.True((await users.AddToRoleAsync(admin, InstallationRoles.Administrator)).Succeeded);
        using var noConfirm = await Delete(client, status.RowVersion, false); Assert.Equal(HttpStatusCode.BadRequest, noConfirm.StatusCode);
        using var cleared = await Delete(client, status.RowVersion, true); Assert.Equal(HttpStatusCode.OK, cleared.StatusCode);
        var empty = (await cleared.Content.ReadFromJsonAsync<GusConfigurationResponse>())!;
        Assert.False(empty.IsConfigured); Assert.NotEqual(status.RowVersion, empty.RowVersion);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PutAsJsonAsync(Route, new SaveGusKeyRequest { ApiKey = key, RowVersion = status.RowVersion })).StatusCode);
        using var finalScope = app.Services.CreateScope(); Assert.Null(await finalScope.ServiceProvider.GetRequiredService<IGusApiKeyProvider>().Read(default));
        // Neither users nor this singleton nor sessions are committed. The outer transaction rolls back.
    }

    private static async Task VerifyHistoryWithRollback(CompanyDbContext company, SystemDbContext system, IGusApiKeyProvider provider, Guid actor)
    {
        var before = await company.ExternalVerifications.CountAsync();
        await using var rollback = await company.Database.BeginTransactionAsync();
        var contractor = new Contractor("GUS-FIXTURE-" + Guid.NewGuid().ToString("N"), "Fikcyjny test GUS — wycofywany", "PL", "3245174504");
        company.Contractors.Add(contractor); await company.SaveChangesAsync();
        var calls = 0;
        var reply = await new RegistryAdapters(new ProductionRegistryTests.Factory(request =>
        {
            Assert.Equal(RegistrySettings.GusProduction, request.RequestUri!.AbsoluteUri);
            calls++;
            return calls == 1 ? ProductionRegistryTests.Reply("<Envelope><ZalogujResult>fake-sid</ZalogujResult></Envelope>") :
                calls == 2 ? ProductionRegistryTests.GusFound() : ProductionRegistryTests.Reply("<Envelope><WylogujResult>true</WylogujResult></Envelope>");
        }), ProductionRegistryTests.Settings(), new(), provider).Execute("Gus", "3245174504", null, null, new(2026, 10, 23), default);
        Assert.Equal("Completed", reply.ServiceStatus); Assert.Equal(3, calls);
        var now = DateTimeOffset.UtcNow;
        // The transport is a local fixture, so never persist it as an official Production confirmation.
        company.ExternalVerifications.Add(new ExternalVerification(Guid.NewGuid(), contractor.Id, null, "GUS", "Gus", "Mock",
            "3245174504", null, null, null, now, now, reply.Outcome, reply.ServiceStatus, null,
            "MOCK — lokalny test kontraktu produkcyjnego", reply.Reference, JsonSerializer.Serialize(reply.Data),
            "fixture-" + reply.AdapterVersion, "fixture", false, actor, await system.InstallationIdentities.Select(x => x.Id).SingleAsync(), "Fikcyjny wykonawca testu", "fixture"));
        await company.SaveChangesAsync();
        var stored = await company.ExternalVerifications.SingleAsync(x => x.ContractorId == contractor.Id);
        Assert.Equal("Mock", stored.Environment); Assert.Equal("GUS", stored.Source); Assert.False(stored.Executed);
        Assert.Equal(before + 1, await company.ExternalVerifications.CountAsync());
        await rollback.RollbackAsync();
        Assert.Equal(before, await company.ExternalVerifications.CountAsync());
    }
    internal static async Task Csrf(HttpClient client)
    {
        var token = (await client.GetFromJsonAsync<CsrfResponse>("/api/v1/auth/csrf"))!;
        client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN"); client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", token.Token);
    }
    internal static async Task<SessionResponse> Login(HttpClient client, string user, string password)
    {
        await Csrf(client); using var response = await client.PostAsJsonAsync("/api/v1/auth/login", new { userName = user, password });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode); var session = (await response.Content.ReadFromJsonAsync<SessionResponse>())!;
        await Csrf(client); return session;
    }
    private static async Task<HttpResponseMessage> Delete(HttpClient client, string? version, bool confirm)
    {
        using var request = new HttpRequestMessage(HttpMethod.Delete, Route) { Content = JsonContent.Create(new DeleteGusKeyRequest(version, confirm)) };
        return await client.SendAsync(request);
    }
}
