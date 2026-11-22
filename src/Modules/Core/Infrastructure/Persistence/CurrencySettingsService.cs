using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Vatowiec.Core.Application;
using Vatowiec.Core.Domain;
using Vatowiec.SharedContracts;

namespace Vatowiec.Core.Infrastructure.Persistence;

public sealed record CurrencyActor(Guid Id, Guid Issuer, string Name);
public sealed class CurrencyOperationException(int status, string code, string message) : Exception(message)
{ public int Status { get; } = status; public string Code { get; } = code; }

// ADR-048: kursy NBP czytamy ze wspólnej bazy System, niezależnie od aktywności waluty w firmie.
public sealed class ExchangeRateReader(SystemDbContext db) : IExchangeRateReader
{
    public Task<PublishedRate?> OnDate(string code, string table, DateOnly date, CancellationToken cancellationToken = default) => Read(code, table, date, false, cancellationToken);
    public Task<PublishedRate?> Before(string code, string table, DateOnly date, CancellationToken cancellationToken = default) => Read(code, table, date, true, cancellationToken);
    private Task<PublishedRate?> Read(string code, string table, DateOnly date, bool before, CancellationToken ct)
    {
        if (table is not ("A" or "B" or "C") || !System.Text.RegularExpressions.Regex.IsMatch(code, "^[A-Z]{3}$")) throw new ArgumentException("Nieprawidłowa waluta lub tabela.");
        return db.ExchangeRates.AsNoTracking()
            .Where(rate => rate.CurrencyCode == code && rate.Source == "NBP" && rate.TableType == table && (before ? rate.EffectiveDate < date : rate.EffectiveDate == date))
            .OrderByDescending(rate => rate.EffectiveDate)
            .Select(rate => new PublishedRate(rate.Id, rate.CurrencyCode, rate.Source, rate.TableType, rate.TableNumber, rate.EffectiveDate, rate.TradingDate, rate.Mid, rate.Bid, rate.Ask))
            .FirstOrDefaultAsync(ct);
    }
}

// Aktywność walut bazy firmy z idempotencją operacji i niezmiennym audytem (audit.CurrencyOperations).
public sealed class CurrencySettingsService(CompanyDbContext db, TimeProvider clock)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static CurrencyOperationException Error(int status, string code, string message) => new(status, code, message);
    private static string Hash(object value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value, Json))));
    private async Task Lock()
    {
        await db.Database.ExecuteSqlRawAsync("DECLARE @r int; EXEC @r=sys.sp_getapplock @Resource=N'CurrencyOperations', @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=15000; IF @r<0 THROW 51000, 'Currency operation lock unavailable', 1;");
    }
    private async Task<CurrencyOperation?> Previous(CurrencyActor actor, Guid operation, string hash)
    {
        if (operation == Guid.Empty) throw Error(400, "currency_operation_invalid", "Brak identyfikatora operacji.");
        var old = await db.CurrencyOperations.AsNoTracking().SingleOrDefaultAsync(x => x.ActorId == actor.Id && x.ActorIssuerId == actor.Issuer && x.OperationId == operation);
        if (old != null && old.RequestHash != hash) throw Error(409, "operation_payload_mismatch", "Ta operacja została już użyta z innymi danymi.");
        return old;
    }
    private CurrencyOperation Audit(CurrencyActor actor, Guid operation, string hash, string kind, object details)
    {
        var row = new CurrencyOperation
        {
            OperationId = operation,
            RequestHash = hash,
            Kind = kind,
            ActorId = actor.Id,
            ActorIssuerId = actor.Issuer,
            ActorName = actor.Name,
            OccurredAtUtc = clock.GetUtcNow(),
            DetailsJson = JsonSerializer.Serialize(details, Json)
        };
        db.CurrencyOperations.Add(row); return row;
    }
    public static CurrencyResponse Response(Currency row, Guid company) => new(company, row.Code, row.Name, row.IsActive, Convert.ToBase64String(row.RowVersion));
    public sealed record ConfigurationAudit(CurrencyResponse Before, CurrencyResponse After);
    public sealed record ActivityAudit(CurrencyActivityResponse Result, ConfigurationAudit[] Changes);

    public async Task<CurrencyResponse> Configure(Guid company, string currencyCode, CurrencyWriteRequest value, CurrencyActor actor)
    {
        if (!System.Text.RegularExpressions.Regex.IsMatch(currencyCode, "\\A[A-Z]{3}\\z"))
            throw Error(400, "currency_code", "Walutę wskazuje trzyliterowy kod ISO zapisany wielkimi literami, np. EUR.");
        var hash = Hash(new { Kind = "Configuration", currencyCode, value });
        await using var transaction = await db.Database.BeginTransactionAsync(); await Lock();
        var previous = await Previous(actor, value.OperationId, hash);
        if (previous != null) return JsonSerializer.Deserialize<ConfigurationAudit>(previous.DetailsJson, Json)!.After;
        var row = await db.Currencies.SingleOrDefaultAsync(x => x.Code == currencyCode) ?? throw Error(404, "currency_missing", "Nie znaleziono waluty.");
        if (Convert.ToBase64String(row.RowVersion) != value.RowVersion) throw Error(409, "currency_stale", "Ustawienia waluty zmieniły się. Odśwież i porównaj wybór.");
        if (row.Code == "PLN" && !value.IsActive)
            throw Error(400, "currency_invalid", "PLN jest walutą podstawową i pozostaje aktywny.");
        var before = Response(row, company);
        row.IsActive = value.IsActive;
        // Wersja odpowiedzi powstaje w SQL; dwa SaveChanges w jednej transakcji.
        await db.SaveChangesAsync();
        var response = Response(row, company);
        Audit(actor, value.OperationId, hash, "Configuration", new ConfigurationAudit(before, response));
        await db.SaveChangesAsync(); await transaction.CommitAsync(); return response;
    }

    public async Task<CurrencyActivityResponse> ChangeActivity(Guid company, CurrencyActivityRequest value, CurrencyActor actor)
    {
        if (!value.Confirm) throw Error(400, "currency_confirmation", "Potwierdź zbiorową zmianę aktywności walut.");
        if (value.Currencies is not { Length: > 0 and <= 1000 } || value.Currencies.Any(x => x == null ||
            !System.Text.RegularExpressions.Regex.IsMatch(x.Code ?? "", "\\A[A-Z]{3}\\z") || string.IsNullOrEmpty(x.RowVersion)) ||
            value.Currencies.Select(x => x.Code).Distinct(StringComparer.Ordinal).Count() != value.Currencies.Length)
            throw Error(400, "currency_selection", "Wskaż różne kody walut wraz z ich wersjami.");
        var hash = Hash(new { Kind = "BulkActivity", value });
        await using var transaction = await db.Database.BeginTransactionAsync(); await Lock();
        var previous = await Previous(actor, value.OperationId, hash);
        if (previous != null) return JsonSerializer.Deserialize<ActivityAudit>(previous.DetailsJson, Json)!.Result;
        var codes = value.Currencies.Select(x => x.Code).ToArray();
        var rows = await db.Currencies.Where(x => value.All || codes.Contains(x.Code)).OrderBy(x => x.Code).ToArrayAsync();
        if (!rows.Select(x => x.Code).ToHashSet(StringComparer.Ordinal).SetEquals(codes))
            throw Error(409, "currency_selection_changed", "Słownik zmienił się lub brakuje waluty. Odśwież i potwierdź wybór ponownie.");
        var versions = value.Currencies.ToDictionary(x => x.Code, x => x.RowVersion, StringComparer.Ordinal);
        // Cały zakres sprawdzany przed zmianą. PLN nigdy nie jest dezaktywowany.
        foreach (var row in rows.Where(x => value.IsActive || x.Code != "PLN"))
            if (Convert.ToBase64String(row.RowVersion) != versions[row.Code])
                throw Error(409, "currency_stale", "Ustawienia walut zmieniły się. Nie zapisano żadnej zmiany; odśwież i porównaj wybór.");
        var changed = rows.Where(x => x.IsActive != value.IsActive && (value.IsActive || x.Code != "PLN")).ToArray();
        var before = changed.Select(x => Response(x, company)).ToArray();
        foreach (var row in changed) row.IsActive = value.IsActive;
        await db.SaveChangesAsync();
        var result = new CurrencyActivityResponse(company, value.IsActive, changed.Select(x => Response(x, company)).ToArray(),
            rows.Except(changed).Select(x => x.Code).ToArray());
        Audit(actor, value.OperationId, hash, "BulkActivity", new ActivityAudit(result,
            before.Zip(result.Changed, (old, current) => new ConfigurationAudit(old, current)).ToArray()));
        await db.SaveChangesAsync(); await transaction.CommitAsync(); return result;
    }

    public static CurrencyHistoryResponse History(CurrencyOperation row, Guid company) => new(row.Id, company, row.Kind, row.OccurredAtUtc, row.ActorName, row.DetailsJson);
}
