using System.Net;
using System.Net.Http.Json;
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
using Vatowiec.Core.Domain;
using Vatowiec.Core.Infrastructure.Identity;
using Vatowiec.Core.Infrastructure.Persistence;
using Vatowiec.Core.Infrastructure.Registries;
using Vatowiec.SharedContracts;

namespace Vatowiec.Tests;

[Collection("LocalSql")]
[Trait("Category", "SqlIntegration")]
public sealed class RegistrySettingsSqlTests
{
    private const string Route = "/api/v1/installation/integrations/registries";
    [PreservedVatSqlFact]
    public async Task SettingsAreCentralAuditedAuthorizedAndLive_TransportAlwaysStubbed_AllWritesRolledBack()
    {
        await using var transaction = await PreservedSystemTransaction.Open();
        var urls = new List<Uri>();
        using var app = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development"); builder.ConfigureLogging(log => log.ClearProviders());
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                // These legacy values must have no influence on the normal server snapshot.
                ["Registries:AllowProduction"] = "false",
                ["Registries:MF:Environment"] = "Production",
                ["Registries:MF:BaseUrl"] = "https://evil.invalid/",
                ["Registries:GUS:Environment"] = "Production"
            }));
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<SystemDbContext>(); services.AddScoped(_ => transaction.Context());
                services.AddDataProtection().UseEphemeralDataProtectionProvider();
                services.AddSingleton<IGusKeyProtector>(new GusKeyProtector(new EphemeralDataProtectionProvider()));
                services.AddHttpClient("registries").ConfigurePrimaryHttpMessageHandler(() => new DataPortTests.Stub(request =>
                {
                    urls.Add(request.RequestUri!);
                    if (request.RequestUri!.Host.Contains("stat.gov.pl", StringComparison.Ordinal))
                    {
                        var body = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
                        if (body.Contains("Zaloguj", StringComparison.Ordinal)) return ProductionRegistryTests.Reply("<Envelope><ZalogujResult>fixture-session</ZalogujResult></Envelope>");
                        return body.Contains("Wyloguj", StringComparison.Ordinal) ? ProductionRegistryTests.Reply("<Envelope><WylogujResult>true</WylogujResult></Envelope>") : ProductionRegistryTests.GusFound();
                    }
                    if (request.RequestUri.Host == "ec.europa.eu") return ProductionRegistryTests.Reply("<Envelope><countryCode>EL</countryCode><vatNumber>123456789</vatNumber><valid>true</valid></Envelope>");
                    Assert.Equal("wykazpodatnikowvat.mf.gov.pl", request.RequestUri.Host);
                    return request.RequestUri.AbsolutePath.Contains("/status/", StringComparison.Ordinal)
                        ? ProductionRegistryTests.Reply("{\"result\":{\"subject\":{\"nip\":\"3245174504\",\"statusVat\":\"Czynny\",\"requestedDate\":\"23-10-2026\",\"responseDateTime\":\"23-10-2026 12:00:00\",\"requestId\":\"fixture-MF\"}}}")
                        : ProductionRegistryTests.Reply("{\"result\":{\"subject\":{\"nip\":\"3245174504\",\"statusVat\":\"Czynny\"},\"requestId\":\"fixture-MF\"}}");
                }));
            });
        });
        using var scope = app.Services.CreateScope(); var system = scope.ServiceProvider.GetRequiredService<SystemDbContext>();
        var initialAuditCount = await system.RegistryConfigurationChanges.CountAsync();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
        var password = "Fake!a1-" + Guid.NewGuid().ToString("N");
        var admin = new ApplicationUser { Id = Guid.NewGuid(), UserName = "fixture-settings-admin-" + Guid.NewGuid().ToString("N") };
        var ordinary = new ApplicationUser { Id = Guid.NewGuid(), UserName = "fixture-company-manager-" + Guid.NewGuid().ToString("N") };
        Assert.True((await users.CreateAsync(admin, password)).Succeeded); Assert.True((await users.CreateAsync(ordinary, password)).Succeeded);
        if (!await roles.RoleExistsAsync(InstallationRoles.Administrator)) Assert.True((await roles.CreateAsync(new IdentityRole<Guid>(InstallationRoles.Administrator) { Id = Guid.NewGuid() })).Succeeded);
        Assert.True((await users.AddToRoleAsync(admin, InstallationRoles.Administrator)).Succeeded);
        system.CompanyAccesses.Add(new CompanyAccess(ordinary.Id, Guid.Parse("aaaaaaaa-0000-4000-8000-000000000001"), CompanyPermission.ViewProfile | CompanyPermission.ManageCompanyIntegrations));
        await system.SaveChangesAsync();
        using var client = app.CreateClient(new() { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
        using var regular = app.CreateClient(new() { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(Route)).StatusCode);
        await GusConfigurationSqlTests.Login(client, admin.UserName!, password); await GusConfigurationSqlTests.Login(regular, ordinary.UserName!, password);
        Assert.Equal(HttpStatusCode.Forbidden, (await regular.GetAsync(Route)).StatusCode);
        var rows = (await client.GetFromJsonAsync<RegistryConfigurationResponse[]>(Route))!;
        Assert.Equal(3, rows.Length); Assert.All(rows, x => Assert.Equal("Test", x.Environment));
        var mf = rows.Single(x => x.Source == "MF");
        Assert.Equal(HttpStatusCode.Forbidden, (await regular.PutAsJsonAsync(Route + "/MF", Request(mf, "Production", true))).StatusCode);
        client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync(Route + "/MF", Request(mf, "Production", true))).StatusCode);
        await GusConfigurationSqlTests.Csrf(client);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync(Route + "/MF", Request(mf, "Production", false))).StatusCode);
        foreach (var bad in new[] { "https://evil.invalid/", RegistrySettings.MfTest, RegistrySettings.MfProduction + "?key=fake-secret" })
        {
            using var rejected = await client.PutAsJsonAsync(Route + "/MF", Request(mf, "Production", true) with { ProductionUrl = bad });
            Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
            var json = await rejected.Content.ReadFromJsonAsync<JsonElement>();
            Assert.True(json.GetProperty("errors").TryGetProperty("ProductionUrl", out _));
            Assert.DoesNotContain("fake-secret", await rejected.Content.ReadAsStringAsync());
        }
        Assert.Equal(initialAuditCount, await system.RegistryConfigurationChanges.CountAsync()); Assert.Empty(urls);

        // Preserve the real secret: override only inside the outer rollback transaction.
        const string keyRoute = "/api/v1/installation/integrations/gus";
        var keyStatus = (await client.GetFromJsonAsync<GusConfigurationResponse>(keyRoute))!;
        Assert.Equal("Test", keyStatus.Environment);
        using var keySaved = await client.PutAsJsonAsync(keyRoute, new SaveGusKeyRequest { ApiKey = "fake-" + Guid.NewGuid().ToString("N"), RowVersion = keyStatus.RowVersion });
        Assert.Equal(HttpStatusCode.OK, keySaved.StatusCode);

        foreach (var source in new[] { "MF", "GUS", "VIES" })
        {
            var row = rows.Single(x => x.Source == source);
            using var oldScope = app.Services.CreateScope();
            var before = oldScope.ServiceProvider.GetRequiredService<RegistrySettings>();
            Assert.Equal("Test", before.Mode(source));
            using var saved = await client.PutAsJsonAsync(Route + "/" + source, Request(row, "Production", true));
            Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
            var current = (await saved.Content.ReadFromJsonAsync<RegistryConfigurationResponse>())!;
            Assert.NotEqual(row.RowVersion, current.RowVersion);
            Assert.Equal("Test", before.Mode(source)); // Existing operation snapshot remains unchanged.
            using var newScope = app.Services.CreateScope();
            var after = newScope.ServiceProvider.GetRequiredService<RegistrySettings>();
            Assert.Equal("Production", after.Mode(source)); // Same running server, new request/scope.
            var operation = source == "GUS" ? "Gus" : source == "MF" ? "MfStatus" : "Vies";
            var countBefore = urls.Count;
            var reply = await newScope.ServiceProvider.GetRequiredService<IRegistryAdapters>().Execute(operation, "3245174504", "EL123456789", null, new(2026, 10, 23), default);
            Assert.Equal("Completed", reply.ServiceStatus); Assert.Equal("Production", reply.Environment);
            Assert.Equal(RegistrySettings.OfficialUrl(source, "Production", source == "MF").TrimEnd('/') + (source == "MF" ? "/status/nip/3245174504" : ""),
                urls[countBefore].GetLeftPart(UriPartial.Path).TrimEnd('/'));
            // Results from the HTTP fixture are not persisted as official Production confirmations.
            Assert.Equal(HttpStatusCode.Conflict, (await client.PutAsJsonAsync(Route + "/" + source, Request(row, "Test", false))).StatusCode);
            using var toTest = await client.PutAsJsonAsync(Route + "/" + source, Request(current, "Test", false));
            Assert.Equal(HttpStatusCode.OK, toTest.StatusCode);
            using var finalScope = app.Services.CreateScope(); Assert.Equal("Test", finalScope.ServiceProvider.GetRequiredService<RegistrySettings>().Mode(source));
        }
        var audit = await system.RegistryConfigurationChanges.AsNoTracking().Where(x => x.Id > 0 && x.ActorId == admin.Id).OrderBy(x => x.Id).ToArrayAsync();
        Assert.Equal(7, audit.Length); // 6 public transitions and one secret-metadata event.
        Assert.All(audit, x => { Assert.Equal(admin.UserName, x.ActorName); Assert.NotEqual(Guid.Empty, x.ActorIssuerId); Assert.NotEqual(default, x.OccurredAtUtc); });
        Assert.Contains(audit, x => x.BeforeJson.Contains("\"Environment\":\"Test\"") && x.AfterJson.Contains("\"Environment\":\"Production\""));
        Assert.All(audit, x => { Assert.DoesNotContain("ProtectedApiKey", x.BeforeJson); Assert.DoesNotContain("ProtectedApiKey", x.AfterJson); });
        system.Attach(audit[0]); system.Entry(audit[0]).State = EntityState.Deleted;
        await Assert.ThrowsAsync<InvalidOperationException>(() => system.SaveChangesAsync()); system.ChangeTracker.Clear();
        // A FK must not couple historical actors to deletable central users.
        Assert.Empty(system.Model.FindEntityType(typeof(RegistryConfigurationChange))!.GetForeignKeys());
    }
    private static RegistryConfigurationRequest Request(RegistryConfigurationResponse row, string mode, bool confirm) =>
        new(mode, row.TestUrl, row.ProductionUrl, row.TestWl2Url, row.ProductionWl2Url, row.RowVersion, confirm);
}
