using Vatowiec.SharedContracts;

namespace Vatowiec.Desktop.ViewModels;

public sealed partial class MainViewModel
{
    private readonly Dictionary<string, TableViewResponse> confirmedTableViews = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (long Revision, TableViewResponse View)> pendingTableLayouts = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> layoutRevisions = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim layoutSaveGate = new(1, 1);
    private int layoutEpoch;
    private bool layoutSaveError;
    private (string Table, TableViewResponse Server)? layoutConflict;
    public bool HasPendingTableLayouts => pendingTableLayouts.Count > 0;
    public bool IsTableLayoutSaving { get; private set; }
    public bool HasTableLayoutSaveError => layoutSaveError;
    public bool HasTableLayoutConflict => layoutConflict != null;
    public string TableLayoutStatus { get; private set; } = "";
    public string TableLayoutComparison => layoutConflict is { } conflict
        ? $"{TableDisplayNames.For(conflict.Table)} — zapis na serwerze: {Describe(conflict.Server)}\nMój niezapisany układ: {Describe(View(conflict.Table))}" : "";
    private static string Describe(TableViewResponse view) => string.Join(" | ", (view.ColumnOrder ?? view.Columns.Select(c => c.Id).ToArray())
        .Select(id => $"{view.Columns.FirstOrDefault(c => c.Id == id)?.Label}{(view.VisibleColumns.Contains(id, StringComparer.Ordinal) ? "" : " (ukryta)")}: {view.ColumnWidths?.GetValueOrDefault(id, TableLayouts.DefaultWidth(id)) ?? TableLayouts.DefaultWidth(id):0.#}"));

    private void NotifyTableLayout(string tableId)
    {
        Changed(tableId switch { "Contractors" => nameof(ContractorColumns), "Currencies" => nameof(CurrencyColumns), _ => nameof(RateColumns) });
        Changed(nameof(TableLayoutComparison));
    }
    public void PreviewColumnWidth(string tableId, string columnId, double width)
    {
        if (!double.IsFinite(width) || !IsWorkspace || !View(tableId).Columns.Any(c => c.Id == columnId)) return;
        var view = View(tableId);
        var widths = new Dictionary<string, double>(view.ColumnWidths ?? [], StringComparer.Ordinal)
        { [columnId] = Math.Clamp(width, TableLayouts.MinimumWidth, TableLayouts.MaximumWidth) };
        tableViews[tableId] = view with { ColumnWidths = widths };
        layoutRevisions[tableId] = layoutRevisions.GetValueOrDefault(tableId) + 1;
        NotifyTableLayout(tableId);
    }
    public Task ChangeTableLayout(string tableId, string columnId, string? beforeColumnId = null, double? width = null)
    {
        if (width.HasValue && !double.IsFinite(width.Value) || !IsWorkspace || busy || api == null || profile == null || !View(tableId).Columns.Any(c => c.Id == columnId)) return Task.CompletedTask;
        if (width.HasValue) PreviewColumnWidth(tableId, columnId, width.Value);
        else
        {
            var view = View(tableId);
            var order = (view.ColumnOrder ?? view.Columns.OrderBy(c => c.Order).Select(c => c.Id).ToArray()).ToList();
            var originalOrder = order.ToArray();
            if (columnId == beforeColumnId || beforeColumnId != null && !order.Contains(beforeColumnId)) return Task.CompletedTask;
            order.Remove(columnId); order.Insert(beforeColumnId == null ? order.Count : order.IndexOf(beforeColumnId), columnId);
            if (originalOrder.SequenceEqual(order)) return Task.CompletedTask;
            tableViews[tableId] = view with { ColumnOrder = order.ToArray() };
            layoutRevisions[tableId] = layoutRevisions.GetValueOrDefault(tableId) + 1;
            NotifyTableLayout(tableId);
        }
        pendingTableLayouts[tableId] = (layoutRevisions.GetValueOrDefault(tableId), View(tableId));
        if (message is "Zapisano widok tabeli." or "Zresetowano wybrany zakres widoku.") { message = ""; Changed(nameof(Message)); }
        Changed(nameof(HasUnsavedChanges));
        return SavePendingTableLayouts();
    }
    public async Task SavePendingTableLayouts()
    {
        var epoch = layoutEpoch;
        await layoutSaveGate.WaitAsync();
        try
        {
            if (epoch != layoutEpoch || layoutSaveError || pendingTableLayouts.Count == 0 || api == null || profile == null || disposed) return;
            var client = api; var company = profile.CompanyId;
            IsTableLayoutSaving = true; TableLayoutStatus = "Zapisywanie układu tabeli…"; ChangedAll();
            while (epoch == layoutEpoch && pendingTableLayouts.Count > 0)
            {
                var (table, draft) = pendingTableLayouts.First();
                var version = View(table).UserVersion;
                try
                {
                    var response = await client.SaveTableView(company, table, new(draft.View.VisibleColumns, RowVersion: version,
                        ColumnOrder: draft.View.ColumnOrder ?? draft.View.Columns.Select(c => c.Id).ToArray(),
                        ColumnWidths: draft.View.ColumnWidths), false);
                    if (epoch != layoutEpoch) return;
                    confirmedTableViews[table] = response;
                    if (pendingTableLayouts.TryGetValue(table, out var latest) && latest.Revision == draft.Revision) pendingTableLayouts.Remove(table);
                    tableViews[table] = layoutRevisions.GetValueOrDefault(table) == draft.Revision ? response : View(table) with
                    { UserVersion = response.UserVersion, InstallationVersion = response.InstallationVersion };
                    NotifyTableLayout(table);
                }
                catch (Exception ex) when (IsExpected(ex))
                {
                    if (epoch != layoutEpoch) return;
                    if (HandleLayoutAuthorization(ex)) return;
                    layoutSaveError = true;
                    TableLayoutStatus = "Nie zapisano układu tabeli. Zmiany pozostają tylko w tym oknie. Ponów zapis lub wczytaj zapisany układ.";
                    if (ex is Services.ApiException { StatusCode: 409 })
                    {
                        try
                        {
                            var server = await client.TableView(company, table);
                            if (epoch == layoutEpoch)
                            {
                                layoutConflict = (table, server);
                                TableLayoutStatus = "Nie zapisano układu: zmienił się na innym stanowisku. Porównaj oba układy przed ponownym zapisem.";
                            }
                        }
                        catch (Exception readError) when (IsExpected(readError)) { HandleLayoutAuthorization(readError); }
                    }
                    return;
                }
            }
            if (epoch == layoutEpoch) TableLayoutStatus = "Zapisano układ tabeli.";
        }
        finally
        {
            IsTableLayoutSaving = false; layoutSaveGate.Release(); ChangedAll();
        }
    }
    public async Task RetryTableLayoutSave(bool acceptComparedConflict = false)
    {
        if (IsTableLayoutSaving) return;
        if (layoutConflict is { } conflict)
        {
            if (!acceptComparedConflict) return;
            tableViews[conflict.Table] = View(conflict.Table) with { UserVersion = conflict.Server.UserVersion };
            layoutConflict = null;
        }
        layoutSaveError = false; await SavePendingTableLayouts();
    }
    private bool HandleLayoutAuthorization(Exception ex)
    {
        if (ex is Services.ApiException { StatusCode: 401 }) { ResetSession(); return true; }
        if (ex is not Services.ApiException { StatusCode: 403 } denied) return false;
        ClearCompanyData();
        if (denied.Code == "user_password_change_required" && session != null) session = session with { MustChangePassword = true };
        message = "Odmowa zapisu układu. Odśwież dostęp do firmy lub zmień wymagane hasło.";
        return true;
    }
    public Task ReloadSavedTableLayouts() => Run(async () =>
    {
        if (IsTableLayoutSaving || api == null || profile == null) return;
        await layoutSaveGate.WaitAsync();
        try
        {
            var loaded = new Dictionary<string, TableViewResponse>(StringComparer.Ordinal);
            foreach (var id in pendingTableLayouts.Keys.ToArray())
                loaded[id] = await api.TableView(profile.CompanyId, id);
            foreach (var (id, view) in loaded) { tableViews[id] = view; confirmedTableViews[id] = view; NotifyTableLayout(id); }
            pendingTableLayouts.Clear(); layoutConflict = null; layoutSaveError = false; TableLayoutStatus = "Wczytano zapisany układ.";
        }
        finally { layoutSaveGate.Release(); }
    });
    private async Task RequireSavedTableLayouts()
    {
        await SavePendingTableLayouts();
        if (HasPendingTableLayouts) throw new ArgumentException("Najpierw ponów zapis układu tabeli lub wczytaj układ z serwera.");
    }
    private void DiscardPendingTableLayouts()
    {
        layoutEpoch++;
        foreach (var id in pendingTableLayouts.Keys) if (confirmedTableViews.TryGetValue(id, out var saved)) tableViews[id] = saved;
        pendingTableLayouts.Clear(); layoutRevisions.Clear(); layoutConflict = null; layoutSaveError = false; TableLayoutStatus = "";
    }
}
