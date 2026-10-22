using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Vatowiec.Core.Domain;
using Vatowiec.Core.Infrastructure.Persistence;
using Vatowiec.SharedContracts;

namespace Vatowiec.Api.Security;

public static class ContractorEndpoints
{
    public static void MapContractors(this RouteGroupBuilder companies)
    {
        var group = companies.MapGroup("/{companyId:guid}/contractors")
            .AddEndpointFilter(CompanyEndpoints.ValidateCompanySource);
        group.MapGet("", List);
        group.MapGet("/{contractorId:int}", Get);
        group.MapPost("", Create);
        group.MapPut("/{contractorId:int}", Update);
        group.MapPost("/identifiers/validate", ValidateIdentifiers);
    }

    private static Task<AuthorizedCompanyDatabase> Resolve(Guid companyId, HttpContext context,
        CompanyDatabaseResolver resolver, IWebHostEnvironment environment, bool edit = false) =>
        resolver.Resolve(Guid.Parse(context.User.FindFirstValue(ClaimTypes.NameIdentifier)!), companyId,
            edit ? CompanyPermission.ViewContractors | CompanyPermission.EditContractors : CompanyPermission.ViewContractors,
            environment.IsDevelopment(), context.RequestAborted);

    private static async Task<IResult> List(Guid companyId, HttpContext context, CompanyDatabaseResolver resolver,
        IWebHostEnvironment environment, string? q, string? country, bool? active, int? parentId, int? excludeId, int offset = 0, int limit = 50)
    {
        await using var db = await Resolve(companyId, context, resolver, environment);
        if (offset < 0 || offset > 1000000 || limit is < 1 or > 100 || q?.Length > 128 ||
            (!string.IsNullOrWhiteSpace(country) && (country.Trim().Length != 2 || country.Trim().Any(c => !char.IsAsciiLetter(c)))))
            return Error(400, "invalid_filter", "Nieprawidłowe filtry lub strona wyników.");
        var rows = db.Context.Contractors.AsNoTracking();
        if (parentId.HasValue) rows = rows.Where(x => x.ParentId == parentId);
        if (excludeId.HasValue) rows = rows.Where(x => x.Id != excludeId);
        var search = q?.Trim();
        if (!string.IsNullOrEmpty(search)) rows = rows.Where(x => x.Code.Contains(search) || x.Name.Contains(search) ||
            (x.TaxId != null && x.TaxId.Contains(search)) || (x.VatEuId != null && x.VatEuId.Contains(search)));
        if (!string.IsNullOrWhiteSpace(country))
        {
            var code = country.Trim().ToUpperInvariant();
            rows = rows.Where(x => x.CountryCode == code);
        }
        if (active.HasValue) rows = rows.Where(x => x.IsActive == active.Value);
        var page = await rows.OrderBy(x => x.Code).ThenBy(x => x.Id).Skip(offset).Take(limit).ToListAsync(context.RequestAborted);
        return Results.Ok(page.Select(x => Response(x, companyId)));
    }

    private static async Task<IResult> Get(Guid companyId, int contractorId, HttpContext context,
        CompanyDatabaseResolver resolver, IWebHostEnvironment environment)
    {
        await using var db = await Resolve(companyId, context, resolver, environment);
        var row = await db.Context.Contractors.AsNoTracking().SingleOrDefaultAsync(x => x.Id == contractorId, context.RequestAborted);
        return row is null ? Error(404, "contractor_not_found", "Nie znaleziono kontrahenta w tej firmie.") : Results.Ok(Response(row, companyId));
    }

    private static Task<IResult> Create(Guid companyId, ContractorWriteRequest value, HttpContext context,
        CompanyDatabaseResolver resolver, IWebHostEnvironment environment, IAntiforgery csrf, SystemDbContext system, TimeProvider clock, IdentifierChecks identifiers) =>
        Write(companyId, null, value, context, resolver, environment, csrf, system, clock, identifiers);

    private static Task<IResult> Update(Guid companyId, int contractorId, ContractorWriteRequest value, HttpContext context,
        CompanyDatabaseResolver resolver, IWebHostEnvironment environment, IAntiforgery csrf, SystemDbContext system, TimeProvider clock, IdentifierChecks identifiers) =>
        Write(companyId, contractorId, value, context, resolver, environment, csrf, system, clock, identifiers);

    private static async Task<IResult> ValidateIdentifiers(Guid companyId, ContractorWriteRequest value, HttpContext context,
        CompanyDatabaseResolver resolver, IWebHostEnvironment environment, IAntiforgery csrf, IdentifierChecks identifiers)
    {
        await using var db = await Resolve(companyId, context, resolver, environment, edit: true);
        try { await csrf.ValidateRequestAsync(context); }
        catch (AntiforgeryValidationException) { return Error(400, "csrf_invalid", "Nie udało się potwierdzić żądania."); }
        var errors = ContractorValidation.Validate(value);
        if (errors.Count > 0) return Results.Json(new { code = "validation_failed", errors }, statusCode: 400);
        return Results.Ok(await identifiers.Check(db.Context, companyId, Guid.Parse(context.User.FindFirstValue(ClaimTypes.NameIdentifier)!), value, context.RequestAborted));
    }

    private static async Task<IResult> Write(Guid companyId, int? contractorId, ContractorWriteRequest value, HttpContext context,
        CompanyDatabaseResolver resolver, IWebHostEnvironment environment, IAntiforgery csrf, SystemDbContext system, TimeProvider clock, IdentifierChecks identifiers)
    {
        await using var db = await Resolve(companyId, context, resolver, environment, edit: true);
        try { await csrf.ValidateRequestAsync(context); }
        catch (AntiforgeryValidationException) { return Error(400, "csrf_invalid", "Nie udało się potwierdzić żądania. Spróbuj ponownie."); }
        var errors = ContractorValidation.Validate(value);
        byte[] version = [];
        if (contractorId.HasValue)
        {
            if (contractorId <= 0 || contractorId != value.Id) errors["Id"] = ["Identyfikator formularza nie pasuje do adresu."];
            if (value.OperationId.HasValue) errors["OperationId"] = ["Klucz tworzenia nie dotyczy edycji."];
            try { version = Convert.FromBase64String(value.RowVersion ?? ""); }
            catch (FormatException) { }
            if (version.Length != 8) errors["RowVersion"] = ["Brak prawidłowej wersji. Otwórz kontrahenta ponownie."];
        }
        else
        {
            if (value.Id != 0) errors["Id"] = ["Id nowej pozycji nadaje serwer."];
            if (value.OperationId is null || value.OperationId == Guid.Empty) errors["OperationId"] = ["Brak klucza operacji tworzenia."];
            if (value.RowVersion is not null) errors["RowVersion"] = ["Nowa pozycja nie może zawierać wersji istniejącego rekordu."];
        }
        if (errors.Count != 0) return Results.Json(new { code = "validation_failed", errors }, statusCode: 400);
        var issuer = await system.InstallationIdentities.AsNoTracking().Select(x => x.Id).SingleOrDefaultAsync(context.RequestAborted);
        var actor = Guid.Parse(context.User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var actorName = context.User.Identity?.Name;
        if (issuer == Guid.Empty || string.IsNullOrWhiteSpace(actorName))
            return Error(503, "audit_identity_unavailable", "Brak identyfikacji wykonawcy lub instalacji. Zapis jest zablokowany.");

        var initial = await identifiers.Check(db.Context, companyId, actor, value, context.RequestAborted);
        var normalized = new Contractor(value.Code, value.Name, value.CountryCode,
            initial.Checks[0].Normalized, initial.Checks[1].Normalized, initial.Checks[2].Normalized, value.ParentId);
        normalized.Update(value.Code, value.Name, value.CountryCode, value.IsActive);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Snapshot(normalized))));
        await using var transaction = await db.Context.Database.BeginTransactionAsync(context.RequestAborted);
        try
        {
            // Jedna blokada na bazę serializuje zmiany hierarchii i tworzenie M0.
            await CompanyWriteLock.Acquire(db.Context, "ContractorHierarchy", context.RequestAborted);
            if (!contractorId.HasValue)
            {
                var previous = await db.Context.ContractorCreateOperations.AsNoTracking().SingleOrDefaultAsync(x =>
                    x.ActorId == actor && x.ActorIssuerId == issuer && x.OperationKind == "Contractor" && x.OperationId == value.OperationId, context.RequestAborted);
                if (previous is not null)
                    return previous.RequestHash == hash ? Results.Content(previous.ResponseJson, "application/json", statusCode: 200)
                        : Error(409, "operation_payload_mismatch", "Ta operacja tworzenia została już wykonana z innymi danymi. Odśwież kartotekę.");
            }
            Contractor row;
            string? before = null;
            if (contractorId.HasValue)
            {
                var existing = await db.Context.Contractors.SingleOrDefaultAsync(x => x.Id == contractorId, context.RequestAborted);
                if (existing is null) return Error(404, "contractor_not_found", "Nie znaleziono kontrahenta w tej firmie.");
                row = existing;
                if (!row.RowVersion.SequenceEqual(version)) return Conflict(row, companyId);
                before = Snapshot(row);
                row.Update(value.Code, value.Name, value.CountryCode, value.IsActive);
                row.SetParent(value.ParentId);
                row.SetIdentifiers(normalized.TaxId, normalized.VatEuId, normalized.KsefIdWew);
            }
            else
            {
                row = normalized;
                db.Context.Contractors.Add(row);
            }
            // Pod tą samą blokadą co zapisy hierarchii i identyfikatorów: aktualne duplikaty/reguły.
            var preview = await identifiers.Check(db.Context, companyId, actor, value, context.RequestAborted);
            if (preview.Checks.Select(x => x.Normalized).SequenceEqual(initial.Checks.Select(x => x.Normalized)) == false)
                return Error(409, "identifier_rules_changed", "Reguły zmieniły się podczas zapisu. Sprawdź identyfikatory ponownie.");
            if (preview.NeedsWarning)
            {
                if (!value.AcceptWarning || value.WarningHash != preview.WarningHash)
                    return Results.Json(new { code = "identifiers_warning", message = preview.ToString(), warningHash = preview.WarningHash, preview }, statusCode: 422);
                if ((db.Permissions & CompanyPermission.AcceptWarnings) == 0)
                    return Error(403, "warning_permission_denied", "Brak uprawnienia do świadomego zapisu mimo ostrzeżenia.");
            }
            if (row.ParentId.HasValue && !await db.Context.Contractors.AnyAsync(x => x.Id == row.ParentId, context.RequestAborted))
                return Error(400, "invalid_parent", "Rekord nadrzędny nie istnieje w tej firmie.");
            var after = Snapshot(row);
            if (before == after) return Results.Ok(Response(row, companyId));
            await db.Context.SaveChangesAsync(context.RequestAborted); // Uzyskanie int IDENTITY i rowversion.
            db.Context.ContractorChanges.Add(new ContractorChange(row.Id, actor, issuer, actorName,
                clock.GetUtcNow(), context.TraceIdentifier, before, after,
                warningJson: preview.NeedsWarning ? JsonSerializer.Serialize(new { accepted = true, preview.WarningHash, preview.Checks, preview.Duplicates }) : null));
            if (!contractorId.HasValue)
                db.Context.ContractorCreateOperations.Add(new ContractorCreateOperation(value.OperationId!.Value, actor, issuer,
                    hash, row.Id, JsonSerializer.Serialize(Response(row, companyId), new JsonSerializerOptions(JsonSerializerDefaults.Web))));
            await db.Context.SaveChangesAsync(context.RequestAborted);
            await transaction.CommitAsync(context.RequestAborted);
            return contractorId.HasValue ? Results.Ok(Response(row, companyId))
                : Results.Created($"/api/v1/companies/{companyId}/contractors/{row.Id}", Response(row, companyId));
        }
        catch (DbUpdateConcurrencyException)
        {
            await transaction.RollbackAsync(context.RequestAborted);
            db.Context.ChangeTracker.Clear();
            var current = await db.Context.Contractors.AsNoTracking().SingleOrDefaultAsync(x => x.Id == value.Id, context.RequestAborted);
            return current is null ? Error(404, "contractor_not_found", "Kontrahent nie jest już dostępny.") : Conflict(current, companyId);
        }
        catch (DbUpdateException error) when (error.InnerException is SqlException { Number: 51001 })
        {
            return Error(400, "hierarchy_cycle", "Powiązanie utworzyłoby cykl w hierarchii kontrahentów.");
        }
        catch (DbUpdateException error) when (error.InnerException is SqlException { Number: 2601 or 2627 })
        {
            return Error(409, "contractor_code_exists", "Ten kod kontrahenta jest już zajęty w tej firmie. Wybierz inny kod i spróbuj ponownie.");
        }
        catch (Exception error) when (error is DbUpdateException or SqlException)
        {
            return Error(503, "contractor_save_failed", "Nie zapisano kontrahenta ani audytu. Sprawdź połączenie i ponów zapis.");
        }
    }

    private static IResult Conflict(Contractor row, Guid companyId) => Results.Json(new
    {
        code = "contractor_conflict",
        message = "Inna osoba zmieniła kontrahenta. Porównaj wersje przed ponownym zapisem.",
        current = Response(row, companyId)
    }, statusCode: 409);

    private static IResult Error(int status, string code, string message) => Results.Json(new { code, message }, statusCode: status);
    private static string Snapshot(Contractor row) => JsonSerializer.Serialize(new
    {
        row.Code,
        row.Name,
        row.TaxId,
        row.VatEuId,
        row.KsefIdWew,
        row.ParentId,
        row.CountryCode,
        row.IsActive
    });
    private static ContractorResponse Response(Contractor row, Guid companyId) => new(row.Id, companyId, row.Code, row.Name,
        row.CountryCode, row.TaxId, row.IsActive, Convert.ToBase64String(row.RowVersion), row.ParentId, row.VatEuId, row.KsefIdWew);
}
