using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using Vatowiec.Core.Application;
using Vatowiec.Core.Infrastructure;
using Vatowiec.Core.Infrastructure.Registries;

namespace Vatowiec.Tests;

internal sealed class EwibFixture
{
    public string Body { get; set; } = "{\"listaWlascicieli\":[]}";
    public ConcurrentQueue<string> Requests { get; } = new();
}
internal sealed class EwibStub(EwibFixture fixture) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        fixture.Requests.Enqueue(request.RequestUri!.AbsoluteUri);
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(fixture.Body) });
    }
}

public sealed class EwibTests
{
    internal static string Reply(string clearing = "11402105", params string[] bics) => JsonSerializer.Serialize(new
    {
        listaWlascicieli = new[] { new { numer = "114", nazwa = "Bank demonstracyjny EWIB", siedziba = new
        { numeryRozliczeniowe = new[] { new { numer = clearing, numeryBic = bics.Select(x => new { numer = x, nazwa = "BIC" }) } } } } }
    });
    [Theory]
    [InlineData("39-1140-2105-0000-5616-4000-1001", "39114021050000561640001001")]
    [InlineData("39 1140 2105 0000 5616 4000 1001", "39114021050000561640001001")]
    [InlineData("39114021050000561640001001", "39114021050000561640001001")]
    [InlineData("PL39 1140 2105 0000 5616 4000 1001", "PL39114021050000561640001001")]
    [InlineData("PL39-1140-2105-0000-5616-4000-1001", "PL39114021050000561640001001")]
    public void UserExamples_NormalizeAndExtractClearingNumber(string number, string expected)
    {
        var check = new IbanNetBankAccountValidator().Validate(number);
        Assert.Equal(BankAccountStatus.ValidIban, check.Status);
        Assert.Equal(expected, check.NormalizedNumber);
        Assert.Equal("11402105", check.PolishClearingNumber);
    }
    [Theory]
    [InlineData("-")][InlineData("\u2010")][InlineData("\u2011")][InlineData("\u2012")]
    [InlineData("\u2013")][InlineData("\u2014")][InlineData("\u2015")]
    [InlineData("\u2212")][InlineData("\uFE63")][InlineData("\uFF0D")]
    public void ExplicitVisualSeparatorsAndUnicodeWhitespaceAreShared(string dash)
    {
        var input = "\t pl39\u00a01140" + dash + "2105\u20030000 5616 4000 1001\r\n";
        var check = new IbanNetBankAccountValidator().Validate(input);
        Assert.Equal(BankAccountStatus.ValidIban, check.Status);
        Assert.Equal("PL39114021050000561640001001", check.NormalizedNumber);
        Assert.Equal(check.NormalizedNumber, BankAccountPreparation.Normalize(input));
    }
    [Theory]
    [InlineData("PL39-1140?2105-0000-5616-4000-1001", "?")]
    [InlineData("PL39-1140/2105-0000-5616-4000-1001", "/")]
    [InlineData("PL39-1140.2105-0000-5616-4000-1001", ".")]
    [InlineData("PL39-1140\u200b2105-0000-5616-4000-1001", "\u200b")]
    public void UnknownCharactersArePreservedAndNeverValidate(string number, string preserved)
    {
        var check = new IbanNetBankAccountValidator().Validate(number);
        Assert.Contains(preserved, check.NormalizedNumber);
        Assert.True(check.RequiresWarning);
        Assert.Null(check.PolishClearingNumber);
    }
    [Theory]
    [InlineData("38114021050000561640001001")]
    [InlineData("DE89370400440532013000")]
    [InlineData("LOCAL-12-34")]
    public void OnlyValidPolishNumbersHaveClearingNumber(string number)
    {
        var check = new IbanNetBankAccountValidator().Validate(number);
        Assert.Null(check.PolishClearingNumber);
        if (number.StartsWith("LOCAL", StringComparison.Ordinal)) Assert.Equal(number, check.NormalizedNumber);
    }
    [Theory]
    [InlineData(0)][InlineData(1)][InlineData(2)]
    public async Task FindsNameAndExactBicsWithoutGuessing(int count)
    {
        var body = Reply("11402105", new[] { "TESTPLPW", "TESTPLPW001" }.Take(count).ToArray());
        using var stub = new DataPortTests.Stub(request =>
        {
            Assert.Equal("https://ewib.nbp.pl/api/v1/zapytanie1/?nrRozliczeniowy=11402105&format=json", request.RequestUri!.AbsoluteUri);
            Assert.Contains(request.Headers.Accept, x => x.MediaType == "application/json");
            return new(HttpStatusCode.OK) { Content = new StringContent(body) };
        });
        var reply = await new EwibBankDirectory(new DataPortTests.FreshFactory(stub)).Find("11402105", default);
        Assert.Equal("Found", reply.Status);
        Assert.Equal("Bank demonstracyjny EWIB", reply.BankName);
        Assert.Equal(count, reply.Bics!.Length);
    }
    [Fact]
    public async Task ReadsBranchesAndDeduplicatesOnlyRequestedClearingBics()
    {
        const string body = """
        {"listaWlascicieli":[{"numer":"101","nazwa":"Bank demonstracyjny EWIB",
        "siedziba":{"numeryRozliczeniowe":[{"numer":"99999999","numeryBic":[{"numer":"WRONGXXX"}]}]},
        "jednostki":[{"numeryRozliczeniowe":[{"numer":"11402105","numeryBic":[{"numer":"TESTPLPW"},{"numer":"TESTPLPW"}]}]}]}]}
        """;
        using var stub = new DataPortTests.Stub(_ => new(HttpStatusCode.OK) { Content = new StringContent(body) });
        var reply = await new EwibBankDirectory(new DataPortTests.FreshFactory(stub)).Find("11402105", default);
        Assert.Equal("TESTPLPW", Assert.Single(reply.Bics!));
    }
    [Theory]
    [InlineData("{\"listaWlascicieli\":[]}", "NotFound")]
    [InlineData("{}", "InvalidResponse")]
    [InlineData("[]", "InvalidResponse")]
    [InlineData("not json", "InvalidResponse")]
    [InlineData("{\"listaWlascicieli\":null}", "InvalidResponse")]
    public async Task MissingAndMalformedRepliesNeverProduceBankDetails(string body, string status)
    {
        using var stub = new DataPortTests.Stub(_ => new(HttpStatusCode.OK) { Content = new StringContent(body) });
        var reply = await new EwibBankDirectory(new DataPortTests.FreshFactory(stub)).Find("11402105", default);
        Assert.Equal(status, reply.Status); Assert.Null(reply.BankName); Assert.Null(reply.Bics);
    }
    [Fact]
    public async Task AmbiguousOwnersAndMismatchedRoutingNeverProduceProposals()
    {
        var first = JsonDocument.Parse(Reply("11402105", "TESTPLPW")).RootElement.GetProperty("listaWlascicieli")[0].GetRawText();
        foreach (var (body, status) in new[] { ("{\"listaWlascicieli\":[" + first + "," + first.Replace("\"114\"", "\"999\"") + "]}", "Ambiguous"),
                     (Reply("99999999", "TESTPLPW"), "InvalidResponse"), (Reply("11402105", "bad?"), "InvalidResponse") })
        {
            using var stub = new DataPortTests.Stub(_ => new(HttpStatusCode.OK) { Content = new StringContent(body) });
            var result = await new EwibBankDirectory(new DataPortTests.FreshFactory(stub)).Find("11402105", default);
            Assert.Equal(status, result.Status); Assert.Null(result.BankName); Assert.Null(result.Bics);
        }
    }
    [Theory]
    [InlineData(400)][InlineData(500)][InlineData(302)][InlineData(429)]
    public async Task HttpErrorsNeverBlockManualEntryOrEchoBody(int status)
    {
        var calls = 0;
        using var stub = new DataPortTests.Stub(_ => { calls++; return new((HttpStatusCode)status) { Content = new StringContent("PRIVATE REMOTE ERROR") }; });
        var result = await new EwibBankDirectory(new DataPortTests.FreshFactory(stub)).Find("11402105", default);
        Assert.Equal("Unavailable", result.Status); Assert.Contains("ręcznie", result.Message);
        Assert.DoesNotContain("PRIVATE", result.Message); Assert.Equal(1, calls);
    }
    [Theory]
    [InlineData(true)][InlineData(false)]
    public async Task TimeoutAndConnectionFailureAreNotMissingBank(bool timeout)
    {
        using var stub = new DataPortTests.Stub(_ => { if (timeout) throw new TaskCanceledException(); throw new HttpRequestException(); });
        var result = await new EwibBankDirectory(new DataPortTests.FreshFactory(stub)).Find("11402105", default);
        Assert.Equal("Unavailable", result.Status);
    }
    [Theory]
    [InlineData("")][InlineData("1234567")][InlineData("11402105&format=xml")]
    public async Task NeverRequestsFullDirectoryOrArbitraryQuery(string number)
    {
        using var stub = new DataPortTests.Stub(_ => throw new InvalidOperationException("No HTTP expected"));
        await Assert.ThrowsAsync<ArgumentException>(() => new EwibBankDirectory(new DataPortTests.FreshFactory(stub)).Find(number, default));
    }
}
