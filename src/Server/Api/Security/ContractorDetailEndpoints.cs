using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Vatowiec.Core.Application;
using Vatowiec.Core.Domain;
using Vatowiec.Core.Infrastructure.Persistence;
using Vatowiec.SharedContracts;
using static Vatowiec.Api.Security.ContractorDetailMapping;

namespace Vatowiec.Api.Security;

public static class ContractorDetailEndpoints
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public static void MapContractorDetails(this RouteGroupBuilder companies)
    {
        var types = companies.MapGroup("/{companyId:guid}/address-types").AddEndpointFilter(CompanyEndpoints.ValidateCompanySource);
        types.MapGet("", async (Guid companyId, HttpContext http, CompanyDatabaseResolver resolver, IWebHostEnvironment env) =>
        {
            await using var db = await Resolve(companyId, http, resolver, env, false);
            return Results.Ok(await db.Context.AddressTypes.AsNoTracking().OrderBy(x => x.Name)
                .Select(x => new AddressTypeResponse(x.Id, x.Name)).ToArrayAsync(http.RequestAborted));
        });
        types.MapPost("", AddType);
        foreach (var kind in new[] { "addresses", "contacts", "bank-accounts" })
        {
            var group = companies.MapGroup("/{companyId:guid}/contractors/{contractorId:int}/" + kind)
                .AddEndpointFilter(CompanyEndpoints.ValidateCompanySource);
            group.MapGet("", (Guid companyId, int contractorId, HttpContext http, CompanyDatabaseResolver resolver,
                IWebHostEnvironment env, int offset = 0, int limit = 50) => List(companyId, contractorId, kind, offset, limit, http, resolver, env));
            group.MapGet("/{detailId:int}", (Guid companyId, int contractorId, int detailId, HttpContext http,
                CompanyDatabaseResolver resolver, IWebHostEnvironment env) => Get(companyId, contractorId, detailId, kind, http, resolver, env));
            group.MapPost("", (Guid companyId, int contractorId, DetailWriteRequest value, HttpContext http,
                CompanyDatabaseResolver resolver, IWebHostEnvironment env, IAntiforgery csrf, SystemDbContext system, TimeProvider clock) =>
                Write(companyId, contractorId, null, kind, value, http, resolver, env, csrf, system, clock));
            group.MapPut("/{detailId:int}", (Guid companyId, int contractorId, int detailId, DetailWriteRequest value, HttpContext http,
                CompanyDatabaseResolver resolver, IWebHostEnvironment env, IAntiforgery csrf, SystemDbContext system, TimeProvider clock) =>
                Write(companyId, contractorId, detailId, kind, value, http, resolver, env, csrf, system, clock));
        }
    }
    private static Task<AuthorizedCompanyDatabase> Resolve(Guid id, HttpContext http, CompanyDatabaseResolver resolver,
        IWebHostEnvironment env, bool edit) => resolver.Resolve(Actor(http), id,
        CompanyPermission.ViewContractors | (edit ? CompanyPermission.EditContractors : CompanyPermission.None), env.IsDevelopment(), http.RequestAborted);
    private static Guid Actor(HttpContext http) => Guid.Parse(http.User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    private static IResult Error(int status, string code, string message) => Results.Json(new { code, message }, statusCode: status);
    private static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    private static async Task<bool> Csrf(IAntiforgery csrf, HttpContext http)
    { try { await csrf.ValidateRequestAsync(http); return true; } catch (AntiforgeryValidationException) { return false; } }
    private static async Task<List<object>> Rows(CompanyDbContext db, int contractorId, string kind, int offset, int limit)
    {
        return kind switch
        {
            "addresses" => (await db.ContractorAddresses.Where(x => x.ContractorId == contractorId).OrderBy(x => x.Id).Skip(offset).Take(limit).ToListAsync()).Cast<object>().ToList(),
            "contacts" => (await db.ContractorContacts.Where(x => x.ContractorId == contractorId).OrderBy(x => x.Id).Skip(offset).Take(limit).ToListAsync()).Cast<object>().ToList(),
            _ => (await db.ContractorBankAccounts.Where(x => x.ContractorId == contractorId).OrderBy(x => x.Id).Skip(offset).Take(limit).ToListAsync()).Cast<object>().ToList()
        };
    }
    private static async Task<object?> Find(CompanyDbContext db, int owner, int id, string kind) => kind switch
    {
        "addresses" => await db.ContractorAddresses.SingleOrDefaultAsync(x => x.ContractorId == owner && x.Id == id),
        "contacts" => await db.ContractorContacts.SingleOrDefaultAsync(x => x.ContractorId == owner && x.Id == id),
        _ => await db.ContractorBankAccounts.SingleOrDefaultAsync(x => x.ContractorId == owner && x.Id == id)
    };
    private static async Task<IResult> List(Guid companyId, int contractorId, string kind, int offset, int limit,
        HttpContext http, CompanyDatabaseResolver resolver, IWebHostEnvironment env)
    {
        await using var access = await Resolve(companyId, http, resolver, env, false);
        if (offset < 0 || limit is < 1 or > 100) return Error(400, "invalid_filter", "Nieprawidłowa strona.");
        if (!await access.Context.Contractors.AnyAsync(x => x.Id == contractorId)) return Error(404, "contractor_not_found", "Nie znaleziono kontrahenta.");
        return Results.Ok((await Rows(access.Context, contractorId, kind, offset, limit)).Select(x => Response(x, kind, companyId, contractorId)));
    }
    private static async Task<IResult> Get(Guid companyId, int contractorId, int detailId, string kind,
        HttpContext http, CompanyDatabaseResolver resolver, IWebHostEnvironment env)
    {
        await using var access = await Resolve(companyId, http, resolver, env, false);
        var row = await Find(access.Context, contractorId, detailId, kind);
        return row is null ? Error(404, "detail_not_found", "Nie znaleziono pozycji u tego kontrahenta.") : Results.Ok(Response(row, kind, companyId, contractorId));
    }
    private static async Task<IResult> Write(Guid companyId, int contractorId, int? detailId, string kind, DetailWriteRequest value,
        HttpContext http, CompanyDatabaseResolver resolver, IWebHostEnvironment env, IAntiforgery csrf, SystemDbContext system, TimeProvider clock)
    {
        await using var access = await Resolve(companyId, http, resolver, env, true);
        if (!await Csrf(csrf, http)) return Error(400, "csrf_invalid", "Nie potwierdzono żądania.");
        if (value.Data is null) return Error(400, "validation_failed", "Brak danych formularza.");
        var data = value.Data;
        if (kind == "bank-accounts" && data.AccountNumber is not null)
            data = data with { AccountNumber = BankAccountPreparation.Normalize(data.AccountNumber), CurrencyCode = string.IsNullOrWhiteSpace(data.CurrencyCode) ? null : data.CurrencyCode.Trim().ToUpperInvariant() };
        var errors = DetailValidation.Validate(kind, data);
        byte[] version = [];
        if (detailId.HasValue)
        {
            if (value.Id != detailId || detailId <= 0) errors["Id"] = ["Niezgodny identyfikator pozycji."];
            try { version = Convert.FromBase64String(value.RowVersion ?? ""); } catch (FormatException) { }
            if (version.Length != 8) errors["RowVersion"] = ["Otwórz pozycję ponownie, aby pobrać jej wersję."];
            if (value.OperationId.HasValue) errors["OperationId"] = ["Klucz tworzenia nie dotyczy edycji."];
        }
        else if (value.Id != 0 || value.OperationId is null || value.OperationId == Guid.Empty || value.RowVersion is not null)
            errors["OperationId"] = ["Nowa pozycja wymaga Id=0, klucza tworzenia i braku RowVersion."];
        if (errors.Count != 0) return Results.Json(new { code = "validation_failed", errors }, statusCode: 400);
        var db = access.Context;
        var issuer = await system.InstallationIdentities.Select(x => x.Id).SingleOrDefaultAsync();
        if (issuer == Guid.Empty || string.IsNullOrWhiteSpace(http.User.Identity?.Name)) return Error(503, "audit_identity_unavailable", "Zapis zablokowany: brak identyfikacji wykonawcy.");
        var normalized = New(kind, contractorId, data);
        Apply(normalized, data);
        var fingerprint = Hash(JsonSerializer.Serialize(new { kind, contractorId, Data = Values(normalized) }));
        var warningHash = Hash(BankAccountPreparation.Rule + fingerprint + value.Id + value.RowVersion);
        var warning = kind == "bank-accounts" && BankAccountPreparation.NeedsWarning(data.AccountNumber!);
        await using var transaction = await db.Database.BeginTransactionAsync();
        try
        {
            await CompanyWriteLock.Acquire(db, "ContractorDetails");
            if (!await db.Contractors.AnyAsync(x => x.Id == contractorId)) return Error(404, "contractor_not_found", "Nie znaleziono kontrahenta.");
            if (!detailId.HasValue)
            {
                var previous = await db.ContractorCreateOperations.SingleOrDefaultAsync(x => x.ActorId == Actor(http) && x.ActorIssuerId == issuer && x.OperationKind == Type(kind) && x.OperationId == value.OperationId);
                if (previous is not null) return previous.RequestHash == fingerprint ? Results.Content(previous.ResponseJson, "application/json", statusCode: 200)
                    : Error(409, "operation_payload_mismatch", "Ta operacja została już zapisana z innymi danymi. Odśwież listę.");
            }
            var entity = detailId.HasValue ? await Find(db, contractorId, detailId.Value, kind) : normalized;
            if (entity is null) return Error(404, "detail_not_found", "Nie znaleziono pozycji.");
            if (detailId.HasValue && !Version(entity).SequenceEqual(version)) return Conflict(entity, kind, companyId, contractorId);
            if (warning)
            {
                if (!value.AcceptWarning || value.WarningHash != warningHash)
                    return Results.Json(new { code = "bank_warning", message = BankAccountPreparation.Warning, warningHash }, statusCode: 422);
                if ((access.Permissions & CompanyPermission.AcceptWarnings) == 0) return Error(403, "warning_permission_required", "Brak uprawnienia do świadomej akceptacji ostrzeżeń.");
            }
            if (kind == "addresses" && !await db.AddressTypes.AnyAsync(x => x.Id == data.AddressTypeId)) return Error(400, "invalid_address_type", "Wybierz istniejący rodzaj adresu.");
            var before = detailId.HasValue ? Snapshot(entity) : null;
            Apply(entity, data);
            var changes = new List<(object Entity, string? Before)>();
            if (data.IsDefault && kind != "contacts")
            {
                // Wyłącz poprzedni znacznik przed INSERT/UPDATE nowego; jedna transakcja i audyt obu rekordów.
                var siblings = kind == "addresses"
                    ? (await db.ContractorAddresses.Where(x => x.ContractorId == contractorId && x.AddressTypeId == data.AddressTypeId && x.IsDefault && x.Id != value.Id).ToListAsync()).Cast<object>().ToList()
                    : (await db.ContractorBankAccounts.Where(x => x.ContractorId == contractorId && x.CurrencyCode == data.CurrencyCode && x.IsDefault && x.Id != value.Id).ToListAsync()).Cast<object>().ToList();
                foreach (var sibling in siblings)
                {
                    changes.Add((sibling, Snapshot(sibling)));
                    sibling.GetType().GetProperty("IsDefault")!.SetValue(sibling, false);
                }
                // Nie zapisuj jeszcze zmiany selected, aby kolejność UPDATE nie naruszyła unikalnego indeksu.
                if (detailId.HasValue) db.Entry(entity).State = EntityState.Unchanged;
                await db.SaveChangesAsync();
                if (detailId.HasValue) { Apply(entity, data); db.Entry(entity).State = EntityState.Modified; }
            }
            if (!detailId.HasValue) db.Add(entity);
            if (before == Snapshot(entity) && changes.Count == 0 && !warning) return Results.Ok(Response(entity, kind, companyId, contractorId));
            await db.SaveChangesAsync();
            changes.Add((entity, before));
            foreach (var change in changes)
                db.ContractorChanges.Add(new ContractorChange(contractorId, Actor(http), issuer, http.User.Identity!.Name!, clock.GetUtcNow(), http.TraceIdentifier,
                    change.Before, Snapshot(change.Entity), Type(kind), Id(change.Entity), warning && ReferenceEquals(change.Entity, entity)
                        ? JsonSerializer.Serialize(new { Rule = BankAccountPreparation.Rule, Message = BankAccountPreparation.Warning, Accepted = true, warningHash }) : null));
            var response = Response(entity, kind, companyId, contractorId);
            if (!detailId.HasValue) db.ContractorCreateOperations.Add(new ContractorCreateOperation(value.OperationId!.Value, Actor(http), issuer, fingerprint, contractorId, JsonSerializer.Serialize(response, Json), Type(kind)));
            await db.SaveChangesAsync();
            await transaction.CommitAsync();
            return detailId.HasValue ? Results.Ok(response) : Results.Created($"/api/v1/companies/{companyId}/contractors/{contractorId}/{kind}/{response.Id}", response);
        }
        catch (DbUpdateConcurrencyException)
        {
            await transaction.RollbackAsync();
            db.ChangeTracker.Clear();
            var row = await Find(db, contractorId, value.Id, kind);
            return row is null ? Error(404, "detail_not_found", "Pozycja nie jest dostępna.") : Conflict(row, kind, companyId, contractorId);
        }
        catch (DbUpdateException error) when (error.InnerException is SqlException { Number: 2601 or 2627 })
        { return Error(409, "detail_default_conflict", "Ktoś zmienił pozycję preferowaną. Odśwież dane i ponów świadomie."); }
        catch (DbUpdateException) { return Error(503, "detail_save_failed", "Nie zapisano pozycji ani audytu. Ponów operację."); }
    }
    private static IResult Conflict(object entity, string kind, Guid company, int contractor) => Results.Json(new
    { code = "detail_conflict", message = "Inna osoba zmieniła pozycję. Porównaj dane przed ponownym zapisem.", detailCurrent = Response(entity, kind, company, contractor) }, statusCode: 409);

    private static async Task<IResult> AddType(Guid companyId, AddressTypeWriteRequest value, HttpContext http,
        CompanyDatabaseResolver resolver, IWebHostEnvironment env, IAntiforgery csrf, SystemDbContext system, TimeProvider clock)
    {
        await using var access = await Resolve(companyId, http, resolver, env, true);
        if (!await Csrf(csrf, http)) return Error(400, "csrf_invalid", "Nie potwierdzono żądania.");
        if (value.OperationId == Guid.Empty || string.IsNullOrWhiteSpace(value.Name) || value.Name.Trim().Length > 100)
            return Error(400, "invalid_address_type", "Nazwa rodzaju jest wymagana, najwyżej 100 znaków.");
        var issuer = await system.InstallationIdentities.Select(x => x.Id).SingleOrDefaultAsync();
        if (issuer == Guid.Empty) return Error(503, "audit_identity_unavailable", "Brak identyfikacji instalacji.");
        var db = access.Context;
        await using var transaction = await db.Database.BeginTransactionAsync();
        try
        {
            await CompanyWriteLock.Acquire(db, "ContractorDetails");
            var hash = Hash(value.Name.Trim());
            var previous = await db.ContractorCreateOperations.SingleOrDefaultAsync(x => x.ActorId == Actor(http) && x.ActorIssuerId == issuer && x.OperationKind == "AddressType" && x.OperationId == value.OperationId);
            if (previous is not null) return previous.RequestHash == hash ? Results.Content(previous.ResponseJson, "application/json", statusCode: 200) : Error(409, "operation_payload_mismatch", "Ta operacja ma inny zapisany wynik.");
            if (await db.AddressTypes.AnyAsync(x => x.Name == value.Name.Trim())) return Error(409, "address_type_exists", "Taki rodzaj adresu już istnieje. Wybierz go z listy.");
            var row = new AddressType(value.Name);
            db.AddressTypes.Add(row);
            await db.SaveChangesAsync();
            var result = new AddressTypeResponse(row.Id, row.Name);
            db.ContractorChanges.Add(new ContractorChange(null, Actor(http), issuer, http.User.Identity!.Name!, clock.GetUtcNow(), http.TraceIdentifier,
                null, JsonSerializer.Serialize(result), "AddressType", row.Id));
            db.ContractorCreateOperations.Add(new ContractorCreateOperation(value.OperationId, Actor(http), issuer, hash, null, JsonSerializer.Serialize(result, Json), "AddressType"));
            await db.SaveChangesAsync();
            await transaction.CommitAsync();
            return Results.Created($"/api/v1/companies/{companyId}/address-types/{row.Id}", result);
        }
        catch (DbUpdateException) { return Error(503, "address_type_save_failed", "Nie zapisano rodzaju ani audytu. Ponów operację."); }
    }
}
