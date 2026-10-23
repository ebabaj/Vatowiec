using System.Net;
using Vatowiec.Core.Infrastructure.Registries;
using Vatowiec.Desktop.Services;
using Vatowiec.SharedContracts;

namespace Vatowiec.Tests;

public sealed partial class DesktopTests
{
    [Fact]
    public async Task RegistrySettingsSelectServicesAndRequireConsciousProductionConfirmation()
    {
        var api = new FakeApi { InstallationAdmin = true };
        using var model = await Open(api); await model.OpenGusSettings();
        Assert.Equal("GUS", model.RegistrySettingsEditor!.Source); Assert.True(model.IsGusServiceSelected);
        model.RegistryServiceIndex = 1; Assert.Equal("MF", model.RegistrySettingsEditor!.Source);
        Assert.True(model.RegistrySettingsEditor.IsMf); Assert.False(model.IsGusServiceSelected);
        var mf = model.RegistrySettingsEditor; mf.SelectedEnvironment = 1;
        await model.SaveRegistryConfiguration(); Assert.Empty(api.RegistrySaves); Assert.Contains("potwierdzenie", model.Message);
        mf.ConfirmProduction = true; await model.SaveRegistryConfiguration();
        Assert.Single(api.RegistrySaves); Assert.Equal("Production", api.RegistrySaves[0].Environment);
        Assert.Contains("bez restartu", model.Message); Assert.False(model.RegistrySettingsEditor!.ConfirmProduction);
        Assert.Equal("Production", api.RegistryRows.Single(x => x.Source == "MF").Environment);
        Assert.Equal("Test", api.RegistryRows.Single(x => x.Source == "GUS").Environment);
        Assert.Equal(0, api.GusSaves); Assert.Equal(0, api.KeySaves);
        model.RegistryServiceIndex = 2; Assert.Equal("VIES", model.RegistrySettingsEditor!.Source);
        model.RegistrySettingsEditor.TestUrl = "https://evil.invalid/";
        api.RegistrySaveError = new ApiException(400, "Dozwolony jest wyłącznie oficjalny adres HTTPS.");
        await model.SaveRegistryConfiguration(); Assert.Contains("oficjalny adres HTTPS", model.Message);
        await model.RefreshRegistryConfigurations(); Assert.Equal(RegistrySettings.ViesTest, model.RegistrySettingsEditor!.TestUrl);
        model.CloseGusSettings(); Assert.Null(model.RegistrySettingsEditor);
    }
    [Fact]
    public async Task RegistrySettingsClientUsesCentralRouteFreshCsrfAndSurfacesUrlErrors()
    {
        using var http = new HttpClient(new Handler(request =>
        {
            if (request.RequestUri!.AbsolutePath == "/api/v1/auth/csrf") return Task.FromResult(Json(new CsrfResponse("fresh", "X-CSRF-TOKEN")));
            Assert.Equal("/api/v1/installation/integrations/registries/MF", request.RequestUri.AbsolutePath);
            Assert.Equal(HttpMethod.Put, request.Method); Assert.Equal("fresh", request.Headers.GetValues("X-CSRF-TOKEN").Single());
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = System.Net.Http.Json.JsonContent.Create(new
                { code = "validation_failed", errors = new { TestUrl = new[] { "Nieprawidłowy URL: wymagany oficjalny host MF." } } })
            });
        }))
        { BaseAddress = new Uri("https://localhost") };
        using var api = new VatowiecApiClient(http);
        var error = await Assert.ThrowsAsync<ApiException>(() => api.SaveRegistryConfiguration("MF", new("Test", "https://bad.invalid", "https://bad.invalid", null, null, "rv")));
        Assert.Contains("oficjalny host MF", error.Message);
    }
    private sealed partial class FakeApi
    {
        public RegistryConfigurationResponse[] RegistryRows { get; private set; } = [
            new("GUS", "Test", RegistrySettings.GusTest, RegistrySettings.GusProduction, null, null, "AAAAAAAAAAE="),
            new("MF", "Test", RegistrySettings.MfTest, RegistrySettings.MfProduction, RegistrySettings.OfficialUrl("MF", "Test", true), RegistrySettings.OfficialUrl("MF", "Production", true), "AAAAAAAAAAE="),
            new("VIES", "Test", RegistrySettings.ViesTest, RegistrySettings.ViesProduction, null, null, "AAAAAAAAAAE=")];
        public List<RegistryConfigurationRequest> RegistrySaves { get; } = [];
        public ApiException? RegistrySaveError { get; set; }
        public Task<RegistryConfigurationResponse[]> RegistryConfigurations() => Task.FromResult(RegistryRows.ToArray());
        public Task<RegistryConfigurationResponse> SaveRegistryConfiguration(string source, RegistryConfigurationRequest value)
        {
            if (RegistrySaveError != null) throw RegistrySaveError;
            RegistrySaves.Add(value);
            var row = new RegistryConfigurationResponse(source, value.Environment, value.TestUrl, value.ProductionUrl,
                value.TestWl2Url, value.ProductionWl2Url, "AAAAAAAAAAI=");
            RegistryRows = RegistryRows.Select(x => x.Source == source ? row : x).ToArray();
            return Task.FromResult(row);
        }
    }
}
