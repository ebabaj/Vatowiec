using System.Security.Claims;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.EntityFrameworkCore;
using Vatowiec.Core.Infrastructure.Identity;
using Vatowiec.Core.Infrastructure.Persistence;
using Vatowiec.SharedContracts;
using System.Text.Json;

namespace Vatowiec.Api.Security;

public static class UserAdministrationEndpoints
{
    public static void MapUserAdministration(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/installation/users").RequireAuthorization(InstallationAdministration.Policy);
        group.MapGet("/lookups", async (SystemDbContext db, CancellationToken ct) => Results.Ok(UserAdministrationService.Lookups(
            await db.Companies.AsNoTracking().OrderBy(x => x.Name).Select(x => new AdministrationCompany(x.Id, x.Name)).ToArrayAsync(ct))));
        group.MapGet("", async (string? search, int? offset, bool? includeArchived, SystemDbContext db, CancellationToken ct) =>
        {
            if (search?.Length > 256 || offset is < 0 or > 1000000) return Results.BadRequest();
            var text = search?.Trim();
            var rows = await db.Users.AsNoTracking().Where(x => (includeArchived == true || !x.IsArchived) &&
                (text == null || text == "" || x.UserName!.Contains(text) || (x.IsArchived && x.HistoricalUserName != null && x.HistoricalUserName.Contains(text))))
                .OrderBy(x => x.UserName).ThenBy(x => x.Id).Skip(offset ?? 0).Take(51)
                .Select(x => new UserSummary(x.Id, x.UserName!, x.IsBlocked, x.MustChangePassword, false,
                    x.IsArchived, x.HistoricalUserName, x.ArchivedAtUtc, x.ArchivedByName, x.IsLoginReleased))
                .ToArrayAsync(ct);
            var result = new List<UserSummary>();
            foreach (var row in rows) result.Add(row with { IsInstallationAdministrator = await UserAdministrationService.IsAdministrator(db, row.Id, ct) });
            return Results.Ok(result);
        });
        group.MapGet("/{id:guid}", (Guid id, UserAdministrationService service, CancellationToken ct) => Execute(async () => await service.Detail(id, ct)));
        group.MapGet("/history", async (Guid? userId, int? offset, SystemDbContext db, CancellationToken ct) =>
        {
            if (offset is < 0 or > 1000000) return Results.BadRequest();
            var rows = await db.UserAdministrationChanges.AsNoTracking().Where(x => userId == null || x.TargetId == userId)
                .OrderByDescending(x => x.Id).Skip(offset ?? 0).Take(51).ToArrayAsync(ct);
            return Results.Ok(rows.Select(x => new UserAdministrationHistory(x.Id, x.ActorId, x.ActorName, x.TargetId, x.TargetName,
                x.CompanyId, x.OccurredAtUtc, x.Operation, x.PermissionsBefore == null ? null : UserAdministrationService.Codes(x.PermissionsBefore.Value),
                x.PermissionsAfter == null ? null : UserAdministrationService.Codes(x.PermissionsAfter.Value), x.BlockedBefore, x.BlockedAfter, x.RelatedUserId)));
        });
        group.MapPost("", (UserCreateRequest value, HttpContext http, IAntiforgery csrf, UserAdministrationService service, CancellationToken ct) =>
            Write(http, csrf, () => service.Create(Actor(http), Session(http), value, ct)));
        group.MapPost("/{id:guid}/password-reset", (Guid id, UserVersionRequest value, HttpContext http, IAntiforgery csrf, UserAdministrationService service, CancellationToken ct) =>
            Write(http, csrf, () => service.ResetPassword(Actor(http), Session(http), id, value, ct)));
        group.MapPost("/{id:guid}/archive", (Guid id, UserVersionRequest value, HttpContext http, IAntiforgery csrf, UserAdministrationService service, CancellationToken ct) =>
            Write(http, csrf, () => service.Archive(Actor(http), Session(http), id, value, ct)));
        group.MapPost("/{id:guid}/restore", (Guid id, UserVersionRequest value, HttpContext http, IAntiforgery csrf, UserAdministrationService service, CancellationToken ct) =>
            Write(http, csrf, () => service.Restore(Actor(http), Session(http), id, value, false, ct)));
        group.MapPost("/{id:guid}/reuse-login", (Guid id, UserVersionRequest value, HttpContext http, IAntiforgery csrf, UserAdministrationService service, CancellationToken ct) =>
            Write(http, csrf, () => service.Restore(Actor(http), Session(http), id, value, true, ct)));
        group.MapPut("/{id:guid}/state", (Guid id, UserBlockRequest value, HttpContext http, IAntiforgery csrf, UserAdministrationService service, CancellationToken ct) =>
            Write(http, csrf, () => service.Block(Actor(http), Session(http), id, value, ct)));
        group.MapPut("/{id:guid}/access", (Guid id, UserAccessRequest value, HttpContext http, IAntiforgery csrf, UserAdministrationService service, CancellationToken ct) =>
            Write(http, csrf, () => service.SaveAccess(Actor(http), Session(http), id, value, ct)));
        group.MapPut("/{id:guid}/administrator-role", (Guid id, AdministratorRoleRequest value, HttpContext http, IAntiforgery csrf, UserAdministrationService service, CancellationToken ct) =>
            Write(http, csrf, () => service.ChangeAdministratorRole(Actor(http), Session(http), id, value, ct)));
        var policy = app.MapGroup("/api/v1/installation/security/password-policy").RequireAuthorization(InstallationAdministration.Policy);
        policy.MapGet("", (UserAdministrationService service, CancellationToken ct) => Execute(() => service.ReadPasswordPolicy(ct), passwordPolicy: true));
        policy.MapPut("", (PasswordPolicyRequest value, HttpContext http, IAntiforgery csrf, UserAdministrationService service, CancellationToken ct) =>
            Write(http, csrf, () => service.SavePasswordPolicy(Actor(http), Session(http), value, ct), passwordPolicy: true));
        policy.MapGet("/history", async (int? offset, SystemDbContext db, CancellationToken ct) =>
        {
            if (offset is < 0 or > 1000000) return Results.BadRequest();
            return await Execute(async () =>
            {
                var rows = await db.PasswordPolicyChanges.AsNoTracking().OrderByDescending(x => x.Id).Skip(offset ?? 0).Take(51).ToArrayAsync(ct);
                return rows.Select(x => new PasswordPolicyHistory(x.Id, x.ActorName, x.OccurredAtUtc,
                    JsonSerializer.Deserialize<PasswordPolicyResponse>(x.BeforeJson)!, JsonSerializer.Deserialize<PasswordPolicyResponse>(x.AfterJson)!)).ToArray();
            }, passwordPolicy: true);
        });
        app.MapPost("/api/v1/auth/password", (OwnPasswordRequest value, HttpContext http, IAntiforgery csrf, UserAdministrationService service, CancellationToken ct) =>
            Write(http, csrf, async () => { await service.ChangeOwnPassword(Actor(http), Session(http), value, ct); return new { changed = true }; })).RequireAuthorization().RequireRateLimiting("login");
    }
    private static Guid Actor(HttpContext http) => Guid.Parse(http.User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    private static Guid Session(HttpContext http) => Guid.Parse(http.User.FindFirstValue(ApiSecurity.SessionClaim)!);
    private static async Task<IResult> Write<T>(HttpContext http, IAntiforgery csrf, Func<Task<T>> action, bool passwordPolicy = false)
    {
        try { await csrf.ValidateRequestAsync(http); }
        catch (AntiforgeryValidationException) { return Results.Json(new { code = "csrf_invalid" }, statusCode: 400); }
        return await Execute(action, passwordPolicy);
    }
    private static async Task<IResult> Execute<T>(Func<Task<T>> action, bool passwordPolicy = false)
    {
        try { return Results.Ok(await action()); }
        catch (UserAdministrationException e) { return Results.Json(new { code = e.Code, message = e.Message }, statusCode: e.Status); }
        catch (DbUpdateConcurrencyException) { return Results.Json(new { code = passwordPolicy ? "password_policy_stale" : "user_stale", message = passwordPolicy ? "Polityka haseł zmieniła się. Odśwież ustawienia i porównaj szkic." : "Konto lub prawa zmieniły się. Odśwież szczegóły i porównaj szkic." }, statusCode: 409); }
        catch (DbUpdateException) { return Results.Json(new { code = passwordPolicy ? "password_policy_save_failed" : "user_save_failed", message = passwordPolicy ? "Nie zapisano polityki haseł ani audytu w bazie systemowej instalacji. Odśwież stan i spróbuj ponownie." : "Nie zapisano konta, praw ani audytu. Odśwież stan i spróbuj ponownie." }, statusCode: 503); }
    }
}
