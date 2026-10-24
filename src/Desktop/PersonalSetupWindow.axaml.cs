using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Interactivity;
using Vatowiec.Desktop.Services;
using Vatowiec.Desktop.ViewModels;
using Vatowiec.SharedContracts;

namespace Vatowiec.Desktop;

public partial class PersonalSetupWindow : Window
{
    private readonly IPersonalBackend backend;
    private readonly Action<MainWindow>? openLogin;
    private bool busy;
    public PersonalSetupWindow() : this(new PersonalBackend()) { }
    public PersonalSetupWindow(IPersonalBackend backend, Action<MainWindow>? openLogin = null)
    {
        this.backend = backend; this.openLogin = openLogin; InitializeComponent();
        Opened += async (_, _) => await Inspect();
        Closed += (_, _) => { SetupPassword.Text = ""; SetupPasswordConfirmation.Text = ""; };
    }
    private void Busy(bool value) { busy = value; SetupProgress.IsVisible = value; SetupForm.IsEnabled = !value; OpenPersonalLogin.IsEnabled = !value; RetryPersonalCheck.IsEnabled = !value; }
    private async Task Inspect()
    {
        if (busy) return; Busy(true);
        try
        {
            var status = await backend.Send(new()); ShowStatus(status);
            if (!status.Success) { SetupForm.IsEnabled = false; return; }
            SetupCompany.Text = status.CompanyName ?? ""; SetupNip.Text = status.Nip ?? "";
            SetupCompany.IsReadOnly = SetupNip.IsReadOnly = status.CompanyName != null;
            SetupForm.IsVisible = !status.Complete; OpenPersonalLogin.IsVisible = status.Complete;
            if (status.Complete) await OpenLogin();
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or TimeoutException or System.ComponentModel.Win32Exception)
        { SetupStatus.Text = ex.Message; RetryPersonalCheck.IsVisible = true; }
        finally { Busy(false); if (RetryPersonalCheck.IsVisible) SetupForm.IsEnabled = false; }
    }
    private void ShowStatus(PersonalSetupStatus status)
    {
        SetupStatus.Text = status.Message; RetryPersonalCheck.IsVisible = !status.Success;
    }
    private async void InitializeClick(object? sender, RoutedEventArgs e)
    {
        if (busy) return;
        if (string.IsNullOrEmpty(SetupPassword.Text) || SetupPassword.Text != SetupPasswordConfirmation.Text)
        { SetupStatus.Text = "Wpisz takie samo niepuste hasło w obu polach."; SetupPassword.Text = SetupPasswordConfirmation.Text = ""; return; }
        var command = new PersonalSetupCommand { Operation = "initialize", UserName = SetupLogin.Text, Password = SetupPassword.Text,
            CompanyName = SetupCompany.Text, Nip = SetupNip.Text };
        SetupPassword.Text = SetupPasswordConfirmation.Text = ""; Busy(true);
        try
        {
            var status = await backend.Send(command, ShowStatus); ShowStatus(status);
            if (status.Complete) { SetupForm.IsVisible = false; OpenPersonalLogin.IsVisible = true; }
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or TimeoutException)
        { SetupStatus.Text = ex.Message; RetryPersonalCheck.IsVisible = true; }
        finally { command.Password = null; Busy(false); }
    }
    private async Task OpenLogin()
    {
        var status = await backend.Send(new() { Operation = "serve" }); ShowStatus(status);
        if (!status.Success || status.Stage != "ready" || status.ApiAddress != "https://localhost:18443") return;
        var model = new MainViewModel(preferencesStore: new FileLoginPreferencesStore(Path.Combine(backend.Root, "login-preferences.json")),
            credentialStore: new WindowsLoginCredentialStore(), apiAddressOverride: status.ApiAddress);
        var login = new MainWindow(model) { Title = "VATowiec Personal" };
        if (openLogin != null) openLogin(login);
        else if (Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime lifetime)
        { lifetime.MainWindow = login; login.Show(); Close(); }
    }
    private async void LoginClick(object? sender, RoutedEventArgs e)
    {
        if (busy) return; Busy(true);
        try { await OpenLogin(); }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or TimeoutException) { SetupStatus.Text = ex.Message; }
        finally { Busy(false); }
    }
    private async void RetryClick(object? sender, RoutedEventArgs e) => await Inspect();
}
