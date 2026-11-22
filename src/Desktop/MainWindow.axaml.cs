using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Vatowiec.Desktop.ViewModels;
using Vatowiec.Desktop.Services;
using Vatowiec.SharedContracts;

namespace Vatowiec.Desktop;

public partial class MainWindow : Window
{
    private readonly MainViewModel model;
    private readonly DispatcherTimer expiryTimer = new() { Interval = TimeSpan.FromSeconds(1) };

    public MainWindow() : this(new MainViewModel(preferencesStore: new FileLoginPreferencesStore(), credentialStore: new WindowsLoginCredentialStore())) { }

    public MainWindow(MainViewModel model)
    {
        this.model = model;
        InitializeComponent();
        DataContext = model;
        model.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(MainViewModel.ApiAddress) or nameof(MainViewModel.UserName)) PasswordInput.Text = "";
            if (!model.IsOwnPasswordChange) ClearOwnPasswords();
        };
        Activated += (_, _) => model.RefreshSavedPasswordState();
        model.PropertyChanged += (_, _) => { if (!model.IsDataPortSettings) DataPortKeyInput.Text = ""; };
        model.PropertyChanged += (_, _) => { if (!model.IsGusSettings || !model.IsGusServiceSelected || model.IsBusy) GusKeyInput.Text = ""; };
        expiryTimer.Tick += (_, _) => model.CheckExpiry();
        expiryTimer.Start();
        // Siatka bezpieczeństwa dla wyjątków spoza UiActions (np. obsługi w kontrolkach): komunikat
        // w oknie zamiast zamknięcia programu z utratą niezapisanego szkicu (ADR-046).
        Dispatcher.UIThread.UnhandledException += UnhandledUiException;
        Closed += (_, _) => { Dispatcher.UIThread.UnhandledException -= UnhandledUiException; expiryTimer.Stop(); model.Dispose(); };
        Opened += (_, _) => Run(async () =>
        {
            var screen = Screens.ScreenFromWindow(this);
            if (screen is not null)
            {
                MaxHeight = screen.WorkingArea.Height / screen.Scaling;
                MaxWidth = screen.WorkingArea.Width / screen.Scaling;
                Height = Math.Min(Height, MaxHeight);
                Width = Math.Min(Width, MaxWidth);
            }
            await model.InitializeLogin();
            if (model.IsLogin) LoginInput.Focus();
        });
        var confirmedClose = false;
        Closing += (_, e) =>
        {
            if (confirmedClose) return;
            if (model.IsBusy || model.IsTableLayoutSaving) { e.Cancel = true; return; }
            if (!model.HasUnsavedChanges) return;
            e.Cancel = true;
            model.RequestDiscard(() => { confirmedClose = true; Close(); });
        };
    }

    private void Run(Func<Task> action) => UiActions.Run(action, model.ReportUnexpectedError);

    private void UnhandledUiException(object? sender, DispatcherUnhandledExceptionEventArgs e)
    {
        System.Diagnostics.Trace.TraceError("VATowiec unhandled UI exception: {0}", e.Exception.GetType().FullName);
        model.ReportUnexpectedError(e.Exception);
        e.Handled = true;
    }

    private void CompaniesDoubleTapped(object? sender, TappedEventArgs e) => Run(async () =>
    {
        if (sender is not ListBox list || e.Source is not Avalonia.Visual source) return;
        var item = source.GetSelfAndVisualAncestors().OfType<ListBoxItem>().FirstOrDefault();
        if (item == null || list.ItemFromContainer(item) is not CompanyResponse company) return;
        e.Handled = true;
        await model.OpenClickedCompany(company);
    });

    private void SelectVisibleCurrenciesClick(object? sender, RoutedEventArgs e) => model.SelectCurrencies(false);
    private void SelectAllCurrenciesClick(object? sender, RoutedEventArgs e) => model.SelectCurrencies(true);
    private void ClearCurrencySelectionClick(object? sender, RoutedEventArgs e) => model.ClearCurrencySelection();
    private void ActivateCurrenciesClick(object? sender, RoutedEventArgs e) => model.BeginCurrencyActivity(true);
    private void DeactivateCurrenciesClick(object? sender, RoutedEventArgs e) => model.BeginCurrencyActivity(false);
    private void ActivateAllCurrenciesClick(object? sender, RoutedEventArgs e) => model.BeginCurrencyActivity(true, true);
    private void DeactivateAllCurrenciesClick(object? sender, RoutedEventArgs e) => model.BeginCurrencyActivity(false, true);
    private void ConfirmCurrencyActivityClick(object? sender, RoutedEventArgs e) => Run(() => model.ConfirmCurrencyActivity());
    private void CancelCurrencyActivityClick(object? sender, RoutedEventArgs e) => model.CancelCurrencyActivity();

    private async Task Login()
    {
        var password = PasswordInput.Text ?? "";
        PasswordInput.Text = "";
        await model.Login(password);
    }

    private void LoginClick(object? sender, RoutedEventArgs e) => Run(() => Login());
    private void UsersSettingsClick(object? sender, RoutedEventArgs e) => Run(() => model.OpenUsersSettings());
    private void CompanyOperationsClick(object? sender, RoutedEventArgs e) => Run(() => model.OpenCompanyOperations());
    private void ProvisionCompanyClick(object? sender, RoutedEventArgs e) => Run(() => model.ProvisionCompany());
    private void RefreshCompanyOperationsClick(object? sender, RoutedEventArgs e) => Run(() => model.RefreshCompanyOperations());
    private void RetryCompanyOperationClick(object? sender, RoutedEventArgs e) => Run(() => model.RetryCompanyOperation());
    private void CompanyOperationsPreviousClick(object? sender, RoutedEventArgs e) => Run(() => model.RefreshCompanyOperations(-1));
    private void CompanyOperationsNextClick(object? sender, RoutedEventArgs e) => Run(() => model.RefreshCompanyOperations(1));
    private void CloseCompanyOperationsClick(object? sender, RoutedEventArgs e) => Run(() => model.CloseCompanyOperations());
    private void SaveBackupPolicyClick(object? sender, RoutedEventArgs e) => Run(() => model.SaveBackupPolicy());
    private void RequestCompanyDeleteClick(object? sender, RoutedEventArgs e) => model.RequestCompanyDelete();
    private void RequestRestoreClick(object? sender, RoutedEventArgs e) => model.RequestRestore();
    private void ConfirmRestoreClick(object? sender, RoutedEventArgs e) => Run(() => model.ConfirmRestore());
    private void CancelRestoreConfirmationClick(object? sender, RoutedEventArgs e) => model.CancelRestoreConfirmation();
    private void ConfirmCompanyDeleteClick(object? sender, RoutedEventArgs e) => Run(() => model.ConfirmCompanyDelete());
    private void CancelCompanyDeleteConfirmationClick(object? sender, RoutedEventArgs e) => model.CancelCompanyDeleteConfirmation();
    private void CancelCompanyOperationClick(object? sender, RoutedEventArgs e) => Run(() => model.CancelCompanyOperation());
    private void CompanyBackupsClick(object? sender, RoutedEventArgs e) => Run(() => model.OpenCompanyBackups());
    private void RequestCompanyBackupClick(object? sender, RoutedEventArgs e) => Run(() => model.RequestCompanyBackup());
    private void RefreshCompanyBackupsClick(object? sender, RoutedEventArgs e) => Run(() => model.RefreshCompanyBackups());
    private void SaveBackupScheduleClick(object? sender, RoutedEventArgs e) => Run(() => model.SaveBackupSchedule());
    private void CloseCompanyBackupsClick(object? sender, RoutedEventArgs e) => model.CloseCompanyBackups();
    private void BackupFromReminderClick(object? sender, RoutedEventArgs e) => Run(() => model.BackupFromReminder());
    private void SnoozeBackupReminderClick(object? sender, RoutedEventArgs e) => Run(() => model.SnoozeBackupReminder());
    private void PasswordPolicyClick(object? sender, RoutedEventArgs e) => Run(() => model.OpenPasswordPolicy());
    private async Task SavePasswordPolicy()
    {
        if (model.PasswordPolicyEditor == null) return;
        var confirm = model.PasswordPolicyEditor.IsEnabled || await AdministrationDialogs.Confirm(this,
            "Wyłączyć wymagania złożoności haseł w całej instalacji? Nie będzie ograniczeń długości ani klas znaków. Historia i ważność hasła pozostają niezależne.");
        if (confirm) await model.SavePasswordPolicy(!model.PasswordPolicyEditor.IsEnabled);
    }
    private void SavePasswordPolicyClick(object? sender, RoutedEventArgs e) => Run(() => SavePasswordPolicy());
    private void RefreshPasswordPolicyClick(object? sender, RoutedEventArgs e) => Run(() => model.RefreshPasswordPolicy());
    private void ClosePasswordPolicyClick(object? sender, RoutedEventArgs e) => model.ClosePasswordPolicy();
    private void PolicyHistoryPreviousClick(object? sender, RoutedEventArgs e) => Run(() => model.LoadPolicyHistory(-1));
    private void PolicyHistoryRefreshClick(object? sender, RoutedEventArgs e) => Run(() => model.LoadPolicyHistory(0));
    private void PolicyHistoryNextClick(object? sender, RoutedEventArgs e) => Run(() => model.LoadPolicyHistory(1));
    private void OwnPasswordClick(object? sender, RoutedEventArgs e) => model.OpenOwnPasswordChange();
    private void ClearOwnPasswords() { OwnCurrentPassword.Text = ""; OwnNewPassword.Text = ""; OwnRepeatPassword.Text = ""; }
    private void CancelOwnPasswordClick(object? sender, RoutedEventArgs e) { ClearOwnPasswords(); model.CancelOwnPasswordChange(); }
    private async Task ChangeOwnPassword()
    {
        if (OwnNewPassword.Text != OwnRepeatPassword.Text) { model.ReportOwnPasswordMismatch(); return; }
        var current = OwnCurrentPassword.Text ?? ""; var replacement = OwnNewPassword.Text ?? "";
        ClearOwnPasswords(); await model.ChangeOwnPassword(current, replacement);
    }
    private void ChangeOwnPasswordClick(object? sender, RoutedEventArgs e) => Run(() => ChangeOwnPassword());
    private void OwnPasswordKeyDown(object? sender, KeyEventArgs e) => Run(async () => { if (e.Key == Key.Enter) { e.Handled = true; await ChangeOwnPassword(); } });
    private void ForgetLoginClick(object? sender, RoutedEventArgs e) => Run(async () => { PasswordInput.Text = ""; await model.ForgetLoginCredentials(); });
    private void SetDefaultCompanyClick(object? sender, RoutedEventArgs e) => Run(() => model.SetDefaultCompany());
    private void ClearDefaultCompanyClick(object? sender, RoutedEventArgs e) => Run(() => model.ClearDefaultCompany());
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
    private void LimitCurrencySelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is ComboBox { SelectedItem: FinanceCurrency item } box &&
            (box.IsDropDownOpen || box.IsKeyboardFocusWithin)) model.SelectedLimitCurrency = item;
    }
    private void RateCurrencySelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is ComboBox { SelectedItem: FinanceCurrency item } box &&
            (box.IsDropDownOpen || box.IsKeyboardFocusWithin)) model.SelectedRateCurrency = item;
    }
    private void ShowInactiveRateCurrenciesClick(object? sender, RoutedEventArgs e) => Run(async () =>
    {
        if (sender is CheckBox box) await model.ShowInactiveCurrencies(box.IsChecked == true);
    });
    private void PasswordKeyDown(object? sender, KeyEventArgs e) => Run(async () =>
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        await Login();
    });
    private void OpenCompanyClick(object? sender, RoutedEventArgs e) => Run(() => model.OpenCompany());
    private void RefreshCompaniesClick(object? sender, RoutedEventArgs e) => Run(() => model.RefreshCompanies());
    private void ContractorsClick(object? sender, RoutedEventArgs e) => Run(() => model.LoadContractors());
    private void NavigateContractorsClick(object? sender, RoutedEventArgs e) => Run(() => model.NavigateContractors());
    private void ContractorColumnsClick(object? sender, RoutedEventArgs e) => Run(() => model.OpenColumnSettings("Contractors"));
    private void CurrencyColumnsClick(object? sender, RoutedEventArgs e) => Run(() => model.OpenColumnSettings("Currencies"));
    private void RateColumnsClick(object? sender, RoutedEventArgs e) => Run(() => model.OpenColumnSettings("CurrencyRates"));
    private void TableLayoutPreview(object? sender, ColumnLayoutEventArgs e)
    {
        if (sender is ConfigurableTable table && e.Width.HasValue) model.PreviewColumnWidth(table.TableId, e.ColumnId, e.Width.Value);
    }
    private void TableLayoutCommitted(object? sender, ColumnLayoutEventArgs e) => Run(async () =>
    {
        if (sender is ConfigurableTable table) await model.ChangeTableLayout(table.TableId, e.ColumnId, e.BeforeColumnId, e.Width);
    });
    private void ColumnUpClick(object? sender, RoutedEventArgs e) { if (sender is Control { DataContext: ColumnChoice choice }) model.MoveColumnChoice(choice, -1); }
    private void ColumnDownClick(object? sender, RoutedEventArgs e) { if (sender is Control { DataContext: ColumnChoice choice }) model.MoveColumnChoice(choice, 1); }
    private void RetryTableLayoutClick(object? sender, RoutedEventArgs e) => Run(() => model.RetryTableLayoutSave());
    private void AcceptTableLayoutConflictClick(object? sender, RoutedEventArgs e) => Run(() => model.RetryTableLayoutSave(true));
    private void ReloadTableLayoutClick(object? sender, RoutedEventArgs e) => Run(() => model.ReloadSavedTableLayouts());
    private void SaveMyColumnsClick(object? sender, RoutedEventArgs e) => Run(() => model.SaveColumns());
    private void ResetMyColumnsClick(object? sender, RoutedEventArgs e) => Run(() => model.SaveColumns(reset: true));
    private void SaveInstallationColumnsClick(object? sender, RoutedEventArgs e) => Run(() => model.SaveColumns(installation: true));
    private void ResetInstallationColumnsClick(object? sender, RoutedEventArgs e) => Run(() => model.SaveColumns(installation: true, reset: true));
    private void CloseColumnSettingsClick(object? sender, RoutedEventArgs e) => model.CloseColumnSettings();
    private void ChangeCompanyClick(object? sender, RoutedEventArgs e) => Run(() => model.ChangeCompany());
    private void LogoutClick(object? sender, RoutedEventArgs e) => Run(() => model.Logout());
    private void NewContractorClick(object? sender, RoutedEventArgs e) { model.NewContractor(); ContractorCodeInput.Focus(); }
    private void EditContractorClick(object? sender, RoutedEventArgs e) => Run(async () => { await model.EditContractor(); ContractorCodeInput.Focus(); });
    private void ContractorDoubleTapped(object? sender, TappedEventArgs e) => Run(async () =>
    {
        if (sender is not ListBox list || e.Source is not Avalonia.Visual source) return;
        var item = source.GetSelfAndVisualAncestors().OfType<ListBoxItem>().FirstOrDefault();
        if (item == null || list.ItemFromContainer(item) is not ContractorResponse row) return;
        e.Handled = true; model.SelectedContractor = row;
        await model.ShowContractor();
    });
    private void SaveContractorClick(object? sender, RoutedEventArgs e) => Run(() => model.SaveContractor());
    private void ValidateIdentifiersClick(object? sender, RoutedEventArgs e) => Run(() => model.CheckIdentifiers());
    private void ValidateBankAccountClick(object? sender, RoutedEventArgs e) => Run(() => model.ValidateBankAccount());
    private void ApplyBankNameClick(object? sender, RoutedEventArgs e) => model.ApplyBankNameProposal();
    private void ApplyBankBicClick(object? sender, RoutedEventArgs e) => model.ApplyBankBicProposal();
    private void AcceptIdentifiersClick(object? sender, RoutedEventArgs e) => Run(() => model.AcceptIdentifierWarning());
    private void ViesClick(object? sender, RoutedEventArgs e) => Run(() => model.VerifyRegistry("Vies"));
    private void GusClick(object? sender, RoutedEventArgs e) => Run(() => model.VerifyRegistry("Gus"));
    private void DataPortClick(object? sender, RoutedEventArgs e) => Run(() => model.VerifyRegistry("DataPort"));
    private void DataPortSettingsClick(object? sender, RoutedEventArgs e) => Run(() => model.OpenDataPortSettings());
    private void GusSettingsClick(object? sender, RoutedEventArgs e) => Run(() => model.OpenGusSettings());
    private void SaveRegistrySettingsClick(object? sender, RoutedEventArgs e) => Run(() => model.SaveRegistryConfiguration());
    private void RefreshRegistrySettingsClick(object? sender, RoutedEventArgs e) => Run(() => model.RefreshRegistryConfigurations());
    private void SaveGusKeyClick(object? sender, RoutedEventArgs e) => Run(async () =>
    {
        var key = GusKeyInput.Text ?? ""; GusKeyInput.Text = "";
        await model.SaveGusKey(key);
    });
    private void RefreshGusStatusClick(object? sender, RoutedEventArgs e) => Run(async () => { GusKeyInput.Text = ""; await model.RefreshGusStatus(); });
    private void RequestGusDeleteClick(object? sender, RoutedEventArgs e) { GusKeyInput.Text = ""; model.RequestGusDelete(); }
    private void DeleteGusKeyClick(object? sender, RoutedEventArgs e) => Run(() => model.DeleteGusKey());
    private void CancelGusDeleteClick(object? sender, RoutedEventArgs e) => model.CancelGusDelete();
    private void CloseGusSettingsClick(object? sender, RoutedEventArgs e) => model.CloseGusSettings();
    private void GusWebsiteClick(object? sender, RoutedEventArgs e)
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("https://api.stat.gov.pl/Home/RegonApi") { UseShellExecute = true }); }
        catch (System.ComponentModel.Win32Exception) { }
    }
    private void CurrenciesClick(object? sender, RoutedEventArgs e) => Run(() => model.OpenCurrencies());
    private void CurrencySettingsClick(object? sender, RoutedEventArgs e) => Run(() => model.OpenCurrencies(true));
    private void CurrencyRatesTabClick(object? sender, RoutedEventArgs e) => model.ShowCurrencySettings(false);
    private void CurrencySettingsTabClick(object? sender, RoutedEventArgs e) => model.ShowCurrencySettings(true);
    private void CloseCurrenciesClick(object? sender, RoutedEventArgs e) => model.CloseCurrencies();
    private void SaveCurrencyClick(object? sender, RoutedEventArgs e) => Run(() => model.SaveCurrencySettings());
    private void RefreshCurrenciesClick(object? sender, RoutedEventArgs e) => Run(() => model.RefreshCurrencies());
    private void CancelCurrencyClick(object? sender, RoutedEventArgs e) => model.CancelCurrencyChanges();
    private void ImportNbpClick(object? sender, RoutedEventArgs e) => Run(() => model.ImportNbpRates());
    private void FilterRatesClick(object? sender, RoutedEventArgs e) => Run(() => model.LoadRates());
    private void PreviousRatesClick(object? sender, RoutedEventArgs e) => Run(() => model.PreviousRates());
    private void NextRatesClick(object? sender, RoutedEventArgs e) => Run(() => model.NextRates());
    private void ClearRateFiltersClick(object? sender, RoutedEventArgs e) => Run(async () => { model.ClearRateFilters(); await model.LoadRates(); });
    private void KeepRateClick(object? sender, RoutedEventArgs e) => Run(() => model.ResolveNbpConflict(false));
    private void AcceptRateClick(object? sender, RoutedEventArgs e) => Run(() => model.ResolveNbpConflict(true));
    private void CurrencyHistoryClick(object? sender, RoutedEventArgs e) => Run(() => model.LoadCurrencyHistory());
    private void PreviousCurrencyHistoryClick(object? sender, RoutedEventArgs e) => Run(() => model.PreviousCurrencyHistory());
    private void NextCurrencyHistoryClick(object? sender, RoutedEventArgs e) => Run(() => model.NextCurrencyHistory());
    private void SaveDataPortKeyClick(object? sender, RoutedEventArgs e) => Run(async () =>
    {
        var key = DataPortKeyInput.Text ?? "";
        DataPortKeyInput.Text = "";
        await model.SaveDataPortKey(key);
    });
    private void RefreshDataPortStatusClick(object? sender, RoutedEventArgs e) => Run(async () => { DataPortKeyInput.Text = ""; await model.RefreshDataPortStatus(); });
    private void RequestDataPortDeleteClick(object? sender, RoutedEventArgs e) { DataPortKeyInput.Text = ""; model.RequestDataPortDelete(); }
    private void DeleteDataPortKeyClick(object? sender, RoutedEventArgs e) => Run(() => model.DeleteDataPortKey());
    private void CancelDataPortDeleteClick(object? sender, RoutedEventArgs e) => model.CancelDataPortDelete();
    private void CloseDataPortSettingsClick(object? sender, RoutedEventArgs e) => model.CloseDataPortSettings();
    private void DataPortWebsiteClick(object? sender, RoutedEventArgs e)
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("https://dataport.pl/api") { UseShellExecute = true }); }
        catch (System.ComponentModel.Win32Exception) { /* URL remains visible for manual navigation. */ }
    }
    private void MfTaxpayerClick(object? sender, RoutedEventArgs e) => Run(() => model.VerifyRegistry("MfTaxpayer"));
    private void MfStatusClick(object? sender, RoutedEventArgs e) => Run(() => model.VerifyRegistry("MfStatus"));
    private void MfAccountClick(object? sender, RoutedEventArgs e) => Run(() => model.VerifyRegistry("MfAccount"));
    private void VerificationsClick(object? sender, RoutedEventArgs e) => Run(() => model.LoadVerifications());
    private void PreviousVerificationsClick(object? sender, RoutedEventArgs e) => Run(() => model.PreviousVerifications());
    private void NextVerificationsClick(object? sender, RoutedEventArgs e) => Run(() => model.NextVerifications());
    private void ApplyGusClick(object? sender, RoutedEventArgs e) => model.ApplyGusSelection();
    private void SaveTermsClick(object? sender, RoutedEventArgs e) => Run(() => model.SaveTradeTerms());
    private void CancelTermsClick(object? sender, RoutedEventArgs e) => model.CancelTradeTerms();
    private void RefreshFinanceClick(object? sender, RoutedEventArgs e) => model.RefreshFinance();
    private void NewLimitClick(object? sender, RoutedEventArgs e) => model.NewCreditLimit();
    private void EditLimitClick(object? sender, RoutedEventArgs e) => model.EditCreditLimit();
    private void SaveLimitClick(object? sender, RoutedEventArgs e) => Run(() => model.SaveCreditLimit());
    private void CancelLimitClick(object? sender, RoutedEventArgs e) => model.CancelCreditLimit();
    private void NextLimitsClick(object? sender, RoutedEventArgs e) => Run(() => model.NextCreditLimits());
    private void PreviousLimitsClick(object? sender, RoutedEventArgs e) => Run(() => model.PreviousCreditLimits());
    private void NewPaymentMethodClick(object? sender, RoutedEventArgs e) => model.NewPaymentMethod();
    private void EditPaymentMethodClick(object? sender, RoutedEventArgs e) => model.EditPaymentMethod();
    private void SavePaymentMethodClick(object? sender, RoutedEventArgs e) => Run(() => model.SavePaymentMethod());
    private void CancelPaymentMethodClick(object? sender, RoutedEventArgs e) => model.CancelPaymentMethod();
    private void ReloadFinanceConflictClick(object? sender, RoutedEventArgs e) => model.ResolveFinanceConflict(false);
    private void KeepFinanceConflictClick(object? sender, RoutedEventArgs e) => model.ResolveFinanceConflict(true);
    private void CancelContractorClick(object? sender, RoutedEventArgs e) => model.CancelEditor();
    private void ReloadConflictClick(object? sender, RoutedEventArgs e) => model.ResolveConflict(false);
    private void KeepConflictClick(object? sender, RoutedEventArgs e) => model.ResolveConflict(true);
    private void NextPageClick(object? sender, RoutedEventArgs e) => Run(() => model.NextPage());
    private void PreviousPageClick(object? sender, RoutedEventArgs e) => Run(() => model.PreviousPage());
    private void SearchParentsClick(object? sender, RoutedEventArgs e) => Run(() => model.SearchParents());
    private void NextParentsClick(object? sender, RoutedEventArgs e) => Run(() => model.NextParents());
    private void PreviousParentsClick(object? sender, RoutedEventArgs e) => Run(() => model.PreviousParents());
    private void ChooseParentClick(object? sender, RoutedEventArgs e) => model.ChooseParent();
    private void ClearParentClick(object? sender, RoutedEventArgs e) => model.ClearParent();
    private void OpenChildClick(object? sender, RoutedEventArgs e) => model.OpenSelectedChild();
    private void ChildDoubleTapped(object? sender, TappedEventArgs e) => model.OpenSelectedChild();
    private void NextChildrenClick(object? sender, RoutedEventArgs e) => Run(() => model.NextChildren());
    private void PreviousChildrenClick(object? sender, RoutedEventArgs e) => Run(() => model.PreviousChildren());
    private void NewDetailClick(object? sender, RoutedEventArgs e) => model.NewDetail();
    private void EditDetailClick(object? sender, RoutedEventArgs e) => Run(() => model.EditDetail());
    private void DetailDoubleTapped(object? sender, TappedEventArgs e) => Run(() => model.EditDetail());
    private void SaveDetailClick(object? sender, RoutedEventArgs e) => Run(() => model.SaveDetail());
    private void CancelDetailClick(object? sender, RoutedEventArgs e) => model.CancelDetail();
    private void RefreshDetailsClick(object? sender, RoutedEventArgs e) => Run(() => model.LoadDetails());
    private void NextDetailsClick(object? sender, RoutedEventArgs e) => Run(() => model.NextDetails());
    private void PreviousDetailsClick(object? sender, RoutedEventArgs e) => Run(() => model.PreviousDetails());
    private void AddAddressTypeClick(object? sender, RoutedEventArgs e) => Run(() => model.AddAddressType());
    private void AcceptBankWarningClick(object? sender, RoutedEventArgs e) => Run(() => model.SaveDetail(true));
    private void CancelBankWarningClick(object? sender, RoutedEventArgs e) => model.CancelBankWarning();
    private void ReloadDetailConflictClick(object? sender, RoutedEventArgs e) => model.ResolveDetailConflict(false);
    private void KeepDetailConflictClick(object? sender, RoutedEventArgs e) => model.ResolveDetailConflict(true);
    private void DiscardClick(object? sender, RoutedEventArgs e) => model.ConfirmDiscard();
    private void KeepEditingClick(object? sender, RoutedEventArgs e) => model.KeepEditing();
    private void SearchKeyDown(object? sender, KeyEventArgs e) => Run(async () =>
    {
        if (e.Key == Key.Enter) { e.Handled = true; await model.LoadContractors(); }
    });
    private void WindowKeyDown(object? sender, KeyEventArgs e) => Run(async () =>
    {
        if (model.IsBusy) return;
        if (model.IsColumnSettingsOpen) { if (e.Key == Key.Escape) model.CloseColumnSettings(); e.Handled = true; return; }
        if (model.DiscardPrompt) { if (e.Key == Key.Escape) model.KeepEditing(); e.Handled = true; return; }
        if (model.IsPasswordPolicyScreen)
        {
            if (e.Key == Key.Escape) { e.Handled = true; model.ClosePasswordPolicy(); }
            else if (e.KeyModifiers == KeyModifiers.Control && e.Key == Key.S) { e.Handled = true; await SavePasswordPolicy(); }
            return;
        }
        if (model.IsUsersSettings)
        {
            if (e.Key == Key.Escape) { e.Handled = true; model.CloseUsersSettings(); }
            else if (e.KeyModifiers == KeyModifiers.Control && e.Key == Key.S) { e.Handled = true; await model.SaveAdminUserAccess(); }
            return;
        }
        if (model.IsOwnPasswordChange)
        {
            if (e.Key == Key.Escape && model.CanCancelPasswordChange) { e.Handled = true; ClearOwnPasswords(); model.CancelOwnPasswordChange(); }
            return;
        }
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
        else if (model.CanShowSelectedContractor && e.Key is Key.F2 or Key.Enter)
        { e.Handled = true; await model.ShowContractor(); }
    });
}
