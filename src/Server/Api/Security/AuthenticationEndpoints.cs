using System.Security.Claims;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Vatowiec.Core.Infrastructure.Identity;
using Vatowiec.Core.Infrastructure.Persistence;

namespace Vatowiec.Api.Security;

public sealed record LoginRequest(string UserName, string Password);

public static class AuthenticationEndpoints
{
    public static void MapAuthentication(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/auth");
        group.MapGet("/csrf", (HttpContext context, IAntiforgery csrf) =>
        {
            context.Response.Headers.CacheControl = "no-cache, no-store";
            return Results.Ok(new { token = csrf.GetAndStoreTokens(context).RequestToken, headerName = ApiSecurity.CsrfHeader });
        }).AllowAnonymous();

        group.MapPost("/login", async (LoginRequest request, HttpContext context, IAntiforgery csrf,
            UserManager<ApplicationUser> users, SignInManager<ApplicationUser> signIn,
            SystemDbContext system, TimeProvider clock, IOptionsMonitor<CookieAuthenticationOptions> options, DummyPassword dummy,
            ILoggerFactory loggers) =>
        {
            if (!await ValidCsrf(context, csrf)) return Results.BadRequest(new { code = "csrf_invalid" });
            if (string.IsNullOrWhiteSpace(request.UserName) || request.UserName.Length > 256 ||
                string.IsNullOrEmpty(request.Password) || request.Password.Length > 1024)
                return Results.Json(new { code = "invalid_credentials" }, statusCode: 401);
            var user = await users.FindByNameAsync(request.UserName);
            var logger = loggers.CreateLogger("Vatowiec.Authentication");
            if (user is null)
            {
                dummy.Verify(request.Password);
                logger.LogWarning("Login rejected; correlation {CorrelationId}", context.TraceIdentifier);
                return Results.Json(new { code = "invalid_credentials" }, statusCode: 401);
            }
            var result = await signIn.CheckPasswordSignInAsync(user, request.Password, lockoutOnFailure: true);
            if (!result.Succeeded || user.IsBlocked || await users.GetTwoFactorEnabledAsync(user))
            {
                logger.LogWarning("Login rejected for user {UserId}; correlation {CorrelationId}", user.Id, context.TraceIdentifier);
                return Results.Json(new { code = "invalid_credentials" }, statusCode: 401);
            }
            var now = clock.GetUtcNow();
            var session = new UserSession(user.Id, now, options.Get(IdentityConstants.ApplicationScheme).ExpireTimeSpan);
            system.UserSessions.Add(session);
            await system.SaveChangesAsync(context.RequestAborted);
            var principal = await signIn.CreateUserPrincipalAsync(user);
            ((ClaimsIdentity)principal.Identity!).AddClaim(new Claim(ApiSecurity.SessionClaim, session.Id.ToString()));
            await context.SignInAsync(IdentityConstants.ApplicationScheme, principal, new AuthenticationProperties
            {
                IsPersistent = false,
                AllowRefresh = false,
                IssuedUtc = now,
                ExpiresUtc = session.ExpiresAtUtc
            });
            logger.LogInformation("Login succeeded: user {UserId}, session {SessionId}; correlation {CorrelationId}", user.Id, session.Id, context.TraceIdentifier);
            return Results.Ok(new
            {
                userName = user.UserName,
                expiresAtUtc = session.ExpiresAtUtc,
                isInstallationAdministrator = await InstallationAdministration.IsAdministrator(system, user.Id, context.RequestAborted),
                mustChangePassword = user.MustChangePassword
            });
        }).AllowAnonymous().RequireRateLimiting("login");

        group.MapPost("/logout", async (HttpContext context, IAntiforgery csrf, SystemDbContext system, TimeProvider clock, ILoggerFactory loggers) =>
        {
            if (!await ValidCsrf(context, csrf)) return Results.BadRequest(new { code = "csrf_invalid" });
            var sessionId = Guid.Parse(context.User.FindFirstValue(ApiSecurity.SessionClaim)!);
            var userId = Guid.Parse(context.User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            // Warunek jest także ochroną przed unieważnieniem cudzej sesji.
            await system.UserSessions.Where(x => x.Id == sessionId && x.UserId == userId && x.RevokedAtUtc == null)
                .ExecuteUpdateAsync(update => update.SetProperty(x => x.RevokedAtUtc, clock.GetUtcNow()), context.RequestAborted);
            await context.SignOutAsync(IdentityConstants.ApplicationScheme);
            loggers.CreateLogger("Vatowiec.Authentication").LogInformation("Logout: user {UserId}, session {SessionId}; correlation {CorrelationId}",
                userId, sessionId, context.TraceIdentifier);
            return Results.NoContent();
        }).RequireAuthorization();

        group.MapGet("/me", async (HttpContext context, UserManager<ApplicationUser> users, SystemDbContext system) =>
        {
            var user = (await users.GetUserAsync(context.User))!;
            var sessionId = Guid.Parse(context.User.FindFirstValue(ApiSecurity.SessionClaim)!);
            var expiry = await system.UserSessions.Where(x => x.Id == sessionId).Select(x => x.ExpiresAtUtc).SingleAsync();
            return Results.Ok(new
            {
                userName = user.UserName,
                roles = await users.GetRolesAsync(user),
                expiresAtUtc = expiry,
                isInstallationAdministrator = await InstallationAdministration.IsAdministrator(system, user.Id, context.RequestAborted),
                mustChangePassword = user.MustChangePassword
            });
        }).RequireAuthorization();
    }

    private static async Task<bool> ValidCsrf(HttpContext context, IAntiforgery csrf)
    {
        try { await csrf.ValidateRequestAsync(context); return true; }
        catch (AntiforgeryValidationException) { return false; }
    }
}
