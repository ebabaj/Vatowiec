namespace Vatowiec.Core.Application;

public sealed record PublishedRate(long Id, string CurrencyCode, string Source, string TableType,
    string TableNumber, DateOnly EffectiveDate, DateOnly? TradingDate, decimal? Mid, decimal? Bid, decimal? Ask);
public interface IExchangeRateReader
{
    Task<PublishedRate?> OnDate(string code, string table, DateOnly date, CancellationToken cancellationToken = default);
    Task<PublishedRate?> Before(string code, string table, DateOnly date, CancellationToken cancellationToken = default);
}

public static class NbpDates
{
    // Archiwum kursów walut NBP Web API (api.nbp.pl, sprawdzone 2026-11-22).
    public static readonly DateOnly ArchiveStart = new(2002, 1, 2);
    // Pojedyncze zapytanie NBP obejmuje najwyżej 93 dni (włącznie).
    public const int MaxRangeDays = 93;

    public static IEnumerable<(DateOnly From, DateOnly To)> Split(DateOnly from, DateOnly to)
    {
        if (to.DayNumber - from.DayNumber > 3660)
            throw new ArgumentException("Zakres: od 2002-01-02, od ≤ do, najwyżej 10 lat na operację.");
        return Chunks(from, to);
    }

    // Rozłączne odcinki po najwyżej 93 dni włącznie, od najstarszego.
    public static IEnumerable<(DateOnly From, DateOnly To)> Chunks(DateOnly from, DateOnly to)
    {
        if (from < ArchiveStart || to < from)
            throw new ArgumentException("Zakres: od 2002-01-02, od ≤ do.");
        while (from <= to)
        {
            var end = DateOnly.FromDayNumber(Math.Min(to.DayNumber, from.DayNumber + MaxRangeDays - 1));
            yield return (from, end);
            if (end == to) yield break;
            from = end.AddDays(1);
        }
    }
}

// ADR-048: reguły harmonogramu pobierania kursów NBP przez usługę Operations. Czysta logika (bez SQL/HTTP),
// testowana z kontrolowanym zegarem. Czas polski niezależnie od strefy serwera.
// AttemptsPerChunk/RetryDelaySeconds: krótkie ponowienia awarii przejściowych NBP w ramach przebiegu (decyzja właściciela 2026-11-22).
public sealed record ExchangeRateScheduleOptions(int DailyHour = 13, int LastHour = 16, int RetryMinutes = 60, int OverlapDays = 7, int MaxChunksPerTable = 40,
    int AttemptsPerChunk = 3, int RetryDelaySeconds = 5);

public sealed record ExchangeRateSyncSnapshot(DateOnly? CheckedThroughA, DateOnly? CheckedThroughB, DateOnly? CheckedThroughC,
    DateTimeOffset? RunRequestedAtUtc, DateTimeOffset? LastAttemptUtc, string? LastErrorCode, bool TodayTableAReceived);

public static class ExchangeRateSchedule
{
    public static readonly string[] Tables = ["A", "B", "C"];

    public static TimeZoneInfo Warsaw { get; } = FindWarsaw();

    private static TimeZoneInfo FindWarsaw()
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById("Europe/Warsaw"); }
        catch (TimeZoneNotFoundException) { return TimeZoneInfo.FindSystemTimeZoneById("Central European Standard Time"); }
    }

    public static DateOnly Today(DateTimeOffset now, TimeZoneInfo zone) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, zone).DateTime);

    // Domyślna data początkowa historii: 1 stycznia poprzedniego roku (decyzja właściciela 2026-11-22).
    public static DateOnly DefaultHistoryStart(DateOnly today) => new(today.Year - 1, 1, 1);

    // Przebieg należny: ręczne zlecenie; zaległość (punkt kontrolny starszy niż przedwczoraj, tzn. wczorajszy
    // przebieg dzienny się nie odbył); albo przebieg dzienny od DailyHour, dopóki brak dzisiejszej tabeli A —
    // ponowienia co RetryMinutes do LastHour; po LastHour jedna próba, jeśli dziś w oknie jeszcze nie próbowano.
    // Po nieudanej próbie kolejna najwcześniej po RetryMinutes (poza ręcznym zleceniem).
    public static bool IsDue(ExchangeRateSyncSnapshot state, DateTimeOffset now, TimeZoneInfo zone, ExchangeRateScheduleOptions options)
    {
        if (state.RunRequestedAtUtc != null) return true;
        var retry = TimeSpan.FromMinutes(options.RetryMinutes);
        if (state.LastErrorCode != null && state.LastAttemptUtc is { } failed && now - failed < retry) return false;
        var local = TimeZoneInfo.ConvertTime(now, zone);
        var today = DateOnly.FromDateTime(local.DateTime);
        var checkpoints = new[] { state.CheckedThroughA, state.CheckedThroughB, state.CheckedThroughC };
        if (checkpoints.Any(x => x == null || x < today.AddDays(-2))) return true;
        if (local.Hour < options.DailyHour || state.TodayTableAReceived) return false;
        var windowStart = TimeZoneInfo.ConvertTimeToUtc(today.ToDateTime(new TimeOnly(options.DailyHour, 0)), zone);
        if (state.LastAttemptUtc == null || state.LastAttemptUtc < windowStart) return true;
        return local.Hour <= options.LastHour && now - state.LastAttemptUtc >= retry;
    }

    // Zakres zapytania tabeli: od punktu kontrolnego minus zakładka (lub od daty początkowej) do dziś.
    public static (DateOnly From, DateOnly To) Range(DateOnly? checkedThrough, DateOnly historyStart, DateOnly today, ExchangeRateScheduleOptions options)
    {
        var from = checkedThrough is { } done ? done.AddDays(-options.OverlapDays) : historyStart;
        if (from < historyStart) from = historyStart;
        if (from < NbpDates.ArchiveStart) from = NbpDates.ArchiveStart;
        return (from, today);
    }

    // Nowy punkt kontrolny po zapisanym odcinku: do końca odcinka, nigdy dalej niż wczoraj i nigdy wstecz.
    public static DateOnly? Advance(DateOnly? current, DateOnly chunkEnd, DateOnly today)
    {
        var closed = chunkEnd < today ? chunkEnd : today.AddDays(-1);
        return current is { } old && old >= closed ? old : closed;
    }
}
