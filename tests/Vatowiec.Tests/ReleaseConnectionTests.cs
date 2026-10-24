using System.Net.Http;
using Vatowiec.Desktop.ViewModels;

namespace Vatowiec.Tests;

public sealed partial class DesktopTests
{
    [Theory]
    [InlineData(HttpRequestError.SecureConnectionError, "certyfikat")]
    [InlineData(HttpRequestError.NameResolutionError, "adres HTTPS serwera")]
    [InlineData(HttpRequestError.ConnectionError, "LAN lub VPN")]
    public async Task Login_ExplainsConnectionFailureWithoutExposingDiagnostics(HttpRequestError error, string expected)
    {
        var api = new FakeApi { LoginError = new HttpRequestException(error, "private-diagnostics") };
        using var model = new MainViewModel(_ => api);
        model.UserName = "fixture";
        await model.Login("fixture-password");
        Assert.True(model.IsLogin);
        Assert.Contains(expected, model.Message);
        Assert.DoesNotContain("private-diagnostics", model.Message);
        Assert.Empty(api.ProfileCalls);
    }

    [Fact]
    public async Task Login_ExplainsTimeoutWithoutClaimingSuccess()
    {
        var api = new FakeApi { LoginError = new TaskCanceledException("private-diagnostics") };
        using var model = new MainViewModel(_ => api);
        model.UserName = "fixture";
        await model.Login("fixture-password");
        Assert.True(model.IsLogin);
        Assert.Contains("czas oczekiwania", model.Message);
        Assert.DoesNotContain("private-diagnostics", model.Message);
    }
}
