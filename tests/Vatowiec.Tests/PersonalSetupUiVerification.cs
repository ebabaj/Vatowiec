using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia;
using Avalonia.Media.Imaging;
using Vatowiec.Core.Infrastructure.Personal;
using Vatowiec.Core.Infrastructure.Persistence;
using Vatowiec.Desktop;
using Vatowiec.Desktop.Services;
using Vatowiec.Desktop.ViewModels;
using Vatowiec.SharedContracts;
using static Vatowiec.Tests.InstallationSecurityUiTests;

namespace Vatowiec.Tests;

internal static class PersonalSetupUiVerification
{
    public static async Task Run(IClassicDesktopStyleApplicationLifetime lifetime)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        var root = PersonalSetupTests.NewRoot();
        PersonalDatabaseTarget target;
        var firstBackend = new PersonalBackend(root);
        using (var backend = firstBackend)
        {
            MainWindow? login = null;
            var wizard = new PersonalSetupWindow(backend, window => { login = window; window.Show(); });
            try
            {
                wizard.Show();
                await Wait(() => Find<Button>(wizard, "InitializePersonal").IsEffectivelyEnabled);
                Capture(wizard);
                Find<TextBox>(wizard, "PersonalSetupLogin").Text = "wizard.ui.fixture";
                Find<TextBox>(wizard, "PersonalSetupCompany").Text = "Fikcyjna firma z kreatora Avalonia";
                Assert.Contains("PRODUKCJA", Find<TextBlock>(wizard, "PersonalDefaultsNotice").Text);
                Find<TextBox>(wizard, "PersonalSetupPassword").Text = "";
                Find<TextBox>(wizard, "PersonalSetupPasswordConfirmation").Text = "";
                Click(wizard, "InitializePersonal");
                await Wait(() => Find<TextBlock>(wizard, "PersonalSetupStatus").Text!.Contains("niepuste"));
                Assert.Empty(Find<TextBox>(wizard, "PersonalSetupPassword").Text!);
                Assert.Empty(Find<TextBox>(wizard, "PersonalSetupPasswordConfirmation").Text!);
                Find<TextBox>(wizard, "PersonalSetupPassword").Text = "a";
                Find<TextBox>(wizard, "PersonalSetupPasswordConfirmation").Text = "a";
                Find<TextBox>(wizard, "PersonalSetupNip").Text = "1234567890";
                Click(wizard, "InitializePersonal");
                await Wait(() => Find<TextBlock>(wizard, "PersonalSetupStatus").Text!.Contains("NIP"));
                Find<TextBox>(wizard, "PersonalSetupNip").Text = "";
                Find<TextBox>(wizard, "PersonalSetupPassword").Text = "a";
                Find<TextBox>(wizard, "PersonalSetupPasswordConfirmation").Text = "a";
                Click(wizard, "InitializePersonal");
                await Wait(() => Find<Button>(wizard, "OpenPersonalLogin").IsEffectivelyVisible && Find<Button>(wizard, "OpenPersonalLogin").IsEffectivelyEnabled);
                Assert.Empty(Find<TextBox>(wizard, "PersonalSetupPassword").Text!);
                Click(wizard, "OpenPersonalLogin");
                await Wait(() => login?.IsVisible == true); wizard.Close();
                var model = (MainViewModel)login!.DataContext!;
                Assert.True(PersonalApiIdentity.TryParse(model.ApiAddress, out _));
                Find<TextBox>(login, "Login").Text = "wizard.ui.fixture";
                Find<TextBox>(login, "Password").Text = "a";
                Click(login, "LoginButton");
                await Wait(() => model.IsCompanySelection && !model.IsBusy);
                Assert.True(model.IsInstallationAdministrator);
                Assert.Equal("Fikcyjna firma z kreatora Avalonia", Assert.Single(model.Companies).Name);
                model.SelectedCompany = model.Companies[0]; await model.OpenCompany();
                Assert.Equal("Fikcyjna firma z kreatora Avalonia", model.CurrentCompany);
            }
            finally { wizard.Close(); login?.Close(); }
        }
        Assert.True(firstBackend.GracefulExit, "Personal API must stop gracefully when its desktop channel closes.");
        // A real second wizard window must skip editing and open normal login, with the same persisted identity.
        var secondBackend = new PersonalBackend(root);
        using (var backend = secondBackend)
        {
            MainWindow? login = null;
            var wizard = new PersonalSetupWindow(backend, window => { login = window; window.Show(); });
            try
            {
                wizard.Show(); await Wait(() => login?.IsVisible == true);
                Assert.False(Find<Button>(wizard, "InitializePersonal").IsEffectivelyVisible);
                var model = (MainViewModel)login!.DataContext!;
                Assert.True(model.IsLogin); // No saved password or automatic login was created by the wizard.
                Find<TextBox>(login, "Login").Text = "wizard.ui.fixture";
                Find<TextBox>(login, "Password").Text = "a";
                Click(login, "LoginButton");
                await Wait(() => !model.IsLogin && !model.IsBusy);
                Assert.Single(model.Companies);
            }
            finally { wizard.Close(); login?.Close(); }
        }
        Assert.True(secondBackend.GracefulExit, "Restarted Personal API must stop gracefully as well.");
        using (var installation = new PersonalInstallation(root))
        { Assert.True((await installation.Status()).Complete); target = installation.Target; }
        await PersonalInstallation.LocalDb("stop", target.Instance);
    }
    private static void Capture(Window window)
    {
        Directory.CreateDirectory(PersonalLocalPaths.VerificationDirectory);
        using var bitmap = new RenderTargetBitmap(new PixelSize((int)window.Bounds.Width, (int)window.Bounds.Height), new Vector(96, 96));
        bitmap.Render(window);
        bitmap.Save(Path.Combine(PersonalLocalPaths.VerificationDirectory, "personal-wizard.png"), PngBitmapEncoderOptions.Default);
    }
    private static async Task Wait(Func<bool> ready)
    {
        var deadline = DateTime.UtcNow.AddSeconds(90);
        while (!ready()) { if (DateTime.UtcNow > deadline) throw new TimeoutException("Personal native Avalonia verification did not complete."); await Task.Delay(30); }
    }
}
