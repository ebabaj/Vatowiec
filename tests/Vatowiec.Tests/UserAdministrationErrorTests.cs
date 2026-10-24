using System.Net;
using Vatowiec.Desktop.Services;
using Vatowiec.Desktop.ViewModels;
using Vatowiec.SharedContracts;

namespace Vatowiec.Tests;

public sealed partial class DesktopTests
{
    [Theory]
    [InlineData(404, null, "operacji zarządzania użytkownikami")]
    [InlineData(404, "user_missing", "Nie znaleziono użytkownika")]
    [InlineData(409, null, "Inny administrator")]
    [InlineData(409, "user_stale", "Wersja użytkownika zmieniła się")]
    [InlineData(403, null, "Brak uprawnień do zarządzania użytkownikami")]
    [InlineData(401, null, "Sesja wygasła")]
    [InlineData(503, null, "Zarządzanie użytkownikami")]
    public async Task RoleTransportErrorsUseUserContextAndDistinguishUnknownEndpointFromMissingUser(int status, string? code, string expected)
    {
        var id = Guid.NewGuid();
        using var client = new VatowiecApiClient(new HttpClient(new Handler(request =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/csrf", StringComparison.Ordinal)) return Task.FromResult(Json(new CsrfResponse("csrf", "X-CSRF-TOKEN")));
            Assert.Equal(HttpMethod.Put, request.Method);
            Assert.EndsWith($"/users/{id}/administrator-role", request.RequestUri.AbsolutePath);
            var response = code == null ? new HttpResponseMessage((HttpStatusCode)status) { Content = new StringContent("<html>unrelated server diagnostics</html>") }
                : Json(new { code, message = code == "user_missing" ? "Nie znaleziono użytkownika. Odśwież listę." : "Wersja użytkownika zmieniła się. Odśwież szczegóły." });
            response.StatusCode = (HttpStatusCode)status; return Task.FromResult(response);
        })) { BaseAddress = new Uri("https://localhost") });
        var error = await Assert.ThrowsAsync<ApiException>(() => client.SaveAdministratorRole(id, new("version", true, true)));
        Assert.Equal(status, error.StatusCode); Assert.Contains(expected, error.Message);
        Assert.DoesNotContain("kontrahent", error.Message); Assert.DoesNotContain("diagnostics", error.Message);
    }

    [Fact]
    public async Task Generic404DoesNotAssumeContractorContext()
    {
        using var client = new VatowiecApiClient(new HttpClient(new Handler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)
        { Content = new StringContent("") }))) { BaseAddress = new Uri("https://localhost") });
        var error = await Assert.ThrowsAsync<ApiException>(() => client.Profile(A));
        Assert.Contains("zasobu lub operacji", error.Message); Assert.DoesNotContain("kontrahent", error.Message);
    }

    [Theory]
    [InlineData(404)]
    [InlineData(409)]
    [InlineData(0)]
    public async Task FailedRoleWriteRestoresLastConfirmedCheckboxAndRequiresRefreshWithoutSuccessMessage(int status)
    {
        var api = new FakeApi { InstallationAdmin = true };
        using var model = new MainViewModel(_ => api) { UserName = "admin" };
        await model.Login("fixture-password"); await model.OpenUsersSettings(); model.SelectedAdminUser = model.AdminUsers[0];
        model.AdministratorRole = true;
        Assert.Contains("niezapisaną propozycję", model.AdminRoleStatus);
        api.RoleError = status == 0 ? new HttpRequestException("diagnostics") : new ApiException(status, status == 409 ? "Konflikt wersji użytkownika." : "Brak operacji w API.");
        await model.SaveAdministratorRole();
        Assert.False(model.AdministratorRole); Assert.False(model.CanEditAdministratorRole); Assert.False(model.CanSaveAdministratorRole);
        Assert.False(model.HasRoleChanges); Assert.Contains("nie został potwierdzony", model.AdminRoleStatus);
        Assert.Contains("Nie potwierdzono zapisu roli", model.Message); Assert.DoesNotContain("Zapisano rolę", model.Message);
        Assert.DoesNotContain("kontrahent", model.Message); Assert.DoesNotContain("diagnostics", model.Message);
        if (status == 0) Assert.Contains("połączyć się z API", model.Message);
        // Starting a fresh read clears the previous operation's error, then permits a new proposal.
        api.RoleError = null; await model.RefreshAdminUser();
        Assert.Equal("", model.Message); Assert.True(model.CanEditAdministratorRole);
        model.AdministratorRole = true; var reads = api.UserDetailReads;
        await model.SaveAdministratorRole();
        Assert.Equal(reads + 1, api.UserDetailReads); Assert.True(model.AdministratorRole);
        Assert.False(model.HasUnsavedChanges); Assert.Equal("Brak niezapisanych zmian", model.AdminDirtyStatus);
        Assert.Contains("odczytano aktualny stan z serwera", model.Message); Assert.DoesNotContain("Nie potwierdzono", model.Message);
    }

    [Fact]
    public async Task RoleWriteUsesFreshReadbackAndDoesNotClaimProposalIsCurrentWhenServerDisagrees()
    {
        var api = new FakeApi { InstallationAdmin = true, RoleReadbackOverride = false };
        using var model = new MainViewModel(_ => api) { UserName = "admin" };
        await model.Login("fixture-password"); await model.OpenUsersSettings(); model.SelectedAdminUser = model.AdminUsers[0];
        model.AdministratorRole = true; await model.SaveAdministratorRole();
        Assert.False(model.AdministratorRole); Assert.False(model.HasUnsavedChanges);
        Assert.Contains("różni się", model.Message); Assert.Contains("bez roli", model.AdminRoleStatus);
        Assert.False(model.SelectedAdminUser!.IsInstallationAdministrator);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RoleFollowupErrorsAreNotHiddenBySuccessAndIdentifyReadbackOrHistory(bool readback)
    {
        var api = new FakeApi { InstallationAdmin = true };
        using var model = new MainViewModel(_ => api) { UserName = "admin" };
        await model.Login("fixture-password"); await model.OpenUsersSettings(); model.SelectedAdminUser = model.AdminUsers[0];
        if (readback) api.RoleReadbackError = new ApiException(404, "Brak użytkownika.");
        else api.HistoryError = new ApiException(404, "Brak operacji historii w API.");
        model.AdministratorRole = true; await model.SaveAdministratorRole();
        Assert.True(model.AdministratorRole); Assert.False(model.HasUnsavedChanges);
        Assert.Contains(readback ? "nie udało się ponownie odczytać" : "nie udało się odświeżyć historii", model.Message);
        Assert.DoesNotContain("Zapisano rolę systemową", model.Message);
        Assert.Equal(!readback, model.CanEditAdministratorRole);
    }
}
