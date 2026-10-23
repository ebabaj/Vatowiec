using System.Net;
using System.Net.Http.Json;
using Vatowiec.Desktop.Services;
using Vatowiec.Desktop.ViewModels;
using Vatowiec.SharedContracts;

namespace Vatowiec.Tests;

public sealed partial class DesktopTests
{
    [Fact]
    public async Task GusSettingsWorkBeforeCompanySelectionAndRequireExplicitDelete()
    {
        var api = new FakeApi { InstallationAdmin = true };
        using var model = new MainViewModel(_ => api) { UserName = "installation-admin" };
        await model.Login("fake-password");
        Assert.True(model.IsCompanySelection); Assert.True(model.CanOpenGusSettings);
        await model.OpenGusSettings(); Assert.True(model.IsGusSettings); Assert.False(model.IsCompanySelection);
        Assert.Equal("Nie skonfigurowano", model.GusStatus);
        await model.SaveGusKey("fake-key"); Assert.Equal(1, api.GusSaves); Assert.Equal("Skonfigurowano", model.GusStatus);
        Assert.DoesNotContain("fake-key", model.Message); Assert.Contains("całej instalacji", model.Message);
        await model.DeleteGusKey(); Assert.Equal(0, api.GusDeletes);
        model.RequestGusDelete(); model.CancelGusDelete(); await model.DeleteGusKey(); Assert.Equal(0, api.GusDeletes);
        model.RequestGusDelete(); await model.DeleteGusKey(); Assert.Equal(1, api.GusDeletes);
        Assert.Equal("Nie skonfigurowano", model.GusStatus);
        model.CloseGusSettings(); Assert.True(model.IsCompanySelection); Assert.False(model.IsGusSettings);
        Assert.Equal(0, api.KeySaves); Assert.Equal(0, api.KeyDeletes);
    }
    [Fact]
    public async Task CompanyIntegrationRightDoesNotGrantInstallationSettings()
    {
        var api = new FakeApi { Permissions = ["ViewProfile", "ManageCompanyIntegrations"] };
        using var model = await Open(api);
        Assert.False(model.CanOpenGusSettings);
        await model.OpenGusSettings(); await model.SaveGusKey("fake-key");
        Assert.False(model.IsGusSettings); Assert.Equal(0, api.GusSaves);
    }
    [Fact]
    public async Task GusSettingsClearOnLogoutAndForbiddenResponse()
    {
        var api = new FakeApi { InstallationAdmin = true };
        using var model = await Open(api);
        await model.OpenGusSettings(); Assert.False(model.IsBrowsingWorkspace);
        api.GusForbidden = true; await model.RefreshGusStatus();
        Assert.False(model.IsGusSettings); Assert.False(model.IsInstallationAdministrator);
        Assert.Equal("Stan niepobrany", model.GusStatus);
        await model.Logout(); Assert.True(model.IsLogin);
    }
    [Fact]
    public async Task GusClientUsesCentralRouteAndFreshCsrfForEveryMutation()
    {
        var csrf = 0; var calls = 0;
        using var http = new HttpClient(new Handler(request =>
        {
            if (request.RequestUri!.AbsolutePath == "/api/v1/auth/csrf")
                return Task.FromResult(Json(new CsrfResponse("csrf-" + (++csrf), "X-CSRF-TOKEN")));
            Assert.Equal("/api/v1/installation/integrations/gus", request.RequestUri.AbsolutePath);
            if (request.Method != HttpMethod.Get) Assert.Equal("csrf-" + csrf, request.Headers.GetValues("X-CSRF-TOKEN").Single());
            calls++; return Task.FromResult(Json(new GusConfigurationResponse(true, "rv", "Production")));
        }))
        { BaseAddress = new Uri("https://localhost") };
        using var client = new VatowiecApiClient(http);
        await client.GusConfiguration(); await client.SaveGusKey(new() { ApiKey = "fake" }); await client.DeleteGusKey(new("rv", true));
        Assert.Equal(3, calls); Assert.Equal(2, csrf);
    }
    private sealed partial class FakeApi
    {
        public bool InstallationAdmin { get; init; }
        public bool GusForbidden { get; set; }
        public int GusSaves { get; private set; }
        public int GusDeletes { get; private set; }
        private bool gusConfigured;
        private int gusVersion;
        public Task<GusConfigurationResponse> GusConfiguration()
        {
            if (GusForbidden) throw new ApiException(403, "Brak roli administratora instalacji.");
            return Task.FromResult(new GusConfigurationResponse(gusConfigured, gusVersion.ToString(), "Test"));
        }
        public Task<GusConfigurationResponse> SaveGusKey(SaveGusKeyRequest value) { GusSaves++; gusConfigured = true; gusVersion++; return GusConfiguration(); }
        public Task<GusConfigurationResponse> DeleteGusKey(DeleteGusKeyRequest value) { GusDeletes++; gusConfigured = false; gusVersion++; return GusConfiguration(); }
    }
}
