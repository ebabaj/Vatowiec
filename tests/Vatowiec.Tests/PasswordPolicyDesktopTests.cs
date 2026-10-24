using System.Net;
using Vatowiec.Desktop.Services;
using Vatowiec.Desktop.ViewModels;
using Vatowiec.SharedContracts;

namespace Vatowiec.Tests;

public sealed partial class DesktopTests
{
    [Fact]
    public async Task PolicyReadFailureNeverShowsDefaultsOrAllowsSave_RefreshLoadsActualValues()
    {
        var api = new FakeApi { InstallationAdmin = true, PolicyError = new ApiException(503, "Brak schematu polityki haseł w bazie systemowej.") };
        using var model = new MainViewModel(_ => api) { UserName = "admin" };
        await model.Login("fixture-password"); await model.OpenPasswordPolicy();
        Assert.True(model.IsPasswordPolicyScreen); Assert.Null(model.PasswordPolicyEditor);
        Assert.False(model.HasPasswordPolicy || model.CanSavePasswordPolicy || model.HasPolicyHistory);
        await model.SavePasswordPolicy(false); Assert.Equal(0, api.PolicyWrites);
        api.PolicyError = null; await model.RefreshPasswordPolicy();
        Assert.True(model.HasPasswordPolicy && model.HasPolicyHistory); Assert.Equal(17, model.PasswordPolicyEditor!.MinimumLength);
        Assert.False(model.CanSavePasswordPolicy); model.PasswordPolicyEditor.RequireDigit = false;
        Assert.True(model.CanSavePasswordPolicy); model.PasswordPolicyEditor.MinimumLength = 0; Assert.False(model.CanSavePasswordPolicy);
        model.PasswordPolicyEditor.MinimumLength = 17; Assert.True(model.CanSavePasswordPolicy);
        var reads = api.PolicyReads; await model.SavePasswordPolicy(false);
        Assert.Equal(reads + 1, api.PolicyReads); Assert.False(model.CanSavePasswordPolicy || model.HasUnsavedChanges);
        model.ClosePasswordPolicy(); await model.OpenPasswordPolicy();
        Assert.False(model.PasswordPolicyEditor!.RequireDigit); Assert.False(model.CanSavePasswordPolicy);
        api.PolicyError = new ApiException(503, "Brak rekordu konfiguracji."); await model.RefreshPasswordPolicy();
        Assert.Null(model.PasswordPolicyEditor); Assert.False(model.HasPasswordPolicy || model.CanSavePasswordPolicy || model.HasPolicyHistory);
    }
    [Fact]
    public async Task PolicyHistoryFailureIsExplicitAndDoesNotReplaceLoadedConfiguration()
    {
        var api = new FakeApi { InstallationAdmin = true, PolicyHistoryError = new ApiException(503, "Historia polityki jest niedostępna.") };
        using var model = new MainViewModel(_ => api) { UserName = "admin" };
        await model.Login("fixture-password"); await model.OpenPasswordPolicy();
        Assert.True(model.HasPasswordPolicy); Assert.False(model.HasPolicyHistory);
        Assert.Contains("Historia", model.Message); api.PolicyHistoryError = null; await model.LoadPolicyHistory(0);
        Assert.True(model.HasPolicyHistory && model.IsPolicyHistoryEmpty); Assert.Equal("", model.Message);
    }
    [Fact]
    public async Task SystemDatabaseErrorsNeverClaimCompanyDatabaseFailure()
    {
        using var client = new VatowiecApiClient(new HttpClient(new Handler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
        { Content = System.Net.Http.Json.JsonContent.Create(new { code = "system_unavailable" }) }))) { BaseAddress = new Uri("https://localhost") });
        var error = await Assert.ThrowsAsync<ApiException>(() => client.PasswordPolicy());
        Assert.Contains("systemowa instalacji", error.Message); Assert.DoesNotContain("Baza firmy", error.Message);
    }
    private sealed partial class FakeApi
    {
        public PasswordPolicyResponse PolicyData = new(true, 17, true, false, true, false, "initial-policy-version");
        public ApiException? PolicyError, PolicyHistoryError;
        public int PolicyWrites, PolicyReads;
        public Task<PasswordPolicyResponse> PasswordPolicy()
        { PolicyReads++; if (PolicyError != null) throw PolicyError; return Task.FromResult(PolicyData); }
        public Task<PasswordPolicyResponse> SavePasswordPolicy(PasswordPolicyRequest value)
        {
            PolicyWrites++; PolicyData = new(value.IsEnabled, value.MinimumLength, value.RequireUppercase, value.RequireLowercase, value.RequireDigit,
                value.RequireSpecialCharacter, "version-" + PolicyWrites, value.PreventPasswordReuse, value.PreviousPasswordCount,
                value.EnforcePasswordExpiration, value.PasswordValidityDays); return Task.FromResult(PolicyData);
        }
        public Task<PasswordPolicyHistory[]> PasswordPolicyHistory(int offset)
        { if (PolicyHistoryError != null) throw PolicyHistoryError; return Task.FromResult<PasswordPolicyHistory[]>([]); }
    }
}
