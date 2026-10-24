using System.Security.Claims;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.EntityFrameworkCore;
using Vatowiec.Core.Domain;
using Vatowiec.Core.Infrastructure.Persistence;
using Vatowiec.Core.Infrastructure.Registries;
using Vatowiec.SharedContracts;

namespace Vatowiec.Api.Security;

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
        group.MapPost("/exchange-rates/imports", Import).RequireRateLimiting("registries");
        group.MapPost("/exchange-rates/imports/{auditId:long}/resolve", Resolve);
        group.MapGet("/exchange-rates/history", History);
        group.MapGet("/exchange-rates/lookup", Lookup);
    }
    private static Guid ActorId(HttpContext http) => Guid.Parse(http.User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    private static Task<AuthorizedCompanyDatabase> Access(Guid company, HttpContext http, CompanyDatabaseResolver resolver, IWebHostEnvironment env, CompanyPermission permission) =>
        resolver.Resolve(ActorId(http), company, permission, env.IsDevelopment(), http.RequestAborted);
    private static async Task<CurrencyActor> Actor(HttpContext http, SystemDbContext system)
    {
        var issuer = await system.InstallationIdentities.Select(x => x.Id).SingleAsync(http.RequestAborted);
        var name = http.User.Identity?.Name;
        if (issuer == Guid.Empty || string.IsNullOrWhiteSpace(name)) throw new CurrencyOperationException(503, "currency_actor", "Brak trwałej identyfikacji wykonawcy.");
        return new(ActorId(http), issuer, name);
    }
    private static async Task<IResult> ListCurrencies(Guid companyId, HttpContext http, CompanyDatabaseResolver resolver, IWebHostEnvironment env, bool withRatesOnly = false)
    {
        await using var access = await Access(companyId, http, resolver, env, CompanyPermission.ViewExchangeRates);
        var rows = await access.Context.Currencies.AsNoTracking()
            .Where(x => !withRatesOnly || access.Context.ExchangeRates.Any(rate => rate.CurrencyCode == x.Code))
            .OrderBy(x => x.Code).ToArrayAsync(http.RequestAborted);
        return Results.Ok(rows.Select(x => ExchangeRateService.Response(x, companyId)));
    }
    private static async Task<IResult> Configure(Guid companyId, string currencyCode, CurrencyWriteRequest value, HttpContext http, CompanyDatabaseResolver resolver,
        IWebHostEnvironment env, IAntiforgery csrf, SystemDbContext system, TimeProvider clock)
    {
        await using var access = await Access(companyId, http, resolver, env, CompanyPermission.ViewExchangeRates | CompanyPermission.ManageCurrencies);
        await csrf.ValidateRequestAsync(http);
        return Results.Ok(await new ExchangeRateService(access.Context, clock).Configure(companyId, currencyCode, value, await Actor(http, system)));
    }
    private static async Task<IResult> ChangeActivity(Guid companyId, CurrencyActivityRequest value, HttpContext http, CompanyDatabaseResolver resolver,
        IWebHostEnvironment env, IAntiforgery csrf, SystemDbContext system, TimeProvider clock)
    {
        await using var access = await Access(companyId, http, resolver, env, CompanyPermission.ViewExchangeRates | CompanyPermission.ManageCurrencies);
        await csrf.ValidateRequestAsync(http);
        return Results.Ok(await new ExchangeRateService(access.Context, clock).ChangeActivity(companyId, value, await Actor(http, system)));
    }
    private static async Task<IResult> Import(Guid companyId, RateImportRequest value, HttpContext http, CompanyDatabaseResolver resolver,
        IWebHostEnvironment env, IAntiforgery csrf, SystemDbContext system, INbpClient client, TimeProvider clock)
    {
        await using var access = await Access(companyId, http, resolver, env, CompanyPermission.ViewExchangeRates | CompanyPermission.ImportExchangeRates);
        await csrf.ValidateRequestAsync(http);
        return Results.Ok(await new ExchangeRateService(access.Context, clock).Import(companyId, value, await Actor(http, system), client, http.RequestAborted));
    }
    private static async Task<IResult> Resolve(Guid companyId, long auditId, RateResolutionRequest value, HttpContext http, CompanyDatabaseResolver resolver,
        IWebHostEnvironment env, IAntiforgery csrf, SystemDbContext system, TimeProvider clock)
    {
        await using var access = await Access(companyId, http, resolver, env, CompanyPermission.ViewExchangeRates | CompanyPermission.ManageCurrencies | CompanyPermission.ImportExchangeRates);
        await csrf.ValidateRequestAsync(http);
        return Results.Ok(await new ExchangeRateService(access.Context, clock).Resolve(companyId, auditId, value, await Actor(http, system)));
    }
    private static async Task<IResult> ListRates(Guid companyId, HttpContext http, CompanyDatabaseResolver resolver, IWebHostEnvironment env,
        string? code = null, string? table = null, DateOnly? from = null, DateOnly? to = null, int offset = 0, int limit = 51, bool descending = true)
    {
        await using var access = await Access(companyId, http, resolver, env, CompanyPermission.ViewExchangeRates);
        if (offset is < 0 or > 1_000_000 || limit is < 1 or > 100 || code != null && !System.Text.RegularExpressions.Regex.IsMatch(code, "^[A-Z]{3}$") ||
            table != null && table is not ("A" or "B" or "C") || from > to)
            return Results.BadRequest(new { code = "currency_filter", message = "Nieprawidłowe filtry kursów." });
        var db = access.Context;
        var start = from;
        var query = from rate in db.ExchangeRates.AsNoTracking()
                    join currency in db.Currencies on rate.CurrencyCode equals currency.Code
                    where (code == null || currency.Code == code) && (table == null || rate.TableType == table) &&
                        (start == null || rate.EffectiveDate >= start) && (to == null || rate.EffectiveDate <= to)
                    select new { Rate = rate, currency.Code };
        var ordered = descending ? query.OrderByDescending(x => x.Rate.EffectiveDate).ThenBy(x => x.Code).ThenBy(x => x.Rate.TableType) :
            query.OrderBy(x => x.Rate.EffectiveDate).ThenBy(x => x.Code).ThenBy(x => x.Rate.TableType);
        var rows = await ordered.Skip(offset).Take(limit).ToArrayAsync(http.RequestAborted);
        var last = await db.ExchangeRates.GroupBy(x => x.TableType).Select(x => new { Type = x.Key, Date = x.Max(r => r.EffectiveDate) }).ToArrayAsync(http.RequestAborted);
        return Results.Ok(new RatePage(companyId, rows.Select(x => new RateResponse(x.Rate.Id, companyId, x.Code, x.Rate.Source, x.Rate.TableType, x.Rate.TableNumber,
            x.Rate.EffectiveDate, x.Rate.TradingDate, x.Rate.Mid, x.Rate.Bid, x.Rate.Ask, x.Rate.RetrievedAtUtc, Convert.ToBase64String(x.Rate.RowVersion))).ToArray(),
            last.Where(x => x.Type == "A").Select(x => (DateOnly?)x.Date).SingleOrDefault(), last.Where(x => x.Type == "B").Select(x => (DateOnly?)x.Date).SingleOrDefault(),
            last.Where(x => x.Type == "C").Select(x => (DateOnly?)x.Date).SingleOrDefault()));
    }
    private static async Task<IResult> History(Guid companyId, HttpContext http, CompanyDatabaseResolver resolver, IWebHostEnvironment env, int offset = 0)
    {
        await using var access = await Access(companyId, http, resolver, env, CompanyPermission.ViewExchangeRates);
        if (offset is < 0 or > 1_000_000) return Results.BadRequest(new { code = "currency_filter" });
        var rows = await access.Context.CurrencyOperations.AsNoTracking().OrderByDescending(x => x.Id).Skip(offset).Take(51).ToArrayAsync(http.RequestAborted);
        return Results.Ok(rows.Select(x => ExchangeRateService.History(x, companyId)));
    }
    private static async Task<IResult> Lookup(Guid companyId, HttpContext http, CompanyDatabaseResolver resolver, IWebHostEnvironment env,
        string code, string table, DateOnly date, bool before = false)
    {
        await using var access = await Access(companyId, http, resolver, env, CompanyPermission.ViewExchangeRates);
        var reader = new ExchangeRateReader(access.Context);
        try { return Results.Ok(before ? await reader.Before(code, table, date, http.RequestAborted) : await reader.OnDate(code, table, date, http.RequestAborted)); }
        catch (ArgumentException) { return Results.BadRequest(new { code = "currency_filter" }); }
    }
}
