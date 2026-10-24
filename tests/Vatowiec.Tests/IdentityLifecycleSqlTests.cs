using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Vatowiec.Core.Domain;
using Vatowiec.Core.Infrastructure.Identity;
using Vatowiec.Core.Infrastructure.Persistence;
using Vatowiec.SharedContracts;

namespace Vatowiec.Tests;

public sealed partial class UserAdministrationSqlTests
{
    [PreservedVatSqlFact]
    public async Task HistoryRejectsCurrentAndNPreviousAcrossResetForcedAndOwnChange_WhenComplexityDisabled_AllRolledBack()
    {
        await using var tx = await PreservedSystemTransaction.Open(); using var app = App(tx); using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SystemDbContext>(); var strong = "Fixture!1a-" + Guid.NewGuid().ToString("N");
        var actor = await Account(scope.ServiceProvider, true, strong); using var admin = Client(app); await Login(admin, actor.UserName!, strong);
        var p = (await admin.GetFromJsonAsync<PasswordPolicyResponse>(PolicyRoute))!;
        await Ok<PasswordPolicyResponse>(await admin.PutAsJsonAsync(PolicyRoute, PolicyWrite(p, false, confirm: true) with { PreventPasswordReuse = true, PreviousPasswordCount = 2 }));
        var name = "fixture-history-" + Guid.NewGuid().ToString("N");
        var created = await Ok<TemporaryPasswordResponse>(await admin.PostAsJsonAsync(Route, new UserCreateRequest(name, "a"))); var id = created.User.User.Id;
        Assert.Empty(await db.UserPasswordHistories.Where(x => x.UserId == id).ToArrayAsync());
        Assert.NotNull((await db.Users.AsNoTracking().SingleAsync(x => x.Id == id)).PasswordChangedAtUtc);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync(Route + $"/{id}/password-reset", new UserVersionRequest(created.User.ConcurrencyStamp, true, "a"))).StatusCode);
        Assert.Equal(created.User.ConcurrencyStamp, (await Detail(admin, id)).ConcurrencyStamp);
        using var target = Client(app); Assert.True((await Login(target, name, "a")).MustChangePassword);
        await Ok<JsonElement>(await target.PostAsJsonAsync("/api/v1/auth/password", new OwnPasswordRequest("a", "b")));
        await Login(target, name, "b");
        Assert.Equal(HttpStatusCode.BadRequest, (await target.PostAsJsonAsync("/api/v1/auth/password", new OwnPasswordRequest("b", "a"))).StatusCode);
        await Ok<JsonElement>(await target.PostAsJsonAsync("/api/v1/auth/password", new OwnPasswordRequest("b", "c")));
        await Login(target, name, "c"); var current = await Detail(admin, id);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync(Route + $"/{id}/password-reset", new UserVersionRequest(current.ConcurrencyStamp, true, "a"))).StatusCode);
        await Ok<JsonElement>(await target.PostAsJsonAsync("/api/v1/auth/password", new OwnPasswordRequest("c", "d")));
        await Login(target, name, "d"); current = await Detail(admin, id);
        var reset = await Ok<TemporaryPasswordResponse>(await admin.PostAsJsonAsync(Route + $"/{id}/password-reset", new UserVersionRequest(current.ConcurrencyStamp, true, "a")));
        Assert.True(reset.User.User.MustChangePassword); Assert.Equal(HttpStatusCode.Unauthorized, (await target.GetAsync("/api/v1/auth/me")).StatusCode);
        Assert.True((await Login(target, name, "a")).MustChangePassword);
        var hashes = await db.UserPasswordHistories.AsNoTracking().Where(x => x.UserId == id).OrderByDescending(x => x.Id).ToArrayAsync();
        Assert.Equal(4, hashes.Length);
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<ApplicationUser>>(); var user = (await db.Users.AsNoTracking().SingleAsync(x => x.Id == id));
        Assert.True(hasher.VerifyHashedPassword(user, hashes[0].PasswordHash, "d") != PasswordVerificationResult.Failed);
        Assert.All(hashes, x => Assert.True(x.PasswordHash.Length > 16));
    }

    [PreservedVatSqlFact]
    public async Task ExpirationGrantsGraceThenRestrictsAlreadyOpenBusinessAndAdministratorSessions_AllRolledBack()
    {
        await using var tx = await PreservedSystemTransaction.Open(); var clock = new LifecycleClock(); using var app = App(tx, clock: clock);
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<SystemDbContext>();
        var password = "Fixture!1a-" + Guid.NewGuid().ToString("N"); var actor = await Account(scope.ServiceProvider, true, password);
        var other = await Account(scope.ServiceProvider, false, password); actor.PasswordChangedAtUtc = other.PasswordChangedAtUtc = clock.Now.AddYears(-2);
        db.CompanyAccesses.Add(new(other.Id, A, CompanyPermission.ViewProfile)); await db.SaveChangesAsync();
        using var admin = Client(app); using var ordinary = Client(app); await Login(admin, actor.UserName!, password); await Login(ordinary, other.UserName!, password);
        var p = (await admin.GetFromJsonAsync<PasswordPolicyResponse>(PolicyRoute))!;
        await Ok<PasswordPolicyResponse>(await admin.PutAsJsonAsync(PolicyRoute, PolicyWrite(p, true) with { EnforcePasswordExpiration = true, PasswordValidityDays = 1 }));
        Assert.False((await Login(ordinary, other.UserName!, password)).MustChangePassword); // Old timestamp receives the full grace period.
        Assert.Equal(HttpStatusCode.OK, (await ordinary.GetAsync("/api/v1/companies")).StatusCode);
        var policy = await db.PasswordPolicies.SingleAsync();
        db.Entry(policy).Property(x => x.PasswordExpirationEnabledAtUtc).CurrentValue = clock.Now.AddDays(-1).AddMinutes(5); await db.SaveChangesAsync();
        clock.Now = clock.Now.AddMinutes(6); // Cookies are still within their original 30-minute validity.
        Assert.Equal(HttpStatusCode.Forbidden, (await ordinary.GetAsync("/api/v1/companies")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.GetAsync(PolicyRoute)).StatusCode);
        var state = (await ordinary.GetFromJsonAsync<SessionResponse>("/api/v1/auth/me"))!; Assert.True(state.MustChangePassword); Assert.Equal(other.Id, state.UserId);
        var next = "Personal!2b-" + Guid.NewGuid().ToString("N");
        await Ok<JsonElement>(await ordinary.PostAsJsonAsync("/api/v1/auth/password", new OwnPasswordRequest(password, next)));
        Assert.False((await Login(ordinary, other.UserName!, next)).MustChangePassword);
        Assert.Equal(HttpStatusCode.OK, (await ordinary.GetAsync("/api/v1/companies")).StatusCode);
        Assert.Equal(clock.Now, (await db.Users.AsNoTracking().SingleAsync(x => x.Id == other.Id)).PasswordChangedAtUtc);
    }

    [PreservedVatSqlFact]
    public async Task ArchiveRestoreSameAndReuseAsNewPersonPreserveIdentityAuditAndNeverInheritAccess_AllRolledBack()
    {
        await using var tx = await PreservedSystemTransaction.Open(); using var app = App(tx); using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SystemDbContext>(); var password = "Fixture!1a-" + Guid.NewGuid().ToString("N");
        var actor = await Account(scope.ServiceProvider, true, password); var old = await Account(scope.ServiceProvider, true, password);
        db.CompanyAccesses.Add(new(old.Id, A, CompanyPermission.ViewProfile)); await db.SaveChangesAsync();
        using var admin = Client(app); using var target = Client(app); await Login(admin, actor.UserName!, password); await Login(target, old.UserName!, password);
        var ordinaryAccount = await Account(scope.ServiceProvider, false, password); using var denied = Client(app); await Login(denied, ordinaryAccount.UserName!, password);
        var before = await Detail(admin, old.Id); var self = await Detail(admin, actor.Id);
        Assert.Equal(HttpStatusCode.Forbidden, (await denied.PostAsJsonAsync(Route + $"/{old.Id}/archive", new UserVersionRequest(before.ConcurrencyStamp, true))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync(Route + $"/{actor.Id}/archive", new UserVersionRequest(self.ConcurrencyStamp, true))).StatusCode);
        admin.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync(Route + $"/{old.Id}/archive", new UserVersionRequest(before.ConcurrencyStamp, true))).StatusCode); await Csrf(admin);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsJsonAsync(Route + $"/{old.Id}/archive", new UserVersionRequest("stale", true))).StatusCode);
        var archived = await Ok<UserAdministrationDetail>(await admin.PostAsJsonAsync(Route + $"/{old.Id}/archive", new UserVersionRequest(before.ConcurrencyStamp, true)));
        Assert.True(archived.User.IsArchived); Assert.Equal(actor.UserName, archived.User.ArchivedByName); Assert.NotNull(archived.User.ArchivedAtUtc);
        Assert.False(archived.User.IsInstallationAdministrator); Assert.Empty(archived.Access);
        Assert.Equal(HttpStatusCode.Unauthorized, (await target.GetAsync("/api/v1/auth/me")).StatusCode);
        await Csrf(target); Assert.Equal(HttpStatusCode.Unauthorized, (await target.PostAsJsonAsync("/api/v1/auth/login", new { userName = old.UserName, password })).StatusCode);
        Assert.DoesNotContain((await admin.GetFromJsonAsync<UserSummary[]>(Route))!, x => x.Id == old.Id);
        Assert.Contains((await admin.GetFromJsonAsync<UserSummary[]>(Route + "?includeArchived=true"))!, x => x.Id == old.Id && x.IsArchived);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync(Route, new UserCreateRequest(old.UserName!, password))).StatusCode);
        var initializer = new DemoUserInitializer(db, scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>(), scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>());
        Assert.False(await initializer.CreateIfMissing(old.UserName!, password, "Operator", new Dictionary<Guid, CompanyPermission> { [A] = CompanyPermission.ViewProfile }));
        var personal = "Restored!2b-" + Guid.NewGuid().ToString("N");
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync(Route + $"/{old.Id}/restore", new UserVersionRequest(archived.ConcurrencyStamp, false, personal))).StatusCode);
        var restored = await Ok<TemporaryPasswordResponse>(await admin.PostAsJsonAsync(Route + $"/{old.Id}/restore", new UserVersionRequest(archived.ConcurrencyStamp, true, personal)));
        Assert.Equal(old.Id, restored.User.User.Id); Assert.Equal(old.UserName, restored.User.User.UserName); Assert.False(restored.User.User.IsArchived);
        Assert.Empty(restored.User.Access); Assert.False(restored.User.User.IsInstallationAdministrator); Assert.True(restored.User.User.MustChangePassword);
        Assert.True((await Login(target, old.UserName!, personal)).MustChangePassword);
        Assert.Equal(HttpStatusCode.Forbidden, (await target.GetAsync("/api/v1/companies")).StatusCode);
        Assert.True(await db.UserPasswordHistories.AnyAsync(x => x.UserId == old.Id));
        var again = await Ok<UserAdministrationDetail>(await admin.PostAsJsonAsync(Route + $"/{old.Id}/archive", new UserVersionRequest(restored.User.ConcurrencyStamp, true)));
        var newer = "NewPerson!3c-" + Guid.NewGuid().ToString("N");
        var reused = await Ok<TemporaryPasswordResponse>(await admin.PostAsJsonAsync(Route + $"/{old.Id}/reuse-login", new UserVersionRequest(again.ConcurrencyStamp, true, newer)));
        Assert.NotEqual(old.Id, reused.User.User.Id); Assert.Equal(old.UserName, reused.User.User.UserName); Assert.Empty(reused.User.Access); Assert.False(reused.User.User.IsInstallationAdministrator);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsJsonAsync(Route + $"/{old.Id}/reuse-login", new UserVersionRequest(again.ConcurrencyStamp, true))).StatusCode);
        Assert.False(await db.UserPasswordHistories.AnyAsync(x => x.UserId == reused.User.User.Id)); Assert.False(await db.UserSessions.AnyAsync(x => x.UserId == reused.User.User.Id));
        var oldAfter = await Detail(admin, old.Id); Assert.True(oldAfter.User.IsArchived && oldAfter.User.IsLoginReleased);
        Assert.Equal(old.UserName, oldAfter.User.HistoricalUserName); Assert.NotEqual(old.UserName, oldAfter.User.UserName);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync(Route + $"/{old.Id}/restore", new UserVersionRequest(oldAfter.ConcurrencyStamp, true))).StatusCode);
        Assert.Equal(reused.User.User.Id, (await Login(target, old.UserName!, newer)).UserId);
        var history = (await admin.GetFromJsonAsync<UserAdministrationHistory[]>(Route + $"/history?userId={old.Id}"))!;
        Assert.Contains(history, x => x.Operation == "ArchiveUser"); Assert.Contains(history, x => x.Operation == "RestoreSameUser");
        Assert.Contains(history, x => x.Operation == "ReleaseArchivedLogin" && x.RelatedUserId == reused.User.User.Id);
        Assert.Contains((await admin.GetFromJsonAsync<UserAdministrationHistory[]>(Route + $"/history?userId={reused.User.User.Id}"))!, x => x.Operation == "CreateFromArchivedLogin" && x.RelatedUserId == old.Id);
        Assert.All(history, x => Assert.Equal(old.Id, x.TargetId));
    }

    [PreservedVatSqlFact]
    public async Task FailedPasswordOrArchiveAuditRollsBackHashDateHistoryRolesAndSessions_AllRolledBack()
    {
        await using var tx = await PreservedSystemTransaction.Open(); var failure = new FailAudit(); using var app = App(tx, failure); using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SystemDbContext>(); var password = "Fixture!1a-" + Guid.NewGuid().ToString("N");
        var actor = await Account(scope.ServiceProvider, true, password); var target = await Account(scope.ServiceProvider, true, password);
        db.CompanyAccesses.Add(new(target.Id, A, CompanyPermission.ViewProfile)); await db.SaveChangesAsync();
        using var admin = Client(app); using var ordinary = Client(app); await Login(admin, actor.UserName!, password); await Login(ordinary, target.UserName!, password);
        var before = await Detail(admin, target.Id); var snapshot = await db.Users.AsNoTracking().SingleAsync(x => x.Id == target.Id);
        failure.Enabled = true;
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await admin.PostAsJsonAsync(Route + $"/{target.Id}/password-reset", new UserVersionRequest(before.ConcurrencyStamp, true, "Next!2b-" + Guid.NewGuid().ToString("N")))).StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await admin.PostAsJsonAsync(Route + $"/{target.Id}/archive", new UserVersionRequest(before.ConcurrencyStamp, true))).StatusCode);
        failure.Enabled = false;
        var after = await db.Users.AsNoTracking().SingleAsync(x => x.Id == target.Id);
        Assert.True(snapshot.PasswordHash == after.PasswordHash); Assert.Equal(snapshot.PasswordChangedAtUtc, after.PasswordChangedAtUtc); Assert.False(after.IsArchived);
        Assert.Empty(await db.UserPasswordHistories.Where(x => x.UserId == target.Id).ToArrayAsync());
        var detail = await Detail(admin, target.Id); Assert.True(detail.User.IsInstallationAdministrator); Assert.Single(detail.Access);
        Assert.Equal(HttpStatusCode.OK, (await ordinary.GetAsync("/api/v1/auth/me")).StatusCode);
        var archived = await Ok<UserAdministrationDetail>(await admin.PostAsJsonAsync(Route + $"/{target.Id}/archive", new UserVersionRequest(detail.ConcurrencyStamp, true)));
        failure.Enabled = true;
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await admin.PostAsJsonAsync(Route + $"/{target.Id}/reuse-login", new UserVersionRequest(archived.ConcurrencyStamp, true))).StatusCode);
        failure.Enabled = false; var unchanged = await Detail(admin, target.Id);
        Assert.True(unchanged.User.IsArchived); Assert.False(unchanged.User.IsLoginReleased); Assert.Equal(target.UserName, unchanged.User.UserName);
    }
    private sealed class LifecycleClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => Now;
    }
    [PreservedVatSqlFact]
    public async Task PasswordHistoryRetainsOnly20KnownHashes_AndVerifiesDifferentSaltedHashesThroughIdentity_AllRolledBack()
    {
        await using var tx = await PreservedSystemTransaction.Open(); using var app = App(tx); using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SystemDbContext>(); var manager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var policy = await db.PasswordPolicies.SingleAsync(); policy.Update(false, 12, true, true, true, true); await db.SaveChangesAsync();
        var user = await Account(scope.ServiceProvider, false, "p0");
        for (var i = 1; i <= 22; i++) Assert.True((await manager.ChangePasswordAsync(user, "p" + (i - 1), "p" + i)).Succeeded);
        Assert.Equal(20, await db.UserPasswordHistories.CountAsync(x => x.UserId == user.Id));
        policy.UpdateLifetime(true, 20, false, 90, DateTimeOffset.UtcNow); await db.SaveChangesAsync();
        var rejected = await manager.ResetPasswordAsync(user, await manager.GeneratePasswordResetTokenAsync(user), "p2");
        Assert.Contains(rejected.Errors, x => x.Code == "PasswordReused");
        Assert.True((await manager.ResetPasswordAsync(user, await manager.GeneratePasswordResetTokenAsync(user), "p1")).Succeeded);
        Assert.Equal(20, await db.UserPasswordHistories.CountAsync(x => x.UserId == user.Id));
    }
}
