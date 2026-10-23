using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Vatowiec.Core.Domain;
using Vatowiec.Core.Infrastructure.Persistence;
using Vatowiec.SharedContracts;

namespace Vatowiec.Api.Security;

public static class ContractorFinanceEndpoints
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public static void MapContractorFinance(this RouteGroupBuilder companies)
    {
        var root = companies.MapGroup("/{companyId:guid}").AddEndpointFilter(CompanyEndpoints.ValidateCompanySource);
        root.MapGet("/financial-lookups", async (Guid companyId, HttpContext http, CompanyDatabaseResolver resolver, IWebHostEnvironment env) =>
        {
            await using var access = await Resolve(companyId, http, resolver, env, false);
            var currencies = await access.Context.Currencies.AsNoTracking().OrderBy(x => x.Code).Select(x => new FinanceCurrency(x.Code, x.Name, x.IsActive)).ToArrayAsync(http.RequestAborted);
            var methods = await access.Context.PaymentMethods.AsNoTracking().OrderBy(x => x.Name).ToArrayAsync(http.RequestAborted);
            return Results.Ok(new FinanceLookups(currencies, methods.Select(MethodResponse).ToArray()));
        });
        root.MapGet("/contractors/{contractorId:int}/trade-terms/{direction}", async (Guid companyId, int contractorId, string direction, HttpContext http, CompanyDatabaseResolver resolver, IWebHostEnvironment env) =>
        {
            await using var access = await Resolve(companyId, http, resolver, env, false);
            if (!TradeDirection.IsValid(direction)) return Error(400, "finance_invalid", "Wybierz Sprzedaż lub Zakup.");
            if (!await access.Context.Contractors.AnyAsync(x => x.Id == contractorId, http.RequestAborted)) return Missing();
            return Results.Ok(TermsResponse(await access.Context.ContractorTradeTerms.AsNoTracking().SingleOrDefaultAsync(x => x.ContractorId == contractorId && x.Direction == direction, http.RequestAborted), companyId, contractorId, direction));
        });
        root.MapGet("/contractors/{contractorId:int}/credit-limits", async (Guid companyId, int contractorId, string direction, HttpContext http, CompanyDatabaseResolver resolver, IWebHostEnvironment env, int offset = 0, int limit = 50) =>
        {
            await using var access = await Resolve(companyId, http, resolver, env, false);
            if (!TradeDirection.IsValid(direction) || offset < 0 || offset > 1000000 || limit is < 1 or > 100) return Error(400, "finance_invalid", "Nieprawidłowy kierunek lub strona limitów.");
            if (!await access.Context.Contractors.AnyAsync(x => x.Id == contractorId, http.RequestAborted)) return Missing();
            var rows = await access.Context.ContractorCreditLimits.AsNoTracking().Where(x => x.ContractorId == contractorId && x.Direction == direction).OrderBy(x => x.CurrencyCode).ThenByDescending(x => x.IsActive).ThenBy(x => x.Id).Skip(offset).Take(limit).ToArrayAsync(http.RequestAborted);
            return Results.Ok(rows.Select(x => LimitResponse(x, companyId)));
        });
        root.MapPut("/contractors/{contractorId:int}/trade-terms/{direction}", (Guid companyId, int contractorId, string direction, TradeTermsRequest value, HttpContext http, CompanyDatabaseResolver resolver, IWebHostEnvironment env, IAntiforgery csrf, SystemDbContext system, TimeProvider clock) =>
            Save(companyId, contractorId, "ContractorTradeTerms", direction, 0, null, value, null, null, http, resolver, env, csrf, system, clock));
        root.MapPost("/contractors/{contractorId:int}/credit-limits", (Guid companyId, int contractorId, CreditLimitRequest value, HttpContext http, CompanyDatabaseResolver resolver, IWebHostEnvironment env, IAntiforgery csrf, SystemDbContext system, TimeProvider clock) =>
            Save(companyId, contractorId, "ContractorCreditLimit", value.Direction, 0, null, null, value, null, http, resolver, env, csrf, system, clock));
        root.MapPut("/contractors/{contractorId:int}/credit-limits/{limitId:int}", (Guid companyId, int contractorId, int limitId, CreditLimitRequest value, HttpContext http, CompanyDatabaseResolver resolver, IWebHostEnvironment env, IAntiforgery csrf, SystemDbContext system, TimeProvider clock) =>
            limitId <= 0 ? Task.FromResult(Error(400, "finance_invalid", "Nieprawidłowy identyfikator limitu.")) : Save(companyId, contractorId, "ContractorCreditLimit", value.Direction, limitId, null, null, value, null, http, resolver, env, csrf, system, clock));
        root.MapPost("/payment-methods", (Guid companyId, PaymentMethodRequest value, HttpContext http, CompanyDatabaseResolver resolver, IWebHostEnvironment env, IAntiforgery csrf, SystemDbContext system, TimeProvider clock) =>
            Save(companyId, null, "PaymentMethod", null, 0, null, null, null, value, http, resolver, env, csrf, system, clock));
        root.MapPut("/payment-methods/{code}", (Guid companyId, string code, PaymentMethodRequest value, HttpContext http, CompanyDatabaseResolver resolver, IWebHostEnvironment env, IAntiforgery csrf, SystemDbContext system, TimeProvider clock) =>
            Save(companyId, null, "PaymentMethod", null, 0, code, null, null, value, http, resolver, env, csrf, system, clock));
    }
    private static Guid Actor(HttpContext http) => Guid.Parse(http.User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    private static Task<AuthorizedCompanyDatabase> Resolve(Guid id, HttpContext http, CompanyDatabaseResolver resolver, IWebHostEnvironment env, bool edit) =>
        resolver.Resolve(Actor(http), id, CompanyPermission.ViewContractors | (edit ? CompanyPermission.EditContractors : CompanyPermission.None), env.IsDevelopment(), http.RequestAborted);
    private static IResult Error(int status, string code, string message) => Results.Json(new { code, message }, statusCode: status);
    private static IResult Missing() => Error(404, "finance_not_found", "Nie znaleziono kontrahenta lub jego ustawień w tej firmie.");
    private static IResult Invalid(Dictionary<string, string[]> errors) => Results.Json(new { code = "validation_failed", errors }, statusCode: 400);
    private static object Response(object entity, Guid company) => entity switch
    {
        ContractorTradeTerms x => TermsResponse(x, company, x.ContractorId, x.Direction),
        ContractorCreditLimit x => LimitResponse(x, company),
        PaymentMethod x => MethodResponse(x),
        _ => throw new InvalidOperationException()
    };
    private static TradeTermsResponse TermsResponse(ContractorTradeTerms? x, Guid company, int contractor, string direction) => new(x?.Id ?? 0, company, contractor, direction,
        x == null ? new() : new(x.PaymentDays, x.PaymentMethodCode, x.CurrencyCode, x.DiscountPercent, x.Notes), x == null ? null : Convert.ToBase64String(x.RowVersion));
    private static CreditLimitResponse LimitResponse(ContractorCreditLimit x, Guid company) => new(x.Id, company, x.ContractorId, x.Direction, x.CurrencyCode, x.Amount, x.IsActive, Convert.ToBase64String(x.RowVersion));
    private static PaymentMethodResponse MethodResponse(PaymentMethod x) => new(x.Id, x.Code, x.Name, x.IsActive, Convert.ToBase64String(x.RowVersion));
    private static int Id(object entity) => entity switch { ContractorTradeTerms x => x.Id, ContractorCreditLimit x => x.Id, PaymentMethod x => x.Id, _ => 0 };
    private static byte[] Version(object entity) => entity switch { ContractorTradeTerms x => x.RowVersion, ContractorCreditLimit x => x.RowVersion, PaymentMethod x => x.RowVersion, _ => [] };
    private static string Snapshot(object entity) => entity switch
    {
        ContractorTradeTerms x => JsonSerializer.Serialize(new { x.ContractorId, x.Direction, x.PaymentDays, x.PaymentMethodCode, x.CurrencyCode, x.DiscountPercent, x.Notes }),
        ContractorCreditLimit x => JsonSerializer.Serialize(new { x.ContractorId, x.Direction, x.CurrencyCode, x.Amount, x.IsActive }),
        PaymentMethod x => JsonSerializer.Serialize(new { x.Code, x.Name, x.IsActive }),
        _ => throw new InvalidOperationException()
    };
    private static IResult Conflict(object? entity, Guid company, int? contractor, string? direction) => Results.Json(new
    {
        code = "finance_conflict",
        message = "Inna osoba zmieniła ustawienia. Porównaj wersje przed ponownym zapisem.",
        financeCurrent = new FinanceConflictResponse(entity is ContractorTradeTerms t ? TermsResponse(t, company, t.ContractorId, t.Direction) : entity == null && contractor.HasValue ? TermsResponse(null, company, contractor.Value, direction!) : null,
            entity is ContractorCreditLimit l ? LimitResponse(l, company) : null, entity is PaymentMethod p ? MethodResponse(p) : null)
    }, statusCode: 409);
    private static async Task<IResult> Save(Guid companyId, int? contractorId, string kind, string? direction, int id, string? methodRoute,
        TradeTermsRequest? terms, CreditLimitRequest? limit, PaymentMethodRequest? method, HttpContext http,
        CompanyDatabaseResolver resolver, IWebHostEnvironment env, IAntiforgery csrf, SystemDbContext system, TimeProvider clock)
    {
        await using var access = await Resolve(companyId, http, resolver, env, true);
        try { await csrf.ValidateRequestAsync(http); } catch (AntiforgeryValidationException) { return Error(400, "csrf_invalid", "Nie potwierdzono żądania."); }
        var errors = terms != null ? terms.Data == null ? new Dictionary<string, string[]> { ["Data"] = ["Brak danych warunków."] } : FinanceValidation.Terms(terms.Data) : limit != null ? FinanceValidation.Limit(limit) : new();
        if (contractorId.HasValue && !TradeDirection.IsValid(direction!)) errors["Direction"] = ["Wybierz Sprzedaż lub Zakup."];
        if (method != null && (!FinanceValidation.Method(method.Code) || string.IsNullOrWhiteSpace(method.Name) || method.Name.Trim().Length > 100 || methodRoute != null && methodRoute != method.Code))
            errors["PaymentMethod"] = ["Nazwa: 1–100 znaków. Kod: 1–32 wielkie litery ASCII/cyfry/_/-, początek literą. Kod zapisanej pozycji jest niezmienny."];
        var version = terms?.RowVersion ?? limit?.RowVersion ?? method?.RowVersion;
        var operation = terms?.OperationId ?? limit?.OperationId ?? method?.OperationId;
        var create = kind == "ContractorTradeTerms" ? version == null : kind == "PaymentMethod" ? methodRoute == null : id == 0;
        byte[] bytes = [];
        try { bytes = Convert.FromBase64String(version ?? ""); } catch (FormatException) { }
        if (create ? version != null || operation is null || operation == Guid.Empty : bytes.Length != 8 || operation.HasValue)
            errors["RowVersion"] = ["Tworzenie wymaga klucza operacji, edycja bieżącej wersji. Odśwież formularz."];
        if (limit != null && (id < 0 || id != limit.Id)) errors["Id"] = ["Id pozycji nie pasuje do adresu."];
        if (errors.Count > 0) return Invalid(errors);
        var db = access.Context; var ct = http.RequestAborted;
        if (contractorId.HasValue && !await db.Contractors.AnyAsync(x => x.Id == contractorId, ct)) return Missing();
        var issuer = await system.InstallationIdentities.Select(x => x.Id).SingleOrDefaultAsync(ct);
        if (issuer == Guid.Empty || string.IsNullOrWhiteSpace(http.User.Identity?.Name)) return Error(503, "audit_identity_unavailable", "Brak trwałej identyfikacji wykonawcy.");
        var actor = Actor(http);
        var hash = IdentifierChecks.Hash(JsonSerializer.Serialize(new { contractorId, direction, id, methodRoute, terms, limit, method }));
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        async Task<object?> Find() => kind switch
        {
            "ContractorTradeTerms" => await db.ContractorTradeTerms.SingleOrDefaultAsync(x => x.ContractorId == contractorId && x.Direction == direction, ct),
            "ContractorCreditLimit" => await db.ContractorCreditLimits.SingleOrDefaultAsync(x => x.ContractorId == contractorId && x.Id == id, ct),
            _ => await db.PaymentMethods.SingleOrDefaultAsync(x => x.Code == (methodRoute ?? method!.Code), ct)
        };
        try
        {
            await CompanyWriteLock.Acquire(db, "ContractorFinance", ct);
            if (create)
            {
                var previous = await db.ContractorCreateOperations.AsNoTracking().SingleOrDefaultAsync(x => x.ActorId == actor && x.ActorIssuerId == issuer && x.OperationKind == kind && x.OperationId == operation, ct);
                if (previous != null) return previous.RequestHash == hash ? Results.Content(previous.ResponseJson, "application/json", statusCode: 200) : Error(409, "operation_payload_mismatch", "Ta operacja została zapisana z innymi danymi.");
            }
            var entity = create && kind == "ContractorCreditLimit" ? null : await Find();
            if (create && entity != null) return kind == "ContractorTradeTerms" ? Conflict(entity, companyId, contractorId, direction) : Error(409, "finance_key_exists", "Ten kod sposobu płatności jest już zajęty.");
            if (!create && entity == null) return Missing();
            if (!create && !Version(entity!).SequenceEqual(bytes)) return Conflict(entity, companyId, contractorId, direction);
            var before = entity == null ? null : Snapshot(entity);
            var currency = terms?.Data?.CurrencyCode ?? limit?.CurrencyCode;
            var oldCurrency = entity switch { ContractorTradeTerms x => x.CurrencyCode, ContractorCreditLimit x => x.CurrencyCode, _ => null };
            if (currency != null && !await db.Currencies.AnyAsync(x => x.Code == currency && (x.IsActive || x.Code == oldCurrency), ct))
                return Invalid(new() { ["CurrencyCode"] = ["Nieznana lub nieaktywna waluta. Wybierz pozycję istniejącego słownika."] });
            if (terms?.Data is { } data)
            {
                var oldMethod = (entity as ContractorTradeTerms)?.PaymentMethodCode;
                if (data.PaymentMethodCode != null && !await db.PaymentMethods.AnyAsync(x => x.Code == data.PaymentMethodCode && (x.IsActive || x.Code == oldMethod), ct))
                    return Invalid(new() { ["PaymentMethodCode"] = ["Nieznany lub nieaktywny sposób płatności."] });
                var row = entity as ContractorTradeTerms ?? new(contractorId!.Value, direction!);
                row.Update(data.PaymentDays, data.PaymentMethodCode, data.CurrencyCode, data.DiscountPercent, data.Notes);
                if (create) db.ContractorTradeTerms.Add(row); entity = row;
            }
            else if (limit != null)
            {
                var row = entity as ContractorCreditLimit ?? new(contractorId!.Value, direction!);
                if (row.Direction != direction) return Error(400, "finance_invalid", "Nie można przenieść limitu do innego kierunku.");
                row.Update(limit.CurrencyCode, limit.Amount, limit.IsActive);
                if (create) db.ContractorCreditLimits.Add(row); entity = row;
            }
            else
            {
                var row = entity as PaymentMethod ?? new(method!.Code, method.Name);
                row.Update(method!.Name, method.IsActive); if (create) db.PaymentMethods.Add(row); entity = row;
            }
            var after = Snapshot(entity!);
            if (before == after) return Results.Ok(Response(entity!, companyId));
            await db.SaveChangesAsync(ct);
            var response = Response(entity!, companyId);
            db.ContractorChanges.Add(new(contractorId, actor, issuer, http.User.Identity!.Name!, clock.GetUtcNow(), http.TraceIdentifier, before, after, kind, Id(entity!), snapshotVersion: 3));
            if (create) db.ContractorCreateOperations.Add(new(operation!.Value, actor, issuer, hash, contractorId, JsonSerializer.Serialize(response, Json), kind));
            await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct);
            return Results.Json(response, statusCode: create ? 201 : 200);
        }
        catch (DbUpdateConcurrencyException)
        {
            await transaction.RollbackAsync(ct); db.ChangeTracker.Clear();
            return Conflict(await Find(), companyId, contractorId, direction);
        }
        catch (DbUpdateException error) when (error.InnerException is SqlException { Number: 2601 or 2627 })
        { return Error(409, "finance_key_exists", "Istnieją już warunki, kod sposobu płatności lub aktywny limit w tej walucie i kierunku. Odśwież i porównaj dane."); }
        catch (Exception error) when (error is DbUpdateException or SqlException)
        { return Error(503, "finance_save_failed", "Nie zapisano ustawień ani audytu. Spróbuj ponownie."); }
    }
}
