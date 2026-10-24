using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Vatowiec.Core.Application;
using Vatowiec.Core.Domain;
using Vatowiec.Core.Infrastructure.Registries;
using Vatowiec.SharedContracts;

namespace Vatowiec.Core.Infrastructure.Persistence;

public sealed record CurrencyActor(Guid Id, Guid Issuer, string Name);
public sealed class CurrencyOperationException(int status, string code, string message) : Exception(message)
{ public int Status { get; } = status; public string Code { get; } = code; }

public sealed class ExchangeRateReader(CompanyDbContext db) : IExchangeRateReader
{
    public Task<PublishedRate?> OnDate(string code, string table, DateOnly date, CancellationToken cancellationToken = default) => Read(code, table, date, false, cancellationToken);
    public Task<PublishedRate?> Before(string code, string table, DateOnly date, CancellationToken cancellationToken = default) => Read(code, table, date, true, cancellationToken);
    private Task<PublishedRate?> Read(string code, string table, DateOnly date, bool before, CancellationToken ct)
    {
        if (table is not ("A" or "B" or "C") || !System.Text.RegularExpressions.Regex.IsMatch(code, "^[A-Z]{3}$")) throw new ArgumentException("Nieprawidłowa waluta lub tabela.");
        return (from rate in db.ExchangeRates.AsNoTracking()
                join currency in db.Currencies on rate.CurrencyCode equals currency.Code
                where currency.Code == code && rate.Source == "NBP" && rate.TableType == table && (before ? rate.EffectiveDate < date : rate.EffectiveDate == date)
                orderby rate.EffectiveDate descending
                select new PublishedRate(rate.Id, currency.Code, rate.Source, rate.TableType, rate.TableNumber, rate.EffectiveDate, rate.TradingDate, rate.Mid, rate.Bid, rate.Ask))
            .FirstOrDefaultAsync(ct);
    }
}

public sealed class ExchangeRateService(CompanyDbContext db, TimeProvider clock)
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
    public static CurrencyResponse Response(Currency row, Guid company) => new(company, row.Code, row.Name, row.IsActive,
        row.DownloadA, row.DownloadB, row.DownloadC, Convert.ToBase64String(row.RowVersion));
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
        if (row.Code == "PLN" && (!value.IsActive || value.DownloadA || value.DownloadB || value.DownloadC) ||
            !value.IsActive && (value.DownloadA || value.DownloadB || value.DownloadC))
            throw Error(400, "currency_invalid", "PLN pozostaje aktywny bez kursu NBP. Waluta nieaktywna nie może mieć włączonego pobierania.");
        var before = Response(row, company);
        row.IsActive = value.IsActive; row.DownloadA = value.DownloadA; row.DownloadB = value.DownloadB; row.DownloadC = value.DownloadC;
        // The returned version is stored after SQL generated it. Two SaveChanges, one transaction.
        await db.SaveChangesAsync();
        var response = Response(row, company);
        Audit(actor, value.OperationId, hash, "Configuration", response);
        // Include original and resulting values in a single immutable audit payload.
        var audit = db.ChangeTracker.Entries<CurrencyOperation>().Single(x => x.State == EntityState.Added).Entity;
        audit.DetailsJson = JsonSerializer.Serialize(new ConfigurationAudit(before, response), Json);
        await db.SaveChangesAsync(); await transaction.CommitAsync(); return response;
    }
    public sealed record ConfigurationAudit(CurrencyResponse Before, CurrencyResponse After);
    public sealed record ActivityAudit(CurrencyActivityResponse Result, ConfigurationAudit[] Changes);
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
        // Validate the entire scope before changing anything. PLN is always excluded from deactivation.
        foreach (var row in rows.Where(x => value.IsActive || x.Code != "PLN"))
            if (Convert.ToBase64String(row.RowVersion) != versions[row.Code])
                throw Error(409, "currency_stale", "Ustawienia walut zmieniły się. Nie zapisano żadnej zmiany; odśwież i porównaj wybór.");
        var changed = rows.Where(x => x.IsActive != value.IsActive && (value.IsActive || x.Code != "PLN")).ToArray();
        var before = changed.Select(x => Response(x, company)).ToArray();
        foreach (var row in changed)
        {
            row.IsActive = value.IsActive;
            if (!value.IsActive) row.DownloadA = row.DownloadB = row.DownloadC = false;
        }
        await db.SaveChangesAsync();
        var result = new CurrencyActivityResponse(company, value.IsActive, changed.Select(x => Response(x, company)).ToArray(),
            rows.Except(changed).Select(x => x.Code).ToArray());
        Audit(actor, value.OperationId, hash, "BulkActivity", new ActivityAudit(result,
            before.Zip(result.Changed, (old, current) => new ConfigurationAudit(old, current)).ToArray()));
        await db.SaveChangesAsync(); await transaction.CommitAsync(); return result;
    }
    private static string Signature(IEnumerable<Currency> rows) => Hash(rows.OrderBy(x => x.Code).Select(x => new { x.Code, x.IsActive, x.DownloadA, x.DownloadB, x.DownloadC, Version = Convert.ToBase64String(x.RowVersion) }));
    private static RateValue Value(ExchangeRate row) => new(row.TableNumber, row.EffectiveDate, row.TradingDate, row.Mid, row.Bid, row.Ask);
    private static void Apply(ExchangeRate row, RateValue value, DateTimeOffset at)
    {
        row.TableNumber = value.TableNumber; row.EffectiveDate = value.EffectiveDate; row.TradingDate = value.TradingDate;
        row.Mid = value.Mid; row.Bid = value.Bid; row.Ask = value.Ask; row.RetrievedAtUtc = at;
    }
    public async Task<RateImportResponse> Import(Guid company, RateImportRequest value, CurrencyActor actor, INbpClient nbp, CancellationToken ct)
    {
        var hash = Hash(new { Kind = "Import", value });
        var previous = await Previous(actor, value.OperationId, hash);
        if (previous != null) return JsonSerializer.Deserialize<RateImportResponse>(previous.DetailsJson, Json)! with { AuditId = previous.Id };
        if (value.From.HasValue != value.To.HasValue || value.To > DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime))
            throw Error(400, "currency_dates", "Podaj obie daty publikacji, bez dat przyszłych.");
        (DateOnly? From, DateOnly? To)[] periods;
        try { periods = value.From == null ? [(null, null)] : NbpDates.Split(value.From.Value, value.To!.Value).Select(x => ((DateOnly?)x.From, (DateOnly?)x.To)).ToArray(); }
        catch (ArgumentException e) { throw Error(400, "currency_dates", e.Message); }
        var currencies = await db.Currencies.AsNoTracking().ToArrayAsync(); var signature = Signature(currencies);
        var selected = currencies.Where(x => x.Downloads("A") || x.Downloads("B") || x.Downloads("C")).ToArray();
        if (selected.Length == 0) throw Error(400, "currency_configuration", "Włącz przynajmniej jedną walutę i tabelę NBP w ustawieniach firmy.");
        var tables = new List<NbpTable>(); var errors = new List<string>(); var notices = new List<string>();
        var receivedAt = new Dictionary<NbpTable, DateTimeOffset>();
        foreach (var type in new[] { "A", "B", "C" }.Where(t => selected.Any(x => x.Downloads(t))))
        {
            foreach (var period in periods)
            {
                var reply = await nbp.Fetch(type, period.From, period.To, ct);
                if (reply.Error != null) errors.Add(reply.Error);
                else if (reply.Tables.Length == 0) notices.Add($"Brak publikacji {type}: {(period.From == null ? "najnowsza tabela" : $"{period.From:dd.MM.yyyy}–{period.To:dd.MM.yyyy}")}. To nie jest awaria.");
                if (reply.Tables.Any(x => x.EffectiveDate > DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime)))
                    errors.Add("NBP zwrócił przyszłą datę publikacji. Nie zapisano tej partii.");
                else
                {
                    var received = clock.GetUtcNow();
                    tables.AddRange(reply.Tables);
                    foreach (var table in reply.Tables) receivedAt[table] = received;
                }
                if (ct.IsCancellationRequested) break;
            }
            if (ct.IsCancellationRequested) break;
        }
        await using var transaction = await db.Database.BeginTransactionAsync(); await Lock();
        previous = await Previous(actor, value.OperationId, hash);
        if (previous != null) return JsonSerializer.Deserialize<RateImportResponse>(previous.DetailsJson, Json)! with { AuditId = previous.Id };
        var currentCurrencies = await db.Currencies.AsNoTracking().ToArrayAsync();
        var added = 0; var skipped = 0; var conflicts = new List<RateConflict>();
        if (Signature(currentCurrencies) != signature) errors.Add("Konfiguracja zmieniła się podczas pobierania. Nie zapisano kursów; odśwież ustawienia i ponów świadomie.");
        else
        {
            var missing = new HashSet<string>();
            foreach (var table in tables)
            {
                var forTable = selected.Where(x => x.Downloads(table.Type)).ToArray();
                var codes = forTable.Select(x => x.Code).ToArray();
                var existing = await db.ExchangeRates.Where(x => x.Source == "NBP" && x.TableType == table.Type && x.EffectiveDate == table.EffectiveDate && codes.Contains(x.CurrencyCode)).ToDictionaryAsync(x => x.CurrencyCode);
                foreach (var currency in forTable)
                {
                    var rate = table.Rates.SingleOrDefault(x => x.Code == currency.Code);
                    if (rate == null) { missing.Add($"Waluta {currency.Code} nie występuje w części lub wszystkich pobranych tabelach {table.Type}; nie utworzono fikcyjnego kursu."); continue; }
                    var incoming = new RateValue(table.Number, table.EffectiveDate, table.TradingDate, rate.Mid, rate.Bid, rate.Ask);
                    if (existing.TryGetValue(currency.Code, out var old))
                    {
                        if (Value(old) == incoming) skipped++;
                        else conflicts.Add(new(old.Id, currency.Code, table.Type, Convert.ToBase64String(old.RowVersion), Value(old), incoming, receivedAt[table]));
                    }
                    else
                    {
                        var row = new ExchangeRate { CurrencyCode = currency.Code, TableType = table.Type };
                        Apply(row, incoming, receivedAt[table]); db.ExchangeRates.Add(row); added++;
                    }
                }
            }
            notices.AddRange(missing.Order());
        }
        DateOnly? Last(string type) => tables.Where(x => x.Type == type).Select(x => (DateOnly?)x.EffectiveDate).Max();
        var result = new RateImportResponse(0, company, tables.Count, added, skipped, conflicts.ToArray(), errors.ToArray(), notices.ToArray(),
            value.From, value.To, Last("A"), Last("B"), Last("C"),
            selected.SelectMany(x => new[] { "A", "B", "C" }.Where(x.Downloads).Select(t => x.Code + "/" + t)).Order().ToArray());
        var audit = Audit(actor, value.OperationId, hash, "Import", result);
        await db.SaveChangesAsync(); await transaction.CommitAsync(); return result with { AuditId = audit.Id };
    }
    public async Task<CurrencyHistoryResponse> Resolve(Guid company, long auditId, RateResolutionRequest value, CurrencyActor actor)
    {
        if (!value.Confirm) throw Error(400, "currency_confirmation", "Potwierdź świadomie sposób rozstrzygnięcia konfliktu.");
        var hash = Hash(new { Kind = "ResolveConflict", auditId, value });
        await using var transaction = await db.Database.BeginTransactionAsync(); await Lock();
        var previous = await Previous(actor, value.OperationId, hash);
        if (previous != null) return History(previous, company);
        var import = await db.CurrencyOperations.AsNoTracking().SingleOrDefaultAsync(x => x.Id == auditId && x.Kind == "Import") ?? throw Error(404, "currency_import_missing", "Nie znaleziono importu w tej firmie.");
        var result = JsonSerializer.Deserialize<RateImportResponse>(import.DetailsJson, Json)!;
        if (value.ConflictIndex < 0 || value.ConflictIndex >= result.Conflicts.Length) throw Error(400, "currency_conflict_missing", "Nie znaleziono konfliktu.");
        var conflict = result.Conflicts[value.ConflictIndex];
        var row = await db.ExchangeRates.SingleOrDefaultAsync(x => x.Id == conflict.RateId) ?? throw Error(404, "currency_rate_missing", "Nie znaleziono kursu.");
        if (Convert.ToBase64String(row.RowVersion) != conflict.RowVersion) throw Error(409, "currency_stale", "Kurs zmienił się od wykrycia konfliktu. Pobierz i porównaj ponownie.");
        var before = Value(row);
        if (value.AcceptIncoming) Apply(row, conflict.Incoming, conflict.RetrievedAtUtc ?? import.OccurredAtUtc);
        // A keep-existing decision also advances the version, preventing a stale proposal being accepted afterwards.
        db.Entry(row).Property(x => x.RetrievedAtUtc).IsModified = true;
        var audit = Audit(actor, value.OperationId, hash, "ResolveConflict", new
        {
            ImportId = auditId,
            value.ConflictIndex,
            conflict.CurrencyCode,
            conflict.TableType,
            Decision = value.AcceptIncoming ? "Przyjęto dane NBP" : "Zachowano zapisany kurs",
            Before = before,
            After = Value(row)
        });
        await db.SaveChangesAsync(); await transaction.CommitAsync(); return History(audit, company);
    }
    public static CurrencyHistoryResponse History(CurrencyOperation row, Guid company) => new(row.Id, company, row.Kind, row.OccurredAtUtc, row.ActorName, row.DetailsJson);
}
