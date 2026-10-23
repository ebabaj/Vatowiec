using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Vatowiec.Core.Infrastructure.Registries;
using Vatowiec.SharedContracts;

namespace Vatowiec.Tests;

public sealed class ProductionRegistryTests
{
    private static readonly DateOnly Date = new(2026, 10, 23);
    [Fact]
    public void CentralConfigurationHasNoCompanyIdentityAndNoCompanyMapping()
    {
        using var system = new Vatowiec.Core.Infrastructure.Persistence.SystemDbContextFactory().CreateDbContext([]);
        using var company = new Vatowiec.Core.Infrastructure.Persistence.CompanyDbContextFactory().CreateDbContext([]);
        var entity = system.Model.FindEntityType(typeof(Vatowiec.Core.Domain.GusConfiguration))!;
        Assert.Equal("SingletonKey", Assert.Single(entity.FindPrimaryKey()!.Properties).Name);
        Assert.Equal("security", entity.GetSchema()); Assert.True(entity.FindProperty("RowVersion")!.IsConcurrencyToken);
        Assert.Null(entity.FindProperty("CompanyId")); Assert.Null(company.Model.FindEntityType(typeof(Vatowiec.Core.Domain.GusConfiguration)));
    }
    internal static RegistrySettings Settings(string mode = "Production") => new(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["Registries:AllowProduction"] = "true",
        ["Registries:MF:Environment"] = mode,
        ["Registries:GUS:Environment"] = mode,
        ["Registries:VIES:Environment"] = mode
    }).Build());

    [Theory]
    [InlineData("MF", RegistrySettings.MfProduction)]
    [InlineData("GUS", RegistrySettings.GusProduction)]
    [InlineData("VIES", RegistrySettings.ViesProduction)]
    public void ExplicitProductionUsesOnlyOfficialHttps(string source, string expected)
    {
        Assert.Equal(expected, Settings().Endpoint(source).AbsoluteUri);
        Assert.Equal("https://wykazpodatnikowvat.mf.gov.pl/api2/", Settings().Endpoint("MF", true).AbsoluteUri);
        var forbidden = new RegistrySettings(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { ["Registries:AllowProduction"] = "true", [$"Registries:{source}:Environment"] = "Production", [$"Registries:{source}:BaseUrl"] = "https://wl-api.mf.gov.pl/" }).Build());
        Assert.Throws<InvalidOperationException>(() => forbidden.Endpoint(source));
    }

    [Fact]
    public async Task GusProductionReadsCentralKey_TestAndMockNeverReadIt()
    {
        foreach (var mode in new[] { "Test", "Production", "Mock" })
        {
            var key = "fake-" + Guid.NewGuid().ToString("N"); var provider = new KeyProvider(key);
            var calls = 0;
            var factory = new Factory(request =>
            {
                calls++;
                Assert.Equal(mode == "Test" ? RegistrySettings.GusTest : RegistrySettings.GusProduction, request.RequestUri!.AbsoluteUri);
                var xml = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
                if (calls == 1)
                {
                    Assert.Contains(mode == "Test" ? RegistrySettings.GusTestApiKey : key, xml);
                    return Reply("<Envelope><ZalogujResult>fake-session</ZalogujResult></Envelope>");
                }
                Assert.Equal("fake-session", request.Headers.GetValues("sid").Single());
                return calls == 2 ? GusFound() : Reply("<Envelope><WylogujResult>true</WylogujResult></Envelope>");
            });
            var result = await new RegistryAdapters(factory, Settings(mode), new(), provider).Execute("Gus", "3245174504", null, null, Date, default);
            Assert.Equal(mode == "Production" ? 1 : 0, provider.Reads);
            Assert.Equal(mode == "Mock" ? 0 : 3, calls);
            Assert.Equal(mode, result.Environment);
            Assert.DoesNotContain(key, JsonSerializer.Serialize(result)); Assert.DoesNotContain("fake-session", JsonSerializer.Serialize(result));
            if (mode == "Production") { Assert.Equal("Completed", result.ServiceStatus); Assert.DoesNotContain("TEST", result.Message); }
            if (mode == "Test") Assert.Contains("TEST", result.Message);
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData(RegistrySettings.GusTestApiKey)]
    [InlineData("lost-key-ring")]
    public async Task MissingOrLostProductionKeyNeverCallsTestOrOtherService(string? key)
    {
        var provider = new KeyProvider(key) { Lost = key == "lost-key-ring" };
        var result = await new RegistryAdapters(new Factory(_ => throw new Exception("No network expected")), Settings(), new(), provider)
            .Execute("Gus", "3245174504", null, null, Date, default);
        Assert.Equal("NotConfigured", result.ServiceStatus); Assert.False(result.Executed);
        Assert.Equal("Production", result.Environment); Assert.Equal("Unknown", result.Outcome);
    }

    [Fact]
    public void EncryptionIsInstallationBoundAndIndependentOfDataPort()
    {
        var protection = new EphemeralDataProtectionProvider();
        var gus = new GusKeyProtector(protection); var dataport = new DataPortKeyProtector(protection);
        var id = Guid.NewGuid(); var key = "fake-" + Guid.NewGuid().ToString("N");
        var cipher = gus.Protect(id, key);
        Assert.DoesNotContain(key, cipher); Assert.Equal(key, gus.Unprotect(id, cipher));
        Assert.Throws<CryptographicException>(() => gus.Unprotect(Guid.NewGuid(), cipher));
        Assert.Throws<CryptographicException>(() => dataport.Unprotect(id, cipher));
        Assert.Throws<CryptographicException>(() => gus.Unprotect(id, dataport.Protect(id, key)));
        Assert.Throws<CryptographicException>(() => new GusKeyProtector(new EphemeralDataProtectionProvider()).Unprotect(id, cipher));
        Assert.Throws<InvalidOperationException>(() => gus.Protect(Guid.Empty, key));
        Assert.DoesNotContain(key, new SaveGusKeyRequest { ApiKey = key }.ToString());
    }

    [Fact]
    public void WindowsDpapiProtectsPersistentGusRingOutsideAppAndSurvivesRestart()
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Test wymaga Windows DPAPI; nie oznaczaj jako zaliczony na innej platformie.");
        var directory = Path.Combine(Path.GetTempPath(), "Vatowiec-GUS-" + Guid.NewGuid().ToString("N"));
        var host = new Host(AppContext.BaseDirectory);
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["GUS:KeyRingDirectory"] = directory }).Build();
        try
        {
            var id = Guid.NewGuid(); var key = "fake-" + Guid.NewGuid().ToString("N");
            var cipher = GusKeyProtector.Create(config, host).Protect(id, key);
            Assert.Equal(key, GusKeyProtector.Create(config, host).Unprotect(id, cipher));
            Assert.All(Directory.GetFiles(directory), file => Assert.Contains("encryptedSecret", File.ReadAllText(file)));
            var unsafeConfig = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["GUS:KeyRingDirectory"] = AppContext.BaseDirectory }).Build();
            Assert.Throws<InvalidOperationException>(() => GusKeyProtector.Create(unsafeConfig, host));
        }
        finally
        {
            var resolved = Path.GetFullPath(directory);
            Assert.StartsWith(Path.GetFullPath(Path.GetTempPath()), resolved, StringComparison.OrdinalIgnoreCase);
            if (Directory.Exists(resolved)) Directory.Delete(resolved, true);
        }
    }

    [Theory]
    [InlineData("Czynny")]
    [InlineData("Zwolniony")]
    [InlineData("Niezarejestrowany")]
    public async Task ProductionWl2UsesIsoQueryDateAndPreservesNestedReferenceAndStatus(string status)
    {
        var result = await new RegistryAdapters(new Factory(request =>
        {
            Assert.Equal("https://wykazpodatnikowvat.mf.gov.pl/api2/status/nip/3245174504?date=2026-10-23", request.RequestUri!.AbsoluteUri);
            return Reply(JsonSerializer.Serialize(new
            {
                result = new
                {
                    subject = new
                    {
                        nip = "3245174504",
                        statusVat = status,
                        requestedDate = "23-10-2026",
                        responseDateTime = "23-10-2026 12:34:56",
                        requestId = "fixture-reference"
                    }
                }
            }));
        }), Settings(), new()).Execute("MfStatus", "3245174504", null, null, Date, default);
        Assert.Equal("Completed", result.ServiceStatus); Assert.Equal("Information", result.Outcome);
        Assert.Equal("fixture-reference", result.Reference); Assert.Equal(status, result.Data.VatStatus);
        Assert.Contains("2026-10-23", result.Message);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"result\":{\"subject\":null}}")]
    [InlineData("{\"result\":{\"subject\":{\"nip\":\"3245174504\",\"statusVat\":\"Czynny\"}}}")]
    public async Task MalformedWl2IsNeverNegative(string json)
    {
        var result = await new RegistryAdapters(new Factory(_ => Reply(json)), Settings(), new()).Execute("MfStatus", "3245174504", null, null, Date, default);
        Assert.Equal("Unknown", result.Outcome); Assert.Equal("Error", result.ServiceStatus);
    }

    [Theory]
    [InlineData("MfTaxpayer")]
    [InlineData("MfAccount")]
    [InlineData("Vies")]
    public async Task MissingGusKeyDoesNotBlockOtherProductionAdapters(string operation)
    {
        var key = new KeyProvider(null) { Lost = true };
        var result = await new RegistryAdapters(new Factory(request =>
        {
            Assert.NotEqual("wyszukiwarkaregon.stat.gov.pl", request.RequestUri!.Host);
            if (operation == "Vies") return Reply("<Envelope><countryCode>XI</countryCode><vatNumber>123456789</vatNumber><valid>true</valid></Envelope>");
            Assert.Equal("wykazpodatnikowvat.mf.gov.pl", request.RequestUri.Host);
            return Reply(operation == "MfAccount" ? "{\"result\":{\"accountAssigned\":\"TAK\",\"requestId\":\"fixture\"}}" :
                "{\"result\":{\"subject\":{\"nip\":\"3245174504\",\"statusVat\":\"Czynny\"},\"requestId\":\"fixture\"}}");
        }), Settings(), new(), key).Execute(operation, "3245174504", "XI123456789", "70506405335016096312945164", Date, default);
        Assert.Equal("Completed", result.ServiceStatus); Assert.Equal(0, key.Reads);
    }

    [Theory]
    [InlineData("EL")]
    [InlineData("XI")]
    public async Task ProductionViesPreservesPrefixAndNeverCallsGus(string prefix)
    {
        var result = await new RegistryAdapters(new Factory(request =>
        {
            Assert.Equal(RegistrySettings.ViesProduction, request.RequestUri!.AbsoluteUri);
            var xml = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            Assert.Contains($">{prefix}</", xml); Assert.Contains(">123456789</", xml);
            return Reply($"<Envelope><countryCode>{prefix}</countryCode><vatNumber>123456789</vatNumber><valid>false</valid></Envelope>");
        }), Settings(), new()).Execute("Vies", null, prefix + "123456789", null, Date, default);
        Assert.Equal("Negative", result.Outcome); Assert.Equal("Production", result.Environment);
    }

    [Theory]
    [InlineData("SERVICE_UNAVAILABLE", "Unavailable")]
    [InlineData("MS_UNAVAILABLE", "Unavailable")]
    [InlineData("TIMEOUT", "Timeout")]
    [InlineData("MS_MAX_CONCURRENT_REQ", "Error")]
    public async Task ProductionViesFaultIsNotInvalidVat(string fault, string status)
    {
        var result = await new RegistryAdapters(new Factory(_ => Reply($"<Envelope><Fault><faultstring>{fault}</faultstring></Fault></Envelope>", 500)), Settings(), new())
            .Execute("Vies", null, "EL123456789", null, Date, default);
        Assert.Equal(status, result.ServiceStatus); Assert.Equal("Unknown", result.Outcome);
    }

    [Fact]
    public void GusThrottleIsSharedAcrossCompaniesAndCoversWholeSessionAndCooldown()
    {
        var throttle = new RegistryThrottle(); var now = DateTimeOffset.UtcNow;
        Assert.True(throttle.TryEnter("GUS", "Production", "Gus", now));
        Assert.False(throttle.TryEnter("GUS", "Production", "Gus", now.AddMinutes(1)));
        Assert.True(throttle.TryEnter("MF", "Production", "MfStatus", now));
        throttle.CompleteGus("Production", now.AddMinutes(2));
        Assert.False(throttle.TryEnter("GUS", "Production", "Gus", now.AddMinutes(2).AddSeconds(1)));
        Assert.True(throttle.TryEnter("GUS", "Production", "Gus", now.AddMinutes(2).AddSeconds(2)));
    }

    [Theory]
    [InlineData("timeout", "Timeout")]
    [InlineData("network", "Unavailable")]
    [InlineData("malformed", "Error")]
    public async Task ProductionGusFailureNeverFallsBackOrMeansNoEntity(string failure, string status)
    {
        var calls = 0;
        var reply = await new RegistryAdapters(new Factory(request =>
        {
            calls++; Assert.Equal(RegistrySettings.GusProduction, request.RequestUri!.AbsoluteUri);
            return failure switch { "timeout" => throw new TaskCanceledException(), "network" => throw new HttpRequestException(), _ => Reply("bad xml") };
        }), Settings(), new(), new KeyProvider("fake-key")).Execute("Gus", "3245174504", null, null, Date, default);
        Assert.Equal(status, reply.ServiceStatus); Assert.Equal("Unknown", reply.Outcome); Assert.Equal(1, calls);
    }

    [Fact]
    public async Task GusProviderCannotEchoSecretIntoReturnedData()
    {
        var key = "fake-" + Guid.NewGuid().ToString("N"); var calls = 0;
        var reply = await new RegistryAdapters(new Factory(_ =>
        {
            calls++;
            return calls == 1 ? Reply("<Envelope><ZalogujResult>fixture-session</ZalogujResult></Envelope>") :
                calls == 2 ? Reply($"<Envelope><DaneSzukajPodmiotyResult>&lt;root&gt;&lt;dane&gt;&lt;Nip&gt;3245174504&lt;/Nip&gt;&lt;Nazwa&gt;{key}&lt;/Nazwa&gt;&lt;/dane&gt;&lt;/root&gt;</DaneSzukajPodmiotyResult></Envelope>") :
                Reply("<Envelope><WylogujResult>true</WylogujResult></Envelope>");
        }), Settings(), new(), new KeyProvider(key)).Execute("Gus", "3245174504", null, null, Date, default);
        Assert.Equal("invalid_response", reply.ErrorCode); Assert.DoesNotContain(key, JsonSerializer.Serialize(reply));
        Assert.Equal(3, calls);
    }

    [Fact]
    public async Task GusNestedXmlAboveTwoThousandCharactersIsParsedWithoutTruncation()
    {
        var calls = 0;
        var reply = await new RegistryAdapters(new Factory(_ =>
        {
            calls++;
            return calls == 1 ? Reply("<Envelope><ZalogujResult>fixture-session</ZalogujResult></Envelope>") :
                calls == 2 ? Reply("<Envelope><DaneSzukajPodmiotyResult>&lt;root&gt;&lt;dane&gt;&lt;Nip&gt;3245174504&lt;/Nip&gt;&lt;Nazwa&gt;Fikcyjny test&lt;/Nazwa&gt;&lt;Opis&gt;" + new string('x', 3000) + "&lt;/Opis&gt;&lt;/dane&gt;&lt;/root&gt;</DaneSzukajPodmiotyResult></Envelope>") :
                Reply("<Envelope><WylogujResult>true</WylogujResult></Envelope>");
        }), Settings(), new(), new KeyProvider("fake-key")).Execute("Gus", "3245174504", null, null, Date, default);
        Assert.Equal("Completed", reply.ServiceStatus); Assert.Equal("Fikcyjny test", reply.Data.Name);
    }

    internal static HttpResponseMessage Reply(string value, int status = 200) => new((HttpStatusCode)status) { Content = new StringContent(value) };
    internal static HttpResponseMessage GusFound() => Reply("<Envelope><DaneSzukajPodmiotyResult>&lt;root&gt;&lt;dane&gt;&lt;Nip&gt;3245174504&lt;/Nip&gt;&lt;Nazwa&gt;Fikcyjny test GUS&lt;/Nazwa&gt;&lt;/dane&gt;&lt;/root&gt;</DaneSzukajPodmiotyResult></Envelope>");
    internal sealed class KeyProvider(string? key) : IGusApiKeyProvider
    {
        public int Reads { get; private set; }
        public bool Lost { get; init; }
        public Task<string?> Read(CancellationToken ct) { Reads++; if (Lost) throw new CryptographicException(); return Task.FromResult(key); }
    }
    internal sealed class Factory(Func<HttpRequestMessage, HttpResponseMessage> reply) : IHttpClientFactory
    { public HttpClient CreateClient(string name) => new(new DataPortTests.Stub(reply)); }
    private sealed class Host(string root) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Development";
        public string ApplicationName { get; set; } = "Test";
        public string ContentRootPath { get; set; } = root;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
