using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Interactivity;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Vatowiec.Desktop;
using Vatowiec.Desktop.Services;
using Vatowiec.Desktop.ViewModels;
using Vatowiec.SharedContracts;

namespace Vatowiec.Tests;

[Collection("LocalSql")]
public sealed class InstallationSecurityUiTests
{
    [RealSecurityUiFact]
    public async Task NativeAvalonia_PolicyBindingsDisableConfirmationAndRoleGrantRevoke()
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                AppBuilder.Configure<SecurityTestApp>().UsePlatformDetect().AfterSetup(_ =>
                    Dispatcher.UIThread.Post(async () =>
                    {
                        var lifetime = (IClassicDesktopStyleApplicationLifetime)Application.Current!.ApplicationLifetime!;
                        var api = new SecurityApi();
                        using var model = new MainViewModel(_ => api) { UserName = "fixture-admin" };
                        var window = new MainWindow(model); lifetime.MainWindow = window;
                        try
                        {
                            window.Show(); await Task.Delay(100);
                            await model.Login("fixture-memory-only");
                            Assert.True(model.IsInstallationAdministrator);
                            await model.OpenPasswordPolicy();
                            await Until(() => window.GetVisualDescendants().OfType<CheckBox>()
                                .Any(x => AutomationProperties.GetAutomationId(x) == "PasswordPolicyEnabled" && x.IsEffectivelyVisible));
                            Find<NumericUpDown>(window, "PasswordMinimumLength").Value = 8;
                            Find<CheckBox>(window, "PasswordRequireUppercase").IsChecked = false;
                            Find<CheckBox>(window, "PasswordRequireDigit").IsChecked = false;
                            Assert.Equal(8, model.PasswordPolicyEditor!.MinimumLength);
                            Assert.True(model.PasswordPolicyEditor.RequireLowercase && model.PasswordPolicyEditor.RequireSpecialCharacter);
                            Assert.True(model.HasUnsavedChanges);
                            Click(window, "SavePasswordPolicy"); await Until(() => api.PolicyWrites == 1 && !model.IsBusy);
                            Assert.False(model.HasUnsavedChanges);
                            Find<CheckBox>(window, "PasswordPolicyEnabled").IsChecked = false;
                            Assert.False(Find<NumericUpDown>(window, "PasswordMinimumLength").IsEffectivelyEnabled);
                            Click(window, "SavePasswordPolicy"); await Dialog(lifetime, "CancelAdministration");
                            Assert.Equal(1, api.PolicyWrites); Assert.True(model.HasUnsavedChanges);
                            Click(window, "SavePasswordPolicy"); await Dialog(lifetime, "ConfirmAdministration");
                            await Until(() => api.PolicyWrites == 2 && !model.IsBusy);
                            Assert.False(api.Policy.IsEnabled); Assert.True(api.DisableConfirmed);
                            Find<CheckBox>(window, "PreventPasswordReuse").IsChecked = true;
                            Find<NumericUpDown>(window, "PreviousPasswordCount").Value = 2;
                            Find<CheckBox>(window, "EnforcePasswordExpiration").IsChecked = true;
                            Find<NumericUpDown>(window, "PasswordValidityDays").Value = 30;
                            Assert.True(Find<NumericUpDown>(window, "PreviousPasswordCount").IsEffectivelyEnabled);
                            Assert.True(model.CanSavePasswordPolicy);
                            Click(window, "SavePasswordPolicy"); await Dialog(lifetime, "ConfirmAdministration");
                            await Until(() => api.PolicyWrites == 3 && !model.IsBusy);
                            Assert.True(api.Policy.PreventPasswordReuse && api.Policy.EnforcePasswordExpiration);
                            Assert.Equal(2, api.Policy.PreviousPasswordCount); Assert.Equal(30, api.Policy.PasswordValidityDays);
                            Find<CheckBox>(window, "PasswordPolicyEnabled").IsChecked = true;
                            Click(window, "ClosePasswordPolicy"); Assert.True(model.DiscardPrompt);
                            model.KeepEditing(); Assert.True(model.IsPasswordPolicyScreen);
                            Click(window, "ClosePasswordPolicy"); model.ConfirmDiscard();
                            await model.OpenUsersSettings();
                            await Until(() => window.GetVisualDescendants().OfType<ListBox>()
                                .Any(x => AutomationProperties.GetAutomationId(x) == "UsersList" && x.IsEffectivelyVisible));
                            Find<ListBox>(window, "UsersList").SelectedIndex = 0;
                            await Until(() => model.HasAdminUser && !model.IsBusy);
                            Find<CheckBox>(window, "AdministratorRole").IsChecked = true;
                            Assert.True(model.HasRoleChanges && model.CanSaveAdministratorRole);
                            Click(window, "SaveAdministratorRole"); await Dialog(lifetime, "CancelAdministration");
                            Assert.Equal(0, api.RoleWrites); Assert.True(model.HasRoleChanges);
                            api.FailRole = true;
                            Click(window, "SaveAdministratorRole"); await Dialog(lifetime, "ConfirmAdministration");
                            await Until(() => !model.IsBusy && model.Message.Contains("Nie potwierdzono zapisu", StringComparison.Ordinal));
                            Assert.False(Find<CheckBox>(window, "AdministratorRole").IsChecked);
                            Assert.False(Find<CheckBox>(window, "AdministratorRole").IsEffectivelyEnabled);
                            Assert.Contains("nie został potwierdzony", Find<TextBlock>(window, "AdminRoleStatus").Text);
                            api.FailRole = false; await model.RefreshAdminUser();
                            Find<CheckBox>(window, "AdministratorRole").IsChecked = true;
                            Click(window, "SaveAdministratorRole"); await Dialog(lifetime, "ConfirmAdministration");
                            await Until(() => api.RoleWrites == 1 && !model.IsBusy);
                            Assert.True(api.Target.User.IsInstallationAdministrator);
                            Assert.Empty(api.Target.Access); Assert.False(model.HasUnsavedChanges);
                            Find<CheckBox>(window, "AdministratorRole").IsChecked = false;
                            Click(window, "SaveAdministratorRole"); await Dialog(lifetime, "ConfirmAdministration");
                            await Until(() => api.RoleWrites == 2 && !model.IsBusy);
                            Assert.False(api.Target.User.IsInstallationAdministrator);
                            Assert.False(model.HasUnsavedChanges);
                            // Generated reset retains the one-time result window with Show and Copy.
                            Click(window, "ResetUserPassword");
                            await Until(() => lifetime.Windows.Any(x => x.Title == "Resetuj hasło"));
                            Click(lifetime.Windows.Single(x => x.Title == "Resetuj hasło"), "AcceptTemporaryPassword");
                            await Until(() => lifetime.Windows.Any(x => x.Title == "Jednorazowe hasło tymczasowe"));
                            var generatedWindow = lifetime.Windows.Single(x => x.Title == "Jednorazowe hasło tymczasowe");
                            Assert.NotNull(Find<Button>(generatedWindow, "CopyTemporaryPassword"));
                            Assert.NotNull(Find<Button>(generatedWindow, "ShowTemporaryPassword"));
                            Click(generatedWindow, "CloseTemporaryPassword");
                            // A manually supplied reset never reopens a window containing the password.
                            Click(window, "ResetUserPassword");
                            await Until(() => lifetime.Windows.Any(x => x.Title == "Resetuj hasło"));
                            var customWindow = lifetime.Windows.Single(x => x.Title == "Resetuj hasło");
                            Find<CheckBox>(customWindow, "UseCustomTemporaryPassword").IsChecked = true;
                            Find<TextBox>(customWindow, "CustomTemporaryPassword").Text = "CustomFixture!2a";
                            Click(customWindow, "AcceptTemporaryPassword");
                            await Until(() => !model.IsBusy && model.Message.StartsWith("Hasło użytkownika zostało zmienione.", StringComparison.Ordinal));
                            Assert.Equal("Hasło użytkownika zostało zmienione. Przy następnym logowaniu użytkownik będzie musiał ustawić własne hasło", model.Message);
                            Assert.DoesNotContain(lifetime.Windows, x => x.Title == "Jednorazowe hasło tymczasowe");
                            if (Environment.GetEnvironmentVariable("VATOWIEC_REAL_POLICY_API") == "1")
                            {
                                Assert.Equal("1", Environment.GetEnvironmentVariable("VATOWIEC_PRESERVED_VAT_SQL"));
                                await UserAdministrationSqlTests.VerifyPolicyThroughNativeWindow(lifetime);
                            }
                            finished.TrySetResult();
                        }
                        catch (Exception e) { finished.TrySetException(e); }
                        finally
                        {
                            foreach (var dialog in lifetime.Windows.Where(x => x != window).ToArray()) dialog.Close();
                            if (model.HasUnsavedChanges) { window.Close(); model.ConfirmDiscard(); }
                            else window.Close();
                            lifetime.Shutdown();
                        }
                    })).StartWithClassicDesktopLifetime([], ShutdownMode.OnExplicitShutdown);
            }
            catch (Exception e) { finished.TrySetException(e); }
        }) { IsBackground = true, Name = "Avalonia installation security verification" };
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        await finished.Task.WaitAsync(TimeSpan.FromSeconds(40));
        Assert.True(thread.Join(TimeSpan.FromSeconds(5)));
    }
    internal static T Find<T>(Window window, string id) where T : Control => window.GetVisualDescendants().OfType<T>()
        .Single(x => AutomationProperties.GetAutomationId(x) == id);
    internal static void Click(Window window, string id)
    {
        var button = Find<Button>(window, id); Assert.True(button.IsEffectivelyEnabled);
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    }
    internal static async Task Until(Func<bool> predicate)
    {
        var until = DateTime.UtcNow.AddSeconds(5);
        while (!predicate()) { if (DateTime.UtcNow > until) throw new TimeoutException("Avalonia operation did not finish."); await Task.Delay(15); }
    }
    internal static async Task Dialog(IClassicDesktopStyleApplicationLifetime lifetime, string action)
    {
        await Until(() => lifetime.Windows.Any(x => x.Title == "Potwierdzenie operacji"));
        var dialog = lifetime.Windows.Single(x => x.Title == "Potwierdzenie operacji");
        Click(dialog, action); await Until(() => !lifetime.Windows.Contains(dialog));
    }
    public sealed class SecurityTestApp : Application
    {
        public override void Initialize() => Styles.Add(new FluentTheme());
    }
    private sealed class SecurityApi : IVatowiecApiClient
    {
        public PasswordPolicyResponse Policy = new(true, 12, true, true, true, true, "initial");
        public UserAdministrationDetail Target = new(new(Guid.NewGuid(), "fixture-target", false, false, false), "initial", []);
        public int PolicyWrites, RoleWrites;
        public bool DisableConfirmed;
        public bool FailRole;
        public Task<SessionResponse> Login(string u, string p) => Task.FromResult(new SessionResponse(u, DateTimeOffset.UtcNow.AddMinutes(30), true, UserId: Guid.Parse("dddddddd-0000-4000-8000-000000000001")));
        public Task<CompanyResponse[]> Companies() => Task.FromResult<CompanyResponse[]>([]);
        public Task<PasswordPolicyResponse> PasswordPolicy() => Task.FromResult(Policy);
        public Task<PasswordPolicyResponse> SavePasswordPolicy(PasswordPolicyRequest v)
        {
            Assert.Equal(Policy.RowVersion, v.RowVersion); PolicyWrites++; DisableConfirmed = v.ConfirmDisable;
            Policy = new(v.IsEnabled, v.MinimumLength, v.RequireUppercase, v.RequireLowercase, v.RequireDigit, v.RequireSpecialCharacter, PolicyWrites.ToString(),
                v.PreventPasswordReuse, v.PreviousPasswordCount, v.EnforcePasswordExpiration, v.PasswordValidityDays);
            return Task.FromResult(Policy);
        }
        public Task<PasswordPolicyHistory[]> PasswordPolicyHistory(int offset) => Task.FromResult<PasswordPolicyHistory[]>([]);
        public Task<UserAdministrationLookups> UserLookups() => Task.FromResult(new UserAdministrationLookups([], []));
        public Task<UserSummary[]> Users(string search, int offset, bool includeArchived = false) => Task.FromResult(new[] { Target.User });
        public Task<UserAdministrationDetail> UserDetail(Guid id) => Task.FromResult(Target);
        public Task<UserAdministrationHistory[]> UserHistory(Guid? id, int offset) => Task.FromResult<UserAdministrationHistory[]>([]);
        public Task<UserAdministrationDetail> SaveAdministratorRole(Guid id, AdministratorRoleRequest value)
        {
            if (FailRole) throw new ApiException(404, "API nie udostępnia operacji zarządzania użytkownikami.");
            Assert.True(value.Confirm); Assert.Equal(Target.ConcurrencyStamp, value.ConcurrencyStamp); RoleWrites++;
            Target = Target with { User = Target.User with { IsInstallationAdministrator = value.IsAdministrator }, ConcurrencyStamp = RoleWrites.ToString() };
            return Task.FromResult(Target);
        }
        public Task<TemporaryPasswordResponse> ResetUserPassword(Guid id, UserVersionRequest value)
        {
            Target = Target with { User = Target.User with { MustChangePassword = true }, ConcurrencyStamp = Guid.NewGuid().ToString() };
            return Task.FromResult(new TemporaryPasswordResponse(Target, value.TemporaryPassword ?? "AbcD2345EfGh678!", value.TemporaryPassword == null));
        }
        public Task<CompanyProfileResponse> Profile(Guid companyId) => throw new NotSupportedException();
        public Task<ContractorResponse[]> Contractors(Guid companyId, string? search = null, string? country = null, bool? active = null, int offset = 0, int limit = 50) => throw new NotSupportedException();
        public Task<ContractorResponse> Contractor(Guid companyId, int id) => throw new NotSupportedException();
        public Task<ContractorResponse> SaveContractor(Guid companyId, ContractorWriteRequest value, bool isNew) => throw new NotSupportedException();
        public Task Logout() => Task.CompletedTask;
        public void Dispose() { }
    }
}

public sealed class RealSecurityUiFactAttribute : FactAttribute
{
    public RealSecurityUiFactAttribute()
    {
        if (!OperatingSystem.IsWindows() || Environment.GetEnvironmentVariable("VATOWIEC_REAL_UI") != "1")
            Skip = "Native Avalonia verification requires Windows and VATOWIEC_REAL_UI=1.";
    }
}
