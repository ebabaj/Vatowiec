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
        Opened += (_, _) =>
        {
            var screen = Screens.ScreenFromWindow(this);
            if (screen is not null)
            {
                MaxHeight = screen.WorkingArea.Height / screen.Scaling;
                MaxWidth = screen.WorkingArea.Width / screen.Scaling;
                Height = Math.Min(Height, MaxHeight);
                Width = Math.Min(Width, MaxWidth);
            }
            LoginInput.Focus();
        };
        var confirmedClose = false;
        Closing += (_, e) =>
        {
            if (confirmedClose) return;
            if (model.IsBusy) { e.Cancel = true; return; }
            if (!model.HasUnsavedChanges) return;
            e.Cancel = true;
            model.RequestDiscard(() => { confirmedClose = true; Close(); });
        };
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
        if (model.CanViewContractors) { await model.EditContractor(); ContractorCodeInput.Focus(); }
    }
    private async void SaveContractorClick(object? sender, RoutedEventArgs e) => await model.SaveContractor();
    private void CancelContractorClick(object? sender, RoutedEventArgs e) => model.CancelEditor();
    private void ReloadConflictClick(object? sender, RoutedEventArgs e) => model.ResolveConflict(false);
    private void KeepConflictClick(object? sender, RoutedEventArgs e) => model.ResolveConflict(true);
    private async void NextPageClick(object? sender, RoutedEventArgs e) => await model.NextPage();
    private async void PreviousPageClick(object? sender, RoutedEventArgs e) => await model.PreviousPage();
    private async void SearchParentsClick(object? sender, RoutedEventArgs e) => await model.SearchParents();
    private async void NextParentsClick(object? sender, RoutedEventArgs e) => await model.NextParents();
    private async void PreviousParentsClick(object? sender, RoutedEventArgs e) => await model.PreviousParents();
    private void ChooseParentClick(object? sender, RoutedEventArgs e) => model.ChooseParent();
    private void ClearParentClick(object? sender, RoutedEventArgs e) => model.ClearParent();
    private void OpenChildClick(object? sender, RoutedEventArgs e) => model.OpenSelectedChild();
    private void ChildDoubleTapped(object? sender, TappedEventArgs e) => model.OpenSelectedChild();
    private async void NextChildrenClick(object? sender, RoutedEventArgs e) => await model.NextChildren();
    private async void PreviousChildrenClick(object? sender, RoutedEventArgs e) => await model.PreviousChildren();
    private void NewDetailClick(object? sender, RoutedEventArgs e) => model.NewDetail();
    private async void EditDetailClick(object? sender, RoutedEventArgs e) => await model.EditDetail();
    private async void DetailDoubleTapped(object? sender, TappedEventArgs e) => await model.EditDetail();
    private async void SaveDetailClick(object? sender, RoutedEventArgs e) => await model.SaveDetail();
    private void CancelDetailClick(object? sender, RoutedEventArgs e) => model.CancelDetail();
    private async void RefreshDetailsClick(object? sender, RoutedEventArgs e) => await model.LoadDetails();
    private async void NextDetailsClick(object? sender, RoutedEventArgs e) => await model.NextDetails();
    private async void PreviousDetailsClick(object? sender, RoutedEventArgs e) => await model.PreviousDetails();
    private async void AddAddressTypeClick(object? sender, RoutedEventArgs e) => await model.AddAddressType();
    private async void AcceptBankWarningClick(object? sender, RoutedEventArgs e) => await model.SaveDetail(true);
    private void CancelBankWarningClick(object? sender, RoutedEventArgs e) => model.CancelBankWarning();
    private void ReloadDetailConflictClick(object? sender, RoutedEventArgs e) => model.ResolveDetailConflict(false);
    private void KeepDetailConflictClick(object? sender, RoutedEventArgs e) => model.ResolveDetailConflict(true);
    private void DiscardClick(object? sender, RoutedEventArgs e) => model.ConfirmDiscard();
    private void KeepEditingClick(object? sender, RoutedEventArgs e) => model.KeepEditing();
    private async void SearchKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { e.Handled = true; await model.LoadContractors(); }
    }
    private async void WindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (model.IsBusy) return;
        if (model.DiscardPrompt) { if (e.Key == Key.Escape) model.KeepEditing(); e.Handled = true; return; }
        if (model.IsEditing && !model.IsMainTab)
        {
            if (e.KeyModifiers == KeyModifiers.Control && e.Key == Key.S) { e.Handled = true; await model.SaveDetail(); }
            else if (e.KeyModifiers == KeyModifiers.Control && e.Key == Key.N) { e.Handled = true; model.NewDetail(); }
            else if (e.Key == Key.F2) { e.Handled = true; await model.EditDetail(); }
            else if (e.Key == Key.Escape) { e.Handled = true; if (model.IsDetailEditing) model.CancelDetail(); else model.CancelEditor(); }
            return;
        }
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
