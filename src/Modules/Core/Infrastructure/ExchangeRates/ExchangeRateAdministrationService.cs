using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Vatowiec.Core.Application;
using Vatowiec.Core.Domain;
using Vatowiec.Core.Infrastructure.Operations;
using Vatowiec.Core.Infrastructure.Persistence;
using Vatowiec.SharedContracts;

namespace Vatowiec.Core.Infrastructure.ExchangeRates;

// ADR-048: Ustawienia instalacji → Kursy NBP. Wywołuje API po świeżej kontroli roli administratora instalacji.
// API nie łączy się z NBP: „Pobierz teraz” zapisuje zlecenie, które wykonuje usługa Operations.
public sealed class ExchangeRateAdministrationService(SystemDbContext db, TimeProvider clock)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static CurrencyOperationException Error(int status, string code, string message) => new(status, code, message);
    private DateOnly Today => ExchangeRateSchedule.Today(clock.GetUtcNow(), ExchangeRateSchedule.Warsaw);

    public static async Task<ExchangeRateSyncStatus> Status(SystemDbContext db, CancellationToken ct)
    {
        var row = await db.ExchangeRateSyncs.AsNoTracking().SingleAsync(ct);
        var last = await db.ExchangeRates.AsNoTracking().GroupBy(x => x.TableType)
            .Select(x => new { Type = x.Key, Date = x.Max(r => r.EffectiveDate) }).ToArrayAsync(ct);
        DateOnly? Last(string type) => last.Where(x => x.Type == type).Select(x => (DateOnly?)x.Date).SingleOrDefault();
        return new(row.LastAttemptUtc, row.LastSuccessUtc, row.LastErrorCode, Last("A"), Last("B"), Last("C"), row.RunRequestedAtUtc != null);
    }

    public async Task<ExchangeRateAdministrationResponse> Read(CancellationToken ct)
    {
        var row = await db.ExchangeRateSyncs.AsNoTracking().SingleAsync(ct);
        var conflicts = await (from conflict in db.ExchangeRateConflicts.AsNoTracking()
                               join rate in db.ExchangeRates.AsNoTracking() on conflict.RateId equals rate.Id
                               where conflict.ResolvedAtUtc == null
                               orderby rate.EffectiveDate descending, rate.CurrencyCode, conflict.Id
                               select new { conflict, rate }).Take(200).ToArrayAsync(ct);
        return new(row.HistoryStartDate ?? ExchangeRateSchedule.DefaultHistoryStart(Today), row.HistoryStartDate == null, await Status(db, ct),
            conflicts.Select(x => Conflict(x.conflict, x.rate)).ToArray(), Convert.ToBase64String(row.RowVersion));
    }

    private static ExchangeRateConflictResponse Conflict(ExchangeRateConflict conflict, ExchangeRate rate) => new(conflict.Id, rate.CurrencyCode, rate.TableType,
        new(rate.TableNumber, rate.EffectiveDate, rate.TradingDate, rate.Mid, rate.Bid, rate.Ask),
        new(conflict.IncomingTableNumber, rate.EffectiveDate, conflict.IncomingTradingDate, conflict.IncomingMid, conflict.IncomingBid, conflict.IncomingAsk),
        conflict.DetectedAtUtc, Convert.ToBase64String(conflict.RowVersion), Convert.ToBase64String(rate.RowVersion));

    public async Task<ExchangeRateAdministrationResponse> RequestRun(OperationRequester requester, CancellationToken ct)
    {
        await using (var transaction = await db.Database.BeginTransactionAsync(ct))
        {
            await ExchangeRateSyncService.Lock(db, ct);
            var row = await db.ExchangeRateSyncs.SingleAsync(ct);
            if (row.RunRequestedAtUtc == null)
            {
                row.RunRequestedAtUtc = clock.GetUtcNow();
                Audit(requester, ExchangeRateOperationKinds.RunRequested, new { requestedAtUtc = row.RunRequestedAtUtc });
                await db.SaveChangesAsync(ct);
            }
            await transaction.CommitAsync(ct);
        }
        db.ChangeTracker.Clear();
        return await Read(ct);
    }

    public async Task<ExchangeRateAdministrationResponse> SetHistoryStart(OperationRequester requester, ExchangeRateHistoryStartRequest value, CancellationToken ct)
    {
        var today = Today;
        if (value.HistoryStartDate < NbpDates.ArchiveStart || value.HistoryStartDate > today)
            throw Error(400, "rates_history_start", "Data początkowa historii: od 02.01.2002 do dziś (archiwum NBP).");
        await using (var transaction = await db.Database.BeginTransactionAsync(ct))
        {
            await ExchangeRateSyncService.Lock(db, ct);
            var row = await db.ExchangeRateSyncs.SingleAsync(ct);
            if (Convert.ToBase64String(row.RowVersion) != value.RowVersion)
                throw Error(409, "rates_settings_stale", "Ustawienia kursów zmieniły się (np. usługa Operations właśnie pobierała kursy). Odśwież i powtórz.");
            var before = row.HistoryStartDate;
            var effective = before ?? ExchangeRateSchedule.DefaultHistoryStart(today);
            if (before != value.HistoryStartDate)
            {
                // Wcześniejsza data: usługa pobierze historię od nowa (istniejące kursy są pomijane jako identyczne).
                if (value.HistoryStartDate < effective) row.CheckedThroughA = row.CheckedThroughB = row.CheckedThroughC = null;
                row.HistoryStartDate = value.HistoryStartDate;
                Audit(requester, ExchangeRateOperationKinds.HistoryStartChanged, new { before = before?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), after = value.HistoryStartDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), refetch = value.HistoryStartDate < effective });
                await db.SaveChangesAsync(ct);
            }
            await transaction.CommitAsync(ct);
        }
        db.ChangeTracker.Clear();
        return await Read(ct);
    }

    public async Task<ExchangeRateAdministrationResponse> Resolve(OperationRequester requester, long conflictId, ExchangeRateConflictResolution value, CancellationToken ct)
    {
        if (!value.Confirm) throw Error(400, "currency_confirmation", "Potwierdź świadomie sposób rozstrzygnięcia konfliktu.");
        await using (var transaction = await db.Database.BeginTransactionAsync(ct))
        {
            await ExchangeRateSyncService.Lock(db, ct);
            var conflict = await db.ExchangeRateConflicts.SingleOrDefaultAsync(x => x.Id == conflictId, ct)
                ?? throw Error(404, "currency_conflict_missing", "Nie znaleziono konfliktu.");
            if (conflict.ResolvedAtUtc != null) throw Error(409, "currency_conflict_resolved", "Konflikt został już rozstrzygnięty.");
            var rate = await db.ExchangeRates.SingleAsync(x => x.Id == conflict.RateId, ct);
            if (Convert.ToBase64String(conflict.RowVersion) != value.RowVersion || Convert.ToBase64String(rate.RowVersion) != value.RateRowVersion)
                throw Error(409, "currency_stale", "Kurs lub konflikt zmienił się. Odśwież i porównaj ponownie.");
            var before = new RateValue(rate.TableNumber, rate.EffectiveDate, rate.TradingDate, rate.Mid, rate.Bid, rate.Ask);
            if (value.AcceptIncoming)
            {
                rate.TableNumber = conflict.IncomingTableNumber; rate.TradingDate = conflict.IncomingTradingDate;
                rate.Mid = conflict.IncomingMid; rate.Bid = conflict.IncomingBid; rate.Ask = conflict.IncomingAsk;
                rate.RetrievedAtUtc = conflict.DetectedAtUtc;
            }
            // Także „zachowaj” zmienia wersję kursu: inne propozycje dla tego kursu wymagają ponownego porównania.
            db.Entry(rate).Property(x => x.RetrievedAtUtc).IsModified = true;
            conflict.ResolvedAtUtc = clock.GetUtcNow();
            conflict.Decision = value.AcceptIncoming ? "AcceptIncoming" : "KeepExisting";
            conflict.ResolvedById = requester.UserId; conflict.ResolvedByName = requester.Name;
            Audit(requester, ExchangeRateOperationKinds.ResolveConflict, new
            {
                conflictId,
                currencyCode = rate.CurrencyCode,
                tableType = rate.TableType,
                effectiveDate = rate.EffectiveDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                decision = conflict.Decision,
                before,
                after = new RateValue(rate.TableNumber, rate.EffectiveDate, rate.TradingDate, rate.Mid, rate.Bid, rate.Ask)
            });
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }
        db.ChangeTracker.Clear();
        return await Read(ct);
    }

    public async Task<ExchangeRateOperationResponse[]> History(int offset, CancellationToken ct)
    {
        if (offset is < 0 or > 1_000_000) throw Error(400, "currency_filter", "Nieprawidłowe stronicowanie.");
        var rows = await db.ExchangeRateOperations.AsNoTracking().OrderByDescending(x => x.Id).Skip(offset).Take(51).ToArrayAsync(ct);
        return rows.Select(x => new ExchangeRateOperationResponse(x.Id, x.Kind, x.AtUtc, x.ActorName, x.DetailsJson)).ToArray();
    }

    private void Audit(OperationRequester requester, string kind, object details) => db.ExchangeRateOperations.Add(new ExchangeRateOperation
    {
        Kind = kind,
        AtUtc = clock.GetUtcNow(),
        ActorId = requester.UserId,
        ActorName = string.IsNullOrWhiteSpace(requester.Name) ? requester.UserId.ToString("D") : requester.Name,
        DetailsJson = JsonSerializer.Serialize(details, Json)
    });
}
