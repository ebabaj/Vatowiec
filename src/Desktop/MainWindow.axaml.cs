using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Vatowiec.Desktop.ViewModels;

namespace Vatowiec.Desktop;

public partial class MainWindow : Window
{
    private readonly MainViewModel model = new();
    private readonly DispatcherTimer expiryTimer = new() { Interval = TimeSpan.FromSeconds(1) };

    public MainWindow()
    {
        InitializeComponent();
        DataContext = model;
        expiryTimer.Tick += (_, _) => model.CheckExpiry();
        expiryTimer.Start();
        Closed += (_, _) => { expiryTimer.Stop(); model.Dispose(); };
        Opened += (_, _) => LoginInput.Focus();
    }

    private async Task Login()
    {
        var password = PasswordInput.Text ?? "";
        PasswordInput.Text = "";
        await model.Login(password);
    }

    private async void LoginClick(object? sender, RoutedEventArgs e) => await Login();
    private async void PasswordKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        await Login();
    }
    private async void OpenCompanyClick(object? sender, RoutedEventArgs e) => await model.OpenCompany();
    private async void RefreshCompaniesClick(object? sender, RoutedEventArgs e) => await model.RefreshCompanies();
    private async void ContractorsClick(object? sender, RoutedEventArgs e) => await model.LoadContractors();
    private async void ChangeCompanyClick(object? sender, RoutedEventArgs e) => await model.ChangeCompany();
    private async void LogoutClick(object? sender, RoutedEventArgs e) => await model.Logout();
}
