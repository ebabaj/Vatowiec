using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Vatowiec.Core.Infrastructure.Registries;

namespace Vatowiec.Tests;

public sealed class DataPortTests
{
    internal const string Success = """
        {"success":true,"nip":"3245174504","regon":"TEST-REGON","nazwa":"Fikcyjna nazwa DataPort",
        "ulica":"Testowa","numer_budynku":"12","numer_lokalu":"3","kod_pocztowy":"00-001","miasto":"Miasto testowe",
        "data_zakonczenia_dzialalnosci":"2024-12-31"}
        """;
    [Theory]
    [InlineData(400, "Error", "bad_request")]
    [InlineData(401, "NotConfigured", "INVALID_API_KEY")]
    [InlineData(404, "Completed", "not_found")]
    [InlineData(429, "RateLimited", "RATE_LIMIT_EXCEEDED")]
    [InlineData(500, "Unavailable", "dataport_http_500")]
    [InlineData(302, "Unavailable", "dataport_http_302")]
    public async Task Errors_AreSanitizedWithoutRetries(int status, string expected, string code)
    {
        var calls = 0;
        var key = "fake-" + Guid.NewGuid().ToString("N");
        using var http = new HttpClient(new Stub(_ => { calls++; return new((HttpStatusCode)status) { Content = new StringContent(key) }; }));
        var result = await new DataPortClient(new Factory(http)).Lookup("3245174504", key, default);
        Assert.Equal(expected, result.ServiceStatus); Assert.Equal(code, result.ErrorCode); Assert.Equal(1, calls);
        Assert.DoesNotContain(key, JsonSerializer.Serialize(result));
        Assert.Equal(status == 404 ? "Negative" : "Unknown", result.Outcome);
    }
    [Theory]
    [InlineData("INVALID_API_KEY", "NotConfigured")]
    [InlineData("RATE_LIMIT_EXCEEDED", "RateLimited")]
    public async Task JsonErrors_AreRecognizedWithoutEchoingMessage(string code, string status)
    {
        var key = "fake-" + Guid.NewGuid().ToString("N");
        using var http = new HttpClient(new Stub(_ => new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new { success = false, error = code, message = key })) }));
        var result = await new DataPortClient(new Factory(http)).Lookup("3245174504", key, default);
        Assert.Equal(status, result.ServiceStatus); Assert.DoesNotContain(key, JsonSerializer.Serialize(result));
    }
    [Fact]
    public async Task Success_UsesPerRequestHeaderAndMapsOnlyKnownFields()
    {
        var keys = new List<string>();
        // Factory returns a shared transport, clients themselves can safely be disposed per call.
        var handler = new Stub(request =>
        {
            Assert.Equal("https://dataport.pl/api/v1/company/3245174504?format=full", request.RequestUri!.AbsoluteUri);
            Assert.Equal(HttpMethod.Get, request.Method); Assert.Contains(request.Headers.Accept, x => x.MediaType == "application/json");
            keys.Add(request.Headers.GetValues("X-API-Key").Single());
            return new(HttpStatusCode.OK) { Content = new StringContent(Success) };
        });
        var adapter = new DataPortClient(new FreshFactory(handler));
        var first = await adapter.Lookup("3245174504", "fake-company-A", default);
        var second = await adapter.Lookup("3245174504", "fake-company-B", default);
        Assert.Equal(["fake-company-A", "fake-company-B"], keys);
        Assert.Equal("Production", first.Environment); Assert.Equal("Information", first.Outcome);
        Assert.Equal("Fikcyjna nazwa DataPort", first.Data.Name); Assert.Equal("12", first.Data.BuildingNo);
        Assert.Equal("3", first.Data.ApartmentNo); Assert.Equal("00-001", first.Data.PostalCode);
        Assert.Equal("Miasto testowe", first.Data.City); Assert.Equal("PL", second.Data.CountryCode);
        Assert.Equal(new DateOnly(2024, 12, 31), first.Data.ActivityEndDate); Assert.Contains("cache", first.Message);
    }
    [Theory]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("{\"success\":true,\"nip\":\"5261040828\",\"nazwa\":\"Fikcyjny\"}")]
    [InlineData("{\"success\":false}")]
    public async Task InvalidResponses_AreNotNegative(string payload)
    {
        using var http = new HttpClient(new Stub(_ => new(HttpStatusCode.OK) { Content = new StringContent(payload) }));
        var result = await new DataPortClient(new Factory(http)).Lookup("3245174504", "fake-key", default);
        Assert.Equal("invalid_response", result.ErrorCode); Assert.Equal("Unknown", result.Outcome);
    }
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task NetworkAndTimeout_AreNotNegativeOrRetried(bool timeout)
    {
        int calls = 0;
        using var http = new HttpClient(new Stub(_ => { calls++; throw timeout ? new TaskCanceledException() : new HttpRequestException(); }));
        var result = await new DataPortClient(new Factory(http)).Lookup("3245174504", "fake-key", default);
        Assert.Equal(timeout ? "Timeout" : "Unavailable", result.ServiceStatus); Assert.Equal("Unknown", result.Outcome); Assert.Equal(1, calls);
    }
    [Fact]
    public async Task InvalidNipOrMissingKey_NeverCallsTransport()
    {
        using var http = new HttpClient(new Stub(_ => throw new InvalidOperationException("Network must not be called")));
        var adapter = new DataPortClient(new Factory(http));
        Assert.False((await adapter.Lookup("3245174505", "fake-key", default)).Executed);
        Assert.False((await adapter.Lookup("3245174504", "", default)).Executed);
    }
    [Fact]
    public async Task ProviderEchoOfSecret_IsRejectedInsteadOfStoredAsData()
    {
        var key = "fake-" + Guid.NewGuid().ToString("N");
        using var http = new HttpClient(new Stub(_ => new(HttpStatusCode.OK) { Content = new StringContent(Success.Replace("Fikcyjna nazwa DataPort", key)) }));
        var reply = await new DataPortClient(new Factory(http)).Lookup("3245174504", key, default);
        Assert.Equal("invalid_response", reply.ErrorCode); Assert.DoesNotContain(key, JsonSerializer.Serialize(reply));
    }
    [Fact]
    public async Task RegisteredHttpClient_LogsNeitherKeyNorNip_AndRequestToStringIsRedacted()
    {
        var key = "fake-" + Guid.NewGuid().ToString("N"); var capture = new LogCapture();
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Trace).AddProvider(capture));
        services.AddRegistryAdapters();
        services.AddHttpClient("dataport").ConfigurePrimaryHttpMessageHandler(() => new Stub(_ => new(HttpStatusCode.OK) { Content = new StringContent(Success) }));
        using var provider = services.BuildServiceProvider(); using var scope = provider.CreateScope();
        var reply = await scope.ServiceProvider.GetRequiredService<IDataPortClient>().Lookup("3245174504", key, default);
        Assert.Equal("Completed", reply.ServiceStatus);
        Assert.DoesNotContain(key, string.Join("\n", capture.Messages));
        Assert.DoesNotContain("3245174504", string.Join("\n", capture.Messages));
        Assert.DoesNotContain(key, new Vatowiec.SharedContracts.SaveDataPortKeyRequest { ApiKey = key }.ToString());
    }
    [Fact]
    public void Encryption_IsBoundToCompany()
    {
        var protector = new DataPortKeyProtector(new EphemeralDataProtectionProvider());
        var a = Guid.NewGuid(); var b = Guid.NewGuid(); var key = "fake-" + Guid.NewGuid().ToString("N");
        var encrypted = protector.Protect(a, key);
        Assert.DoesNotContain(key, encrypted); Assert.Equal(key, protector.Unprotect(a, encrypted));
        Assert.Throws<System.Security.Cryptography.CryptographicException>(() => protector.Unprotect(b, encrypted));
    }
    [Fact]
    public void WindowsDpapiKeyRing_SurvivesProviderRestartOutsideRepository()
    {
        if (!OperatingSystem.IsWindows()) return; // The actual Windows run exercises this branch.
        var directory = Path.Combine(Path.GetTempPath(), "Vatowiec-DataPort-" + Guid.NewGuid().ToString("N"));
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (!File.Exists(Path.Combine(root.FullName, "AGENTS.md"))) root = root.Parent!;
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["DataPort:KeyRingDirectory"] = directory }).Build();
        try
        {
            var key = "fake-" + Guid.NewGuid().ToString("N"); var company = Guid.NewGuid();
            var encrypted = DataPortKeyProtector.Create(config, new Host(root.FullName)).Protect(company, key);
            Assert.Equal(key, DataPortKeyProtector.Create(config, new Host(root.FullName)).Unprotect(company, encrypted));
            var files = Directory.GetFiles(directory); Assert.NotEmpty(files);
            Assert.All(files, file => { var xml = File.ReadAllText(file); Assert.Contains("encryptedSecret", xml); Assert.DoesNotContain(key, xml); });
        }
        finally
        {
            var resolved = Path.GetFullPath(directory);
            Assert.StartsWith(Path.GetFullPath(Path.GetTempPath()), resolved, StringComparison.OrdinalIgnoreCase);
            if (Directory.Exists(resolved)) Directory.Delete(resolved, true);
        }
    }
    private sealed class Host(string root) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Development";
        public string ApplicationName { get; set; } = "Test";
        public string ContentRootPath { get; set; } = root;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
    internal sealed class Stub(Func<HttpRequestMessage, HttpResponseMessage> send) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(send(request)); }
    private sealed class Factory(HttpClient http) : IHttpClientFactory
    { public HttpClient CreateClient(string name) { Assert.Equal("dataport", name); Assert.False(http.DefaultRequestHeaders.Contains("X-API-Key")); return http; } }
    internal sealed class FreshFactory(HttpMessageHandler handler) : IHttpClientFactory
    { public HttpClient CreateClient(string name) => new(handler, disposeHandler: false); }
    private sealed class LogCapture : ILoggerProvider
    {
        public System.Collections.Concurrent.ConcurrentQueue<string> Messages { get; } = new();
        public ILogger CreateLogger(string categoryName) => new CaptureLogger(Messages);
        public void Dispose() { }
        private sealed class CaptureLogger(System.Collections.Concurrent.ConcurrentQueue<string> messages) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) => messages.Enqueue(formatter(state, exception));
        }
    }
}
