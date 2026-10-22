using System.Security.Claims;
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
        group.MapGet("/{contractorId:guid}", Get);
        group.MapPost("", Create);
        group.MapPut("/{contractorId:guid}", Update);
    }

    private static Task<AuthorizedCompanyDatabase> Resolve(Guid companyId, HttpContext context,
        CompanyDatabaseResolver resolver, IWebHostEnvironment environment, bool edit = false) =>
        resolver.Resolve(Guid.Parse(context.User.FindFirstValue(ClaimTypes.NameIdentifier)!), companyId,
            edit ? CompanyPermission.ViewContractors | CompanyPermission.EditContractors : CompanyPermission.ViewContractors,
            environment.IsDevelopment(), context.RequestAborted);

    private static async Task<IResult> List(Guid companyId, HttpContext context, CompanyDatabaseResolver resolver,
        IWebHostEnvironment environment, string? q, string? country, bool? active, int offset = 0, int limit = 50)
    {
        await using var db = await Resolve(companyId, context, resolver, environment);
        if (offset < 0 || offset > 1000000 || limit is < 1 or > 100 || q?.Length > 128 ||
            (!string.IsNullOrWhiteSpace(country) && (country.Trim().Length != 2 || country.Trim().Any(c => !char.IsAsciiLetter(c)))))
            return Error(400, "invalid_filter", "Nieprawidłowe filtry lub strona wyników.");
        var rows = db.Context.Contractors.AsNoTracking();
        var search = q?.Trim();
        if (!string.IsNullOrEmpty(search)) rows = rows.Where(x => x.Code.Contains(search) || x.Name.Contains(search) ||
            (x.TaxIdentifier != null && x.TaxIdentifier.Contains(search)));
        if (!string.IsNullOrWhiteSpace(country))
        {
            var code = country.Trim().ToUpperInvariant();
            rows = rows.Where(x => x.CountryCode == code);
        }
        if (active.HasValue) rows = rows.Where(x => x.IsActive == active.Value);
        var page = await rows.OrderBy(x => x.Code).ThenBy(x => x.Id).Skip(offset).Take(limit).ToListAsync(context.RequestAborted);
        return Results.Ok(page.Select(Response));
    }

    private static async Task<IResult> Get(Guid companyId, Guid contractorId, HttpContext context,
        CompanyDatabaseResolver resolver, IWebHostEnvironment environment)
    {
        await using var db = await Resolve(companyId, context, resolver, environment);
        var row = await db.Context.Contractors.AsNoTracking().SingleOrDefaultAsync(x => x.Id == contractorId, context.RequestAborted);
        return row is null ? Error(404, "contractor_not_found", "Nie znaleziono kontrahenta w tej firmie.") : Results.Ok(Response(row));
    }

    private static Task<IResult> Create(Guid companyId, ContractorWriteRequest value, HttpContext context,
        CompanyDatabaseResolver resolver, IWebHostEnvironment environment, IAntiforgery csrf, SystemDbContext system, TimeProvider clock) =>
        Write(companyId, null, value, context, resolver, environment, csrf, system, clock);

    private static Task<IResult> Update(Guid companyId, Guid contractorId, ContractorWriteRequest value, HttpContext context,
        CompanyDatabaseResolver resolver, IWebHostEnvironment environment, IAntiforgery csrf, SystemDbContext system, TimeProvider clock) =>
        Write(companyId, contractorId, value, context, resolver, environment, csrf, system, clock);

    private static async Task<IResult> Write(Guid companyId, Guid? contractorId, ContractorWriteRequest value, HttpContext context,
        CompanyDatabaseResolver resolver, IWebHostEnvironment environment, IAntiforgery csrf, SystemDbContext system, TimeProvider clock)
    {
        await using var db = await Resolve(companyId, context, resolver, environment, edit: true);
        try { await csrf.ValidateRequestAsync(context); }
        catch (AntiforgeryValidationException) { return Error(400, "csrf_invalid", "Nie udało się potwierdzić żądania. Spróbuj ponownie."); }
        var errors = ContractorValidation.Validate(value);
        if (contractorId.HasValue && contractorId != value.Id) errors["Id"] = ["Identyfikator formularza nie pasuje do adresu."];
        byte[] version = [];
        if (contractorId.HasValue)
        {
            try { version = Convert.FromBase64String(value.RowVersion ?? ""); }
            catch (FormatException) { /* Błąd walidacji poniżej. */ }
            if (version.Length != 8) errors["RowVersion"] = ["Brak prawidłowej wersji. Otwórz kontrahenta ponownie."];
        }
        else if (value.RowVersion is not null) errors["RowVersion"] = ["Nowa pozycja nie może zawierać wersji istniejącego rekordu."];
        if (errors.Count != 0) return Results.Json(new { code = "validation_failed", errors }, statusCode: 400);

        var issuer = await system.InstallationIdentities.AsNoTracking().Select(x => x.Id).SingleOrDefaultAsync(context.RequestAborted);
        if (issuer == Guid.Empty) return Error(503, "audit_identity_unavailable", "Brak identyfikacji instalacji. Zapis jest zablokowany.");
        var actor = Guid.Parse(context.User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var actorName = context.User.Identity?.Name;
        if (string.IsNullOrWhiteSpace(actorName)) return Error(503, "audit_identity_unavailable", "Nie można zidentyfikować wykonawcy zmiany.");
        var row = await db.Context.Contractors.SingleOrDefaultAsync(x => x.Id == value.Id, context.RequestAborted);
        string? before = null;
        if (contractorId.HasValue)
        {
            if (row is null) return Error(404, "contractor_not_found", "Nie znaleziono kontrahenta w tej firmie.");
            if (!row.RowVersion.SequenceEqual(version)) return Conflict(row);
            before = Snapshot(row);
        }
        else if (row is not null)
        {
            // Ten sam Id i ten sam stan: bezpieczne ponowienie po utracie odpowiedzi, bez kolejnego audytu.
            var expected = new Contractor(companyId, value.Id, value.Code, value.Name, value.CountryCode, value.TaxIdentifier);
            expected.Update(value.Code, value.Name, value.TaxIdentifier, value.CountryCode, value.IsActive);
            return Snapshot(row) == Snapshot(expected) ? Results.Ok(Response(row))
                : Error(409, "contractor_id_exists", "Pozycja o tym identyfikatorze już istnieje. Odśwież kartotekę.");
        }
        else
        {
            row = new Contractor(companyId, value.Id, value.Code, value.Name, value.CountryCode, value.TaxIdentifier);
            db.Context.Contractors.Add(row);
        }
        row.Update(value.Code, value.Name, value.TaxIdentifier, value.CountryCode, value.IsActive);
        var after = Snapshot(row);
        if (before == after) return Results.Ok(Response(row)); // Nie audytujemy zapisu bez zmiany wartości.
        db.Context.ContractorChanges.Add(new ContractorChange(companyId, row.Id, actor, issuer, actorName,
            clock.GetUtcNow(), context.TraceIdentifier, before, after));
        try
        {
            // Jedno SaveChanges = jedna transakcja w tej samej bazie, również dla błędu audytu lub rowversion.
            await db.Context.SaveChangesAsync(context.RequestAborted);
            return contractorId.HasValue ? Results.Ok(Response(row)) : Results.Created($"/api/v1/companies/{companyId}/contractors/{row.Id}", Response(row));
        }
        catch (DbUpdateConcurrencyException)
        {
            db.Context.ChangeTracker.Clear();
            var current = await db.Context.Contractors.AsNoTracking().SingleOrDefaultAsync(x => x.Id == value.Id, context.RequestAborted);
            return current is null ? Error(404, "contractor_not_found", "Kontrahent nie jest już dostępny.") : Conflict(current);
        }
        catch (DbUpdateException error) when (error.InnerException is SqlException { Number: 2601 or 2627 })
        {
            return Error(409, "contractor_code_exists", "Ten kod kontrahenta jest już zajęty w tej firmie. Wybierz inny kod i spróbuj ponownie.");
        }
        catch (DbUpdateException)
        {
            return Error(503, "contractor_save_failed", "Nie zapisano kontrahenta ani audytu. Sprawdź połączenie i ponów zapis.");
        }
    }

    private static IResult Conflict(Contractor row) => Results.Json(new
    {
        code = "contractor_conflict",
        message = "Inna osoba zmieniła kontrahenta. Porównaj wersje przed ponownym zapisem.",
        current = Response(row)
    }, statusCode: 409);

    private static IResult Error(int status, string code, string message) => Results.Json(new { code, message }, statusCode: status);
    private static string Snapshot(Contractor row) => JsonSerializer.Serialize(new
    {
        row.Code,
        row.Name,
        row.TaxIdentifier,
        row.CountryCode,
        row.IsActive
    });
    private static ContractorResponse Response(Contractor row) => new(row.Id, row.CompanyId, row.Code, row.Name,
        row.CountryCode, row.TaxIdentifier, row.IsActive, Convert.ToBase64String(row.RowVersion));
}
