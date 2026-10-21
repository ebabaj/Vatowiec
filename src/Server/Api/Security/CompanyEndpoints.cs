using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Vatowiec.Core.Domain;
using Vatowiec.Core.Infrastructure.Persistence;

namespace Vatowiec.Api.Security;

public static class CompanyEndpoints
{
    public static void MapCompanies(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/companies").RequireAuthorization();
        group.MapGet("", async (HttpContext context, SystemDbContext system) =>
        {
            var userId = Guid.Parse(context.User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var rows = await (from access in system.CompanyAccesses.AsNoTracking()
                              join company in system.Companies.AsNoTracking() on access.CompanyId equals company.Id
                              where access.UserId == userId && company.IsActive
                              orderby company.Name
                              select new { company.Id, company.Name, access.Permissions }).ToListAsync(context.RequestAborted);
            return Results.Ok(rows.Select(x => new { companyId = x.Id, name = x.Name, permissions = Permissions(x.Permissions) }));
        });

        group.MapGet("/{companyId:guid}/profile", async (Guid companyId, HttpContext request,
            CompanyDatabaseResolver resolver, IWebHostEnvironment environment) =>
        {
            await using var database = await resolver.Resolve(UserId(request), companyId,
                CompanyPermission.ViewProfile, environment.IsDevelopment(), request.RequestAborted);
            var profile = await database.Context.CompanyProfiles.AsNoTracking().SingleAsync(request.RequestAborted);
            return Results.Ok(new { profile.CompanyId, profile.Name, profile.Nip, profile.Regon });
        }).AddEndpointFilter(ValidateCompanySource);

        group.MapGet("/{companyId:guid}/contractors", async (Guid companyId, HttpContext request,
            CompanyDatabaseResolver resolver, IWebHostEnvironment environment) =>
        {
            await using var database = await resolver.Resolve(UserId(request), companyId,
                CompanyPermission.ViewContractors, environment.IsDevelopment(), request.RequestAborted);
            return Results.Ok(await database.Context.Contractors.AsNoTracking().OrderBy(x => x.Code)
                .Select(x => new { x.Id, x.CompanyId, x.Code, x.Name, x.CountryCode }).ToListAsync(request.RequestAborted));
        }).AddEndpointFilter(ValidateCompanySource);
    }

    private static Guid UserId(HttpContext context) => Guid.Parse(context.User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    private static string[] Permissions(CompanyPermission permissions) => Enum.GetValues<CompanyPermission>()
        .Where(x => x != CompanyPermission.None && (permissions & x) == x).Select(x => x.ToString()).ToArray();

    private static ValueTask<object?> ValidateCompanySource(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        if (context.HttpContext.Request.Query.ContainsKey("companyId") ||
            context.HttpContext.Request.Headers.ContainsKey("X-Company-Id") || context.GetArgument<Guid>(0) == Guid.Empty)
            return ValueTask.FromResult<object?>(Results.BadRequest(new { code = "company_context_invalid" }));
        return next(context);
    }
}
