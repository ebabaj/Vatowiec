using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.EntityFrameworkCore;
using Vatowiec.Core.Domain;
using Vatowiec.Core.Infrastructure.Registries;
using Vatowiec.Core.Infrastructure.Persistence;
using Vatowiec.SharedContracts;

namespace Vatowiec.Api.Security;

public static class DataPortEndpoints
{
    public static void MapDataPort(this RouteGroupBuilder companies)
    {
        var group = companies.MapGroup("/{companyId:guid}/integrations/dataport").AddEndpointFilter(CompanyEndpoints.ValidateCompanySource);
        group.MapGet("", async (Guid companyId, HttpContext http, CompanyDatabaseResolver resolver, IWebHostEnvironment env) =>
        {
            await using var access = await resolver.Resolve(Actor(http), companyId, CompanyPermission.ViewProfile, env.IsDevelopment(), http.RequestAborted);
            return Results.Ok(Status(companyId, await access.Context.DataPortConfigurations.AsNoTracking().SingleOrDefaultAsync(http.RequestAborted)));
        });
        group.MapPut("", Save);
        group.MapDelete("", Delete);
    }
    private static Guid Actor(HttpContext http) => Guid.Parse(http.User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    private static IResult Error(int status, string code, string message) => Results.Json(new { code, message }, statusCode: status);
    private static DataPortConfigurationResponse Status(Guid company, DataPortConfiguration? row) =>
        new(company, row?.ProtectedApiKey != null, row == null ? null : Convert.ToBase64String(row.RowVersion));

    private static Task<IResult> Save(Guid companyId, SaveDataPortKeyRequest value, HttpContext http,
        CompanyDatabaseResolver resolver, IWebHostEnvironment env, IAntiforgery csrf, IDataPortKeyProtector protector) =>
        Write(companyId, value.ApiKey, value.RowVersion, false, http, resolver, env, csrf, protector);
    private static Task<IResult> Delete(Guid companyId, [Microsoft.AspNetCore.Mvc.FromBody] DeleteDataPortKeyRequest value, HttpContext http,
        CompanyDatabaseResolver resolver, IWebHostEnvironment env, IAntiforgery csrf, IDataPortKeyProtector protector) =>
        Write(companyId, null, value.RowVersion, true, http, resolver, env, csrf, protector, value.Confirm);

    private static async Task<IResult> Write(Guid companyId, string? key, string? expectedVersion, bool delete,
        HttpContext http, CompanyDatabaseResolver resolver, IWebHostEnvironment env, IAntiforgery csrf, IDataPortKeyProtector protector, bool confirmed = true)
    {
        await using var access = await resolver.Resolve(Actor(http), companyId,
            CompanyPermission.ViewProfile | CompanyPermission.ManageCompanyIntegrations, env.IsDevelopment(), http.RequestAborted);
        try { await csrf.ValidateRequestAsync(http); }
        catch (AntiforgeryValidationException) { return Error(400, "csrf_invalid", "Nie udało się potwierdzić żądania."); }
        if (delete && !confirmed) return Error(400, "confirmation_required", "Potwierdź usunięcie klucza.");
        key = key?.Trim();
        if (!delete && (string.IsNullOrEmpty(key) || key.Length > 4096 || key.Any(c => c < 33 || c > 126)))
            return Error(400, "dataport_key_invalid", "Wpisz klucz API bez spacji i znaków sterujących (maksymalnie 4096 znaków).");
        var db = access.Context;
        await using var transaction = await db.Database.BeginTransactionAsync(http.RequestAborted);
        // Serialize the absent singleton as well as replacements, without a distributed transaction.
        await db.Database.ExecuteSqlRawAsync("DECLARE @r int; EXEC @r = sys.sp_getapplock @Resource=N'DataPortConfiguration', @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=10000; IF @r < 0 THROW 51000, 'Configuration lock unavailable', 1;", http.RequestAborted);
        var row = await db.DataPortConfigurations.SingleOrDefaultAsync(http.RequestAborted);
        if (expectedVersion != (row == null ? null : Convert.ToBase64String(row.RowVersion)))
            return Error(409, "configuration_stale", "Konfiguracja zmieniła się. Odśwież stan przed ponownym zapisem.");
        string? encrypted = null;
        if (!delete)
        {
            try { encrypted = protector.Protect(companyId, key!); }
            catch (Exception e) when (e is CryptographicException or InvalidOperationException or IOException or UnauthorizedAccessException)
            { return Error(503, "key_protection_unavailable", "Nie można trwale zabezpieczyć klucza. Sprawdź konfigurację kryptograficzną serwera."); }
        }
        if (row == null) { row = new DataPortConfiguration(); db.DataPortConfigurations.Add(row); }
        row.SetKey(encrypted);
        // Force a new rowversion even for repeated clears; stale settings never resurrect a removed key.
        if (db.Entry(row).State != EntityState.Added) db.Entry(row).Property(x => x.ProtectedApiKey).IsModified = true;
        await db.SaveChangesAsync(http.RequestAborted);
        await transaction.CommitAsync(http.RequestAborted);
        return Results.Ok(Status(companyId, row));
    }
}
