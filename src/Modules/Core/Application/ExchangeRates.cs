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
    public static IEnumerable<(DateOnly From, DateOnly To)> Split(DateOnly from, DateOnly to)
    {
        if (from < new DateOnly(2002, 1, 2) || to < from || to.DayNumber - from.DayNumber > 3660)
            throw new ArgumentException("Zakres: od 2002-01-02, od ≤ do, najwyżej 10 lat na operację.");
        while (from <= to)
        {
            var end = DateOnly.FromDayNumber(Math.Min(to.DayNumber, from.DayNumber + 92));
            yield return (from, end);
            if (end == to) yield break;
            from = end.AddDays(1);
        }
    }
}
