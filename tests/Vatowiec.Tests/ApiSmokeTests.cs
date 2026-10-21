using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Vatowiec.SharedContracts;

namespace Vatowiec.Tests;

public sealed class ApiSmokeTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> factory;

    public ApiSmokeTests(WebApplicationFactory<Program> factory)
    {
        this.factory = factory;
    }

    [Fact]
    public async Task Status_ReturnsSkeletonStageWithoutDatabase()
    {
        using var application = CreateApplication("Development");
        using var client = CreateClient(application);

        using var response = await client.GetAsync("/api/v1/status");
        var status = await response.Content.ReadFromJsonAsync<ApiStatusResponse>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(status);
        Assert.Equal("Vatowiec", status.Application);
        Assert.Equal("M0-skeleton", status.Stage);
    }

    [Fact]
    public async Task Health_ReturnsHealthyWithoutDatabase()
    {
        using var application = CreateApplication("Development");
        using var client = CreateClient(application);

        using var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("Development", HttpStatusCode.OK)]
    [InlineData("Production", HttpStatusCode.NotFound)]
    public async Task OpenApi_IsAvailableOnlyInDevelopment(string environment, HttpStatusCode expected)
    {
        using var application = CreateApplication(environment);
        using var client = CreateClient(application);

        using var response = await client.GetAsync("/openapi/v1.json");

        Assert.Equal(expected, response.StatusCode);
        if (response.IsSuccessStatusCode)
        {
            using var document = await System.Text.Json.JsonDocument.ParseAsync(
                await response.Content.ReadAsStreamAsync());
            Assert.True(document.RootElement.GetProperty("paths").TryGetProperty("/api/v1/status", out _));
        }
    }

    private WebApplicationFactory<Program> CreateApplication(string environment) =>
        factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment(environment);
            // Testy nigdy nie dziedziczą adresu bazy ze środowiska uruchamiającego.
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:Vatowiec"] = null
                }));
        });

    private static HttpClient CreateClient(WebApplicationFactory<Program> application) =>
        application.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false
        });
}
