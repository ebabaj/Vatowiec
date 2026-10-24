using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using System.Data.Common;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Vatowiec.Core.Domain;
using Vatowiec.Core.Infrastructure.Identity;
using Vatowiec.Core.Infrastructure.Persistence;
using Vatowiec.SharedContracts;

namespace Vatowiec.Tests;

[Collection("LocalSql")]
[Trait("Category", "SqlIntegration")]
public sealed partial class UserAdministrationSqlTests
{
    private const string Route = "/api/v1/installation/users";
    private static readonly Guid A = Guid.Parse("aaaaaaaa-0000-4000-8000-000000000001");
    private static readonly Guid B = Guid.Parse("bbbbbbbb-0000-4000-8000-000000000002");
    private static readonly string[] Read = ["ViewProfile", "ViewContractors"];
    private static WebApplicationFactory<Program> App(PreservedSystemTransaction tx, DbCommandInterceptor? failure = null, ILoggerProvider? logs = null, TimeProvider? clock = null) => new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
    {
        builder.UseEnvironment("Development"); builder.ConfigureLogging(x => { x.ClearProviders(); if (logs != null) x.AddProvider(logs); });
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<SystemDbContext>(); services.AddScoped(_ => failure == null ? tx.Context() : new SystemDbContext(new DbContextOptionsBuilder<SystemDbContext>(tx.Options).AddInterceptors(failure).Options));
            services.AddDataProtection().UseEphemeralDataProtectionProvider();
            if (clock != null) services.AddSingleton(clock);
        });
    });
    private static HttpClient Client(WebApplicationFactory<Program> app) => app.CreateClient(new() { BaseAddress = new("https://localhost"), AllowAutoRedirect = false });
    private static async Task<ApplicationUser> Account(IServiceProvider services, bool admin, string password)
    {
        var users = services.GetRequiredService<UserManager<ApplicationUser>>();
        var user = new ApplicationUser { UserName = "fixture-users-" + Guid.NewGuid().ToString("N") };
        Assert.True((await users.CreateAsync(user, password)).Succeeded);
        if (admin)
        {
            var roles = services.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
            if (!await roles.RoleExistsAsync(InstallationRoles.Administrator)) Assert.True((await roles.CreateAsync(new(InstallationRoles.Administrator) { Id = Guid.NewGuid() })).Succeeded);
            Assert.True((await users.AddToRoleAsync(user, InstallationRoles.Administrator)).Succeeded);
        }
        return user;
    }
    private static async Task<T> Ok<T>(HttpResponseMessage response)
    {
        using (response) { Assert.Equal(HttpStatusCode.OK, response.StatusCode); return (await response.Content.ReadFromJsonAsync<T>())!; }
    }
    private static Task<SessionResponse> Login(HttpClient client, string name, string password) => GusConfigurationSqlTests.Login(client, name, password);
    private static Task Csrf(HttpClient client) => GusConfigurationSqlTests.Csrf(client);
    private static async Task<UserAdministrationDetail> Detail(HttpClient admin, Guid id) => (await admin.GetFromJsonAsync<UserAdministrationDetail>(Route + "/" + id))!;
    private static UserAccessItem Access(Guid company, string[] permissions, UserAdministrationDetail user) =>
        new(company, permissions, user.Access.SingleOrDefault(x => x.CompanyId == company)?.RowVersion);

    [PreservedVatSqlFact]
    public async Task Administration_IdentityCsrfScopeVersionsResetBlockingAndImmutableAudit_AllWritesRolledBack()
    {
        await using var tx = await PreservedSystemTransaction.Open(); using var app = App(tx);
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<SystemDbContext>();
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
        var password = "Fixture!1a-" + Guid.NewGuid().ToString("N");
        var adminUser = await Account(scope.ServiceProvider, true, password);
        var plainUser = await Account(scope.ServiceProvider, false, password);
        using var admin = Client(app); using var ordinary = Client(app); using var anonymous = Client(app);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(Route)).StatusCode);
        await Login(admin, adminUser.UserName!, password); await Login(ordinary, plainUser.UserName!, password);
        Assert.Equal(HttpStatusCode.Forbidden, (await ordinary.GetAsync(Route)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await ordinary.PostAsJsonAsync(Route, new UserCreateRequest("blocked-access-test"))).StatusCode);
        Assert.Empty((await admin.GetFromJsonAsync<CompanyResponse[]>("/api/v1/companies"))!); // Global role grants no company access.
        var definitions = (await admin.GetFromJsonAsync<UserAdministrationLookups>(Route + "/lookups"))!;
        Assert.Equal(Enum.GetValues<CompanyPermission>().Length - 1, definitions.Permissions.Length);
        admin.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        var name = "fixture-created-" + Guid.NewGuid().ToString("N");
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync(Route, new UserCreateRequest(name))).StatusCode);
        await Csrf(admin);
        var created = await Ok<TemporaryPasswordResponse>(await admin.PostAsJsonAsync(Route, new UserCreateRequest(name)));
        Assert.True(created.IsGenerated && created.TemporaryPassword.Length == 16);
        var id = created.User.User.Id; Assert.True(created.User.User.MustChangePassword); Assert.Empty(created.User.Access);
        admin.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync(Route + $"/{id}/password-reset", new UserVersionRequest(created.User.ConcurrencyStamp, true))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync(Route + $"/{id}/state", new UserBlockRequest(created.User.ConcurrencyStamp, true, true))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync(Route + $"/{id}/access", new UserAccessRequest(created.User.ConcurrencyStamp, [Access(A, Read, created.User)]))).StatusCode);
        await Csrf(admin);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync(Route + $"/{id}/password-reset", new UserVersionRequest(created.User.ConcurrencyStamp, false))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync(Route + $"/{id}/state", new UserBlockRequest(created.User.ConcurrencyStamp, true, false))).StatusCode);
        Assert.Single((await admin.GetFromJsonAsync<UserSummary[]>(Route + "?search=" + name))!);
        using var target = Client(app);
        var initial = await Login(target, name, created.TemporaryPassword); Assert.True(initial.MustChangePassword);
        Assert.Equal(HttpStatusCode.Forbidden, (await target.GetAsync("/api/v1/companies")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await target.GetAsync(Route)).StatusCode);
        // New account can only change its password, never access company/admin data first.
        var personal = "Own!2b-" + Guid.NewGuid().ToString("N");
        Assert.Equal(HttpStatusCode.BadRequest, (await target.PostAsJsonAsync("/api/v1/auth/password", new OwnPasswordRequest(created.TemporaryPassword, "weak"))).StatusCode);
        await Ok<JsonElement>(await target.PostAsJsonAsync("/api/v1/auth/password", new OwnPasswordRequest(created.TemporaryPassword, personal)));
        Assert.Equal(HttpStatusCode.Unauthorized, (await target.GetAsync("/api/v1/auth/me")).StatusCode);
        Assert.False((await Login(target, name, personal)).MustChangePassword);
        var user = await Detail(admin, id);
        var invalid = new UserAccessRequest(user.ConcurrencyStamp, [Access(A, ["EditContractors"], user)]);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync(Route + $"/{id}/access", invalid)).StatusCode);
        user = await Ok<UserAdministrationDetail>(await admin.PutAsJsonAsync(Route + $"/{id}/access", new UserAccessRequest(user.ConcurrencyStamp, [Access(A, Read, user)])));
        Assert.Equal(HttpStatusCode.Unauthorized, (await target.GetAsync("/api/v1/companies")).StatusCode);
        await Login(target, name, personal);
        var companies = (await target.GetFromJsonAsync<CompanyResponse[]>("/api/v1/companies"))!;
        Assert.Equal(A, Assert.Single(companies).CompanyId);
        Assert.Equal(HttpStatusCode.Forbidden, (await target.GetAsync($"/api/v1/companies/{B}/profile")).StatusCode);
        user = await Ok<UserAdministrationDetail>(await admin.PutAsJsonAsync(Route + $"/{id}/access", new UserAccessRequest(user.ConcurrencyStamp,
            [Access(A, [.. Read, "EditContractors"], user), Access(B, ["ViewProfile", "ViewExchangeRates"], user)])));
        await Login(target, name, personal);
        companies = (await target.GetFromJsonAsync<CompanyResponse[]>("/api/v1/companies"))!;
        Assert.Contains("EditContractors", companies.Single(x => x.CompanyId == A).Permissions);
        Assert.DoesNotContain("EditContractors", companies.Single(x => x.CompanyId == B).Permissions);
        var stale = user;
        var auditBefore = await db.UserAdministrationChanges.CountAsync(x => x.TargetId == id);
        user = await Ok<UserAdministrationDetail>(await admin.PutAsJsonAsync(Route + $"/{id}/access", new UserAccessRequest(user.ConcurrencyStamp,
            [Access(A, Read, user), Access(B, [], user)])));
        Assert.Equal(HttpStatusCode.Unauthorized, (await target.GetAsync("/api/v1/auth/me")).StatusCode); // Revoke does not wait 30 minutes.
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PutAsJsonAsync(Route + $"/{id}/access", new UserAccessRequest(stale.ConcurrencyStamp,
            [Access(A, [], stale), Access(B, Read, stale)]))).StatusCode);
        var current = await Detail(admin, id); Assert.Equal(user.ConcurrencyStamp, current.ConcurrencyStamp);
        Assert.Equal(A, Assert.Single(current.Access).CompanyId); Assert.Equal(auditBefore + 2, await db.UserAdministrationChanges.CountAsync(x => x.TargetId == id));
        // No-op permissions keep user/version/audit unchanged.
        var noop = await Ok<UserAdministrationDetail>(await admin.PutAsJsonAsync(Route + $"/{id}/access", new UserAccessRequest(user.ConcurrencyStamp, [Access(A, Read, user)])));
        Assert.Equal(user.ConcurrencyStamp, noop.ConcurrencyStamp);
        var self = await Detail(admin, adminUser.Id);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync(Route + $"/{adminUser.Id}/state", new UserBlockRequest(self.ConcurrencyStamp, true, true))).StatusCode);
        var reset = await Ok<TemporaryPasswordResponse>(await admin.PostAsJsonAsync(Route + $"/{id}/password-reset", new UserVersionRequest(user.ConcurrencyStamp, true)));
        Assert.True(reset.IsGenerated && reset.TemporaryPassword.Length == 16);
        Assert.True(reset.User.User.MustChangePassword);
        await Csrf(target);
        Assert.Equal(HttpStatusCode.Unauthorized, (await target.PostAsJsonAsync("/api/v1/auth/login", new { userName = name, password = personal })).StatusCode);
        Assert.True((await Login(target, name, reset.TemporaryPassword)).MustChangePassword);
        user = await Detail(admin, id); // Identity advances ConcurrencyStamp after failed attempts.
        user = await Ok<UserAdministrationDetail>(await admin.PutAsJsonAsync(Route + $"/{id}/state", new UserBlockRequest(user.ConcurrencyStamp, true, true)));
        Assert.True(user.User.IsBlocked); Assert.Equal(HttpStatusCode.Unauthorized, (await target.GetAsync("/api/v1/auth/me")).StatusCode);
        await Csrf(target);
        Assert.Equal(HttpStatusCode.Unauthorized, (await target.PostAsJsonAsync("/api/v1/auth/login", new { userName = name, password = reset.TemporaryPassword })).StatusCode);
        user = await Ok<UserAdministrationDetail>(await admin.PutAsJsonAsync(Route + $"/{id}/state", new UserBlockRequest(user.ConcurrencyStamp, false, true)));
        Assert.False(user.User.IsBlocked);
        var beforeConflict = await Detail(admin, id);
        var conflictAuditCount = await db.UserAdministrationChanges.CountAsync(x => x.TargetId == id);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PutAsJsonAsync(Route + $"/{id}/access", new UserAccessRequest(beforeConflict.ConcurrencyStamp,
            [Access(A, [.. Read, "EditContractors"], beforeConflict), new(B, Read, "wrong-version")]))).StatusCode);
        var afterConflict = await Detail(admin, id);
        Assert.Equal(beforeConflict.ConcurrencyStamp, afterConflict.ConcurrencyStamp); Assert.Equal(Read, Assert.Single(afterConflict.Access).Permissions);
        Assert.Equal(conflictAuditCount, await db.UserAdministrationChanges.CountAsync(x => x.TargetId == id));
        var history = (await admin.GetFromJsonAsync<UserAdministrationHistory[]>(Route + $"/history?userId={id}"))!;
        Assert.Contains(history, x => x.Operation == "ResetPassword"); Assert.Contains(history, x => x.Operation == "ChangeOwnPassword");
        Assert.Contains(history, x => x.CompanyId == B && x.PermissionsAfter is { Length: 0 });
        var raw = JsonSerializer.Serialize(history); Assert.DoesNotContain(created.TemporaryPassword, raw); Assert.DoesNotContain(reset.TemporaryPassword, raw);
        Assert.DoesNotContain("PasswordHash", raw); Assert.DoesNotContain("SecurityStamp", raw);
        Assert.All(history, x => { Assert.NotEqual(Guid.Empty, x.ActorId); Assert.False(string.IsNullOrWhiteSpace(x.ActorName)); });
        var audit = await db.UserAdministrationChanges.AsNoTracking().FirstAsync(x => x.TargetId == id);
        Assert.NotEqual(Guid.Empty, audit.ActorIssuerId); Assert.Empty(db.Model.FindEntityType(typeof(UserAdministrationChange))!.GetForeignKeys());
        db.Attach(audit); db.Entry(audit).State = EntityState.Deleted;
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync()); db.ChangeTracker.Clear();
        // Fresh role check: even an existing cookie cannot retain administrative authority.
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        adminUser = (await users.FindByIdAsync(adminUser.Id.ToString()))!;
        Assert.True((await users.RemoveFromRoleAsync(adminUser, InstallationRoles.Administrator)).Succeeded);
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.GetAsync(Route)).StatusCode);
    }

    [PreservedVatSqlFact]
    public async Task CustomTemporaryPassword_CreateAndReset_IdentityValidationForcedChangeAndNoSecretAudit_AllRolledBack()
    {
        await using var tx = await PreservedSystemTransaction.Open(); using var app = App(tx); using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SystemDbContext>();
        var adminPassword = "Fixture!1a-" + Guid.NewGuid().ToString("N"); var actor = await Account(scope.ServiceProvider, true, adminPassword);
        using var admin = Client(app); await Login(admin, actor.UserName!, adminPassword);
        var name = "fixture-custom-" + Guid.NewGuid().ToString("N");
        var initialCount = await db.UserAdministrationChanges.CountAsync();
        foreach (var invalid in new[] { "", "weak", new string('a', 16), new string('A', 1025) })
            Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync(Route, new UserCreateRequest(name, invalid))).StatusCode);
        Assert.False(await db.Users.AnyAsync(x => x.UserName == name)); Assert.Equal(initialCount, await db.UserAdministrationChanges.CountAsync());
        var custom = " My!2a-" + Guid.NewGuid().ToString("N") + " ";
        var created = await Ok<TemporaryPasswordResponse>(await admin.PostAsJsonAsync(Route, new UserCreateRequest(name, custom)));
        Assert.False(created.IsGenerated); Assert.True(custom == created.TemporaryPassword); Assert.True(created.User.User.MustChangePassword);
        using var target = Client(app); Assert.True((await Login(target, name, custom)).MustChangePassword);
        var id = created.User.User.Id; var before = await Detail(admin, id);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync(Route + $"/{id}/password-reset", new UserVersionRequest(before.ConcurrencyStamp, true, "weak"))).StatusCode);
        Assert.Equal(before.ConcurrencyStamp, (await Detail(admin, id)).ConcurrencyStamp);
        Assert.Equal(HttpStatusCode.OK, (await target.GetAsync("/api/v1/auth/me")).StatusCode);
        var replacement = " Other!3b-" + Guid.NewGuid().ToString("N") + " ";
        var reset = await Ok<TemporaryPasswordResponse>(await admin.PostAsJsonAsync(Route + $"/{id}/password-reset", new UserVersionRequest(before.ConcurrencyStamp, true, replacement)));
        Assert.False(reset.IsGenerated); Assert.True(replacement == reset.TemporaryPassword); Assert.True(reset.User.User.MustChangePassword);
        Assert.Equal(HttpStatusCode.Unauthorized, (await target.GetAsync("/api/v1/auth/me")).StatusCode);
        Assert.True((await Login(target, name, replacement)).MustChangePassword);
        Assert.Equal(HttpStatusCode.Forbidden, (await target.GetAsync("/api/v1/companies")).StatusCode);
        var own = "Personal!4c-" + Guid.NewGuid().ToString("N");
        await Ok<JsonElement>(await target.PostAsJsonAsync("/api/v1/auth/password", new OwnPasswordRequest(replacement, own)));
        Assert.False((await Login(target, name, own)).MustChangePassword);
        var history = await (await admin.GetAsync(Route + $"/history?userId={id}")).Content.ReadAsStringAsync();
        Assert.False(history.Contains(custom) || history.Contains(replacement) || history.Contains(own));
        Assert.Contains("CreateUser", history); Assert.Contains("ResetPassword", history); Assert.Contains("ChangeOwnPassword", history);
    }

    [PreservedVatSqlFact]
    public async Task DemoSeedPreservesExistingPasswordRolesAccessFlagsAndSessions_AllWritesRolledBack()
    {
        await using var tx = await PreservedSystemTransaction.Open(); using var app = App(tx); using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SystemDbContext>(); var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
        var initializer = new DemoUserInitializer(db, users, roles);
        var name = "fixture-seed-" + Guid.NewGuid().ToString("N"); var initialPassword = "Seed!1a-" + Guid.NewGuid().ToString("N");
        Assert.True(await initializer.CreateIfMissing(name, initialPassword, "Operator", new Dictionary<Guid, CompanyPermission> { [A] = CompanyPermission.ViewProfile }));
        var user = (await users.FindByNameAsync(name))!; var personal = "Personal!2b-" + Guid.NewGuid().ToString("N");
        Assert.True((await users.ChangePasswordAsync(user, initialPassword, personal)).Succeeded);
        Assert.True((await users.RemoveFromRoleAsync(user, "Operator")).Succeeded);
        user.IsBlocked = true; user.MustChangePassword = true; Assert.True((await users.UpdateAsync(user)).Succeeded);
        db.CompanyAccesses.Remove(await db.CompanyAccesses.SingleAsync(x => x.UserId == user.Id));
        db.CompanyAccesses.Add(new(user.Id, B, CompanyPermission.ViewProfile | CompanyPermission.ViewExchangeRates)); await db.SaveChangesAsync();
        var stamp = user.ConcurrencyStamp; var security = user.SecurityStamp;
        var version = (await db.CompanyAccesses.SingleAsync(x => x.UserId == user.Id)).RowVersion.ToArray();
        Assert.False(await initializer.CreateIfMissing(name, initialPassword, "Operator", new Dictionary<Guid, CompanyPermission> { [A] = CompanyPermission.ViewProfile | CompanyPermission.EditContractors }));
        db.ChangeTracker.Clear(); user = (await users.FindByNameAsync(name))!;
        Assert.True(await users.CheckPasswordAsync(user, personal)); Assert.False(await users.CheckPasswordAsync(user, initialPassword));
        Assert.Empty(await users.GetRolesAsync(user)); Assert.True(user.IsBlocked); Assert.True(user.MustChangePassword);
        Assert.Equal(stamp, user.ConcurrencyStamp); Assert.Equal(security, user.SecurityStamp);
        var access = Assert.Single(await db.CompanyAccesses.Where(x => x.UserId == user.Id).ToArrayAsync());
        Assert.Equal(B, access.CompanyId); Assert.Equal(version, access.RowVersion);
    }

    [PreservedVatSqlFact]
    public async Task AuditFailureRollsBackAccountStampAndRevocation_ExistingSessionStillWorks()
    {
        await using var tx = await PreservedSystemTransaction.Open(); var failure = new FailAudit(); using var app = App(tx, failure);
        using var scope = app.Services.CreateScope();
        var password = "Fixture!1a-" + Guid.NewGuid().ToString("N");
        var actor = await Account(scope.ServiceProvider, true, password); var target = await Account(scope.ServiceProvider, false, password);
        using var admin = Client(app); using var ordinary = Client(app);
        await Login(admin, actor.UserName!, password); await Login(ordinary, target.UserName!, password);
        var before = await Detail(admin, target.Id); failure.Enabled = true;
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await admin.PutAsJsonAsync(Route + $"/{target.Id}/state", new UserBlockRequest(before.ConcurrencyStamp, true, true))).StatusCode);
        failure.Enabled = false;
        var after = await Detail(admin, target.Id); Assert.False(after.User.IsBlocked); Assert.Equal(before.ConcurrencyStamp, after.ConcurrencyStamp);
        Assert.Equal(HttpStatusCode.OK, (await ordinary.GetAsync("/api/v1/auth/me")).StatusCode);
        Assert.Empty((await admin.GetFromJsonAsync<UserAdministrationHistory[]>(Route + $"/history?userId={target.Id}"))!);
    }
    private sealed class FailAudit : DbCommandInterceptor
    {
        public bool Enabled { get; set; }
        private void Check(DbCommand command)
        { if (Enabled && command.CommandText.Contains("INSERT INTO [audit].[UserAdministrationChanges]", StringComparison.Ordinal)) throw new DbUpdateException("Fikcyjna awaria audytu."); }
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData data, InterceptionResult<DbDataReader> result, CancellationToken ct = default)
        { Check(command); return ValueTask.FromResult(result); }
        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData data, InterceptionResult<int> result, CancellationToken ct = default)
        { Check(command); return ValueTask.FromResult(result); }
    }

    [PreservedVatSqlFact]
    public async Task AuditTriggerRejectsSqlMutation_OuterTransactionRollsBackFixture()
    {
        await using var tx = await PreservedSystemTransaction.Open(); await using var db = tx.Context();
        var id = Guid.NewGuid(); var audit = new UserAdministrationChange(id, Guid.NewGuid(), "Fikcyjny wykonawca", id, "Fikcyjne konto", DateTimeOffset.UtcNow, "Fixture");
        db.UserAdministrationChanges.Add(audit); await db.SaveChangesAsync();
        // Last statement: THROW can doom the transaction; the outer boundary rolls it back.
        await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE audit.UserAdministrationChanges SET Operation=N'Mutated' WHERE Id={audit.Id}"));
    }
}
