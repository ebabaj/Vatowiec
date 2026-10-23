using System.Net;
using System.Text;
using System.Xml;
using Microsoft.Extensions.Configuration;
using Vatowiec.Core.Application;
using Vatowiec.Core.Infrastructure.Registries;

namespace Vatowiec.Tests;

public sealed class RegistryAdapterTests
{
    private static TaxIdentifierValidator Validator() => new([new PolishNipChecksum()]);
    private static readonly DateOnly Date = new(2026, 10, 22);
    [Theory]
    [InlineData("TaxId", " pl 324-517-45-04 ", "PL", "3245174504", "Valid")]
    [InlineData("TaxId", "3245174505", "PL", "3245174505", "Invalid")]
    [InlineData("TaxId", "1234567890", "PL", "1234567890", "Invalid")]
    [InlineData("TaxId", " AB-12.3 ", "DE", "AB-12.3", "Unknown")]
    [InlineData("VatEuId", " el 123 456 789 ", "GR", "EL123456789", "Unknown")]
    [InlineData("VatEuId", "xi 123-456-789", "GB", "XI123456789", "Unknown")]
    [InlineData("VatEuId", "GR123456789", "GR", "GR123456789", "Unknown")]
    [InlineData("VatEuId", "ie 1*23456A", "IE", "IE1*23456A", "Unknown")]
    [InlineData("VatEuId", "PL3245174504", "DE", "PL3245174504", "Valid")]
    [InlineData("VatEuId", "PL3245174505", "PL", "PL3245174505", "Invalid")]
    [InlineData("KsefIdWew", " 3245174504 - 12345 ", "PL", "3245174504-12345", "Unknown")]
    [InlineData("KsefIdWew", "3245174504-1234", "PL", "3245174504-1234", "Invalid")]
    public void Identifiers_NormalizeWithoutDestroyingLettersAndStateLimits(string field, string input, string country, string normalized, string status)
    {
        var result = Validator().Assess(field, input, country, TaxSchemeSeed.Rules(), Date);
        Assert.Equal(normalized, result.Normalized); Assert.Equal(status, result.Status);
        Assert.NotEmpty(result.RuleVersion);
        if (status == "Unknown") Assert.False(result.ChecksumChecked);
    }
    [Fact]
    public void Rules_All27CountriesPlusXiAreVersionedAndMissingAlgorithmIsNeverValid()
    {
        var rules = TaxSchemeSeed.Rules().Where(x => x.ValidFrom <= Date && (x.ValidTo == null || x.ValidTo >= Date)).ToArray();
        Assert.Equal(28, rules.Count(x => x.IdentifierType == "VatEuId"));
        Assert.Equal(27, rules.Where(x => x.VatPrefix is not null && x.VatPrefix != "XI").Select(x => x.CountryCode).Distinct().Count());
        Assert.Contains(rules, x => x.CountryCode == "GR" && x.VatPrefix == "EL");
        Assert.Contains(rules, x => x.CountryCode == "GB" && x.VatPrefix == "XI");
        Assert.Equal("Empty", Validator().Assess("TaxId", " ", "PL", rules, Date).Status);
        Assert.Equal("Unknown", Validator().Assess("TaxId", "3245174504", "PL", rules, Date.AddDays(-1)).Status);
        var pl = Assert.Single(rules, x => x.IdentifierType == "TaxId"); pl.AlgorithmKey = "not-implemented";
        Assert.Equal("Unknown", Validator().Assess("TaxId", "3245174504", "PL", rules, Date).Status);
    }
    [Fact]
    public void Configuration_RejectsProductionWithoutExplicitSwitchAndAllArbitraryUrls()
    {
        var settings = Settings(new() { ["Registries:MF:Environment"] = "Production" });
        Assert.Throws<InvalidOperationException>(() => settings.Mode("MF"));
        foreach (var url in new[] { "http://localhost/", "https://evil.invalid/", RegistrySettings.MfProduction, RegistrySettings.MfTest + "../api/" })
            Assert.Throws<InvalidOperationException>(() => Settings(new() { ["Registries:MF:BaseUrl"] = url }).Endpoint("MF"));
        Assert.Equal(RegistrySettings.MfTest, Settings().Endpoint("MF").AbsoluteUri);
    }
    [Fact]
    public void Throttle_SeparatesTestFromProductionAndLimitsConsecutiveCalls()
    {
        var throttle = new RegistryThrottle(); var now = DateTimeOffset.UtcNow;
        Assert.True(throttle.TryEnter("MF", "Test", "MfTaxpayer", now));
        Assert.False(throttle.TryEnter("MF", "Test", "MfTaxpayer", now));
        Assert.True(throttle.TryEnter("MF", "Production", "MfTaxpayer", now));
    }
    [Theory]
    [InlineData("TAK", "Positive")]
    [InlineData("NIE", "Negative")]
    public async Task MfAccount_UsesNipNrbAndStateDateAndPreservesReference(string answer, string expected)
    {
        var factory = new FakeFactory(request =>
        {
            Assert.Equal("wykazpodatnikowvat-test.mf.gov.pl", request.RequestUri!.Host);
            Assert.Equal("/api/check/nip/3245174504/bank-account/70506405335016096312945164", request.RequestUri.AbsolutePath);
            Assert.Equal("?date=2026-10-22", request.RequestUri.Query);
            return Reply(System.Text.Json.JsonSerializer.Serialize(new { result = new { accountAssigned = answer, requestId = "TEST-reference" } }));
        });
        var result = await Adapter(factory).Execute("MfAccount", "3245174504", null, "70506405335016096312945164", Date, default);
        Assert.Equal(expected, result.Outcome); Assert.Equal("TEST-reference", result.Reference); Assert.Equal("Test", result.Environment);
    }
    [Theory]
    [InlineData(400, "Error")]
    [InlineData(429, "RateLimited")]
    [InlineData(503, "Unavailable")]
    public async Task MfErrors_AreNotNegative(int status, string expected)
    {
        var result = await Adapter(new(_ => Reply("{}", status))).Execute("MfTaxpayer", "3245174504", null, null, Date, default);
        Assert.Equal("Unknown", result.Outcome); Assert.Equal(expected, result.ServiceStatus);
    }
    [Theory]
    [InlineData("true", "Positive")]
    [InlineData("false", "Negative")]
    public async Task Vies_OfficialTestEndpointAndElXiContract(string valid, string outcome)
    {
        foreach (var prefix in new[] { "EL", "XI" })
        {
            var result = await Adapter(new(request =>
            {
                Assert.Equal(RegistrySettings.ViesTest, request.RequestUri!.AbsoluteUri);
                var body = request.Content!.ReadAsStringAsync().Result;
                Assert.Contains(prefix, body); Assert.Contains("100", body);
                return Reply($"<Envelope><Body><checkVatResponse><countryCode>{prefix}</countryCode><vatNumber>100</vatNumber><valid>{valid}</valid><name>Fikcyjny test</name><address>Fikcyjna</address></checkVatResponse></Body></Envelope>");
            })).Execute("Vies", null, prefix + "100", null, Date, default);
            Assert.Equal(outcome, result.Outcome); Assert.Equal("Fikcyjny test", result.Data.Name);
        }
    }
    [Theory]
    [InlineData("TIMEOUT", "Timeout")]
    [InlineData("MS_UNAVAILABLE", "Unavailable")]
    [InlineData("INVALID_INPUT", "Error")]
    public async Task ViesFaults_AndTimeoutsNeverMeanInvalidTaxpayer(string fault, string expected)
    {
        var result = await Adapter(new(_ => Reply($"<Envelope><Fault><faultstring>{fault}</faultstring></Fault></Envelope>", 500))).Execute("Vies", null, "PL100", null, Date, default);
        Assert.Equal("Unknown", result.Outcome); Assert.Equal(expected, result.ServiceStatus);
        var timed = await Adapter(new(_ => throw new TaskCanceledException())).Execute("Vies", null, "PL100", null, Date, default);
        Assert.Equal("Timeout", timed.ServiceStatus); Assert.Equal("Unknown", timed.Outcome);
    }
    [Fact]
    public async Task Gus_Bir12LoginSearchAndLogout_OnlyPublicTestKeyAndSidInMemory()
    {
        var calls = 0;
        var result = await Adapter(new(request =>
        {
            calls++;
            Assert.Equal(RegistrySettings.GusTest, request.RequestUri!.AbsoluteUri);
            var text = request.Content!.ReadAsStringAsync().Result;
            if (calls == 1) { Assert.Contains("abcde12345abcde12345", text); return Reply("<Envelope><ZalogujResult>test-sid</ZalogujResult></Envelope>"); }
            Assert.Equal("test-sid", request.Headers.GetValues("sid").Single());
            if (calls == 3) { Assert.Contains("Wyloguj", text); return Reply("<Envelope><WylogujResult>true</WylogujResult></Envelope>"); }
            Assert.Contains("DaneSzukajPodmioty", text); Assert.Contains("5261040828", text);
            return Reply("<Envelope><DaneSzukajPodmiotyResult>&lt;root&gt;&lt;dane&gt;&lt;Nip&gt;5261040828&lt;/Nip&gt;&lt;Nazwa&gt;Fikcyjny podmiot&lt;/Nazwa&gt;&lt;Miejscowosc&gt;Miasto testowe&lt;/Miejscowosc&gt;&lt;/dane&gt;&lt;/root&gt;</DaneSzukajPodmiotyResult></Envelope>");
        })).Execute("Gus", "5261040828", null, null, Date, default);
        Assert.Equal(3, calls); Assert.Equal("Information", result.Outcome); Assert.Equal("Miasto testowe", result.Data.City);
        Assert.DoesNotContain("test-sid", System.Text.Json.JsonSerializer.Serialize(result));
    }
    [Fact]
    public async Task Mock_DoesNotMakeNetworkRequestOrClaimOfficialConfirmation()
    {
        var result = await Adapter(new(_ => throw new Exception("Nie wolno łączyć się w Mock")), new() { ["Registries:GUS:Environment"] = "Mock" })
            .Execute("Gus", "5261040828", null, null, Date, default);
        Assert.False(result.Executed); Assert.Equal("Mock", result.Environment); Assert.Equal("Unknown", result.Outcome);
        Assert.Throws<XmlException>(() => RegistryAdapters.ParseXml("<!DOCTYPE x [<!ENTITY a SYSTEM 'file:///secrets'>]><x>&a;</x>"));
    }
    private static RegistrySettings Settings(Dictionary<string, string?>? values = null) => new(new ConfigurationBuilder().AddInMemoryCollection(values ?? []).Build());
    private static RegistryAdapters Adapter(FakeFactory factory, Dictionary<string, string?>? values = null) => new(factory, Settings(values), new());
    private static HttpResponseMessage Reply(string text, int status = 200) => new((HttpStatusCode)status) { Content = new StringContent(text, Encoding.UTF8) };
    private sealed class FakeFactory(Func<HttpRequestMessage, HttpResponseMessage> reply) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(new Handler(reply));
        private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> action) : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(action(request));
        }
    }
}
