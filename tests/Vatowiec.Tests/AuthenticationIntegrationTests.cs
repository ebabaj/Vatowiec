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
using Microsoft.Extensions.Logging;
using Vatowiec.Api.Security;
using Vatowiec.Core.Application;
using Vatowiec.Core.Domain;
using Vatowiec.Core.Infrastructure.Identity;
using Vatowiec.Core.Infrastructure.Persistence;
using Vatowiec.Desktop.Services;
using Vatowiec.Desktop.ViewModels;

namespace Vatowiec.Tests;

[Collection("LocalSql")]
[Trait("Category", "SqlIntegration")]
public sealed partial class AuthenticationIntegrationTests : IAsyncLifetime
{
    private WebApplicationFactory<Program>? application;
    private readonly ManualClock clock = new();
    private readonly string password = "Aa1!" + Guid.NewGuid().ToString("N");
    private readonly Guid companyA = Guid.NewGuid();
    private readonly Guid companyB = Guid.NewGuid();
    private int contractorId;
    private int contractorIdB;
    private Guid operatorId;
    private Guid readerId;
    private string SystemConnection => Environment.GetEnvironmentVariable("ConnectionStrings__VatowiecSystemTest")!;
    private string CompanyConnection(bool second) => Environment.GetEnvironmentVariable(
        second ? "ConnectionStrings__VatowiecCompanyBTest" : "ConnectionStrings__VatowiecCompanyATest")!;

    public async Task InitializeAsync()
    {
        if (Environment.GetEnvironmentVariable("VATOWIEC_DATABASE_PER_COMPANY_SQL") != "1" ||
            Environment.GetEnvironmentVariable("VATOWIEC_DEMO_RESET_AUTHORIZED") != "1") return;
        // Bez dowolnych adresów: te same dokładne fixture co w testach persystencji.
        foreach (var (connection, catalog) in new[] { (SystemConnection, "Vatowiec_System"),
                     (CompanyConnection(false), "Vatowiec_Company_A_Dev"), (CompanyConnection(true), "Vatowiec_Company_B_Dev") })
        {
            var target = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(connection);
            Assert.Equal(@"localhost\SQLEXPRESS", target.DataSource, ignoreCase: true);
            Assert.Equal(catalog, target.InitialCatalog);
            Assert.True(target.IntegratedSecurity);
            if (catalog == "Vatowiec_System") DatabaseConnectionPolicy.ValidateSystem(connection, true);
            else DatabaseConnectionPolicy.ValidateCompany(connection, true);
        }
        application = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureLogging(logging => logging.ClearProviders());
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:VatowiecSystem"] = SystemConnection,
                ["CompanyConnections:LocalWindows"] = "Server=localhost\\SQLEXPRESS;Integrated Security=True;Encrypt=True;TrustServerCertificate=True",
                ["Authentication:SessionMinutes"] = "30",
                ["Registries:MF:Environment"] = "Mock",
                ["Registries:GUS:Environment"] = "Mock",
                ["Registries:VIES:Environment"] = "Mock"
            }));
            builder.ConfigureServices(services =>
            {
                services.AddSingleton<TimeProvider>(clock);
                services.AddDataProtection().UseEphemeralDataProtectionProvider();
                services.AddSingleton<Vatowiec.Core.Infrastructure.Registries.IDataPortKeyProtector>(
                    new Vatowiec.Core.Infrastructure.Registries.DataPortKeyProtector(new EphemeralDataProtectionProvider()));
                services.AddScoped<Vatowiec.Core.Infrastructure.Registries.IDataPortClient>(_ => new MockDataPort(dataPortRequests));
                services.AddSingleton<Vatowiec.Core.Infrastructure.Registries.INbpClient>(nbpFixture);
                services.AddSingleton<IBankAccountValidator>(bankValidator);
                services.AddHttpClient("ewib").ConfigurePrimaryHttpMessageHandler(() => new EwibStub(ewib));
            });
        });
        using var scope = application.Services.CreateScope();
        var system = scope.ServiceProvider.GetRequiredService<SystemDbContext>();
        Assert.False(await system.Companies.AnyAsync());
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
        foreach (var role in new[] { "Operator", "Reader" })
            Assert.True((await roles.CreateAsync(new IdentityRole<Guid>(role) { Id = Guid.NewGuid() })).Succeeded);
        var op = new ApplicationUser { Id = Guid.NewGuid(), UserName = "operator.test" };
        var reader = new ApplicationUser { Id = Guid.NewGuid(), UserName = "reader.test" };
        Assert.True((await users.CreateAsync(op, password)).Succeeded);
        Assert.True((await users.CreateAsync(reader, password)).Succeeded);
        Assert.True((await users.AddToRoleAsync(op, "Operator")).Succeeded);
        Assert.True((await users.AddToRoleAsync(reader, "Reader")).Succeeded);
        operatorId = op.Id;
        readerId = reader.Id;
        foreach (var (id, second, name) in new[] { (companyA, false, "Testowa firma A"), (companyB, true, "Testowa firma B") })
        {
            system.Companies.Add(new Company(id, name));
            system.CompanyDatabases.Add(new CompanyDatabaseRegistration(id, @"localhost\SQLEXPRESS",
                second ? "Vatowiec_Company_B_Dev" : "Vatowiec_Company_A_Dev", "LocalWindows"));
            system.CompanyAccesses.Add(new CompanyAccess(operatorId, id, (CompanyPermission)7));
            await using var company = Company(second, id);
            company.CompanyProfiles.Add(new CompanyProfile(id, name));
            var seeded = new Contractor("SAME", second ? "Kontrahent B" : "Kontrahent A", "PL");
            company.Contractors.Add(seeded);
            await company.SaveChangesAsync();
            if (!second) contractorId = seeded.Id;
            else contractorIdB = seeded.Id;
        }
        system.CompanyAccesses.Add(new CompanyAccess(readerId, companyA, CompanyPermission.ViewProfile));
        await system.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        if (application is null) return;
        using (var scope = application.Services.CreateScope())
        {
            var system = scope.ServiceProvider.GetRequiredService<SystemDbContext>();
            await system.UserSessions.ExecuteDeleteAsync();
            await system.CompanyAccesses.ExecuteDeleteAsync();
            await system.Users.ExecuteDeleteAsync();
            await system.Roles.ExecuteDeleteAsync();
            await system.CompanyDatabases.ExecuteDeleteAsync();
            await system.Companies.ExecuteDeleteAsync();
        }
        foreach (var second in new[] { false, true })
        {
            await using var company = Company(second, second ? companyB : companyA);
            await company.Database.ExecuteSqlRawAsync("""
                BEGIN TRANSACTION;
                BEGIN TRY
                    DISABLE TRIGGER audit.TR_ExternalVerifications_Immutable ON audit.ExternalVerifications;
                    DELETE FROM audit.ExternalVerifications;
                    DISABLE TRIGGER audit.TR_CurrencyOperations_Immutable ON audit.CurrencyOperations;
                    DELETE FROM audit.CurrencyOperations;
                    ENABLE TRIGGER audit.TR_CurrencyOperations_Immutable ON audit.CurrencyOperations;
                    DELETE FROM core.ExchangeRates;
                    UPDATE ref.Currencies SET IsActive=1, DownloadA=CASE WHEN Code IN ('EUR','USD','GBP','CHF') THEN 1 ELSE 0 END, DownloadB=0, DownloadC=0;
                    ENABLE TRIGGER audit.TR_ExternalVerifications_Immutable ON audit.ExternalVerifications;
                    COMMIT;
                END TRY
                BEGIN CATCH
                    IF @@TRANCOUNT>0 ROLLBACK;
                    THROW;
                END CATCH;
                """);
            await company.ContractorCreateOperations.ExecuteDeleteAsync();
            await company.DataPortConfigurations.ExecuteDeleteAsync();
            await company.ContractorChanges.ExecuteDeleteAsync();
            await company.ContractorAddresses.ExecuteDeleteAsync();
            await company.ContractorBankAccounts.ExecuteDeleteAsync();
            await company.ContractorContacts.ExecuteDeleteAsync();
            await company.AddressTypes.Where(x => x.Id > 2).ExecuteDeleteAsync();
            await company.Contractors.ExecuteUpdateAsync(x => x.SetProperty(r => r.ParentId, (int?)null));
            await company.Contractors.ExecuteDeleteAsync();
            await company.CompanyProfiles.ExecuteDeleteAsync();
        }
        await application.DisposeAsync();
    }

    [SqlServerFact]
    public async Task DesktopClient_LogsInSwitchesPhysicalDatabasesAndLogsOut()
    {
        using var model = new MainViewModel(_ => new VatowiecApiClient(Client())) { UserName = "operator.test" };
        await model.Login(password);
        Assert.True(model.IsCompanySelection, model.Message);
        Assert.Equal(2, model.Companies.Length);
        foreach (var id in new[] { companyA, companyB })
        {
            model.SelectedCompany = model.Companies.Single(x => x.CompanyId == id);
            await model.OpenCompany();
            Assert.True(model.IsWorkspace, model.Message);
            await model.LoadContractors();
            var contractor = Assert.Single(model.Contractors);
            Assert.Equal(id, contractor.CompanyId);
            Assert.Equal(id == companyA ? "Kontrahent A" : "Kontrahent B", contractor.Name);
            await model.ChangeCompany();
            Assert.Empty(model.Contractors);
        }
        await model.Logout();
        Assert.True(model.IsLogin);
        Assert.Empty(model.Message);
        using var scope = application!.Services.CreateScope();
        Assert.False(await scope.ServiceProvider.GetRequiredService<SystemDbContext>().UserSessions.AnyAsync(x => x.RevokedAtUtc == null));
    }

    [SqlServerFact]
    public async Task DesktopClient_ReaderCannotDownloadContractorsOrForeignCompany()
    {
        using var api = new VatowiecApiClient(Client());
        await api.Login("reader.test", password);
        Assert.False(Assert.Single(await api.Companies()).CanViewContractors);
        Assert.Equal(403, (await Assert.ThrowsAsync<ApiException>(() => api.Contractors(companyA))).StatusCode);
        Assert.Equal(403, (await Assert.ThrowsAsync<ApiException>(() => api.Profile(companyB))).StatusCode);
        Assert.Equal(403, (await Assert.ThrowsAsync<ApiException>(() => api.Contractors(companyB))).StatusCode);
        await api.Logout();
    }

    [SqlServerFact]
    public async Task DesktopClient_ServerSessionExpiryReturnsToLoginAndClearsData()
    {
        using var model = new MainViewModel(_ => new VatowiecApiClient(Client())) { UserName = "operator.test" };
        await model.Login(password);
        model.SelectedCompany = model.Companies.Single(x => x.CompanyId == companyA);
        await model.OpenCompany();
        await model.LoadContractors();
        Assert.Single(model.Contractors);
        clock.Advance(TimeSpan.FromMinutes(31));
        await model.LoadContractors();
        Assert.True(model.IsLogin, model.Message);
        Assert.Empty(model.Contractors);
        Assert.Empty(model.Companies);
    }

    [SqlServerFact]
    public async Task AnonymousAccess_IsRejectedWithoutRedirects()
    {
        using var client = Client();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/companies")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync($"/api/v1/companies/{companyA}/profile")).StatusCode);
    }

    [SqlServerFact]
    public async Task Login_RequiresCsrfAndDoesNotDiscloseAccountExistence()
    {
        using var client = Client();
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest("operator.test", password))).StatusCode);
        await Csrf(client);
        using var wrong = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest("operator.test", "wrong-fictional-password"));
        using var absent = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest("absent.test", "wrong-fictional-password"));
        Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, absent.StatusCode);
        Assert.Equal(await wrong.Content.ReadAsStringAsync(), await absent.Content.ReadAsStringAsync());
    }

    [SqlServerFact]
    public async Task Users_SeeOnlyTheirCompaniesAndRoles()
    {
        using var op = await Login("operator.test");
        using var reader = await Login("reader.test");
        var all = await Json(op, "/api/v1/companies");
        var own = await Json(reader, "/api/v1/companies");
        Assert.Equal(2, all.GetArrayLength());
        Assert.Equal(companyA, Assert.Single(own.EnumerateArray()).GetProperty("companyId").GetGuid());
        var me = await Json(reader, "/api/v1/auth/me");
        Assert.Contains(me.GetProperty("roles").EnumerateArray(), x => x.GetString() == "Reader");
        Assert.DoesNotContain("Server=", all.GetRawText());
        Assert.DoesNotContain("ConnectionProfileKey", all.GetRawText(), StringComparison.OrdinalIgnoreCase);
    }

    [SqlServerFact]
    public async Task ForeignCompanyAndMissingOperationPermission_AreForbidden()
    {
        using var reader = await Login("reader.test");
        Assert.Equal(HttpStatusCode.OK, (await reader.GetAsync($"/api/v1/companies/{companyA}/profile")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await reader.GetAsync($"/api/v1/companies/{companyB}/profile")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await reader.GetAsync($"/api/v1/companies/{companyA}/contractors")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await reader.GetAsync($"/api/v1/companies/{Guid.NewGuid()}/profile")).StatusCode);
    }

    [SqlServerFact]
    public async Task Resolver_SelectsPhysicalDatabaseWithItsOwnContractors()
    {
        using var op = await Login("operator.test");
        var a = Assert.Single((await Json(op, $"/api/v1/companies/{companyA}/contractors")).EnumerateArray());
        var b = Assert.Single((await Json(op, $"/api/v1/companies/{companyB}/contractors")).EnumerateArray());
        Assert.Equal(contractorId, a.GetProperty("id").GetInt32());
        Assert.Equal(contractorIdB, b.GetProperty("id").GetInt32());
        Assert.Equal(companyA, a.GetProperty("companyId").GetGuid());
        Assert.Equal(companyB, b.GetProperty("companyId").GetGuid());
        Assert.Equal("Kontrahent A", a.GetProperty("name").GetString());
        Assert.Equal("Kontrahent B", b.GetProperty("name").GetString());
    }

    [SqlServerFact]
    public async Task CompanyIdManipulation_IsRejected()
    {
        using var reader = await Login("reader.test");
        Assert.Equal(HttpStatusCode.BadRequest, (await reader.GetAsync($"/api/v1/companies/{companyA}/profile?companyId={companyB}")).StatusCode);
        reader.DefaultRequestHeaders.Add("X-Company-Id", companyB.ToString());
        Assert.Equal(HttpStatusCode.BadRequest, (await reader.GetAsync($"/api/v1/companies/{companyA}/profile")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await reader.GetAsync($"/api/v1/companies/{Guid.Empty}/profile")).StatusCode);
        using var anonymous = Client();
        await Csrf(anonymous);
        var response = await anonymous.PostAsJsonAsync("/api/v1/auth/login", new { userName = "reader.test", password, companyId = companyB });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [SqlServerFact]
    public async Task WrongDatabaseProfile_IsDeniedBeforeBusinessData()
    {
        using var op = await Login("operator.test");
        using (var scope = application!.Services.CreateScope())
        {
            var system = scope.ServiceProvider.GetRequiredService<SystemDbContext>();
            await system.CompanyDatabases.Where(x => x.CompanyId == companyB).ExecuteDeleteAsync();
            await system.CompanyDatabases.Where(x => x.CompanyId == companyA)
                .ExecuteUpdateAsync(x => x.SetProperty(p => p.DatabaseName, "Vatowiec_Company_B_Dev"));
        }
        using var response = await op.GetAsync($"/api/v1/companies/{companyA}/contractors");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("company_identity_mismatch", body);
        Assert.DoesNotContain("Kontrahent B", body);
    }

    [SqlServerFact]
    public async Task MissingRegistrationOrConnectionProfile_HasNoFallback()
    {
        using var op = await Login("operator.test");
        using var scope = application!.Services.CreateScope();
        var system = scope.ServiceProvider.GetRequiredService<SystemDbContext>();
        await system.CompanyDatabases.Where(x => x.CompanyId == companyA).ExecuteDeleteAsync();
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await op.GetAsync($"/api/v1/companies/{companyA}/profile")).StatusCode);
        await system.CompanyDatabases.Where(x => x.CompanyId == companyB)
            .ExecuteUpdateAsync(x => x.SetProperty(p => p.ConnectionProfileKey, "UnknownProfile"));
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await op.GetAsync($"/api/v1/companies/{companyB}/profile")).StatusCode);
    }

    [SqlServerFact]
    public async Task PermissionRevocation_IsAppliedOnNextRequest()
    {
        using var reader = await Login("reader.test");
        using var scope = application!.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<SystemDbContext>().CompanyAccesses
            .Where(x => x.UserId == readerId).ExecuteDeleteAsync();
        Assert.Equal(HttpStatusCode.Forbidden, (await reader.GetAsync($"/api/v1/companies/{companyA}/profile")).StatusCode);
    }

    [SqlServerFact]
    public async Task SessionExpiry_RejectsCookieWithoutWaiting()
    {
        using var op = await Login("operator.test");
        clock.Advance(TimeSpan.FromMinutes(31));
        Assert.Equal(HttpStatusCode.Unauthorized, (await op.GetAsync("/api/v1/companies")).StatusCode);
    }

    [SqlServerFact]
    public async Task Logout_RevokesReplayedCookieAndKeepsOtherUserSession()
    {
        using var op = Client();
        await Csrf(op);
        using var login = await op.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest("operator.test", password));
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var cookie = login.Headers.GetValues("Set-Cookie").Single(x => x.StartsWith("__Host-Vatowiec.Session=", StringComparison.Ordinal));
        Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", cookie, StringComparison.OrdinalIgnoreCase);
        using var replay = Client(false);
        replay.DefaultRequestHeaders.Add("Cookie", cookie.Split(';')[0]);
        Assert.Equal(HttpStatusCode.OK, (await replay.GetAsync("/api/v1/companies")).StatusCode);
        using var reader = await Login("reader.test");
        await Csrf(op); // Po logowaniu token jest związany z uwierzytelnioną tożsamością.
        Assert.Equal(HttpStatusCode.NoContent, (await op.PostAsync("/api/v1/auth/logout", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await replay.GetAsync("/api/v1/companies")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await reader.GetAsync("/api/v1/companies")).StatusCode);
    }

    [SqlServerFact]
    public async Task TwoUsers_CanAccessSameCompanyConcurrently()
    {
        using var op = await Login("operator.test");
        using var reader = await Login("reader.test");
        var responses = await Task.WhenAll(op.GetAsync($"/api/v1/companies/{companyA}/profile"),
            reader.GetAsync($"/api/v1/companies/{companyA}/profile"));
        foreach (var response in responses)
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(companyA, (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("companyId").GetGuid());
            response.Dispose();
        }
    }

    [SqlServerFact]
    public async Task SecurityStampChange_InvalidatesExistingSession()
    {
        using var op = await Login("operator.test");
        using var scope = application!.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        Assert.True((await users.UpdateSecurityStampAsync((await users.FindByIdAsync(operatorId.ToString()))!)).Succeeded);
        Assert.Equal(HttpStatusCode.Unauthorized, (await op.GetAsync("/api/v1/companies")).StatusCode);
    }

    [SqlServerFact]
    public async Task LoginLockout_BlocksCorrectPasswordAfterFiveFailures()
    {
        using var client = Client();
        await Csrf(client);
        for (var i = 0; i < 5; i++)
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest("operator.test", "wrong-fictional-password"))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest("operator.test", password))).StatusCode);
    }

    [SqlServerFact]
    public async Task Logout_RequiresCsrfBoundToLoggedInIdentity()
    {
        using var op = await Login("operator.test");
        // Pozostały token anonimowy z logowania nie może autoryzować wylogowania.
        Assert.Equal(HttpStatusCode.BadRequest, (await op.PostAsync("/api/v1/auth/logout", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await op.GetAsync("/api/v1/companies")).StatusCode);
    }

    [SqlServerFact]
    public async Task SessionDatabaseExpiry_IsCheckedEvenWhileCookieIsValid()
    {
        using var op = await Login("operator.test");
        using (var scope = application!.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<SystemDbContext>().UserSessions
                .Where(x => x.UserId == operatorId).ExecuteUpdateAsync(update =>
                    update.SetProperty(x => x.ExpiresAtUtc, clock.GetUtcNow().AddMinutes(5)));
        clock.Advance(TimeSpan.FromMinutes(6));
        Assert.Equal(HttpStatusCode.Unauthorized, (await op.GetAsync("/api/v1/companies")).StatusCode);
    }

    [SqlServerFact]
    public async Task SystemDatabaseCannotBeSelectedAsCompany()
    {
        using var op = await Login("operator.test");
        using var scope = application!.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<SystemDbContext>().CompanyDatabases
            .Where(x => x.CompanyId == companyA).ExecuteUpdateAsync(update =>
                update.SetProperty(x => x.DatabaseName, "Vatowiec_System"));
        using var response = await op.GetAsync($"/api/v1/companies/{companyA}/profile");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("{\"code\":\"company_unavailable\"}", await response.Content.ReadAsStringAsync());
    }

    [SqlServerFact]
    public async Task LoginOverHttp_IsRejected()
    {
        using var client = Client();
        client.BaseAddress = new Uri("http://localhost");
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest("operator.test", password))).StatusCode);
    }

    [SqlServerFact]
    public async Task ModifiedCookie_IsRejected()
    {
        using var client = Client(false);
        client.DefaultRequestHeaders.Add("Cookie", "__Host-Vatowiec.Session=modified-invalid-cookie");
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/companies")).StatusCode);
    }

    [SqlServerFact]
    public async Task AccountRequiringSecondFactor_CannotBypassItWithPasswordOnly()
    {
        using (var scope = application!.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = (await users.FindByIdAsync(operatorId.ToString()))!;
            Assert.True((await users.SetTwoFactorEnabledAsync(user, true)).Succeeded);
        }
        using var client = Client();
        await Csrf(client);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest("operator.test", password))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/companies")).StatusCode);
    }

    private CompanyDbContext Company(bool second, Guid id) => new(new DbContextOptionsBuilder<CompanyDbContext>()
        .UseSqlServer(CompanyConnection(second)).Options, new CompanyContext(id));
    private HttpClient Client(bool cookies = true) => application!.CreateClient(new WebApplicationFactoryClientOptions
    { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false, HandleCookies = cookies });
    private async Task<HttpClient> Login(string name)
    {
        var client = Client();
        await Csrf(client);
        using var response = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(name, password));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return client;
    }
    private static async Task Csrf(HttpClient client)
    {
        var data = await Json(client, "/api/v1/auth/csrf");
        client.DefaultRequestHeaders.Remove(ApiSecurity.CsrfHeader);
        client.DefaultRequestHeaders.Add(ApiSecurity.CsrfHeader, data.GetProperty("token").GetString());
    }
    private static async Task<JsonElement> Json(HttpClient client, string path)
    {
        using var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }
    private sealed class ManualClock : TimeProvider
    {
        private DateTimeOffset now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => now;
        public void Advance(TimeSpan duration) => now += duration;
    }
}
