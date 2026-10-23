using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Vatowiec.Core.Infrastructure.Persistence;

namespace Vatowiec.Api.Security;

public static class InstallationAdministration
{
    public const string Role = Vatowiec.Core.Infrastructure.Identity.InstallationRoles.Administrator;
    public const string Policy = "InstallationAdministration";
    public static Task<bool> IsAdministrator(SystemDbContext system, Guid user, CancellationToken ct = default) =>
        (from membership in system.UserRoles.AsNoTracking()
         join role in system.Roles.AsNoTracking() on membership.RoleId equals role.Id
         where membership.UserId == user && role.NormalizedName == "INSTALLATIONADMINISTRATOR"
         select membership.UserId).AnyAsync(ct);
}

public sealed class InstallationAdministratorRequirement : IAuthorizationRequirement;
public sealed class InstallationAdministratorHandler(SystemDbContext system) : AuthorizationHandler<InstallationAdministratorRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, InstallationAdministratorRequirement requirement)
    {
        if (context.User.Identity?.IsAuthenticated == true && Guid.TryParse(context.User.FindFirstValue(ClaimTypes.NameIdentifier), out var user)
            && await InstallationAdministration.IsAdministrator(system, user)) context.Succeed(requirement);
    }
}
