using Vatowiec.Core.Application;

namespace Vatowiec.Tests;

// ADR-048: reguły harmonogramu pobierania kursów NBP przez usługę Operations (czas polski, bez SQL/HTTP).
public sealed class ExchangeRateScheduleTests
{
    private static readonly ExchangeRateScheduleOptions Options = new();
    private static readonly TimeZoneInfo Zone = ExchangeRateSchedule.Warsaw;

    // Czas polski → UTC z uwzględnieniem czasu letniego.
    private static DateTimeOffset Warsaw(int year, int month, int day, int hour, int minute = 0) =>
        new(TimeZoneInfo.ConvertTimeToUtc(new DateTime(year, month, day, hour, minute, 0), Zone), TimeSpan.Zero);

    private static ExchangeRateSyncSnapshot Done(DateOnly checkedThrough, DateTimeOffset? lastAttempt = null, string? error = null,
        bool todayA = false, DateTimeOffset? requested = null) =>
        new(checkedThrough, checkedThrough, checkedThrough, requested, lastAttempt, error, todayA);

    [Fact]
    public void DailyRun_StartsAt13RetriesHourlyUntil16AndStopsWhenTableAArrives()
    {
        var yesterday = new DateOnly(2026, 11, 17);
        var lastNight = Warsaw(2026, 11, 17, 13, 5);
        Assert.False(ExchangeRateSchedule.IsDue(Done(yesterday.AddDays(-1), lastNight), Warsaw(2026, 11, 18, 12, 59), Zone, Options));
        Assert.True(ExchangeRateSchedule.IsDue(Done(yesterday.AddDays(-1), lastNight), Warsaw(2026, 11, 18, 13), Zone, Options));
        var attempt = Warsaw(2026, 11, 18, 13, 5);
        Assert.False(ExchangeRateSchedule.IsDue(Done(yesterday, attempt), Warsaw(2026, 11, 18, 13, 45), Zone, Options));
        Assert.True(ExchangeRateSchedule.IsDue(Done(yesterday, attempt), Warsaw(2026, 11, 18, 14, 5), Zone, Options));
        Assert.True(ExchangeRateSchedule.IsDue(Done(yesterday, Warsaw(2026, 11, 18, 15, 10)), Warsaw(2026, 11, 18, 16, 10), Zone, Options));
        Assert.False(ExchangeRateSchedule.IsDue(Done(yesterday, Warsaw(2026, 11, 18, 16, 10)), Warsaw(2026, 11, 18, 17, 20), Zone, Options));
        Assert.False(ExchangeRateSchedule.IsDue(Done(yesterday, attempt, todayA: true), Warsaw(2026, 11, 18, 15), Zone, Options));
    }

    [Fact]
    public void LateServiceStart_TriesOnceAfterWindowAndWeekendStopsWithoutCalendar()
    {
        var friday = new DateOnly(2026, 11, 20);
        // Usługa uruchomiona o 19:00 bez próby w oknie dnia — jedna próba; kolejnej tego dnia już nie ma.
        Assert.True(ExchangeRateSchedule.IsDue(Done(friday.AddDays(-1), Warsaw(2026, 11, 19, 13, 2)), Warsaw(2026, 11, 20, 19), Zone, Options));
        Assert.False(ExchangeRateSchedule.IsDue(Done(friday.AddDays(-1), Warsaw(2026, 11, 20, 19)), Warsaw(2026, 11, 20, 20, 30), Zone, Options));
        // Sobota: NBP nie publikuje A; próby do 16:00, potem koniec. Niedziela rano: brak zaległości.
        var saturday = Warsaw(2026, 11, 21, 16, 5);
        Assert.False(ExchangeRateSchedule.IsDue(Done(friday, saturday), Warsaw(2026, 11, 21, 17, 30), Zone, Options));
        Assert.False(ExchangeRateSchedule.IsDue(Done(friday, saturday), Warsaw(2026, 11, 22, 8), Zone, Options));
    }

    [Fact]
    public void Backlog_RunsImmediatelyAnyHourButFailureWaitsAnHourUnlessRequested()
    {
        var now = Warsaw(2026, 11, 23, 3);
        Assert.True(ExchangeRateSchedule.IsDue(new(null, null, null, null, null, null, false), now, Zone, Options));
        Assert.True(ExchangeRateSchedule.IsDue(Done(new(2026, 11, 20), Warsaw(2026, 11, 20, 13)), now, Zone, Options));
        // Punkt kontrolny = przedwczoraj: wczorajszy przebieg dzienny się odbył, przed 13:00 nic nie robimy.
        Assert.False(ExchangeRateSchedule.IsDue(Done(new(2026, 11, 21), Warsaw(2026, 11, 22, 13)), now, Zone, Options));
        var failed = Done(new(2026, 11, 20), now.AddMinutes(-30), "nbp_unavailable");
        Assert.False(ExchangeRateSchedule.IsDue(failed, now, Zone, Options));
        Assert.True(ExchangeRateSchedule.IsDue(failed, now.AddMinutes(31), Zone, Options));
        Assert.True(ExchangeRateSchedule.IsDue(failed with { RunRequestedAtUtc = now }, now, Zone, Options));
        Assert.True(ExchangeRateSchedule.IsDue(Done(new(2026, 11, 22), now, todayA: true, requested: now), now, Zone, Options));
    }

    [Fact]
    public void DaylightSavingTime_UsesPolishWallClockNotServerOrUtc()
    {
        // Lato (CEST, UTC+2): 13:00 w Warszawie = 11:00 UTC; zima (CET, UTC+1) = 12:00 UTC.
        var summer = Done(new(2026, 6, 30), Warsaw(2026, 6, 30, 13));
        Assert.False(ExchangeRateSchedule.IsDue(summer, new(2026, 7, 1, 10, 59, 0, TimeSpan.Zero), Zone, Options));
        Assert.True(ExchangeRateSchedule.IsDue(summer, new(2026, 7, 1, 11, 0, 0, TimeSpan.Zero), Zone, Options));
        // Dzień zmiany czasu (25.10.2026) i dzień po nim.
        var autumn = Done(new(2026, 10, 25), Warsaw(2026, 10, 25, 13));
        Assert.False(ExchangeRateSchedule.IsDue(autumn, new(2026, 10, 26, 11, 30, 0, TimeSpan.Zero), Zone, Options));
        Assert.True(ExchangeRateSchedule.IsDue(autumn, new(2026, 10, 26, 12, 0, 0, TimeSpan.Zero), Zone, Options));
        Assert.Equal(new DateOnly(2026, 3, 29), ExchangeRateSchedule.Today(new(2026, 3, 28, 23, 30, 0, TimeSpan.Zero), Zone));
    }

    [Fact]
    public void RangeOverlapAndCheckpointNeverCloseTodayOrMoveBackwards()
    {
        var today = new DateOnly(2026, 11, 18);
        Assert.Equal(new DateOnly(2025, 1, 1), ExchangeRateSchedule.DefaultHistoryStart(today));
        Assert.Equal((new DateOnly(2025, 1, 1), today), ExchangeRateSchedule.Range(null, new(2025, 1, 1), today, Options));
        Assert.Equal((new DateOnly(2026, 11, 10), today), ExchangeRateSchedule.Range(new(2026, 11, 17), new(2025, 1, 1), today, Options));
        Assert.Equal((new DateOnly(2026, 11, 15), today), ExchangeRateSchedule.Range(new(2026, 11, 17), new(2026, 11, 15), today, Options));
        Assert.Equal((NbpDates.ArchiveStart, today), ExchangeRateSchedule.Range(null, new(2001, 1, 1), today, Options));
        Assert.Equal(new DateOnly(2026, 11, 17), ExchangeRateSchedule.Advance(null, today, today));
        Assert.Equal(new DateOnly(2025, 4, 3), ExchangeRateSchedule.Advance(null, new(2025, 4, 3), today));
        Assert.Equal(new DateOnly(2026, 11, 17), ExchangeRateSchedule.Advance(new(2026, 11, 17), new(2026, 11, 12), today));
    }

    [Fact]
    public void Chunks_CoverWholeHistoryWithoutTenYearLimitAndNeverExceed93Days()
    {
        var parts = NbpDates.Chunks(NbpDates.ArchiveStart, new DateOnly(2026, 11, 18)).ToArray();
        Assert.Equal(NbpDates.ArchiveStart, parts[0].From); Assert.Equal(new DateOnly(2026, 11, 18), parts[^1].To);
        Assert.All(parts, x => Assert.InRange(x.To.DayNumber - x.From.DayNumber, 0, 92));
        Assert.All(parts.Skip(1).Zip(parts), x => Assert.Equal(x.Second.To.AddDays(1), x.First.From));
        Assert.Throws<ArgumentException>(() => NbpDates.Chunks(new(2001, 12, 31), new(2002, 1, 5)).ToArray());
        Assert.Throws<ArgumentException>(() => NbpDates.Split(NbpDates.ArchiveStart, new DateOnly(2026, 1, 1)).ToArray());
    }
}
