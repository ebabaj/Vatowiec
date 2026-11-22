using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Vatowiec.Core.Application;
using Vatowiec.Core.Domain;
using Vatowiec.Core.Infrastructure.Persistence;
using Vatowiec.Core.Infrastructure.Registries;

namespace Vatowiec.Core.Infrastructure.ExchangeRates;

public sealed record ExchangeRateRunResult(bool Ran, int Added, int Skipped, int Conflicts, string? ErrorCode)
{
    public static readonly ExchangeRateRunResult NotDue = new(false, 0, 0, 0, null);
}

// ADR-048: pobieranie kursów NBP do wspólnej bazy System. Wykonuje wyłącznie usługa VATowiec Operations.
// Wszystkie waluty tabel A, B i C; odcinki ≤ 93 dni zapisywane atomowo z punktem kontrolnym i historią.
// Kurs zapisany nigdy nie jest nadpisywany — rozbieżność trafia do ops.ExchangeRateConflicts.
public sealed class ExchangeRateSyncService(SystemDbContext db, INbpClient nbp, TimeProvider clock, ExchangeRateScheduleOptions options, TimeZoneInfo? zone = null)
{
    public const string ActorName = "VATowiec Operations (kursy NBP)";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly TimeZoneInfo zone = zone ?? ExchangeRateSchedule.Warsaw;

    public async Task<ExchangeRateRunResult> RunIfDue(CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var today = ExchangeRateSchedule.Today(now, zone);
        var state = await db.ExchangeRateSyncs.AsNoTracking().SingleOrDefaultAsync(ct);
        if (state == null) return ExchangeRateRunResult.NotDue; // brak migracji ADR-048
        var todayA = await db.ExchangeRates.AnyAsync(x => x.TableType == "A" && x.EffectiveDate == today, ct);
        var snapshot = new ExchangeRateSyncSnapshot(state.CheckedThroughA, state.CheckedThroughB, state.CheckedThroughC,
            state.RunRequestedAtUtc, state.LastAttemptUtc, state.LastErrorCode, todayA);
        return ExchangeRateSchedule.IsDue(snapshot, now, zone, options) ? await Run(ct) : ExchangeRateRunResult.NotDue;
    }

    public async Task<ExchangeRateRunResult> Run(CancellationToken ct)
    {
        var started = clock.GetUtcNow();
        var today = ExchangeRateSchedule.Today(started, zone);
        DateOnly historyStart;
        DateTimeOffset? requested;
        ExchangeRateSync start;
        db.ChangeTracker.Clear();
        await using (var transaction = await db.Database.BeginTransactionAsync(ct))
        {
            await Lock(ct);
            start = await db.ExchangeRateSyncs.SingleAsync(ct);
            start.HistoryStartDate ??= ExchangeRateSchedule.DefaultHistoryStart(today);
            start.LastAttemptUtc = started;
            historyStart = start.HistoryStartDate.Value;
            requested = start.RunRequestedAtUtc;
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }
        var trigger = requested != null ? "Manual" : "Schedule";
        var known = (await db.Currencies.AsNoTracking().Select(x => x.Code).ToArrayAsync(ct)).ToHashSet(StringComparer.Ordinal);
        int added = 0, skipped = 0, conflicts = 0;
        string? error = null;
        foreach (var table in ExchangeRateSchedule.Tables)
        {
            var (from, to) = ExchangeRateSchedule.Range(start.CheckedThrough(table), historyStart, today, options);
            foreach (var chunk in NbpDates.Chunks(from, to).Take(options.MaxChunksPerTable))
            {
                ct.ThrowIfCancellationRequested();
                // Awarie przejściowe NBP (np. fałszywe 404 HTML) ponawiamy krótko w ramach przebiegu;
                // „Brak danych” i niepoprawne dane — bez ponowień.
                NbpReply reply;
                var attempts = 0;
                while (true)
                {
                    attempts++;
                    reply = await nbp.Fetch(table, chunk.From, chunk.To, ct);
                    if (reply.Error == null || !reply.Transient || attempts >= Math.Max(1, options.AttemptsPerChunk)) break;
                    if (options.RetryDelaySeconds > 0) await Task.Delay(TimeSpan.FromSeconds(options.RetryDelaySeconds), ct);
                }
                var failure = reply.Error ?? (reply.Tables.Any(x => x.EffectiveDate > today) ? "NBP zwrócił przyszłą datę publikacji; nie zapisano tej partii." : null);
                if (failure != null)
                {
                    await Record(ExchangeRateOperationKinds.DownloadFailed, new { trigger, table, from = Date(chunk.From), to = Date(chunk.To), attempts, error = failure }, ct);
                    error = "nbp_unavailable";
                    break;
                }
                var saved = await Save(table, chunk, reply.Tables, known, historyStart, today, trigger, ct);
                if (saved == null) { error = "history_start_changed"; break; }
                added += saved.Value.Added; skipped += saved.Value.Skipped; conflicts += saved.Value.Conflicts;
            }
            if (error == "history_start_changed") break;
        }
        db.ChangeTracker.Clear();
        await using (var transaction = await db.Database.BeginTransactionAsync(ct))
        {
            await Lock(ct);
            var row = await db.ExchangeRateSyncs.SingleAsync(ct);
            row.LastErrorCode = error;
            if (error == null) row.LastSuccessUtc = clock.GetUtcNow();
            // Zlecenie jest zużyte niezależnie od wyniku (nieudana próba ma odstęp ponowień); nowsze zostaje.
            if (row.RunRequestedAtUtc != null && row.RunRequestedAtUtc <= (requested ?? started)) row.RunRequestedAtUtc = null;
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }
        return new(true, added, skipped, conflicts, error);
    }

    private async Task<(int Added, int Skipped, int Conflicts)?> Save(string table, (DateOnly From, DateOnly To) chunk, NbpTable[] tables,
        HashSet<string> known, DateOnly historyStart, DateOnly today, string trigger, CancellationToken ct)
    {
        db.ChangeTracker.Clear();
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await Lock(ct);
        var row = await db.ExchangeRateSyncs.SingleAsync(ct);
        // Administrator zmienił datę początkową w trakcie przebiegu: przerwij bez zapisu, następny przebieg zacznie od nowa.
        if (row.HistoryStartDate != historyStart) return null;
        var existing = await db.ExchangeRates
            .Where(x => x.Source == "NBP" && x.TableType == table && x.EffectiveDate >= chunk.From && x.EffectiveDate <= chunk.To)
            .ToDictionaryAsync(x => (x.CurrencyCode, x.EffectiveDate), ct);
        var knownConflicts = (await db.ExchangeRateConflicts
            .Where(c => db.ExchangeRates.Any(r => r.Id == c.RateId && r.TableType == table && r.EffectiveDate >= chunk.From && r.EffectiveDate <= chunk.To))
            .Select(c => new { c.RateId, c.IncomingHash }).ToArrayAsync(ct)).Select(x => (x.RateId, x.IncomingHash)).ToHashSet();
        var received = clock.GetUtcNow();
        int added = 0, skipped = 0, conflicts = 0;
        var unknown = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var published in tables)
        {
            foreach (var rate in published.Rates)
            {
                if (!known.Contains(rate.Code)) { unknown.Add(rate.Code); continue; }
                if (existing.TryGetValue((rate.Code, published.EffectiveDate), out var old))
                {
                    if (old.TableNumber == published.Number && old.TradingDate == published.TradingDate && old.Mid == rate.Mid && old.Bid == rate.Bid && old.Ask == rate.Ask)
                    { skipped++; continue; }
                    var hash = IncomingHash(published, rate);
                    if (!knownConflicts.Add((old.Id, hash))) { skipped++; continue; }
                    db.ExchangeRateConflicts.Add(new ExchangeRateConflict
                    {
                        RateId = old.Id,
                        IncomingHash = hash,
                        IncomingTableNumber = published.Number,
                        IncomingTradingDate = published.TradingDate,
                        IncomingMid = rate.Mid,
                        IncomingBid = rate.Bid,
                        IncomingAsk = rate.Ask,
                        DetectedAtUtc = received
                    });
                    conflicts++;
                    continue;
                }
                db.ExchangeRates.Add(new ExchangeRate
                {
                    CurrencyCode = rate.Code,
                    TableType = table,
                    TableNumber = published.Number,
                    EffectiveDate = published.EffectiveDate,
                    TradingDate = published.TradingDate,
                    Mid = rate.Mid,
                    Bid = rate.Bid,
                    Ask = rate.Ask,
                    RetrievedAtUtc = received
                });
                added++;
            }
        }
        row.SetCheckedThrough(table, ExchangeRateSchedule.Advance(row.CheckedThrough(table), chunk.To, today));
        // Historia odnotowuje odcinki, które coś zmieniły; same powtórzenia zakładki tylko przesuwają punkt kontrolny.
        if (added > 0 || conflicts > 0 || unknown.Count > 0)
            Add(ExchangeRateOperationKinds.Download, new
            {
                trigger,
                table,
                from = Date(chunk.From),
                to = Date(chunk.To),
                tables = tables.Length,
                added,
                skipped,
                conflicts,
                unknownCodes = unknown.ToArray()
            });
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return (added, skipped, conflicts);
    }

    private async Task Record(string kind, object details, CancellationToken ct)
    {
        db.ChangeTracker.Clear();
        Add(kind, details);
        await db.SaveChangesAsync(ct);
    }

    private void Add(string kind, object details) => db.ExchangeRateOperations.Add(new ExchangeRateOperation
    {
        Kind = kind,
        AtUtc = clock.GetUtcNow(),
        ActorName = ActorName,
        DetailsJson = JsonSerializer.Serialize(details, Json)
    });

    internal static async Task Lock(SystemDbContext db, CancellationToken ct) =>
        await db.Database.ExecuteSqlRawAsync("DECLARE @r int; EXEC @r=sys.sp_getapplock @Resource=N'ExchangeRateSync', @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=15000; IF @r<0 THROW 51000, 'Exchange rate lock unavailable', 1;", ct);

    private Task Lock(CancellationToken ct) => Lock(db, ct);

    private static string Date(DateOnly value) => value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string Number(decimal? value) => value?.ToString("0.############", CultureInfo.InvariantCulture) ?? "-";

    public static string IncomingHash(NbpTable table, NbpRate rate) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
        string.Join('|', table.Number, table.EffectiveDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            table.TradingDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "-", Number(rate.Mid), Number(rate.Bid), Number(rate.Ask)))));
}
