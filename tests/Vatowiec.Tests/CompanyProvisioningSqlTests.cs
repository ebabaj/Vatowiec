using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Internal;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Vatowiec.Core.Application;
using Vatowiec.Core.Domain;
using Vatowiec.Core.Infrastructure.Identity;
using Vatowiec.Core.Infrastructure.Operations;
using Vatowiec.Core.Infrastructure.Persistence;
using Vatowiec.Operations;
using Vatowiec.SharedContracts;

namespace Vatowiec.Tests;

// Tworzy fizyczne bazy Vatowiec_Company_{guid} na lokalnym SQLEXPRESS i usuwa je po teście.
// Zmiany w Vatowiec_System są zawsze wycofywane (PreservedSystemTransaction).
public sealed class ProvisioningSqlFactAttribute : FactAttribute
{
    public ProvisioningSqlFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("VATOWIEC_PROVISIONING_SQL") != "1")
            Skip = "Tworzy i usuwa własne bazy firm na localhost\\SQLEXPRESS; wymaga VATOWIEC_PROVISIONING_SQL=1.";
    }
}

[Collection("LocalSql")]
[Trait("Category", "SqlIntegration")]
public sealed class CompanyProvisioningSqlTests
{
    private const string Route = "/api/v1/installation/company-operations";
    internal const string Server = @"localhost\SQLEXPRESS";
    internal const string Local = @"Server=localhost\SQLEXPRESS;Integrated Security=True;Encrypt=True;TrustServerCertificate=True";

    [ProvisioningSqlFact]
    public async Task Api_RequiresAdministratorAndCsrf_IsIdempotent_AndNeverTouchesSqlServer()
    {
        await using var tx = await PreservedSystemTransaction.Open(); using var app = App(tx);
        using var scope = app.Services.CreateScope();
        var password = "Fixture!1a-" + Guid.NewGuid().ToString("N");
        var admin = await Account(scope.ServiceProvider, true, password);
        var plain = await Account(scope.ServiceProvider, false, password);
        using var anonymous = Client(app); using var ordinary = Client(app); using var client = Client(app);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(Route)).StatusCode);
        await GusConfigurationSqlTests.Login(ordinary, plain.UserName!, password);
        Assert.Equal(HttpStatusCode.Forbidden, (await ordinary.GetAsync(Route)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await ordinary.PostAsJsonAsync(Route + "/provision",
            new ProvisionCompanyRequest(Guid.NewGuid(), "Odmowa", "LocalWindows"))).StatusCode);
        await GusConfigurationSqlTests.Login(client, admin.UserName!, password);

        var profiles = await client.GetStringAsync(Route + "/server-profiles");
        Assert.Contains("LocalWindows", profiles); Assert.DoesNotContain("Integrated", profiles, StringComparison.OrdinalIgnoreCase);
        var key = Guid.NewGuid();
        var unique = Guid.NewGuid().ToString("N")[..8];
        var request = new ProvisionCompanyRequest(key, $"  Firma fikcyjna z API {unique}  ", "LocalWindows");
        using (var noCsrf = Client(app))
        {
            await GusConfigurationSqlTests.Login(noCsrf, admin.UserName!, password);
            noCsrf.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
            Assert.Equal(HttpStatusCode.BadRequest, (await noCsrf.PostAsJsonAsync(Route + "/provision", request)).StatusCode);
        }
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(Route + "/provision", request with { ConnectionProfileKey = "Unknown" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(Route + "/provision", request with { CompanyName = " " })).StatusCode);

        using var created = await client.PostAsJsonAsync(Route + "/provision", request);
        Assert.Equal(HttpStatusCode.Accepted, created.StatusCode);
        var operation = (await created.Content.ReadFromJsonAsync<CompanyOperationResponse>())!;
        Assert.Equal(($"Firma fikcyjna z API {unique}", "Requested", "Pending"), (operation.CompanyName, operation.Stage, operation.Status));
        using var replay = await client.PostAsJsonAsync(Route + "/provision", request);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.Equal(operation.Id, (await replay.Content.ReadFromJsonAsync<CompanyOperationResponse>())!.Id);
        using var mismatch = await client.PostAsJsonAsync(Route + "/provision", request with { CompanyName = "Inna nazwa" });
        Assert.Equal(HttpStatusCode.Conflict, mismatch.StatusCode);
        Assert.Contains("operation_payload_mismatch", await mismatch.Content.ReadAsStringAsync());
        using var retry = await client.PostAsJsonAsync($"{Route}/{operation.Id}/retry", new CompanyOperationRetryRequest(operation.RowVersion));
        Assert.Equal(HttpStatusCode.Conflict, retry.StatusCode); // Oczekujące zlecenie nie jest „ponawialne”.

        // ADR-045: zajęta nazwa bazy — przez niezakończone zlecenie (także inną pisownią), nazwę zastrzeżoną i istniejącą bazę.
        foreach (var taken in new[] { $"FIRMA fikcyjna, z API {unique}", "System", "Company A Dev", $"Zajęta {unique}" })
        {
            if (taken.StartsWith("Zaj", StringComparison.Ordinal)) await Execute("master", $"CREATE DATABASE [Vatowiec_Zajeta_{unique}];");
            try
            {
                using var conflict = await client.PostAsJsonAsync(Route + "/provision", new ProvisionCompanyRequest(Guid.NewGuid(), taken, "LocalWindows"));
                Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
                var body = await conflict.Content.ReadAsStringAsync();
                Assert.Contains("database_name_taken", body); Assert.Contains("Nazwa zajęta", body);
            }
            finally { if (taken.StartsWith("Zaj", StringComparison.Ordinal)) await Drop($"Vatowiec_Zajeta_{unique}"); }
        }

        var list = (await client.GetFromJsonAsync<CompanyOperationResponse[]>(Route))!;
        Assert.Contains(list, x => x.Id == operation.Id);
        Assert.DoesNotContain((await client.GetFromJsonAsync<CompanyResponse[]>("/api/v1/companies"))!, x => x.CompanyId == operation.CompanyId);
        await using var db = tx.Context();
        var stored = await db.CompanyOperations.AsNoTracking().SingleAsync(x => x.Id == operation.Id);
        Assert.Equal($"Vatowiec_Firma_fikcyjna_z_API_{unique}", stored.DatabaseName);
        Assert.Equal((admin.Id, Server), (stored.RequestedById, stored.ServerName));
        Assert.Equal("Requested", Assert.Single(await db.CompanyOperationEvents.AsNoTracking().Where(x => x.OperationId == operation.Id).ToArrayAsync()).Action);
        Assert.False(await DatabaseExists(stored.DatabaseName)); // API niczego nie tworzy w SQL Server.
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            var history = await db.CompanyOperationEvents.FirstAsync(x => x.OperationId == operation.Id);
            db.Remove(history); await db.SaveChangesAsync();
        });
    }

    [ProvisioningSqlFact]
    public async Task Executor_ResumesAfterFailureAtEachStage_AndPublishesCompanyWithCreatorRightsAtomically()
    {
        await using var tx = await PreservedSystemTransaction.Open(); using var app = App(tx);
        using var scope = app.Services.CreateScope();
        var admin = await Account(scope.ServiceProvider, true, "Fixture!1a-" + Guid.NewGuid().ToString("N"));
        var clock = new MutableClock(DateTimeOffset.UtcNow);
        var name = "Firma fikcyjna z usługi " + Guid.NewGuid().ToString("N")[..8];
        CompanyOperation operation;
        await using (var db = tx.Context())
            (operation, _) = await new CompanyOperationRequests(db, clock).RequestProvision(new(admin.Id, admin.UserName!),
                Guid.NewGuid(), name, "LocalWindows", Server, default);
        Assert.StartsWith("Vatowiec_Firma_fikcyjna_z_uslugi_", operation.DatabaseName);
        try
        {
            // Przerwany wykonawca: dzierżawa wygasa, a zlecenie przejmuje kolejny proces.
            await using (var db = tx.Context())
            {
                Assert.NotNull(await Executor(db, clock, "crashed-worker").Claim(default));
                Assert.Null(await Executor(db, clock, "second-worker").Claim(default)); // Aktywna dzierżawa wyklucza drugiego wykonawcę.
            }
            clock.Advance(CompanyOperationExecutor.Lease + TimeSpan.FromSeconds(1));

            foreach (var failAfter in new[] { CompanyOperationStages.DatabaseCreated, CompanyOperationStages.SchemaApplied, CompanyOperationStages.ProfileInitialized })
            {
                var failed = false;
                await using (var db = tx.Context())
                    Assert.True(await Executor(db, clock, "worker", stage =>
                    {
                        if (stage == failAfter && !failed) { failed = true; throw new InvalidOperationException("injected"); }
                        return Task.CompletedTask;
                    }).RunNext(default));
                await using (var db = tx.Context())
                {
                    var state = await db.CompanyOperations.AsNoTracking().SingleAsync(x => x.Id == operation.Id);
                    Assert.Equal((failAfter, CompanyOperationStatuses.Pending, "operation_failed"), (state.Stage, state.Status, state.LastErrorCode));
                    Assert.False(await db.Companies.AnyAsync(x => x.Id == operation.CompanyId)); // Niewidoczna przed Ready.
                    clock.Advance(state.NextAttemptAtUtc!.Value - clock.GetUtcNow() + TimeSpan.FromSeconds(1));
                }
                Assert.True(await DatabaseExists(operation.DatabaseName));
            }
            await using (var db = tx.Context()) Assert.True(await Executor(db, clock, "worker").RunNext(default));

            await using (var db = tx.Context())
            {
                var done = await db.CompanyOperations.AsNoTracking().SingleAsync(x => x.Id == operation.Id);
                Assert.Equal((CompanyOperationStages.Ready, CompanyOperationStatuses.Completed), (done.Stage, done.Status));
                Assert.Equal(name, (await db.Companies.AsNoTracking().SingleAsync(x => x.Id == operation.CompanyId)).Name);
                var registration = await db.CompanyDatabases.AsNoTracking().SingleAsync(x => x.CompanyId == operation.CompanyId);
                Assert.Equal((Server, operation.DatabaseName, "LocalWindows"), (registration.ServerName, registration.DatabaseName, registration.ConnectionProfileKey));
                var access = await db.CompanyAccesses.AsNoTracking().SingleAsync(x => x.CompanyId == operation.CompanyId);
                Assert.Equal((admin.Id, CompanyPermissionCatalog.All), (access.UserId, access.Permissions));
                var audit = await db.UserAdministrationChanges.AsNoTracking().SingleAsync(x => x.CompanyId == operation.CompanyId);
                Assert.Equal(("CompanyCreatorAccessGranted", admin.Id), (audit.Operation, audit.TargetId));
                var actions = await db.CompanyOperationEvents.AsNoTracking().Where(x => x.OperationId == operation.Id).OrderBy(x => x.Id).Select(x => x.Action).ToArrayAsync();
                Assert.Equal("Requested", actions[0]); Assert.Equal("Completed", actions[^1]);
                Assert.Equal(3, actions.Count(x => x == "Failed"));
                Assert.Equal(3, actions.Count(x => x == "StageReached")); // Etapy pośrednie zapisane dokładnie raz, mimo wznowień; Ready razem z Completed.

                // Firma działa przez zwykły resolver API, z rzeczywistym profilem i kompletną historią migracji.
                var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["CompanyConnections:LocalWindows"] = Local }).Build();
                await using var resolved = await new CompanyDatabaseResolver(db, configuration, NullLogger<CompanyDatabaseResolver>.Instance)
                    .Resolve(admin.Id, operation.CompanyId, CompanyPermission.ViewProfile, isDevelopment: true);
                Assert.Equal(operation.CompanyId, (await resolved.Context.CompanyProfiles.SingleAsync()).CompanyId);
                Assert.True(await resolved.Context.Currencies.AnyAsync()); // Słowniki z migracji firmy.
            }
            await using (var db = tx.Context()) Assert.False(await Executor(db, clock, "worker").RunNext(default));
        }
        finally { await DropOwned(operation); }
    }

    [ProvisioningSqlFact]
    public async Task Executor_NeverTakesOverAForeignDatabaseWithTheTargetName()
    {
        await using var tx = await PreservedSystemTransaction.Open(); using var app = App(tx);
        using var scope = app.Services.CreateScope();
        var admin = await Account(scope.ServiceProvider, true, "Fixture!1a-" + Guid.NewGuid().ToString("N"));
        var clock = new MutableClock(DateTimeOffset.UtcNow);
        CompanyOperation operation;
        await using (var db = tx.Context())
            (operation, _) = await new CompanyOperationRequests(db, clock).RequestProvision(new(admin.Id, admin.UserName!),
                Guid.NewGuid(), "Firma z konfliktem nazwy " + Guid.NewGuid().ToString("N")[..8], "LocalWindows", Server, default);
        Assert.True(CompanyDatabaseNames.IsCompanyDatabase(operation.DatabaseName));
        await Execute("master", $"CREATE DATABASE [{operation.DatabaseName}];"); // Obca baza bez znaczników tej operacji.
        try
        {
            await using (var db = tx.Context()) Assert.True(await Executor(db, clock, "worker").RunNext(default));
            await using (var db = tx.Context())
            {
                var state = await db.CompanyOperations.AsNoTracking().SingleAsync(x => x.Id == operation.Id);
                Assert.Equal((CompanyOperationStatuses.NeedsReconciliation, "database_not_owned", CompanyOperationStages.Requested),
                    (state.Status, state.LastErrorCode, state.Stage));
                Assert.False(state.IsClaimable(clock.GetUtcNow().AddYears(1)));
                Assert.False(await db.Companies.AnyAsync(x => x.Id == operation.CompanyId));
            }
            Assert.True(await DatabaseExists(operation.DatabaseName));
            Assert.Equal(0, await Scalar(operation.DatabaseName, "SELECT COUNT(*) FROM sys.tables WHERE is_ms_shipped = 0")); // Bez migracji.
        }
        finally
        {
            // Bazę utworzył ten test; pusta i o nazwie z jego CompanyId.
            if (await DatabaseExists(operation.DatabaseName) && await Scalar(operation.DatabaseName, "SELECT COUNT(*) FROM sys.tables WHERE is_ms_shipped = 0") == 0)
                await Drop(operation.DatabaseName);
        }
    }

    internal static CompanyOperationExecutor Executor(SystemDbContext db, TimeProvider clock, string worker, Func<string, Task>? afterCheckpoint = null,
        string? backupRoot = null)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["OperationsConnections:LocalWindows"] = Local,
            ["Operations:BackupRoot"] = backupRoot
        }).Build();
        var environment = new HostingEnvironment { EnvironmentName = Environments.Development };
        return new(db, new OperationsConnections(configuration, environment), new BackupStorage(configuration), clock,
            NullLogger<CompanyOperationExecutor>.Instance, new OperationsWorkerIdentity(worker), afterCheckpoint);
    }

    internal static async Task DropOwned(CompanyOperation operation)
    {
        if (!await DatabaseExists(operation.DatabaseName)) return;
        var marker = await Scalar<string>(operation.DatabaseName,
            "SELECT CONVERT(nvarchar(128), value) FROM sys.extended_properties WHERE class = 0 AND name = N'Vatowiec.OperationId'");
        if (marker == operation.Id.ToString("D")) await Drop(operation.DatabaseName);
    }

    internal static async Task Drop(string database)
    {
        if (!CompanyDatabaseNames.IsCompanyDatabase(database)) throw new InvalidOperationException("Odmowa usunięcia bazy spoza wzorca testu.");
        SqlConnection.ClearAllPools();
        await Execute("master", $"ALTER DATABASE [{database}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{database}];");
    }

    internal static async Task<bool> DatabaseExists(string database)
    {
        await using var connection = new SqlConnection(Local + ";Database=master");
        await connection.OpenAsync();
        await using var command = new SqlCommand("SELECT COUNT(*) FROM sys.databases WHERE name = @name", connection);
        command.Parameters.AddWithValue("@name", database);
        return (int)(await command.ExecuteScalarAsync())! == 1;
    }

    internal static async Task Execute(string database, string sql)
    {
        await using var connection = new SqlConnection(Local + ";Database=" + database);
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection) { CommandTimeout = 120 };
        await command.ExecuteNonQueryAsync();
    }

    internal static async Task<int> Scalar(string database, string sql) => await Scalar<int>(database, sql);
    internal static async Task<T?> Scalar<T>(string database, string sql)
    {
        await using var connection = new SqlConnection(Local + ";Database=" + database);
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        var value = await command.ExecuteScalarAsync();
        return value is null or DBNull ? default : (T)value;
    }

    internal static WebApplicationFactory<Program> App(PreservedSystemTransaction tx) => new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
    {
        builder.UseEnvironment("Development"); builder.ConfigureLogging(x => x.ClearProviders());
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<SystemDbContext>(); services.AddScoped(_ => tx.Context());
            services.AddDataProtection().UseEphemeralDataProtectionProvider();
        });
    });

    internal static HttpClient Client(WebApplicationFactory<Program> app) => app.CreateClient(new() { BaseAddress = new("https://localhost"), AllowAutoRedirect = false });

    internal static async Task<ApplicationUser> Account(IServiceProvider services, bool admin, string password)
    {
        var users = services.GetRequiredService<UserManager<ApplicationUser>>();
        var user = new ApplicationUser { UserName = "fixture-provisioning-" + Guid.NewGuid().ToString("N") };
        Assert.True((await users.CreateAsync(user, password)).Succeeded);
        if (admin)
        {
            var roles = services.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
            if (!await roles.RoleExistsAsync(InstallationRoles.Administrator)) Assert.True((await roles.CreateAsync(new(InstallationRoles.Administrator) { Id = Guid.NewGuid() })).Succeeded);
            Assert.True((await users.AddToRoleAsync(user, InstallationRoles.Administrator)).Succeeded);
        }
        return user;
    }

    internal sealed class MutableClock(DateTimeOffset start, TimeZoneInfo? zone = null) : TimeProvider
    {
        private DateTimeOffset now = start;
        public override DateTimeOffset GetUtcNow() => now;
        public override TimeZoneInfo LocalTimeZone => zone ?? TimeZoneInfo.Local;
        public void Advance(TimeSpan by) => now += by;
    }
}
