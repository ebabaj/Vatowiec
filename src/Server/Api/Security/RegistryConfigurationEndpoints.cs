using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.EntityFrameworkCore;
using Vatowiec.Core.Domain;
using Vatowiec.Core.Infrastructure.Persistence;
using Vatowiec.Core.Infrastructure.Registries;
using Vatowiec.SharedContracts;

namespace Vatowiec.Api.Security;

public static class RegistryConfigurationEndpoints
{
    public static void MapRegistryConfiguration(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/installation/integrations/registries").RequireAuthorization(InstallationAdministration.Policy);
        group.MapGet("", async (SystemDbContext system, CancellationToken ct) =>
            Results.Ok((await system.RegistryConfigurations.AsNoTracking().OrderBy(x => x.Source).ToArrayAsync(ct)).Select(Response)));
        group.MapPut("/{source}", Save);
    }
    public static RegistryConfigurationResponse Response(RegistryConfiguration row) => new(row.Source, row.Environment,
        row.TestUrl, row.ProductionUrl, row.TestWl2Url, row.ProductionWl2Url, Convert.ToBase64String(row.RowVersion));
    private static string Snapshot(RegistryConfiguration row) => JsonSerializer.Serialize(new
    { row.Source, row.Environment, row.TestUrl, row.ProductionUrl, row.TestWl2Url, row.ProductionWl2Url });
    private static IResult Error(int status, string code, string message) => Results.Json(new { code, message }, statusCode: status);
    private static async Task<IResult> Save(string source, RegistryConfigurationRequest value, HttpContext http,
        SystemDbContext system, IAntiforgery csrf, TimeProvider clock)
    {
        try { await csrf.ValidateRequestAsync(http); }
        catch (AntiforgeryValidationException) { return Error(400, "csrf_invalid", "Nie udało się potwierdzić żądania."); }
        if (source is not ("GUS" or "MF" or "VIES")) return Error(400, "registry_invalid", "Wybierz GUS, MF lub VIES.");
        var errors = new Dictionary<string, string[]>();
        if (value.Environment is not ("Test" or "Production")) errors["Environment"] = ["Wybierz TEST lub PRODUKCJA."];
        foreach (var (field, url, mode, wl2) in new[] { ("TestUrl", value.TestUrl, "Test", false), ("ProductionUrl", value.ProductionUrl, "Production", false),
                     ("TestWl2Url", value.TestWl2Url, "Test", true), ("ProductionWl2Url", value.ProductionWl2Url, "Production", true) })
        {
            if (wl2 && source != "MF")
            { if (url != null) errors[field] = ["Adres WL2 dotyczy wyłącznie MF."]; continue; }
            if (!RegistrySettings.IsAllowedUrl(source, mode, url?.Trim(), wl2))
                errors[field] = [$"Dozwolony jest wyłącznie oficjalny adres HTTPS: {RegistrySettings.OfficialUrl(source, mode, wl2)} (bez parametrów, fragmentów i danych logowania)."];
        }
        byte[] version = [];
        try { version = Convert.FromBase64String(value.RowVersion ?? ""); } catch (FormatException) { }
        if (version.Length != 8) errors["RowVersion"] = ["Odśwież ustawienia — brak prawidłowej wersji."];
        if (errors.Count != 0) return Results.Json(new { code = "validation_failed", errors }, statusCode: 400);
        if (value.Environment == "Production" && !value.ConfirmProduction)
            return Error(400, "production_confirmation_required", "Potwierdź świadomy zapis ustawień PRODUKCJA. Zapytania będą wysyłane do rzeczywistego rejestru.");
        await using var transaction = await system.Database.BeginTransactionAsync(http.RequestAborted);
        await system.Database.ExecuteSqlInterpolatedAsync($"DECLARE @r int; EXEC @r=sys.sp_getapplock @Resource={"RegistryConfiguration:" + source}, @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=10000; IF @r<0 THROW 51000, 'Configuration lock unavailable', 1;", http.RequestAborted);
        var row = await system.RegistryConfigurations.SingleOrDefaultAsync(x => x.Source == source, http.RequestAborted);
        if (row == null) return Error(503, "registry_configuration_missing", "Brak ustawień usługi. Sprawdź migracje bazy systemowej.");
        if (!row.RowVersion.SequenceEqual(version)) return Error(409, "configuration_stale", "Inny administrator zmienił ustawienia. Odśwież stan i ponownie sprawdź wartości.");
        var before = Snapshot(row);
        row.Update(value.Environment, RegistrySettings.OfficialUrl(source, "Test"), RegistrySettings.OfficialUrl(source, "Production"),
            source == "MF" ? RegistrySettings.OfficialUrl(source, "Test", true) : null,
            source == "MF" ? RegistrySettings.OfficialUrl(source, "Production", true) : null);
        var after = Snapshot(row);
        if (before == after) return Results.Ok(Response(row));
        var issuer = await system.InstallationIdentities.AsNoTracking().Select(x => x.Id).SingleOrDefaultAsync(http.RequestAborted);
        if (issuer == Guid.Empty || !Guid.TryParse(http.User.FindFirstValue(ClaimTypes.NameIdentifier), out var actor) || string.IsNullOrWhiteSpace(http.User.Identity?.Name))
            return Error(503, "audit_identity_unavailable", "Brak trwałej identyfikacji wykonawcy. Nie zapisano ustawień.");
        system.RegistryConfigurationChanges.Add(new(source, actor, issuer, http.User.Identity.Name!, clock.GetUtcNow(), before, after));
        try { await system.SaveChangesAsync(http.RequestAborted); await transaction.CommitAsync(http.RequestAborted); }
        catch (DbUpdateConcurrencyException) { return Error(409, "configuration_stale", "Ustawienia zmieniły się. Odśwież stan."); }
        catch (DbUpdateException) { return Error(503, "configuration_audit_failed", "Nie zapisano ustawień i ich audytu. Spróbuj ponownie."); }
        return Results.Ok(Response(row));
    }
}
