using System.Net;
using System.Text.Json;
using Vatowiec.Core.Application;
using Vatowiec.Core.Infrastructure.Persistence;
using Vatowiec.Core.Infrastructure.Registries;

namespace Vatowiec.Tests;

public sealed class NbpTests
{
    internal static string TableJson(string type, decimal mid = 4.123456789012m, DateOnly? date = null)
    {
        var day = date ?? new DateOnly(2026, 10, 21);
        object[] rates = type == "C" ? [new { code = "EUR", currency = "euro", bid = 4.100000000001m, ask = 4.200000000002m }] :
            type == "B" ? [new { code = "AFN", currency = "afgani", mid }] :
            [new { code = "EUR", currency = "euro", mid }, new { code = "USD", currency = "dolar", mid }, new { code = "GBP", currency = "funt", mid }, new { code = "CHF", currency = "frank", mid }];
        return JsonSerializer.Serialize(new[] { new { table = type, no = $"001/{type}/NBP/{day.Year}", effectiveDate = day.ToString("yyyy-MM-dd"),
            tradingDate = type == "C" ? day.AddDays(-1).ToString("yyyy-MM-dd") : null, rates } });
    }
    [Theory]
    [InlineData("A")]
    [InlineData("B")]
    [InlineData("C")]
    public async Task ParseTables_PreservesDecimalAndDistinctDates(string type)
    {
        var calls = 0;
        using var handler = new DataPortTests.Stub(request =>
        {
            calls++; Assert.Equal($"https://api.nbp.pl/api/exchangerates/tables/{type}/?format=json", request.RequestUri!.AbsoluteUri);
            Assert.Contains(request.Headers.Accept, x => x.MediaType == "application/json"); Assert.False(request.Headers.Contains("X-API-Key"));
            return new(HttpStatusCode.OK) { Content = new StringContent(TableJson(type)) };
        });
        var reply = await new NbpClient(new DataPortTests.FreshFactory(handler)).Fetch(type, null, null, default);
        Assert.Null(reply.Error); var table = Assert.Single(reply.Tables); Assert.Equal(new DateOnly(2026, 10, 21), table.EffectiveDate);
        if (type == "C")
        {
            Assert.Equal(new DateOnly(2026, 10, 20), table.TradingDate); Assert.Null(table.Rates[0].Mid);
            Assert.Equal(4.100000000001m, table.Rates[0].Bid); Assert.Equal(4.200000000002m, table.Rates[0].Ask);
        }
        else { Assert.Null(table.TradingDate); Assert.Equal(4.123456789012m, table.Rates[0].Mid); Assert.Null(table.Rates[0].Ask); }
        Assert.Equal(1, calls);
    }
    [Theory]
    [InlineData(404, false)]
    [InlineData(400, true)]
    [InlineData(429, true)]
    [InlineData(500, true)]
    [InlineData(302, true)]
    public async Task HttpStatus_NoPublicationIsDistinctFromFailureAndNoRetry(int status, bool error)
    {
        var count = 0; using var handler = new DataPortTests.Stub(_ => { count++; return new((HttpStatusCode)status); });
        var result = await new NbpClient(new DataPortTests.FreshFactory(handler)).Fetch("B", new(2026, 10, 18), new(2026, 10, 18), default);
        Assert.Equal(error, result.Error != null); Assert.Empty(result.Tables); Assert.Equal(1, count);
    }
    [Theory]
    [InlineData("[]")]
    [InlineData("not json")]
    [InlineData("{}")]
    public async Task InvalidJson_IsNotAnEmptySuccessfulLatestPublication(string json)
    {
        using var handler = new DataPortTests.Stub(_ => new(HttpStatusCode.OK) { Content = new StringContent(json) });
        Assert.NotNull((await new NbpClient(new DataPortTests.FreshFactory(handler)).Fetch("A", null, null, default)).Error);
    }
    [Fact]
    public async Task InvalidPrecisionTypeAndDateAreRejectedWithoutRounding()
    {
        foreach (var text in new[] { TableJson("A", 4.1234567890123m), TableJson("B"), TableJson("A").Replace("001/A/NBP/2026", "001/A/NBP/2025"),
            TableJson("A").Replace("4.123456789012", "1.00000000000000000000000000001") })
        {
            using var handler = new DataPortTests.Stub(_ => new(HttpStatusCode.OK) { Content = new StringContent(text) });
            var result = await new NbpClient(new DataPortTests.FreshFactory(handler)).Fetch("A", null, null, default);
            Assert.NotNull(result.Error); Assert.Empty(result.Tables);
        }
    }
    [Fact]
    public async Task CTradingDate_IsNullableWhenNotProvidedBySource()
    {
        var payload = TableJson("C").Replace("\"tradingDate\":\"2026-10-20\",", "");
        using var handler = new DataPortTests.Stub(_ => new(HttpStatusCode.OK) { Content = new StringContent(payload) });
        var result = await new NbpClient(new DataPortTests.FreshFactory(handler)).Fetch("C", null, null, default);
        Assert.Null(result.Error); Assert.Null(Assert.Single(result.Tables).TradingDate);
    }
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task TimeoutAndConnectionFailuresAreExplicit(bool timeout)
    {
        using var handler = new DataPortTests.Stub(_ => throw (timeout ? new TaskCanceledException() : new HttpRequestException()));
        Assert.NotNull((await new NbpClient(new DataPortTests.FreshFactory(handler)).Fetch("C", null, null, default)).Error);
    }
    [Fact]
    public void DateRanges_AreInclusiveNonOverlappingAndNeverExceed93Days()
    {
        var from = new DateOnly(2026, 1, 1); var to = from.AddDays(185);
        var parts = NbpDates.Split(from, to).ToArray(); Assert.Equal(2, parts.Length);
        Assert.Equal(from, parts[0].From); Assert.Equal(to, parts[^1].To);
        Assert.All(parts, x => Assert.Equal(92, x.To.DayNumber - x.From.DayNumber));
        Assert.Equal(parts[0].To.AddDays(1), parts[1].From);
        Assert.Equal(3, NbpDates.Split(from, to.AddDays(1)).Count());
        Assert.Throws<ArgumentException>(() => NbpDates.Split(to, from).ToArray());
    }
    [Fact]
    public void OfflineDictionary_IsBroadUniqueAndDefaultsNeverRequestPln()
    {
        var rows = CurrencySeed.Rows(); Assert.True(rows.Length >= 140); Assert.Equal(rows.Length, rows.Select(x => x.Code).Distinct().Count());
        Assert.Equal(new[] { "CHF", "EUR", "GBP", "USD" }, rows.Where(x => x.Downloads("A")).Select(x => x.Code).Order());
        Assert.All(rows, x => { Assert.False(x.DownloadB); Assert.False(x.DownloadC); });
        var pln = rows.Single(x => x.Code == "PLN"); Assert.True(pln.IsActive); Assert.False(pln.Downloads("A"));
        Assert.Contains(rows, x => x.Code == "XCG"); Assert.Contains(rows, x => x.Code == "AFN");
    }
}
