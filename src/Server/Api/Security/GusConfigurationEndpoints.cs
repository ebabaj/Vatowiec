using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.EntityFrameworkCore;
using Vatowiec.Core.Domain;
using Vatowiec.Core.Infrastructure.Persistence;
using Vatowiec.Core.Infrastructure.Registries;
using Vatowiec.SharedContracts;

namespace Vatowiec.Api.Security;

public static class GusConfigurationEndpoints
{
    public static void MapGusConfiguration(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/installation/integrations/gus")
            .RequireAuthorization(InstallationAdministration.Policy);
        group.MapGet("", async (SystemDbContext system, RegistrySettings settings, CancellationToken ct) =>
        {
            string mode;
            try { mode = settings.Mode("GUS"); }
            catch (InvalidOperationException) { return Error(503, "registry_configuration_invalid", "Brak prawidłowych ustawień GUS w bazie instalacji. Sprawdź migracje i Ustawienia instalacji → Integracje zewnętrzne."); }
            return Results.Ok(Status(await system.GusConfigurations.AsNoTracking().SingleOrDefaultAsync(ct), mode));
        });
        group.MapPut("", (SaveGusKeyRequest value, HttpContext http, SystemDbContext system, RegistrySettings settings,
            IAntiforgery csrf, IGusKeyProtector protector, ILoggerFactory logs) =>
            Write(value.ApiKey, value.RowVersion, false, true, http, system, settings, csrf, protector, logs));
        group.MapDelete("", ([Microsoft.AspNetCore.Mvc.FromBody] DeleteGusKeyRequest value, HttpContext http,
            SystemDbContext system, RegistrySettings settings, IAntiforgery csrf, IGusKeyProtector protector, ILoggerFactory logs) =>
            Write(null, value.RowVersion, true, value.Confirm, http, system, settings, csrf, protector, logs));
    }
    private static GusConfigurationResponse Status(GusConfiguration? row, string mode) =>
        new(row?.ProtectedApiKey != null, row == null ? null : Convert.ToBase64String(row.RowVersion), mode);
    private static IResult Error(int status, string code, string message) => Results.Json(new { code, message }, statusCode: status);
    private static async Task<IResult> Write(string? key, string? version, bool delete, bool confirm, HttpContext http,
        SystemDbContext system, RegistrySettings settings, IAntiforgery csrf, IGusKeyProtector protector, ILoggerFactory logs)
    {
        try { await csrf.ValidateRequestAsync(http); }
        catch (AntiforgeryValidationException) { return Error(400, "csrf_invalid", "Nie udało się potwierdzić żądania."); }
        string mode;
        try { mode = settings.Mode("GUS"); }
        catch (InvalidOperationException) { return Error(503, "registry_configuration_invalid", "Brak prawidłowych ustawień GUS w bazie instalacji. Sprawdź migracje i Ustawienia instalacji → Integracje zewnętrzne."); }
        if (delete && !confirm) return Error(400, "confirmation_required", "Potwierdź usunięcie wspólnego klucza GUS.");
        key = key?.Trim();
        if (!delete && (string.IsNullOrEmpty(key) || key.Length > 4096 || key.Any(c => c < 33 || c > 126) || key == RegistrySettings.GusTestApiKey))
            return Error(400, "gus_key_invalid", "Wpisz własny produkcyjny klucz GUS, bez spacji i znaków sterujących. Publiczny klucz TEST nie jest kluczem produkcyjnym.");
        await using var transaction = await system.Database.BeginTransactionAsync(http.RequestAborted);
        await system.Database.ExecuteSqlRawAsync("DECLARE @r int; EXEC @r = sys.sp_getapplock @Resource=N'GusConfiguration', @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=10000; IF @r < 0 THROW 51000, 'Configuration lock unavailable', 1;", http.RequestAborted);
        var row = await system.GusConfigurations.SingleOrDefaultAsync(http.RequestAborted);
        if (version != (row == null ? null : Convert.ToBase64String(row.RowVersion)))
            return Error(409, "configuration_stale", "Konfiguracja GUS zmieniła się. Odśwież stan przed ponownym zapisem.");
        var wasConfigured = row?.ProtectedApiKey != null;
        var issuer = await system.InstallationIdentities.AsNoTracking().Select(x => x.Id).SingleOrDefaultAsync(http.RequestAborted);
        if (issuer == Guid.Empty || !Guid.TryParse(http.User.FindFirstValue(ClaimTypes.NameIdentifier), out var actor) || string.IsNullOrWhiteSpace(http.User.Identity?.Name))
            return Error(503, "audit_identity_unavailable", "Brak trwałej identyfikacji wykonawcy. Nie zapisano klucza.");
        string? encrypted = null;
        if (!delete)
        {
            try { encrypted = protector.Protect(issuer, key!); }
            catch (Exception error) when (error is CryptographicException or InvalidOperationException or IOException or UnauthorizedAccessException)
            { return Error(503, "key_protection_unavailable", "Nie można trwale zabezpieczyć klucza GUS. Sprawdź ochronę kryptograficzną serwera."); }
        }
        if (row == null) { row = new GusConfiguration(); system.GusConfigurations.Add(row); }
        row.SetKey(encrypted);
        system.RegistryConfigurationChanges.Add(new("GUS", actor, issuer, http.User.Identity.Name!, DateTimeOffset.UtcNow,
            JsonSerializer.Serialize(new { Category = "GusKey", IsConfigured = wasConfigured }),
            JsonSerializer.Serialize(new { Category = "GusKey", IsConfigured = !delete, Action = delete ? "Cleared" : "Replaced" })));
        if (system.Entry(row).State != EntityState.Added) system.Entry(row).Property(x => x.ProtectedApiKey).IsModified = true;
        try { await system.SaveChangesAsync(http.RequestAborted); }
        catch (DbUpdateException) { return Error(503, "configuration_audit_failed", "Nie zapisano klucza i audytu. Spróbuj ponownie."); }
        await transaction.CommitAsync(http.RequestAborted);
        logs.CreateLogger("Vatowiec.InstallationAdministration").LogInformation("GUS key configuration {Action}; actor {ActorId}; correlation {CorrelationId}",
            delete ? "cleared" : "replaced", http.User.FindFirstValue(ClaimTypes.NameIdentifier), http.TraceIdentifier);
        return Results.Ok(Status(row, mode));
    }
}
