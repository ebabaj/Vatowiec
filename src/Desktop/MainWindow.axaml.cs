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
        model.PropertyChanged += (_, _) => { if (!model.IsDataPortSettings) DataPortKeyInput.Text = ""; };
        model.PropertyChanged += (_, _) => { if (!model.IsGusSettings || !model.IsGusServiceSelected || model.IsBusy) GusKeyInput.Text = ""; };
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
    private void FinanceDirectionSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is not ComboBox box || !(box.IsDropDownOpen || box.IsKeyboardFocusWithin)) return;
        model.FinanceDirectionIndex = box.SelectedIndex;
        // A guarded/rejected switch must also restore the control's visual value.
        if (box.SelectedIndex != model.FinanceDirectionIndex)
            box.SetCurrentValue(Avalonia.Controls.Primitives.SelectingItemsControl.SelectedIndexProperty, model.FinanceDirectionIndex);
    }
    // A rebinding ComboBox may temporarily select its placeholder. Only an
    // interactive choice edits the code; the server snapshot drives presentation.
    private void TradePaymentSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is ComboBox { SelectedItem: Vatowiec.SharedContracts.PaymentMethodResponse item } box &&
            (box.IsDropDownOpen || box.IsKeyboardFocusWithin)) model.SelectedTradePayment = item;
    }
    private void TradeCurrencySelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is ComboBox { SelectedItem: Vatowiec.SharedContracts.FinanceCurrency item } box &&
            (box.IsDropDownOpen || box.IsKeyboardFocusWithin)) model.SelectedTradeCurrency = item;
    }
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
    private async void ValidateIdentifiersClick(object? sender, RoutedEventArgs e) => await model.CheckIdentifiers();
    private async void ValidateBankAccountClick(object? sender, RoutedEventArgs e) => await model.ValidateBankAccount();
    private void ApplyBankNameClick(object? sender, RoutedEventArgs e) => model.ApplyBankNameProposal();
    private void ApplyBankBicClick(object? sender, RoutedEventArgs e) => model.ApplyBankBicProposal();
    private async void AcceptIdentifiersClick(object? sender, RoutedEventArgs e) => await model.AcceptIdentifierWarning();
    private async void ViesClick(object? sender, RoutedEventArgs e) => await model.VerifyRegistry("Vies");
    private async void GusClick(object? sender, RoutedEventArgs e) => await model.VerifyRegistry("Gus");
    private async void DataPortClick(object? sender, RoutedEventArgs e) => await model.VerifyRegistry("DataPort");
    private async void DataPortSettingsClick(object? sender, RoutedEventArgs e) => await model.OpenDataPortSettings();
    private async void GusSettingsClick(object? sender, RoutedEventArgs e) => await model.OpenGusSettings();
    private async void SaveRegistrySettingsClick(object? sender, RoutedEventArgs e) => await model.SaveRegistryConfiguration();
    private async void RefreshRegistrySettingsClick(object? sender, RoutedEventArgs e) => await model.RefreshRegistryConfigurations();
    private async void SaveGusKeyClick(object? sender, RoutedEventArgs e)
    {
        var key = GusKeyInput.Text ?? ""; GusKeyInput.Text = "";
        await model.SaveGusKey(key);
    }
    private async void RefreshGusStatusClick(object? sender, RoutedEventArgs e) { GusKeyInput.Text = ""; await model.RefreshGusStatus(); }
    private void RequestGusDeleteClick(object? sender, RoutedEventArgs e) { GusKeyInput.Text = ""; model.RequestGusDelete(); }
    private async void DeleteGusKeyClick(object? sender, RoutedEventArgs e) => await model.DeleteGusKey();
    private void CancelGusDeleteClick(object? sender, RoutedEventArgs e) => model.CancelGusDelete();
    private void CloseGusSettingsClick(object? sender, RoutedEventArgs e) => model.CloseGusSettings();
    private void GusWebsiteClick(object? sender, RoutedEventArgs e)
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("https://api.stat.gov.pl/Home/RegonApi") { UseShellExecute = true }); }
        catch (System.ComponentModel.Win32Exception) { }
    }
    private async void CurrenciesClick(object? sender, RoutedEventArgs e) => await model.OpenCurrencies();
    private async void CurrencySettingsClick(object? sender, RoutedEventArgs e) => await model.OpenCurrencies(true);
    private void CurrencyRatesTabClick(object? sender, RoutedEventArgs e) => model.ShowCurrencySettings(false);
    private void CurrencySettingsTabClick(object? sender, RoutedEventArgs e) => model.ShowCurrencySettings(true);
    private void CloseCurrenciesClick(object? sender, RoutedEventArgs e) => model.CloseCurrencies();
    private async void SaveCurrencyClick(object? sender, RoutedEventArgs e) => await model.SaveCurrencySettings();
    private async void RefreshCurrenciesClick(object? sender, RoutedEventArgs e) => await model.RefreshCurrencies();
    private void CancelCurrencyClick(object? sender, RoutedEventArgs e) => model.CancelCurrencyChanges();
    private async void ImportNbpClick(object? sender, RoutedEventArgs e) => await model.ImportNbpRates();
    private async void FilterRatesClick(object? sender, RoutedEventArgs e) => await model.LoadRates();
    private async void PreviousRatesClick(object? sender, RoutedEventArgs e) => await model.PreviousRates();
    private async void NextRatesClick(object? sender, RoutedEventArgs e) => await model.NextRates();
    private async void ClearRateFiltersClick(object? sender, RoutedEventArgs e) { model.ClearRateFilters(); await model.LoadRates(); }
    private async void KeepRateClick(object? sender, RoutedEventArgs e) => await model.ResolveNbpConflict(false);
    private async void AcceptRateClick(object? sender, RoutedEventArgs e) => await model.ResolveNbpConflict(true);
    private async void CurrencyHistoryClick(object? sender, RoutedEventArgs e) => await model.LoadCurrencyHistory();
    private async void PreviousCurrencyHistoryClick(object? sender, RoutedEventArgs e) => await model.PreviousCurrencyHistory();
    private async void NextCurrencyHistoryClick(object? sender, RoutedEventArgs e) => await model.NextCurrencyHistory();
    private async void SaveDataPortKeyClick(object? sender, RoutedEventArgs e)
    {
        var key = DataPortKeyInput.Text ?? "";
        DataPortKeyInput.Text = "";
        await model.SaveDataPortKey(key);
    }
    private async void RefreshDataPortStatusClick(object? sender, RoutedEventArgs e) { DataPortKeyInput.Text = ""; await model.RefreshDataPortStatus(); }
    private void RequestDataPortDeleteClick(object? sender, RoutedEventArgs e) { DataPortKeyInput.Text = ""; model.RequestDataPortDelete(); }
    private async void DeleteDataPortKeyClick(object? sender, RoutedEventArgs e) => await model.DeleteDataPortKey();
    private void CancelDataPortDeleteClick(object? sender, RoutedEventArgs e) => model.CancelDataPortDelete();
    private void CloseDataPortSettingsClick(object? sender, RoutedEventArgs e) => model.CloseDataPortSettings();
    private void DataPortWebsiteClick(object? sender, RoutedEventArgs e)
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("https://dataport.pl/api") { UseShellExecute = true }); }
        catch (System.ComponentModel.Win32Exception) { /* URL remains visible for manual navigation. */ }
    }
    private async void MfTaxpayerClick(object? sender, RoutedEventArgs e) => await model.VerifyRegistry("MfTaxpayer");
    private async void MfStatusClick(object? sender, RoutedEventArgs e) => await model.VerifyRegistry("MfStatus");
    private async void MfAccountClick(object? sender, RoutedEventArgs e) => await model.VerifyRegistry("MfAccount");
    private async void VerificationsClick(object? sender, RoutedEventArgs e) => await model.LoadVerifications();
    private async void PreviousVerificationsClick(object? sender, RoutedEventArgs e) => await model.PreviousVerifications();
    private async void NextVerificationsClick(object? sender, RoutedEventArgs e) => await model.NextVerifications();
    private void ApplyGusClick(object? sender, RoutedEventArgs e) => model.ApplyGusSelection();
    private async void SaveTermsClick(object? sender, RoutedEventArgs e) => await model.SaveTradeTerms();
    private void CancelTermsClick(object? sender, RoutedEventArgs e) => model.CancelTradeTerms();
    private void RefreshFinanceClick(object? sender, RoutedEventArgs e) => model.RefreshFinance();
    private void NewLimitClick(object? sender, RoutedEventArgs e) => model.NewCreditLimit();
    private void EditLimitClick(object? sender, RoutedEventArgs e) => model.EditCreditLimit();
    private async void SaveLimitClick(object? sender, RoutedEventArgs e) => await model.SaveCreditLimit();
    private void CancelLimitClick(object? sender, RoutedEventArgs e) => model.CancelCreditLimit();
    private async void NextLimitsClick(object? sender, RoutedEventArgs e) => await model.NextCreditLimits();
    private async void PreviousLimitsClick(object? sender, RoutedEventArgs e) => await model.PreviousCreditLimits();
    private void NewPaymentMethodClick(object? sender, RoutedEventArgs e) => model.NewPaymentMethod();
    private void EditPaymentMethodClick(object? sender, RoutedEventArgs e) => model.EditPaymentMethod();
    private async void SavePaymentMethodClick(object? sender, RoutedEventArgs e) => await model.SavePaymentMethod();
    private void CancelPaymentMethodClick(object? sender, RoutedEventArgs e) => model.CancelPaymentMethod();
    private void ReloadFinanceConflictClick(object? sender, RoutedEventArgs e) => model.ResolveFinanceConflict(false);
    private void KeepFinanceConflictClick(object? sender, RoutedEventArgs e) => model.ResolveFinanceConflict(true);
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
        if (model.IsCurrencyScreen)
        {
            if (e.Key == Key.Escape) { e.Handled = true; model.CloseCurrencies(); }
            else if (model.IsCurrencySettings && e.KeyModifiers == KeyModifiers.Control && e.Key == Key.S) { e.Handled = true; await model.SaveCurrencySettings(); }
            return;
        }
        if (model.IsEditing && !model.IsMainTab)
        {
            if (model.IsFinanceTab)
            {
                if (e.KeyModifiers == KeyModifiers.Control && e.Key == Key.S) { e.Handled = true; await model.SaveTradeTerms(); }
                else if (e.KeyModifiers == KeyModifiers.Control && e.Key == Key.N) { e.Handled = true; model.NewCreditLimit(); }
                else if (e.Key == Key.F2) { e.Handled = true; model.EditCreditLimit(); }
                else if (e.Key == Key.Escape) { e.Handled = true; model.CancelEditor(); }
                return;
            }
            if (model.IsVerificationsTab) { if (e.Key == Key.Escape) model.CancelEditor(); return; }
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
