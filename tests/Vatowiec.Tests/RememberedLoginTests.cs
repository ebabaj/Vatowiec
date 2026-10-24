using Vatowiec.Desktop.Services;
using Vatowiec.Desktop.ViewModels;
using Vatowiec.SharedContracts;

namespace Vatowiec.Tests;

public sealed partial class DesktopTests
{
    private const string LoginOrigin = "https://login-fixture.invalid:7243";
    private static readonly Guid FixtureUserId = Guid.Parse("dddddddd-0000-4000-8000-000000000001");
    private static LoginTarget RememberedTarget => new(LoginOrigin, "operator", FixtureUserId);
    private sealed class FakeLoginPreferences : ILoginPreferencesStore
    {
        public LoginPreferences Value { get; set; } = new();
        public bool FailLoad { get; set; }
        public bool FailSave { get; set; }
        public int Saves { get; private set; }
        public LoginPreferences Load() => FailLoad ? throw new LocalLoginStorageException("Test: preferencje niedostępne.") : Value;
        public void Save(LoginPreferences value) { if (FailSave) throw new LocalLoginStorageException("Test: preferencje niedostępne."); Value = value; Saves++; }
    }
    private sealed class FakeLoginCredentials : ILoginCredentialStore
    {
        private readonly Dictionary<string, string> passwords = [];
        public int Reads { get; private set; }
        public int Probes { get; private set; }
        public int Writes { get; private set; }
        public int Deletes { get; private set; }
        public bool Unavailable { get; set; }
        public bool Contains(LoginTarget target) => passwords.ContainsKey(target.CredentialName);
        bool ILoginCredentialStore.Contains(LoginTarget target) { Probes++; Check(); return Contains(target); }
        public void Seed(LoginTarget target, string password) => passwords[target.CredentialName] = password;
        private void Check() { if (Unavailable) throw new LocalLoginStorageException("Test: Credential Manager niedostępny."); }
        public string? Read(LoginTarget target) { Reads++; Check(); return passwords.GetValueOrDefault(target.CredentialName); }
        public void Write(LoginTarget target, string password) { Writes++; Check(); passwords[target.CredentialName] = password; }
        public void Delete(LoginTarget target) { Deletes++; Check(); passwords.Remove(target.CredentialName); }
    }
    private static FakeLoginPreferences SavedLogin(bool automatic = true, Guid? company = null)
    {
        var target = RememberedTarget;
        return new() { Value = new(LastTargetKey: target.Key, Profiles: [new(target.ApiAddress, target.UserName, true, automatic, company, FixtureUserId)]) };
    }
    private static MainViewModel RememberingModel(FakeApi api, FakeLoginPreferences preferences, FakeLoginCredentials credentials, TimeProvider? clock = null) =>
        new(_ => api, clock, preferences, credentials) { ApiAddress = LoginOrigin, UserName = "operator" };

    [Fact]
    public async Task SavedPasswordPresentationUsesActualCredential_NotRememberCheckboxOrPasswordRead()
    {
        var secrets = new FakeLoginCredentials(); var api = new FakeApi();
        using var model = RememberingModel(api, SavedLogin(automatic: false), secrets);
        await model.InitializeLogin();
        Assert.True(model.RememberCredentials); Assert.False(model.HasSavedPassword);
        Assert.Empty(model.SavedPasswordMask); Assert.Empty(model.SavedPasswordStatus);
        model.RememberCredentials = false; model.RememberCredentials = true;
        Assert.False(model.HasSavedPassword); Assert.Equal(0, secrets.Reads);
        secrets.Seed(RememberedTarget, "fixture-password"); model.RefreshSavedPasswordState();
        Assert.True(model.HasSavedPassword); Assert.Equal("••••••••", model.SavedPasswordMask);
        Assert.Equal("Hasło zapisane na tym komputerze", model.SavedPasswordStatus);
        Assert.Equal(0, secrets.Reads); Assert.Equal(0, api.LoginCalls);
        model.RememberCredentials = false;
        Assert.True(model.HasSavedPassword);
        api.CheckLoginInput = (_, password) => Assert.True(password == "fixture-password");
        await model.Login(""); Assert.Equal(1, api.LoginCalls); Assert.Equal(1, secrets.Reads);
    }

    [Fact]
    public async Task TypedPasswordTakesPriority_AndSuccessfulRememberUpdatesCredential()
    {
        var secrets = new FakeLoginCredentials(); secrets.Seed(RememberedTarget, "old-fixture-password");
        var api = new FakeApi { CheckLoginInput = (_, password) => Assert.True(password == "new-fixture-password") };
        using var model = RememberingModel(api, SavedLogin(automatic: false), secrets);
        await model.InitializeLogin(); Assert.True(model.HasSavedPassword);
        await model.Login("new-fixture-password");
        Assert.True(model.IsCompanySelection); Assert.Equal(0, secrets.Reads); Assert.Equal(1, secrets.Writes);
        Assert.True(secrets.Read(RememberedTarget) == "new-fixture-password");
    }

    [Fact]
    public async Task SavedPasswordPresentationImmediatelyFollowsTargetAndForget()
    {
        var secrets = new FakeLoginCredentials(); secrets.Seed(RememberedTarget, "fixture-password");
        using var model = RememberingModel(new FakeApi(), SavedLogin(automatic: false), secrets);
        await model.InitializeLogin(); Assert.True(model.HasSavedPassword);
        model.ApiAddress = "https://other-fixture.invalid";
        Assert.False(model.HasSavedPassword); Assert.Empty(model.SavedPasswordMask);
        model.ApiAddress = LoginOrigin; Assert.True(model.HasSavedPassword);
        model.UserName = "another-operator";
        Assert.False(model.HasSavedPassword); Assert.Empty(model.SavedPasswordStatus);
        model.UserName = "operator"; Assert.True(model.HasSavedPassword);
        await model.ForgetLoginCredentials();
        Assert.False(model.HasSavedPassword); Assert.Empty(model.SavedPasswordMask); Assert.Empty(model.SavedPasswordStatus);
        Assert.Equal(0, secrets.Reads);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EveryManualLoginOpensDefault_RegardlessOfStartupCheckbox(bool automatic)
    {
        var secrets = new FakeLoginCredentials(); var api = new FakeApi();
        using var model = RememberingModel(api, SavedLogin(automatic: automatic, company: B), secrets);
        await model.InitializeLogin(); // Missing credential leaves startup at the manual form.
        Assert.True(model.IsLogin); Assert.Equal(0, api.LoginCalls);
        await model.Login("fixture-password");
        Assert.True(model.IsWorkspace); Assert.Equal(B, Assert.Single(api.ProfileCalls));
        Assert.Equal(B, model.SelectedCompany!.CompanyId); Assert.Equal(automatic, model.AutomaticLogin);
        await model.ChangeCompany(); Assert.True(model.IsCompanySelection); Assert.Single(api.ProfileCalls);
        model.SelectedCompany = model.Companies.Single(x => x.CompanyId == A); await model.SetDefaultCompany();
        await model.Logout(); await model.Login("");
        Assert.True(model.IsWorkspace); Assert.Equal(A, api.ProfileCalls.Last()); Assert.Equal(2, api.ProfileCalls.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ManualLogin_MissingOrRevokedDefaultShowsPicker(bool configured)
    {
        var api = new FakeApi();
        using var model = RememberingModel(api, SavedLogin(automatic: false, company: configured ? Guid.NewGuid() : null), new());
        await model.InitializeLogin(); await model.Login("fixture-password");
        Assert.True(model.IsCompanySelection); Assert.Null(model.SelectedCompany); Assert.Empty(api.ProfileCalls);
        if (configured) Assert.Contains("nie jest obecnie dostępna", model.Message);
    }

    [Fact]
    public async Task ManualLoginWaitsForFreshCompanyList_AndSerializesDefaultProfileOpen()
    {
        var companies = new TaskCompletionSource<CompanyResponse[]>(); var profile = new TaskCompletionSource<CompanyProfileResponse>();
        var api = new FakeApi { PendingCompanies = companies.Task, PendingProfile = profile.Task };
        using var model = RememberingModel(api, SavedLogin(automatic: false, company: B), new());
        await model.InitializeLogin(); var login = model.Login("fixture-password");
        Assert.True(model.IsBusy); Assert.Empty(api.ProfileCalls);
        model.UserName = "another-operator"; model.ApiAddress = "https://another-fixture.invalid";
        await model.Login("another-fixture-password"); await model.Logout(); await model.ChangeCompany();
        Assert.Equal("operator", model.UserName); Assert.Equal(RememberedTarget.ApiAddress, model.ApiAddress);
        companies.SetResult([new(A, "Firma A", ["ViewProfile"]), new(B, "Firma B", ["ViewProfile"])]);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (api.ProfileCalls.Count == 0) await Task.Delay(5, timeout.Token);
        await model.OpenCompany(); await model.ChangeCompany(); await model.InitializeLogin();
        Assert.True(model.IsBusy); Assert.Equal(B, Assert.Single(api.ProfileCalls));
        profile.SetResult(new(B, "Firma B", null, null)); await login;
        Assert.True(model.IsWorkspace); Assert.Equal("Firma B", model.CurrentCompany); Assert.Equal(1, api.LoginCalls);
    }

    [Fact]
    public async Task NewUserOrServerCannotOpenPreviousDefaultCompany()
    {
        var preferences = SavedLogin(automatic: false, company: B); var api = new FakeApi(); var secrets = new FakeLoginCredentials();
        using var model = RememberingModel(api, preferences, secrets);
        await model.InitializeLogin(); await model.Login("fixture-password"); Assert.Equal(B, Assert.Single(api.ProfileCalls));
        await model.Logout(); model.UserName = "another-operator";
        await model.Login("another-fixture-password"); Assert.True(model.IsCompanySelection); Assert.Single(api.ProfileCalls);
        await model.Logout(); model.UserName = "operator"; model.ApiAddress = "https://another-fixture.invalid";
        await model.Login("another-fixture-password"); Assert.True(model.IsCompanySelection); Assert.Single(api.ProfileCalls);
        Assert.Equal(B, preferences.Value.Find(RememberedTarget)!.DefaultCompanyId);
    }

    [Fact]
    public async Task ManualLogin_DefaultsOff_DoesNotReadOrWritePasswordAndDoesNotPickFirstCompany()
    {
        var api = new FakeApi(); var preferences = new FakeLoginPreferences(); var secrets = new FakeLoginCredentials();
        using var model = RememberingModel(api, preferences, secrets);
        await model.InitializeLogin(); Assert.False(model.RememberCredentials); Assert.False(model.AutomaticLogin);
        model.AutomaticLogin = true; Assert.False(model.AutomaticLogin);
        model.UserName = "operator"; model.ApiAddress = LoginOrigin;
        await model.Login("fixture-password");
        Assert.Equal(1, api.LoginCalls); Assert.True(model.IsCompanySelection); Assert.Null(model.SelectedCompany);
        Assert.Empty(api.ProfileCalls); Assert.Equal(0, secrets.Reads + secrets.Writes + secrets.Deletes);
    }

    [Fact]
    public async Task SuccessfulLoginStoresCredential_AndRestartOpensOnlyGuidDefault_WithFreshPermissions()
    {
        var preferences = new FakeLoginPreferences(); var secrets = new FakeLoginCredentials(); var api = new FakeApi();
        using (var model = RememberingModel(api, preferences, secrets))
        {
            await model.InitializeLogin(); model.RememberCredentials = true; model.AutomaticLogin = true;
            await model.Login("fixture-password");
            Assert.Equal(1, secrets.Writes); Assert.True(secrets.Contains(RememberedTarget));
            model.SelectedCompany = model.Companies.Single(x => x.CompanyId == B); await model.SetDefaultCompany();
            Assert.Contains("Firma B", model.DefaultCompanyLabel);
            model.SelectedCompany = model.Companies.Single(x => x.CompanyId == A); await model.OpenCompany();
            Assert.Equal(B, preferences.Value.Find(RememberedTarget)!.DefaultCompanyId);
        }
        var nextApi = new FakeApi { Permissions = ["ViewProfile", "ViewExchangeRates"] };
        nextApi.CheckLoginInput = (login, password) => { Assert.Equal("operator", login); Assert.True(password == "fixture-password"); };
        using var next = RememberingModel(nextApi, preferences, secrets);
        await next.InitializeLogin(); await next.InitializeLogin();
        Assert.Equal(1, nextApi.LoginCalls); Assert.Equal(B, Assert.Single(nextApi.ProfileCalls));
        Assert.Equal(B, next.SelectedCompany!.CompanyId); Assert.False(next.CanEditContractors);
        await next.ChangeCompany(); Assert.True(next.IsCompanySelection); Assert.Empty(next.CurrentCompany);
        Assert.Equal(1, nextApi.LoginCalls); Assert.Single(nextApi.ProfileCalls);
        await next.ClearDefaultCompany(); Assert.Null(preferences.Value.Find(RememberedTarget)!.DefaultCompanyId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AutomaticLogin_MissingOrRevokedDefaultShowsPickerWithoutArbitraryFallback(bool configured)
    {
        var preferences = SavedLogin(company: configured ? Guid.NewGuid() : null); var secrets = new FakeLoginCredentials();
        secrets.Seed(RememberedTarget, "fixture-password"); var api = new FakeApi();
        using var model = RememberingModel(api, preferences, secrets); await model.InitializeLogin();
        Assert.True(model.IsCompanySelection); Assert.Null(model.SelectedCompany); Assert.Empty(api.ProfileCalls);
        if (configured) Assert.Contains("nie jest obecnie dostępna", model.Message);
    }

    [Theory]
    [InlineData(401)]
    [InlineData(429)]
    [InlineData(503)]
    [InlineData(0)]
    public async Task AutomaticLoginFailure_StaysManualAndNeverRetriesOrOverwritesPassword(int status)
    {
        var preferences = SavedLogin(company: B); var secrets = new FakeLoginCredentials(); secrets.Seed(RememberedTarget, "fixture-password");
        var api = new FakeApi { LoginError = status == 0 ? new HttpRequestException() : new ApiException(status, "Test odmowy.") };
        using var model = RememberingModel(api, preferences, secrets);
        await model.InitializeLogin(); model.CheckExpiry(); await model.InitializeLogin();
        Assert.True(model.IsLogin); Assert.Equal(1, api.LoginCalls); Assert.Empty(api.ProfileCalls); Assert.Equal(0, secrets.Writes);
        Assert.Contains("Automatyczne logowanie nie powiodło", model.Message); Assert.DoesNotContain("fixture-password", model.Message);
        api.LoginError = null; await model.Login("changed-fixture-password");
        Assert.Equal(2, api.LoginCalls); Assert.Equal(1, secrets.Writes);
    }

    [Fact]
    public async Task InvalidManualPassword_IsNotSavedAndCannotEnableAutomaticStartup()
    {
        var preferences = new FakeLoginPreferences(); var secrets = new FakeLoginCredentials();
        var api = new FakeApi { LoginError = new ApiException(401, "Nieprawidłowe hasło.") };
        using var model = RememberingModel(api, preferences, secrets);
        model.RememberCredentials = true; model.AutomaticLogin = true; await model.Login("wrong-fixture-password");
        Assert.True(model.IsLogin); Assert.Equal(0, secrets.Writes); Assert.Null(preferences.Value.LastTargetKey);
    }

    [Fact]
    public async Task DisabledAutomaticLogin_LoadsFormButReadsCredentialOnlyOnExplicitLogin()
    {
        var preferences = SavedLogin(automatic: false); var secrets = new FakeLoginCredentials(); secrets.Seed(RememberedTarget, "fixture-password");
        var api = new FakeApi(); using var model = RememberingModel(api, preferences, secrets);
        await model.InitializeLogin(); Assert.True(model.IsLogin); Assert.Equal(0, api.LoginCalls); Assert.Equal(0, secrets.Reads);
        Assert.Equal("operator", model.UserName); model.AutomaticLogin = true;
        await model.Login(""); Assert.True(model.IsCompanySelection); Assert.Equal(1, secrets.Reads);
        Assert.True(preferences.Value.Find(RememberedTarget)!.AutomaticLogin);
    }

    [Fact]
    public async Task ServerAndUserChangesNeverSendAnotherTargetsPassword_DefaultPreferencesAreSeparate()
    {
        var preferences = SavedLogin(automatic: false, company: B); var secrets = new FakeLoginCredentials(); secrets.Seed(RememberedTarget, "fixture-password");
        var api = new FakeApi(); var connections = new List<string>();
        using var model = new MainViewModel(address => { connections.Add(address); return api; }, preferencesStore: preferences, credentialStore: secrets);
        await model.InitializeLogin(); model.ApiAddress = "https://other-fixture.invalid";
        Assert.False(model.RememberCredentials); Assert.False(model.AutomaticLogin);
        await model.Login(""); Assert.Empty(connections); Assert.Equal(0, secrets.Reads);
        model.ApiAddress = LoginOrigin; model.UserName = "other-operator";
        await model.Login(""); Assert.Empty(connections); Assert.Equal(0, secrets.Reads);
        model.RememberCredentials = true; await model.Login("different-fixture-password");
        model.SelectedCompany = model.Companies.Single(x => x.CompanyId == A); await model.SetDefaultCompany();
        Assert.Equal(B, preferences.Value.Find(RememberedTarget)!.DefaultCompanyId);
        Assert.Equal(A, preferences.Value.Find(new(LoginOrigin, "other-operator"))!.DefaultCompanyId);
        await model.ForgetLoginCredentials(); Assert.True(secrets.Contains(RememberedTarget));
    }

    [Fact]
    public async Task LogoutDisablesAutomaticStartupButRetainsPassword_ForgetDeletesOnlyItsCredential()
    {
        var preferences = SavedLogin(company: B); var secrets = new FakeLoginCredentials(); secrets.Seed(RememberedTarget, "fixture-password");
        var api = new FakeApi(); using var model = RememberingModel(api, preferences, secrets);
        await model.InitializeLogin(); await model.Logout();
        Assert.True(model.IsLogin); Assert.True(api.LoggedOut); Assert.True(api.Disposed);
        Assert.False(preferences.Value.Find(RememberedTarget)!.AutomaticLogin); Assert.True(secrets.Contains(RememberedTarget));
        var nextApi = new FakeApi(); using var next = RememberingModel(nextApi, preferences, secrets);
        await next.InitializeLogin(); Assert.Equal(0, nextApi.LoginCalls);
        await next.ForgetLoginCredentials(); Assert.False(secrets.Contains(RememberedTarget));
        Assert.False(preferences.Value.Find(RememberedTarget)!.RememberCredentials);
        Assert.Equal(B, preferences.Value.Find(RememberedTarget)!.DefaultCompanyId);
    }

    [Fact]
    public async Task ExpiryNeverStartsLogin_ExplicitLogoutAfterExpiryStillDisablesStartupLogin()
    {
        var preferences = SavedLogin(); var secrets = new FakeLoginCredentials(); secrets.Seed(RememberedTarget, "fixture-password");
        var clock = new Clock(); var api = new FakeApi { Expires = clock.Now.AddMinutes(1) };
        using var model = RememberingModel(api, preferences, secrets, clock); await model.InitializeLogin();
        clock.Now = clock.Now.AddMinutes(2); model.CheckExpiry(); model.CheckExpiry();
        Assert.True(model.IsLogin); Assert.Equal(1, api.LoginCalls);
        await model.Logout(); Assert.False(preferences.Value.Find(RememberedTarget)!.AutomaticLogin);
        Assert.Equal(1, api.LoginCalls);
    }

    [Fact]
    public async Task StartupAndManualActionsAreSerialized_OneLoginAndOneDefaultOpen()
    {
        var preferences = SavedLogin(company: B); var secrets = new FakeLoginCredentials(); secrets.Seed(RememberedTarget, "fixture-password");
        var login = new TaskCompletionSource<SessionResponse>(); var profile = new TaskCompletionSource<CompanyProfileResponse>();
        var api = new FakeApi { PendingLogin = login.Task, PendingProfile = profile.Task };
        using var model = RememberingModel(api, preferences, secrets);
        var startup = model.InitializeLogin(); await model.InitializeLogin(); await model.Login("another-fixture-password");
        Assert.Equal(1, api.LoginCalls); Assert.True(model.IsBusy);
        login.SetResult(new("operator", DateTimeOffset.UtcNow.AddMinutes(30), UserId: FixtureUserId));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (api.ProfileCalls.Count == 0) await Task.Delay(5, timeout.Token);
        await model.OpenCompany(); await model.ChangeCompany();
        Assert.Equal(B, Assert.Single(api.ProfileCalls));
        profile.SetResult(new(B, "Firma B", null, null)); await startup;
        Assert.Equal("Firma B", model.CurrentCompany); Assert.Equal(1, api.LoginCalls);
    }

    [Fact]
    public async Task UnavailableCredentialStoreKeepsManualLoginAndNeverFallsBackToAnotherPasswordStore()
    {
        var preferences = SavedLogin(); var secrets = new FakeLoginCredentials { Unavailable = true };
        var api = new FakeApi(); using var model = RememberingModel(api, preferences, secrets);
        await model.InitializeLogin(); Assert.True(model.IsLogin); Assert.Equal(0, api.LoginCalls); Assert.Contains("niedostępny", model.Message);
        await model.Login("fixture-password"); Assert.True(model.IsCompanySelection); Assert.Contains("niedostępny", model.Message);
        Assert.False(preferences.Value.Find(RememberedTarget)!.AutomaticLogin); Assert.False(preferences.Value.Find(RememberedTarget)!.RememberCredentials);
    }

    [Fact]
    public async Task MissingCredentialOrPreferencesDoesNotMakeNetworkAttempts()
    {
        var preferences = SavedLogin(); var secrets = new FakeLoginCredentials(); var api = new FakeApi();
        using var model = RememberingModel(api, preferences, secrets); await model.InitializeLogin();
        Assert.True(model.IsLogin); Assert.Equal(0, api.LoginCalls); Assert.Contains("Nie znaleziono", model.Message);
        var bad = new FakeLoginPreferences { FailLoad = true };
        using var second = RememberingModel(api, bad, secrets); await second.InitializeLogin();
        Assert.True(second.IsLogin); Assert.Equal(0, api.LoginCalls);
    }

    [Fact]
    public async Task LogoutPreferenceFailureRemovesCredentialRatherThanLeavingAutomaticLoginArmed()
    {
        var preferences = SavedLogin(); var secrets = new FakeLoginCredentials(); secrets.Seed(RememberedTarget, "fixture-password");
        using var model = RememberingModel(new FakeApi(), preferences, secrets); await model.InitializeLogin();
        preferences.FailSave = true; await model.Logout();
        Assert.True(model.IsLogin); Assert.False(secrets.Contains(RememberedTarget)); Assert.Contains("Usunięto", model.Message);
    }

    [Fact]
    public async Task DefaultCompanyDeniedAtProfileReadDoesNotOpenAnyOtherCompany()
    {
        var preferences = SavedLogin(company: B); var secrets = new FakeLoginCredentials(); secrets.Seed(RememberedTarget, "fixture-password");
        var api = new FakeApi { ProfileError = new ApiException(403, "Brak dostępu.") };
        using var model = RememberingModel(api, preferences, secrets); await model.InitializeLogin();
        Assert.True(model.IsCompanySelection); Assert.Equal(B, Assert.Single(api.ProfileCalls));
        Assert.Empty(model.CurrentCompany); Assert.Equal(B, preferences.Value.Find(RememberedTarget)!.DefaultCompanyId);
    }

    [Fact]
    public async Task LogoutJustAfterAbsoluteExpiryStillDisablesFutureAutomaticLogin()
    {
        var preferences = SavedLogin(); var secrets = new FakeLoginCredentials(); secrets.Seed(RememberedTarget, "fixture-password");
        var clock = new Clock(); var api = new FakeApi { Expires = clock.Now.AddMinutes(1) };
        using var model = RememberingModel(api, preferences, secrets, clock); await model.InitializeLogin();
        clock.Now = clock.Now.AddMinutes(2); await model.Logout();
        Assert.True(model.IsLogin); Assert.False(preferences.Value.Find(RememberedTarget)!.AutomaticLogin);
        Assert.Equal(1, api.LoginCalls);
    }

    [Fact]
    public async Task ForgetFailureDoesNotClaimDeletion_AndPasswordNeverRemainsInViewModelFields()
    {
        var preferences = new FakeLoginPreferences(); var secrets = new FakeLoginCredentials(); var api = new FakeApi();
        var password = "fixture-" + Guid.NewGuid().ToString("N");
        using var model = RememberingModel(api, preferences, secrets);
        model.RememberCredentials = true; model.AutomaticLogin = true; await model.Login(password);
        var stringFields = typeof(MainViewModel).GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            .Where(x => x.FieldType == typeof(string));
        Assert.DoesNotContain(stringFields, x => string.Equals((string?)x.GetValue(model), password, StringComparison.Ordinal));
        secrets.Unavailable = true; await model.ForgetLoginCredentials();
        Assert.Contains("niedostępny", model.Message); Assert.DoesNotContain("Usunięto", model.Message);
        Assert.False(preferences.Value.Find(RememberedTarget)!.AutomaticLogin); Assert.True(secrets.Contains(RememberedTarget));
        secrets.Unavailable = false; await model.ForgetLoginCredentials(); Assert.False(secrets.Contains(RememberedTarget));
    }
}
