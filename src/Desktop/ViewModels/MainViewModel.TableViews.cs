using System.ComponentModel;
using System.Windows.Input;
using Vatowiec.Desktop.Services;
using Vatowiec.SharedContracts;

namespace Vatowiec.Desktop.ViewModels;

public sealed record WorkspaceOperation(string Label, ICommand Command, string AutomationId);
public sealed class WorkspaceCommand(Action execute, Func<bool> enabled) : ICommand
{
    public bool CanExecute(object? parameter) => enabled();
    public void Execute(object? parameter) { if (enabled()) execute(); }
    public event EventHandler? CanExecuteChanged { add { } remove { } }
}
public sealed class ColumnChoice(TableColumn column, bool visible) : INotifyPropertyChanged
{
    public TableColumn Column { get; } = column;
    public string Label => Column.Label;
    private bool selected = visible;
    public bool IsSelected { get => selected; set { selected = value; PropertyChanged?.Invoke(this, new(nameof(IsSelected))); } }
    public event PropertyChangedEventHandler? PropertyChanged;
}

public sealed partial class MainViewModel
{
    private readonly Dictionary<string, TableViewResponse> tableViews = new(StringComparer.Ordinal);
    private bool connectionSettingsExpanded;
    public bool IsConnectionSettingsExpanded { get => connectionSettingsExpanded; set { connectionSettingsExpanded = value; Changed(); } }
    public bool IsAuthenticated => session != null;
    public bool CanUseCompanyMenu => IsWorkspace && !IsOwnPasswordChange;
    public string StatusBarText => session == null ? "" : $"Użytkownik: {CurrentUser} | Firma: {CurrentCompany} | {SessionExpiry}";
    private TableViewResponse View(string tableId) => tableViews.GetValueOrDefault(tableId) ??
        TableCatalog.Factory(tableId, selectedCompany?.Permissions ?? []);
    public TableViewResponse ContractorTable => View("Contractors");
    public TableViewResponse CurrencyTable => View("Currencies");
    public TableViewResponse RateTable => View("CurrencyRates");
    public TableColumn[] ContractorColumns => Visible(ContractorTable);
    public TableColumn[] CurrencyColumns => Visible(CurrencyTable);
    public TableColumn[] RateColumns => Visible(RateTable);
    private static TableColumn[] Visible(TableViewResponse view) => view.Columns.Where(c => view.VisibleColumns.Contains(c.Id, StringComparer.Ordinal)).OrderBy(c => c.Order).ToArray();
    public int[] PageSizes => TableCatalog.PageSizes;
    public int PageSize { get => ContractorTable.PageSize; set { if (!busy && value != PageSize && TableCatalog.PageSizes.Contains(value)) _ = SetContractorPageSize(value); } }
    public Task SetContractorPageSize(int value) => Run(async () =>
    {
        if (!CanBrowse || api == null || profile == null || !TableCatalog.PageSizes.Contains(value)) return;
        var response = await api.SaveTableView(profile.CompanyId, "Contractors", new(PageSize: value, RowVersion: ContractorTable.UserVersion), false);
        tableViews["Contractors"] = response; pageOffset = 0; await LoadPage(0);
    });
    private async Task ReadTableViews()
    {
        if (api == null || profile == null) return;
        foreach (var table in new[] { TableCatalog.Contractors, TableCatalog.Currencies, TableCatalog.CurrencyRates })
        {
            if (selectedCompany?.Permissions.Contains(table.ReadPermission, StringComparer.Ordinal) != true) continue;
            try { tableViews[table.Id] = await api.TableView(profile.CompanyId, table.Id); }
            catch (ApiException ex) when (ex.StatusCode == 503)
            {
                // A controlled rollout may run the new client before the System migration.
                // Factory browsing remains possible; never silently persist local replacement settings.
                tableViews[table.Id] = TableCatalog.Factory(table.Id, selectedCompany.Permissions);
                message = "Widoki tabel wymagają aktualizacji bazy System przez administratora po backupie. Wyświetlono kolumny fabryczne.";
            }
        }
    }
    private void ResetTableViews() { tableViews.Clear(); columnSettingsTable = null; ColumnChoices = []; }
    private string? columnSettingsTable;
    public bool IsColumnSettingsOpen => columnSettingsTable != null;
    public ColumnChoice[] ColumnChoices { get; private set; } = [];
    public string ColumnSettingsTitle => "Kolumny tabeli: " + columnSettingsTable;
    public string ColumnSettingsSource => columnSettingsTable == null ? "" : View(columnSettingsTable).Source switch
    { "User" => "Obowiązuje mój widok", "Installation" => "Obowiązuje domyślny widok instalacji", _ => "Obowiązuje widok fabryczny" };
    public Task OpenColumnSettings(string tableId) => Run(async () =>
    {
        if (api == null || profile == null || TableCatalog.Find(tableId) is not { } table || !selectedCompany!.Permissions.Contains(table.ReadPermission)) return;
        tableViews[tableId] = await api.TableView(profile.CompanyId, tableId);
        columnSettingsTable = tableId; SetColumnChoices();
    });
    private void SetColumnChoices()
    {
        if (columnSettingsTable == null) return;
        var view = View(columnSettingsTable);
        ColumnChoices = view.Columns.Select(c => new ColumnChoice(c, view.VisibleColumns.Contains(c.Id, StringComparer.Ordinal))).ToArray();
    }
    public void CloseColumnSettings() { if (!busy) { columnSettingsTable = null; ColumnChoices = []; ChangedAll(); } }
    public Task SaveColumns(bool installation = false, bool reset = false) => Run(async () =>
    {
        if (columnSettingsTable == null || api == null || profile == null || installation && !IsInstallationAdministrator) return;
        var tableId = columnSettingsTable; var view = View(tableId);
        var selected = ColumnChoices.Where(c => c.IsSelected).Select(c => c.Column.Id).ToArray();
        if (!reset && selected.Length == 0) { message = "Wybierz co najmniej jedną kolumnę."; return; }
        tableViews[tableId] = await api.SaveTableView(profile.CompanyId, tableId,
            new(reset ? null : selected, RowVersion: installation ? view.InstallationVersion : view.UserVersion, Reset: reset), installation);
        SetColumnChoices();
        if (tableId == "Contractors") await LoadPage(0);
        message = reset ? "Zresetowano wybrany zakres widoku." : "Zapisano widok tabeli.";
    });
    public WorkspaceOperation[] WorkspaceOperations => IsBrowsingWorkspace ? [
        Operation("Nowy", NewContractor, () => CanEditContractors),
        Operation("Pokaż", () => _ = ShowContractor(), () => CanShowSelectedContractor),
        Operation("Edytuj", () => _ = EditContractor(), () => CanEditSelectedContractor),
        Operation("Odśwież", () => _ = LoadContractors(), () => CanBrowse)] : IsContractorReadOnly ? [
        Operation("Edytuj", () => _ = StartContractorEdit(), () => CanStartContractorEdit)] : IsCurrencyScreen ? (IsCurrencySettings ? new[] {
        Operation("Odśwież", () => _ = RefreshCurrencies(), () => !CurrencyActivityPrompt),
        Operation("Aktywuj", () => BeginCurrencyActivity(true), () => CanStartCurrencyActivity),
        Operation("Dezaktywuj", () => BeginCurrencyActivity(false), () => CanStartCurrencyActivity)
        } : new[] {
        Operation("Odśwież", () => _ = LoadRates(), () => CanViewRates),
        Operation("Ustawienia pobierania", () => ShowCurrencySettings(true), () => CanViewRates)
        }) : [];
    private WorkspaceOperation Operation(string label, Action action, Func<bool> enabled) => new(label,
        new WorkspaceCommand(action, () => CanInteract && enabled()), label switch
        {
            "Nowy" => "NewContractor",
            "Pokaż" => "ShowContractor",
            "Edytuj" => "EditContractor",
            "Odśwież" when IsBrowsingWorkspace => "ShowContractors",
            _ => "Workspace" + label
        });

    public Task NavigateContractors() => Run(async () =>
    {
        if (!CanViewContractors) return;
        if (HasUnsavedChanges) { RequestDiscard(() => { ClearNavigationDrafts(); _ = NavigateContractors(); }); return; }
        ClearNavigationDrafts();
        await ReadTableViews(); await LoadPage(0);
    });
    private void ClearNavigationDrafts()
    {
        editor = null; contractorReadOnly = false; ResetDetails(); ResetCurrencyState(); ResetDataPortSettings();
        ResetGusSettings(); ResetUsersSettings(); ResetPasswordPolicy(); ownPasswordForm = false;
    }
}
