using Vatowiec.Core.Infrastructure.Persistence;
using Vatowiec.Desktop.Services;
using Vatowiec.SharedContracts;

namespace Vatowiec.Tests;

public sealed class TableLayoutTests
{
    [Fact]
    public void LegacyJsonAndVersionTwo_KeepVisibilityAndDefaults_UnknownAndProtectedFieldsAreFiltered()
    {
        var legacy = TableViewLayoutJson.Read("[\"Name\",\"Code\"]");
        var result = TableLayouts.Effective(TableCatalog.Contractors, [], legacy, null);
        Assert.Equal(new[] { "Code", "Name" }, result.VisibleColumns);
        Assert.Equal("Code", result.ColumnOrder![0]); Assert.Equal(266, result.ColumnWidths!["Name"]);
        var stored = new TableLayout(["Name", "Pesel", "Removed"], ["Pesel", "Removed", "Name", "Code"], new() { ["Name"] = 327.5, ["Pesel"] = 400 });
        var read = TableViewLayoutJson.Read(TableViewLayoutJson.Write(stored));
        result = TableLayouts.Effective(TableCatalog.Contractors, [], read, null);
        Assert.Equal(new[] { "Name" }, result.VisibleColumns); Assert.Equal("Name", result.ColumnOrder![0]);
        Assert.DoesNotContain("Pesel", result.ColumnOrder); Assert.DoesNotContain("Pesel", result.ColumnWidths!.Keys);
        Assert.Equal(327.5, result.ColumnWidths["Name"]); Assert.Contains("UpdatedAtUtc", result.ColumnOrder);
        Assert.Null(TableViewLayoutJson.Read("{malformed")); Assert.False(TableLayouts.ValidWidth(double.NaN));
        Assert.False(TableLayouts.ValidWidth(double.PositiveInfinity)); Assert.False(TableLayouts.ValidWidth(63)); Assert.False(TableLayouts.ValidWidth(1201));
    }
    [Fact]
    public void WholeLayoutHierarchyAndHiddenColumns_AreStableWhenCatalogGrows()
    {
        var global = new TableLayout(["Code"], ["Name", "Code"], new() { ["Name"] = 410 });
        var own = new TableLayout(["Code"], ["TaxId", "Name", "Code"], new() { ["Name"] = 333 });
        var result = TableLayouts.Effective(TableCatalog.Contractors, [], own, global);
        Assert.Equal(new[] { "TaxId", "Name", "Code" }, result.ColumnOrder![..3]); Assert.Equal(333, result.ColumnWidths!["Name"]);
        Assert.Equal(410, TableLayouts.Effective(TableCatalog.Contractors, [], null, global).ColumnWidths!["Name"]);
        Assert.Equal(266, TableLayouts.Effective(TableCatalog.Contractors, [], null, null).ColumnWidths!["Name"]);
        var grown = TableCatalog.Contractors with { Columns = [.. TableCatalog.Contractors.Columns, new("Future", "Nowe pole", "Text", 999)] };
        var future = TableLayouts.Effective(grown, [], own, global);
        Assert.Equal("Future", future.ColumnOrder![^1]); Assert.Equal(166, future.ColumnWidths!["Future"]);
        Assert.DoesNotContain("Future", future.VisibleColumns);
    }
    [Fact]
    public async Task RapidGestures_AreSerializedAndCoalesced_DoNotReloadRows_AndRestoreAcrossCompaniesAndClients()
    {
        var api = new WorkspaceTestApi(); using var model = await TableViewTests.Open(api);
        var block = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); api.BeforeTableSave = () => block.Task;
        var first = model.ChangeTableLayout("Contractors", "Name", width: 300);
        Assert.Single(api.LayoutWrites);
        var second = model.ChangeTableLayout("Contractors", "Name", width: 380);
        var third = model.ChangeTableLayout("Contractors", "TaxId", "Code");
        Assert.Single(api.LayoutWrites); Assert.Equal(380, model.ContractorColumns.Single(c => c.Id == "Name").Width);
        block.SetResult(); await Task.WhenAll(first, second, third);
        Assert.Equal(2, api.LayoutWrites.Count); Assert.NotNull(api.LayoutWrites[1].RowVersion);
        Assert.False(model.HasPendingTableLayouts); Assert.Equal(1, api.SearchCalls);
        Assert.Equal("TaxId", model.ContractorColumns[0].Id);
        await model.OpenColumnSettings("Contractors"); model.ColumnChoices.Single(c => c.Column.Id == "Name").IsSelected = false;
        await model.SaveColumns(); Assert.DoesNotContain(model.ContractorColumns, c => c.Id == "Name");
        model.ColumnChoices.Single(c => c.Column.Id == "Name").IsSelected = true; await model.SaveColumns(); model.CloseColumnSettings();
        Assert.Equal(380, model.ContractorColumns.Single(c => c.Id == "Name").Width);
        await model.ChangeCompany(); model.SelectedCompany = model.Companies[1]; await model.OpenCompany();
        Assert.Equal(380, model.ContractorColumns.Single(c => c.Id == "Name").Width);
        using var restarted = await TableViewTests.Open(api);
        Assert.Equal("TaxId", restarted.ContractorColumns[0].Id);
        Assert.Equal(380, restarted.ContractorColumns.Single(c => c.Id == "Name").Width);
        Assert.Equal(166, restarted.CurrencyColumns[0].Width);
    }
    [Fact]
    public async Task FailedSave_RetainsDraft_RetryAndConflictRequireExplicitResolution()
    {
        var api = new WorkspaceTestApi(); using var model = await TableViewTests.Open(api);
        api.TableSaveError = new HttpRequestException("offline");
        await model.ChangeTableLayout("Contractors", "Name", width: 444);
        Assert.True(model.HasTableLayoutSaveError && model.HasUnsavedChanges); Assert.Contains("Nie zapisano", model.TableLayoutStatus);
        api.TableSaveError = null; await model.RetryTableLayoutSave(); Assert.False(model.HasPendingTableLayouts);
        api.TableSaveError = new ApiException(409, "conflict"); await model.ChangeTableLayout("Contractors", "Name", width: 480);
        Assert.True(model.HasTableLayoutConflict); var count = api.LayoutWrites.Count;
        await model.RetryTableLayoutSave(); Assert.Equal(count, api.LayoutWrites.Count);
        api.TableSaveError = null; await model.RetryTableLayoutSave(true);
        Assert.False(model.HasTableLayoutConflict); Assert.Equal(480, model.ContractorColumns.Single(c => c.Id == "Name").Width);
        api.TableSaveError = new HttpRequestException("offline"); await model.ChangeTableLayout("Contractors", "Name", width: 500);
        api.TableSaveError = null; await model.ReloadSavedTableLayouts();
        Assert.False(model.HasPendingTableLayouts); Assert.Equal(480, model.ContractorColumns.Single(c => c.Id == "Name").Width);
    }
    [Theory]
    [InlineData(401)]
    [InlineData(403)]
    public async Task AutosaveAuthorizationFailure_ClearsCompanyAndPendingLayout(int status)
    {
        var api = new WorkspaceTestApi(); using var model = await TableViewTests.Open(api);
        api.TableSaveError = new ApiException(status, "denied");
        await model.ChangeTableLayout("Contractors", "Code", width: 250);
        Assert.False(model.IsWorkspace); Assert.False(model.HasPendingTableLayouts); Assert.Empty(model.Contractors);
        Assert.Equal(status == 401, model.IsLogin);
    }
}
