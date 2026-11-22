using Microsoft.AspNetCore.Antiforgery;
using Microsoft.EntityFrameworkCore;
using Vatowiec.Api.Security;
using Vatowiec.Core.Domain;
using Vatowiec.Core.Infrastructure.ExchangeRates;
using Vatowiec.Core.Infrastructure.Operations;
using Vatowiec.Core.Infrastructure.Persistence;
using Vatowiec.SharedContracts;

namespace Vatowiec.Api.Endpoints;

// Waluty firmy (aktywność w bazie firmy) i odczyt wspólnych kursów NBP z System (ADR-048).
// Bez zapytań łączących bazy: kody walut firmy i kursy instalacji czytane osobnymi połączeniami.
public static class CurrencyEndpoints
{
    public static void MapCurrencies(this RouteGroupBuilder companies)
    {
        var group = companies.MapGroup("/{companyId:guid}").AddEndpointFilter(CompanyEndpoints.ValidateCompanySource)
            .AddEndpointFilter(async (context, next) =>
            {
                try { return await next(context); }
                catch (CurrencyOperationException e) { return Results.Json(new { code = e.Code, message = e.Message }, statusCode: e.Status); }
                catch (AntiforgeryValidationException) { return Results.BadRequest(new { code = "csrf_invalid" }); }
                catch (DbUpdateConcurrencyException) { return Results.Conflict(new { code = "currency_stale", message = "Dane zmieniły się. Odśwież i porównaj wersje." }); }
                catch (DbUpdateException) { return Results.Json(new { code = "currency_save_failed", message = "Nie zapisano operacji walutowej; zmiany i audyt wycofano." }, statusCode: 503); }
            });
        group.MapGet("/currencies", ListCurrencies);
        group.MapPut("/currencies/{currencyCode}", Configure);
        group.MapPost("/currencies/activity", ChangeActivity);
        group.MapGet("/exchange-rates", ListRates);
        group.MapGet("/exchange-rates/history", History);
        group.MapGet("/exchange-rates/lookup", Lookup);
    }

    // ADR-048: Ustawienia instalacji → Kursy NBP. Wyłącznie administrator instalacji (świeża kontrola roli), zapisy z CSRF.
    public static void MapExchangeRateAdministration(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/installation/exchange-rates").RequireAuthorization(InstallationAdministration.Policy);
        group.MapGet("", (ExchangeRateAdministrationService service, HttpContext http) => Handle(() => service.Read(http.RequestAborted)));
        group.MapGet("/history", (ExchangeRateAdministrationService service, HttpContext http, int offset = 0) => Handle(() => service.History(offset, http.RequestAborted)));
        group.MapPost("/run", async (HttpContext http, IAntiforgery csrf, ExchangeRateAdministrationService service) =>
            await ValidCsrf(csrf, http) ? await Handle(() => service.RequestRun(Requester(http), http.RequestAborted)) : Csrf());
        group.MapPut("/history-start", async (ExchangeRateHistoryStartRequest value, HttpContext http, IAntiforgery csrf, ExchangeRateAdministrationService service) =>
            await ValidCsrf(csrf, http) ? await Handle(() => service.SetHistoryStart(Requester(http), value, http.RequestAborted)) : Csrf());
        group.MapPost("/conflicts/{conflictId:long}/resolve", async (long conflictId, ExchangeRateConflictResolution value, HttpContext http, IAntiforgery csrf,
            ExchangeRateAdministrationService service) =>
            await ValidCsrf(csrf, http) ? await Handle(() => service.Resolve(Requester(http), conflictId, value, http.RequestAborted)) : Csrf());
    }

    private static async Task<IResult> Handle<T>(Func<Task<T>> action)
    {
        try { return Results.Ok(await action()); }
        catch (CurrencyOperationException e) { return Results.Json(new { code = e.Code, message = e.Message }, statusCode: e.Status); }
        catch (DbUpdateConcurrencyException) { return Results.Conflict(new { code = "currency_stale", message = "Dane zmieniły się. Odśwież i porównaj wersje." }); }
        catch (DbUpdateException) { return Results.Json(new { code = "currency_save_failed", message = "Nie zapisano zmiany; zmiany i historię wycofano." }, statusCode: 503); }
    }

    private static async Task<bool> ValidCsrf(IAntiforgery csrf, HttpContext http)
    {
        try { await csrf.ValidateRequestAsync(http); return true; }
        catch (AntiforgeryValidationException) { return false; }
    }

    private static OperationRequester Requester(HttpContext http) => new(http.UserId(), http.User.Identity?.Name ?? "");
    private static IResult Csrf() => Results.Json(new { code = "csrf_invalid", message = "Nie udało się potwierdzić żądania." }, statusCode: 400);

    private static Task<AuthorizedCompanyDatabase> Access(Guid company, HttpContext http, CompanyDatabaseResolver resolver, IWebHostEnvironment env, CompanyPermission permission) =>
        resolver.Resolve(http.UserId(), company, permission, env.IsDevelopment(), http.RequestAborted);
    private static async Task<CurrencyActor> Actor(HttpContext http, SystemDbContext system)
    {
        var issuer = await system.InstallationIdentities.Select(x => x.Id).SingleAsync(http.RequestAborted);
        var name = http.User.Identity?.Name;
        if (issuer == Guid.Empty || string.IsNullOrWhiteSpace(name)) throw new CurrencyOperationException(503, "currency_actor", "Brak trwałej identyfikacji wykonawcy.");
        return new(http.UserId(), issuer, name);
    }
    private static async Task<IResult> ListCurrencies(Guid companyId, HttpContext http, CompanyDatabaseResolver resolver, IWebHostEnvironment env,
        SystemDbContext system, bool withRatesOnly = false)
    {
        await using var access = await Access(companyId, http, resolver, env, CompanyPermission.ViewExchangeRates);
        var rows = await access.Context.Currencies.AsNoTracking().OrderBy(x => x.Code).ToArrayAsync(http.RequestAborted);
        if (withRatesOnly)
        {
            var withRates = (await system.ExchangeRates.AsNoTracking().Select(x => x.CurrencyCode).Distinct().ToArrayAsync(http.RequestAborted)).ToHashSet(StringComparer.Ordinal);
            rows = rows.Where(x => withRates.Contains(x.Code)).ToArray();
        }
        return Results.Ok(rows.Select(x => CurrencySettingsService.Response(x, companyId)));
    }
    private static async Task<IResult> Configure(Guid companyId, string currencyCode, CurrencyWriteRequest value, HttpContext http, CompanyDatabaseResolver resolver,
        IWebHostEnvironment env, IAntiforgery csrf, SystemDbContext system, TimeProvider clock)
    {
        await using var access = await Access(companyId, http, resolver, env, CompanyPermission.ViewExchangeRates | CompanyPermission.ManageCurrencies);
        await csrf.ValidateRequestAsync(http);
        return Results.Ok(await new CurrencySettingsService(access.Context, clock).Configure(companyId, currencyCode, value, await Actor(http, system)));
    }
    private static async Task<IResult> ChangeActivity(Guid companyId, CurrencyActivityRequest value, HttpContext http, CompanyDatabaseResolver resolver,
        IWebHostEnvironment env, IAntiforgery csrf, SystemDbContext system, TimeProvider clock)
    {
        await using var access = await Access(companyId, http, resolver, env, CompanyPermission.ViewExchangeRates | CompanyPermission.ManageCurrencies);
        await csrf.ValidateRequestAsync(http);
        return Results.Ok(await new CurrencySettingsService(access.Context, clock).ChangeActivity(companyId, value, await Actor(http, system)));
    }
    private static async Task<IResult> ListRates(Guid companyId, HttpContext http, CompanyDatabaseResolver resolver, IWebHostEnvironment env, SystemDbContext system,
        string? code = null, string? table = null, DateOnly? from = null, DateOnly? to = null, int offset = 0, int limit = 51, bool descending = true)
    {
        // Kontrola prawa firmy przed odczytem wspólnych kursów; połączenie z bazą firmy zamykamy od razu.
        await using (await Access(companyId, http, resolver, env, CompanyPermission.ViewExchangeRates)) { }
        if (offset is < 0 or > 1_000_000 || limit is < 1 or > 100 || code != null && !System.Text.RegularExpressions.Regex.IsMatch(code, "^[A-Z]{3}$") ||
            table != null && table is not ("A" or "B" or "C") || from > to)
            return Results.BadRequest(new { code = "currency_filter", message = "Nieprawidłowe filtry kursów." });
        var query = system.ExchangeRates.AsNoTracking().Where(rate => (code == null || rate.CurrencyCode == code) && (table == null || rate.TableType == table) &&
            (from == null || rate.EffectiveDate >= from) && (to == null || rate.EffectiveDate <= to));
        var ordered = descending ? query.OrderByDescending(x => x.EffectiveDate).ThenBy(x => x.CurrencyCode).ThenBy(x => x.TableType) :
            query.OrderBy(x => x.EffectiveDate).ThenBy(x => x.CurrencyCode).ThenBy(x => x.TableType);
        var rows = await ordered.Skip(offset).Take(limit).ToArrayAsync(http.RequestAborted);
        var status = await ExchangeRateAdministrationService.Status(system, http.RequestAborted);
        return Results.Ok(new RatePage(companyId, rows.Select(x => new RateResponse(x.Id, companyId, x.CurrencyCode, x.Source, x.TableType, x.TableNumber,
            x.EffectiveDate, x.TradingDate, x.Mid, x.Bid, x.Ask, x.RetrievedAtUtc, Convert.ToBase64String(x.RowVersion))).ToArray(),
            status.LastA, status.LastB, status.LastC, status));
    }
    private static async Task<IResult> History(Guid companyId, HttpContext http, CompanyDatabaseResolver resolver, IWebHostEnvironment env, int offset = 0)
    {
        await using var access = await Access(companyId, http, resolver, env, CompanyPermission.ViewExchangeRates);
        if (offset is < 0 or > 1_000_000) return Results.BadRequest(new { code = "currency_filter" });
        var rows = await access.Context.CurrencyOperations.AsNoTracking().OrderByDescending(x => x.Id).Skip(offset).Take(51).ToArrayAsync(http.RequestAborted);
        return Results.Ok(rows.Select(x => CurrencySettingsService.History(x, companyId)));
    }
    private static async Task<IResult> Lookup(Guid companyId, HttpContext http, CompanyDatabaseResolver resolver, IWebHostEnvironment env, SystemDbContext system,
        string code, string table, DateOnly date, bool before = false)
    {
        await using (await Access(companyId, http, resolver, env, CompanyPermission.ViewExchangeRates)) { }
        var reader = new ExchangeRateReader(system);
        try { return Results.Ok(before ? await reader.Before(code, table, date, http.RequestAborted) : await reader.OnDate(code, table, date, http.RequestAborted)); }
        catch (ArgumentException) { return Results.BadRequest(new { code = "currency_filter" }); }
    }
}
