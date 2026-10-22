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
    private void NewContractorClick(object? sender, RoutedEventArgs e) { model.NewContractor(); ContractorCodeInput.Focus(); }
    private async void EditContractorClick(object? sender, RoutedEventArgs e) { await model.EditContractor(); ContractorCodeInput.Focus(); }
    private async void ContractorDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (model.CanEditContractors) { await model.EditContractor(); ContractorCodeInput.Focus(); }
    }
    private async void SaveContractorClick(object? sender, RoutedEventArgs e) => await model.SaveContractor();
    private void CancelContractorClick(object? sender, RoutedEventArgs e) => model.CancelEditor();
    private void ReloadConflictClick(object? sender, RoutedEventArgs e) => model.ResolveConflict(false);
    private void KeepConflictClick(object? sender, RoutedEventArgs e) => model.ResolveConflict(true);
    private async void NextPageClick(object? sender, RoutedEventArgs e) => await model.NextPage();
    private async void PreviousPageClick(object? sender, RoutedEventArgs e) => await model.PreviousPage();
    private async void SearchKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { e.Handled = true; await model.LoadContractors(); }
    }
    private async void WindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (model.IsBusy) return;
        if (model.IsEditing && e.KeyModifiers == KeyModifiers.Control && e.Key == Key.S)
        { e.Handled = true; await model.SaveContractor(); }
        else if (model.IsEditing && e.Key == Key.Escape)
        { e.Handled = true; model.CancelEditor(); }
        else if (model.CanBrowse && e.KeyModifiers == KeyModifiers.Control && e.Key == Key.F)
        { e.Handled = true; SearchInput.Focus(); }
        else if (model.CanBrowse && model.CanEditContractors && e.KeyModifiers == KeyModifiers.Control && e.Key == Key.N)
        { e.Handled = true; model.NewContractor(); ContractorCodeInput.Focus(); }
        else if (model.CanBrowse && model.CanEditContractors && e.Key == Key.F2)
        { e.Handled = true; await model.EditContractor(); ContractorCodeInput.Focus(); }
    }
}
