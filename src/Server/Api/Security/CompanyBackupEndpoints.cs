using System.Security.Claims;
using Microsoft.AspNetCore.Antiforgery;
using Vatowiec.Core.Infrastructure.Operations;
using Vatowiec.SharedContracts;

namespace Vatowiec.Api.Security;

// ADR-044 krok 2: zlecenie kopii, harmonogram i przypomnienia. Kopii nie można pobrać przez API.
public static class CompanyBackupEndpoints
{
    public static void MapCompanyBackups(this RouteGroupBuilder companies)
    {
        var group = companies.MapGroup("/{companyId:guid}/backups").AddEndpointFilter(CompanyEndpoints.ValidateCompanySource);
        group.MapGet("", (Guid companyId, HttpContext http, CompanyBackupService backups) =>
            Handle(() => backups.Status(UserId(http), companyId, http.RequestAborted)));
        group.MapPost("", async (Guid companyId, CompanyBackupRequest value, HttpContext http, IAntiforgery csrf, CompanyBackupService backups) =>
        {
            if (!await ValidCsrf(csrf, http)) return Csrf();
            try
            {
                var (operation, created) = await backups.Request(Requester(http), companyId, value.OperationKey, http.RequestAborted);
                var response = CompanyBackupService.Response(operation);
                return created ? Results.Json(response, statusCode: 202) : Results.Ok(response);
            }
            catch (CompanyOperationException error) { return Error(error); }
        });
        group.MapPut("/schedule", async (Guid companyId, CompanyBackupScheduleRequest value, HttpContext http, IAntiforgery csrf, CompanyBackupService backups) =>
            await ValidCsrf(csrf, http) ? await Handle(() => backups.SaveSchedule(Requester(http), companyId, value, http.RequestAborted)) : Csrf());
        group.MapPost("/reminder/snooze", async (Guid companyId, HttpContext http, IAntiforgery csrf, CompanyBackupService backups) =>
            await ValidCsrf(csrf, http) ? await Handle(() => backups.Snooze(UserId(http), companyId, http.RequestAborted)) : Csrf());
    }

    public static void MapBackupPolicy(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/installation/backup-policy").RequireAuthorization(InstallationAdministration.Policy);
        group.MapGet("", (CompanyBackupService backups, CancellationToken ct) => Handle(() => backups.ReadPolicy(ct)));
        group.MapPut("", async (BackupPolicyRequest value, HttpContext http, IAntiforgery csrf, CompanyBackupService backups) =>
            await ValidCsrf(csrf, http) ? await Handle(() => backups.SavePolicy(Requester(http), value, http.RequestAborted)) : Csrf());
    }

    private static async Task<IResult> Handle<T>(Func<Task<T>> action)
    {
        try { return Results.Ok(await action()); }
        catch (CompanyOperationException error) { return Error(error); }
    }

    private static async Task<bool> ValidCsrf(IAntiforgery csrf, HttpContext http)
    {
        try { await csrf.ValidateRequestAsync(http); return true; }
        catch (AntiforgeryValidationException) { return false; }
    }

    private static Guid UserId(HttpContext http) => Guid.Parse(http.User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    private static OperationRequester Requester(HttpContext http) => new(UserId(http), http.User.Identity?.Name ?? "");
    private static IResult Csrf() => Results.Json(new { code = "csrf_invalid", message = "Nie udało się potwierdzić żądania." }, statusCode: 400);
    private static IResult Error(CompanyOperationException error) =>
        Results.Json(new { code = error.Code, message = error.Message }, statusCode: error.Status);
}
