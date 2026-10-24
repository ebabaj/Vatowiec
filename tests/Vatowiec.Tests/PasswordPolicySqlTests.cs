using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Vatowiec.Api.Security;
using Vatowiec.Core.Domain;
using Vatowiec.Core.Infrastructure.Identity;
using Vatowiec.Core.Infrastructure.Persistence;
using Vatowiec.SharedContracts;

namespace Vatowiec.Tests;

public sealed partial class UserAdministrationSqlTests
{
    private const string PolicyRoute = "/api/v1/installation/security/password-policy";
    private static PasswordPolicyRequest PolicyWrite(PasswordPolicyResponse p, bool enabled, int min = 12, bool upper = true,
        bool lower = true, bool digit = true, bool special = true, bool confirm = false) =>
        new(enabled, min, upper, lower, digit, special, p.RowVersion, confirm);

    [PreservedVatSqlFact]
    public async Task PasswordPolicy_IsDynamicForEveryIdentityWrite_AndNeverInvalidatesExistingPasswords_AllRolledBack()
    {
        await using var tx = await PreservedSystemTransaction.Open(); using var app = App(tx); using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SystemDbContext>(); var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        Assert.IsType<InstallationPasswordValidator>(Assert.Single(users.PasswordValidators));
        var strong = "Fixture!1a-" + Guid.NewGuid().ToString("N"); var adminUser = await Account(scope.ServiceProvider, true, strong);
        using var admin = Client(app); using var anonymous = Client(app);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(PolicyRoute)).StatusCode);
        await Login(admin, adminUser.UserName!, strong);
        var policy = (await admin.GetFromJsonAsync<PasswordPolicyResponse>(PolicyRoute))!;
        var initial = policy; Assert.True(policy.IsEnabled && policy.MinimumLength == 12 && policy.RequireUppercase && policy.RequireLowercase && policy.RequireDigit && policy.RequireSpecialCharacter);
        admin.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync(PolicyRoute, PolicyWrite(policy, false, confirm: true))).StatusCode);
        await Csrf(admin);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync(PolicyRoute, PolicyWrite(policy, false))).StatusCode);
        policy = await Ok<PasswordPolicyResponse>(await admin.PutAsJsonAsync(PolicyRoute, PolicyWrite(policy, false, confirm: true)));
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/api/v1/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PutAsJsonAsync(PolicyRoute, PolicyWrite(initial, true))).StatusCode);
        // The very same scoped UserManager used before the update must see the new policy too.
        Assert.True((await Assert.Single(users.PasswordValidators).ValidateAsync(users, new(), "x")).Succeeded);
        var name = "fixture-policy-" + Guid.NewGuid().ToString("N");
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync(Route, new UserCreateRequest(name, ""))).StatusCode);
        var created = await Ok<TemporaryPasswordResponse>(await admin.PostAsJsonAsync(Route, new UserCreateRequest(name, "a")));
        var id = created.User.User.Id; Assert.True(created.User.User.MustChangePassword);
        var generated = await Ok<TemporaryPasswordResponse>(await admin.PostAsJsonAsync(Route, new UserCreateRequest(name + "-generated")));
        Assert.True(generated.IsGenerated && generated.TemporaryPassword.Length == 16);
        using var target = Client(app); Assert.True((await Login(target, name, "a")).MustChangePassword);
        await Ok<JsonElement>(await target.PostAsJsonAsync("/api/v1/auth/password", new OwnPasswordRequest("a", "b")));
        Assert.False((await Login(target, name, "b")).MustChangePassword);
        await Ok<JsonElement>(await target.PostAsJsonAsync("/api/v1/auth/password", new OwnPasswordRequest("b", "c")));
        Assert.False((await Login(target, name, "c")).MustChangePassword);
        Assert.Equal(HttpStatusCode.Forbidden, (await target.GetAsync(PolicyRoute)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await target.PutAsJsonAsync(PolicyRoute, PolicyWrite(policy, true))).StatusCode);
        policy = await Ok<PasswordPolicyResponse>(await admin.PutAsJsonAsync(PolicyRoute, PolicyWrite(policy, true, 8, true, false, false, false)));
        Assert.Equal(HttpStatusCode.OK, (await target.GetAsync("/api/v1/auth/me")).StatusCode); // Policy update did not revoke any session.
        Assert.False((await Login(target, name, "c")).MustChangePassword); // Old weak password remains valid.
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync(Route, new UserCreateRequest(name + "-strict", "aaaaaaaa"))).StatusCode);
        await Ok<TemporaryPasswordResponse>(await admin.PostAsJsonAsync(Route, new UserCreateRequest(name + "-strict", "Aaaaaaaa")));
        var detail = await Detail(admin, id);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync(Route + $"/{id}/password-reset", new UserVersionRequest(detail.ConcurrencyStamp, true, "d"))).StatusCode);
        var reset = await Ok<TemporaryPasswordResponse>(await admin.PostAsJsonAsync(Route + $"/{id}/password-reset", new UserVersionRequest(detail.ConcurrencyStamp, true, "BBBBBBBB")));
        Assert.True(reset.User.User.MustChangePassword);
        Assert.True((await Login(target, name, "BBBBBBBB")).MustChangePassword);
        Assert.Equal(HttpStatusCode.BadRequest, (await target.PostAsJsonAsync("/api/v1/auth/password", new OwnPasswordRequest("BBBBBBBB", "dddddddd"))).StatusCode);
        await Ok<JsonElement>(await target.PostAsJsonAsync("/api/v1/auth/password", new OwnPasswordRequest("BBBBBBBB", "Dddddddd")));
        Assert.False((await Login(target, name, "Dddddddd")).MustChangePassword);
        Assert.Equal(HttpStatusCode.BadRequest, (await target.PostAsJsonAsync("/api/v1/auth/password", new OwnPasswordRequest("Dddddddd", "eeeeeeee"))).StatusCode);
        await Ok<JsonElement>(await target.PostAsJsonAsync("/api/v1/auth/password", new OwnPasswordRequest("Dddddddd", "Eeeeeeee")));
        foreach (var pair in new[] { (true, false, false, false, "aaaaaa", "Aaaaaa"), (false, true, false, false, "AAAAAA", "aAAAAA"),
            (false, false, true, false, "aaaaaa", "2aaaaa"), (false, false, false, true, "aaaaaa", "!aaaaa") })
        {
            policy = await Ok<PasswordPolicyResponse>(await admin.PutAsJsonAsync(PolicyRoute, PolicyWrite(policy, true, 6, pair.Item1, pair.Item2, pair.Item3, pair.Item4)));
            var unique = name + Guid.NewGuid().ToString("N");
            Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync(Route, new UserCreateRequest(unique, pair.Item5))).StatusCode);
            await Ok<TemporaryPasswordResponse>(await admin.PostAsJsonAsync(Route, new UserCreateRequest(unique, pair.Item6)));
        }
        policy = await Ok<PasswordPolicyResponse>(await admin.PutAsJsonAsync(PolicyRoute, PolicyWrite(policy, true, 80)));
        Assert.False((await Login(target, name, "Eeeeeeee")).MustChangePassword);
        var history = (await admin.GetFromJsonAsync<PasswordPolicyHistory[]>(PolicyRoute + "/history"))!;
        Assert.Contains(history, x => !x.After.IsEnabled); Assert.Contains(history, x => x.After.MinimumLength == 80);
        Assert.All(history, x => Assert.Equal(adminUser.UserName, x.ActorName));
        var audit = await db.PasswordPolicyChanges.AsNoTracking().FirstAsync();
        db.Attach(audit); db.Entry(audit).State = EntityState.Deleted;
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync()); db.ChangeTracker.Clear();
        // The immutable trigger also protects raw SQL, not only the EF context.
        await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE audit.PasswordPolicyChanges SET ActorName=N'changed' WHERE Id={audit.Id}"));
    }

    [PreservedVatSqlFact]
    public async Task AdministratorRole_GrantRevokeVersionsCsrfAndSessionRevocation_DoNotGrantCompanyAccess_AllRolledBack()
    {
        await using var tx = await PreservedSystemTransaction.Open(); using var app = App(tx); using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SystemDbContext>(); var strong = "Fixture!1a-" + Guid.NewGuid().ToString("N");
        var actor = await Account(scope.ServiceProvider, true, strong); var other = await Account(scope.ServiceProvider, false, strong);
        db.CompanyAccesses.Add(new(other.Id, A, CompanyPermission.ViewProfile)); await db.SaveChangesAsync();
        var accessVersion = (await db.CompanyAccesses.SingleAsync(x => x.UserId == other.Id)).RowVersion.ToArray();
        using var admin = Client(app); using var target = Client(app);
        await Login(admin, actor.UserName!, strong); await Login(target, other.UserName!, strong);
        var blocked = await Account(scope.ServiceProvider, false, strong);
        blocked.IsBlocked = true;
        Assert.True((await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>().UpdateAsync(blocked)).Succeeded);
        var blockedDetail = await Detail(admin, blocked.Id);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync(Route + $"/{blocked.Id}/administrator-role", new AdministratorRoleRequest(blockedDetail.ConcurrencyStamp, true, true))).StatusCode);
        var before = await Detail(admin, other.Id); var path = Route + $"/{other.Id}/administrator-role";
        Assert.Equal(HttpStatusCode.Forbidden, (await target.PutAsJsonAsync(path, new AdministratorRoleRequest(before.ConcurrencyStamp, true, true))).StatusCode);
        admin.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync(path, new AdministratorRoleRequest(before.ConcurrencyStamp, true, true))).StatusCode);
        await Csrf(admin);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync(path, new AdministratorRoleRequest(before.ConcurrencyStamp, true, false))).StatusCode);
        var granted = await Ok<UserAdministrationDetail>(await admin.PutAsJsonAsync(path, new AdministratorRoleRequest(before.ConcurrencyStamp, true, true)));
        Assert.True(granted.User.IsInstallationAdministrator); Assert.Equal(A, Assert.Single(granted.Access).CompanyId);
        Assert.Equal(HttpStatusCode.Unauthorized, (await target.GetAsync(Route)).StatusCode);
        Assert.True((await Login(target, other.UserName!, strong)).IsInstallationAdministrator);
        Assert.Equal(HttpStatusCode.OK, (await target.GetAsync(Route)).StatusCode);
        Assert.Equal(A, Assert.Single((await target.GetFromJsonAsync<CompanyResponse[]>("/api/v1/companies"))!).CompanyId);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PutAsJsonAsync(path, new AdministratorRoleRequest(before.ConcurrencyStamp, false, true))).StatusCode);
        var self = await Detail(admin, actor.Id);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync(Route + $"/{actor.Id}/administrator-role", new AdministratorRoleRequest(self.ConcurrencyStamp, false, true))).StatusCode);
        var revoked = await Ok<UserAdministrationDetail>(await admin.PutAsJsonAsync(path, new AdministratorRoleRequest(granted.ConcurrencyStamp, false, true)));
        Assert.False(revoked.User.IsInstallationAdministrator);
        Assert.Equal(HttpStatusCode.Unauthorized, (await target.GetAsync(Route)).StatusCode);
        Assert.False((await Login(target, other.UserName!, strong)).IsInstallationAdministrator);
        Assert.Equal(HttpStatusCode.Forbidden, (await target.GetAsync(Route)).StatusCode);
        Assert.Equal(accessVersion, (await db.CompanyAccesses.AsNoTracking().SingleAsync(x => x.UserId == other.Id)).RowVersion);
        var history = (await admin.GetFromJsonAsync<UserAdministrationHistory[]>(Route + $"/history?userId={other.Id}"))!;
        Assert.Equal(2, history.Length); Assert.Contains(history, x => x.Operation == "GrantInstallationAdministrator"); Assert.Contains(history, x => x.Operation == "RevokeInstallationAdministrator");
        Assert.All(history, x => { Assert.Equal(actor.Id, x.ActorId); Assert.Equal(other.Id, x.TargetId); Assert.Null(x.CompanyId); });
    }

    [PreservedVatSqlFact]
    public async Task PolicyAndRoleAuditFailuresRollBackConfigurationMembershipStampsAndSessions()
    {
        await using var tx = await PreservedSystemTransaction.Open(); var failure = new FailSecurityAudit(); using var app = App(tx, failure);
        using var scope = app.Services.CreateScope();
        var password = "Fixture!1a-" + Guid.NewGuid().ToString("N");
        var actor = await Account(scope.ServiceProvider, true, password); var target = await Account(scope.ServiceProvider, false, password);
        using var admin = Client(app); using var ordinary = Client(app);
        await Login(admin, actor.UserName!, password); await Login(ordinary, target.UserName!, password);
        var policy = (await admin.GetFromJsonAsync<PasswordPolicyResponse>(PolicyRoute))!;
        var before = await Detail(admin, target.Id);
        failure.Enabled = true;
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await admin.PutAsJsonAsync(PolicyRoute, PolicyWrite(policy, false, confirm: true))).StatusCode);
        Assert.Equal(policy, await admin.GetFromJsonAsync<PasswordPolicyResponse>(PolicyRoute));
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await admin.PutAsJsonAsync(Route + $"/{target.Id}/administrator-role", new AdministratorRoleRequest(before.ConcurrencyStamp, true, true))).StatusCode);
        failure.Enabled = false;
        var after = await Detail(admin, target.Id);
        Assert.False(after.User.IsInstallationAdministrator); Assert.Equal(before.ConcurrencyStamp, after.ConcurrencyStamp);
        Assert.Equal(HttpStatusCode.OK, (await ordinary.GetAsync("/api/v1/auth/me")).StatusCode);
        Assert.Empty((await admin.GetFromJsonAsync<UserAdministrationHistory[]>(Route + $"/history?userId={target.Id}"))!);
    }
    private sealed class FailSecurityAudit : DbCommandInterceptor
    {
        public bool Enabled;
        private void Check(DbCommand command)
        {
            if (Enabled && (command.CommandText.Contains("INSERT INTO [audit].[PasswordPolicyChanges]", StringComparison.Ordinal)
                || command.CommandText.Contains("INSERT INTO [audit].[UserAdministrationChanges]", StringComparison.Ordinal)))
                throw new DbUpdateException("Fikcyjna awaria audytu bezpieczeństwa.");
        }
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand c, CommandEventData d, InterceptionResult<DbDataReader> r, CancellationToken ct = default)
        { Check(c); return ValueTask.FromResult(r); }
        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand c, CommandEventData d, InterceptionResult<int> r, CancellationToken ct = default)
        { Check(c); return ValueTask.FromResult(r); }
    }

    [PreservedVatSqlFact]
    public async Task ParallelAdministratorRequests_AreSerializedByRealSqlTransactionLock_SeparateConnectionsRollBackBothFixtures()
    {
        await using var firstTx = await PreservedSystemTransaction.Open(); await using var secondTx = await PreservedSystemTransaction.Open();
        var hold = new HoldAdministrationLock(); using var firstApp = App(firstTx, hold); using var secondApp = App(secondTx);
        using var firstScope = firstApp.Services.CreateScope(); using var secondScope = secondApp.Services.CreateScope();
        using var firstClient = Client(firstApp); using var secondClient = Client(secondApp);
        // Two rolled-back session fixtures for an unchanged existing administrator avoid cross-
        // transaction Identity user-name lookups blocking on uncommitted account fixtures.
        await SessionFixture(firstScope.ServiceProvider, firstClient); await SessionFixture(secondScope.ServiceProvider, secondClient);
        var policy = (await firstClient.GetFromJsonAsync<PasswordPolicyResponse>(PolicyRoute))!;
        var unchanged = PolicyWrite(policy, policy.IsEnabled, policy.MinimumLength, policy.RequireUppercase, policy.RequireLowercase,
            policy.RequireDigit, policy.RequireSpecialCharacter, confirm: true);
        var one = firstClient.PutAsJsonAsync(PolicyRoute, unchanged);
        await hold.Acquired.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var two = secondClient.PutAsJsonAsync(PolicyRoute, unchanged);
        try
        {
            await Task.Delay(150); Assert.False(two.IsCompleted);
            hold.Continue.TrySetResult();
            Assert.Equal(policy, await Ok<PasswordPolicyResponse>(await one));
            // Fixture commits are savepoints; release the real first transaction's lock by rolling it back.
            await firstTx.DisposeAsync();
            Assert.Equal(policy, await Ok<PasswordPolicyResponse>(await two));
        }
        finally { hold.Continue.TrySetResult(); }
    }
    private static async Task SessionFixture(IServiceProvider services, HttpClient client)
    {
        var db = services.GetRequiredService<SystemDbContext>(); var now = DateTimeOffset.UtcNow;
        var actor = await (from user in db.Users.AsNoTracking() join membership in db.UserRoles on user.Id equals membership.UserId
            join role in db.Roles on membership.RoleId equals role.Id
            where role.NormalizedName == "INSTALLATIONADMINISTRATOR" && !user.IsBlocked && !user.MustChangePassword &&
                (!user.LockoutEnabled || user.LockoutEnd == null || user.LockoutEnd <= now)
            select user).FirstAsync();
        var session = new UserSession(actor.Id, now, TimeSpan.FromMinutes(5)); db.UserSessions.Add(session); await db.SaveChangesAsync();
        var principal = await services.GetRequiredService<SignInManager<ApplicationUser>>().CreateUserPrincipalAsync(actor);
        ((ClaimsIdentity)principal.Identity!).AddClaim(new Claim(ApiSecurity.SessionClaim, session.Id.ToString()));
        var options = services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>().Get(IdentityConstants.ApplicationScheme);
        var ticket = new AuthenticationTicket(principal, new AuthenticationProperties { IssuedUtc = now, ExpiresUtc = session.ExpiresAtUtc }, IdentityConstants.ApplicationScheme);
        client.DefaultRequestHeaders.Add("Cookie", options.Cookie.Name + "=" + options.TicketDataFormat.Protect(ticket));
        await Csrf(client);
    }
    private sealed class HoldAdministrationLock : DbCommandInterceptor
    {
        public TaskCompletionSource Acquired { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Continue { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask<int> NonQueryExecutedAsync(DbCommand command, CommandExecutedEventData eventData, int result, CancellationToken ct = default)
        {
            if (command.CommandText.Contains("sp_getapplock", StringComparison.Ordinal))
            { Acquired.TrySetResult(); await Continue.Task.WaitAsync(TimeSpan.FromSeconds(10), ct); }
            return result;
        }
    }
}
