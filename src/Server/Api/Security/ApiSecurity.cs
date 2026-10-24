using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Vatowiec.Core.Infrastructure.Identity;
using Vatowiec.Core.Infrastructure.Persistence;

namespace Vatowiec.Api.Security;

public static class ApiSecurity
{
    public const string SessionClaim = "vatowiec:session";
    public const string CsrfHeader = "X-CSRF-TOKEN";

    public static void AddApiSecurity(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddVatowiecIdentity();
        services.AddScoped<CompanyDatabaseResolver>();
        services.AddScoped<ICompanyDatabaseContextFactory, CompanyDatabaseContextFactory>();
        services.AddSingleton<DummyPassword>();
        services.AddScoped<UserAdministrationService>();
        services.AddScoped<Microsoft.AspNetCore.Authorization.IAuthorizationHandler, InstallationAdministratorHandler>();
        services.AddAuthentication(IdentityConstants.ApplicationScheme).AddCookie(IdentityConstants.ApplicationScheme, options =>
        {
            options.Cookie.Name = "__Host-Vatowiec.Session";
            options.Cookie.HttpOnly = true;
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            options.Cookie.SameSite = SameSiteMode.Strict;
            options.Cookie.Path = "/";
            options.SlidingExpiration = false;
            options.ExpireTimeSpan = TimeSpan.FromMinutes(Math.Clamp(configuration.GetValue("Authentication:SessionMinutes", 30), 1, 120));
            options.Events.OnRedirectToLogin = context => { context.Response.StatusCode = 401; return Task.CompletedTask; };
            options.Events.OnRedirectToAccessDenied = context => { context.Response.StatusCode = 403; return Task.CompletedTask; };
            options.Events.OnValidatePrincipal = ValidateSession;
        });
        services.AddOptions<CookieAuthenticationOptions>(IdentityConstants.ApplicationScheme)
            .Configure<TimeProvider>((options, clock) => options.TimeProvider = clock);
        services.AddAuthorization(options =>
        {
            options.FallbackPolicy = new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();
            options.AddPolicy(InstallationAdministration.Policy, policy => policy.RequireAuthenticatedUser().AddRequirements(new InstallationAdministratorRequirement()));
        });
        services.AddAntiforgery(options =>
        {
            options.HeaderName = CsrfHeader;
            options.Cookie.Name = "__Host-Vatowiec.Csrf";
            options.Cookie.HttpOnly = true;
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            options.Cookie.SameSite = SameSiteMode.Strict;
            options.Cookie.Path = "/";
        });
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = (int)HttpStatusCode.TooManyRequests;
            options.AddPolicy("registries", context => RateLimitPartition.GetFixedWindowLimiter(
                context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "anonymous", _ => new FixedWindowRateLimiterOptions
                { PermitLimit = 20, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
            options.AddPolicy("login", context => RateLimitPartition.GetFixedWindowLimiter(
                context.Connection.RemoteIpAddress?.ToString() ?? "local", _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 20,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0
                }));
        });
    }

    private static async Task ValidateSession(CookieValidatePrincipalContext context)
    {
        try
        {
            var services = context.HttpContext.RequestServices;
            var user = await services.GetRequiredService<SignInManager<ApplicationUser>>().ValidateSecurityStampAsync(context.Principal);
            if (user is null || user.IsArchived || user.IsBlocked || !Guid.TryParse(context.Principal?.FindFirstValue(SessionClaim), out var sessionId) ||
                await services.GetRequiredService<UserManager<ApplicationUser>>().IsLockedOutAsync(user))
            {
                context.RejectPrincipal();
                return;
            }
            var now = services.GetRequiredService<TimeProvider>().GetUtcNow();
            var valid = await services.GetRequiredService<SystemDbContext>().UserSessions.AsNoTracking().AnyAsync(
                x => x.Id == sessionId && x.UserId == user.Id && x.RevokedAtUtc == null && x.ExpiresAtUtc > now,
                context.HttpContext.RequestAborted);
            if (!valid) context.RejectPrincipal();
            else if (user.MustChangePassword || (await services.GetRequiredService<IPasswordPolicyProvider>().Read(context.HttpContext.RequestAborted))
                .IsExpired(user.PasswordChangedAtUtc, now)) context.HttpContext.Items["Vatowiec:MustChangePassword"] = true;
        }
        catch (Exception error) when (error is SqlException or InvalidOperationException)
        {
            context.RejectPrincipal(); // Brak katalogu lub weryfikacji nie daje sesji ani domyślnej firmy.
        }
    }
}

public sealed class DummyPassword
{
    private readonly ApplicationUser user = new();
    private readonly PasswordHasher<ApplicationUser> hasher = new();
    private readonly string hash;
    public DummyPassword() => hash = hasher.HashPassword(user, Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
    public void Verify(string password) => hasher.VerifyHashedPassword(user, hash, password);
}
