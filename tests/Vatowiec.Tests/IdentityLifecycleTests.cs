using Vatowiec.Core.Domain;
using Vatowiec.Core.Application;
using Vatowiec.Desktop.Services;
using Vatowiec.Desktop.ViewModels;

namespace Vatowiec.Tests;

public sealed class PasswordLifetimeTests
{
    [Fact]
    public void ArchiveSafetyProtectsSelfAndLastActiveAdministrator()
    {
        var actor = Guid.NewGuid(); var target = Guid.NewGuid();
        Assert.Throws<ArgumentException>(() => UserAdministrationSafety.ValidateArchive(actor, actor, true, 2));
        Assert.Throws<ArgumentException>(() => UserAdministrationSafety.ValidateArchive(actor, target, true, 1));
        UserAdministrationSafety.ValidateArchive(actor, target, true, 2);
        UserAdministrationSafety.ValidateArchive(actor, target, false, 1);
    }
    [Fact]
    public void ExpirationHasGraceForOldOrUnknownDatesAndIsIndependentFromComplexity()
    {
        var at = new DateTimeOffset(2026, 10, 24, 12, 0, 0, TimeSpan.Zero); var p = new PasswordPolicy();
        p.Update(false, 12, true, true, true, true); p.UpdateLifetime(true, 2, true, 1, at);
        Assert.False(p.IsExpired(null, at)); Assert.False(p.IsExpired(at.AddYears(-10), at.AddHours(23)));
        Assert.True(p.IsExpired(null, at.AddDays(1))); Assert.False(p.IsExpired(at.AddHours(12), at.AddDays(1)));
        p.UpdateLifetime(false, 2, false, 1, at); Assert.False(p.IsExpired(at, at.AddYears(3)));
        Assert.Throws<ArgumentException>(() => p.UpdateLifetime(true, 21, false, 90, at));
        Assert.Throws<ArgumentException>(() => p.UpdateLifetime(false, 1, true, 0, at));
    }
}
public sealed partial class DesktopTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReusedLoginWithDifferentUserIdDiscardsCredentialDefaultsAndSessionBeforeAnyCompany(bool automatic)
    {
        var preferences = SavedLogin(automatic, B); var secrets = new FakeLoginCredentials(); secrets.Seed(RememberedTarget, "old-fixture");
        var newId = Guid.NewGuid(); var api = new FakeApi { LoginUserId = newId };
        using var model = RememberingModel(api, preferences, secrets); await model.InitializeLogin();
        if (!automatic) await model.Login("new-fixture-password");
        Assert.True(model.IsLogin); Assert.Empty(api.ProfileCalls); Assert.Empty(model.Companies); Assert.True(api.LoggedOut);
        Assert.False(secrets.Contains(RememberedTarget)); var profile = preferences.Value.Find(RememberedTarget)!;
        Assert.Equal(newId, profile.UserId); Assert.False(profile.AutomaticLogin || profile.RememberCredentials); Assert.Null(profile.DefaultCompanyId);
        Assert.Contains("innej tożsamości", model.Message);
        await model.Login("new-fixture-password"); Assert.True(model.IsCompanySelection); Assert.Empty(api.ProfileCalls);
    }
    [Fact]
    public async Task LegacyUnboundPreferencesNeverReadStoredPasswordOrOpenPreviousDefault()
    {
        var target = new LoginTarget(LoginOrigin, "operator"); var preferences = new FakeLoginPreferences
        { Value = new(LastTargetKey: target.Key, Profiles: [new(target.ApiAddress, target.UserName, true, true, B)]) };
        var secrets = new FakeLoginCredentials(); secrets.Seed(target, "unbound-fixture"); var api = new FakeApi();
        using var model = RememberingModel(api, preferences, secrets); await model.InitializeLogin();
        Assert.Equal(0, secrets.Reads); Assert.Equal(0, api.LoginCalls); Assert.False(model.AutomaticLogin || model.HasSavedPassword);
        await model.Login("manual-fixture"); Assert.True(model.IsCompanySelection); Assert.Empty(api.ProfileCalls);
        Assert.Equal(FixtureUserId, preferences.Value.Find(target)!.UserId); Assert.Null(preferences.Value.Find(target)!.DefaultCompanyId);
    }
    [Fact]
    public async Task ExpiredPasswordResponseSwitchesExistingDesktopSessionToRestrictedForm()
    {
        var api = new FakeApi { InstallationAdmin = true }; using var model = new MainViewModel(_ => api) { UserName = "admin" };
        await model.Login("fixture"); await model.OpenUsersSettings(); model.SelectedAdminUser = model.AdminUsers[0];
        api.AdminError = new ApiException(403, "Zmień hasło.") { Code = "user_password_change_required" };
        await model.RefreshAdminUser(); Assert.True(model.IsOwnPasswordChange); Assert.False(model.IsUsersSettings);
        Assert.False(model.IsCompanySelection); Assert.Empty(model.AdminCompanyAccess);
    }
}
