using Microsoft.EntityFrameworkCore;
using Vatowiec.Core.Infrastructure.Persistence;
using Vatowiec.Core.Application;
using Vatowiec.Desktop.Services;
using Vatowiec.Desktop.ViewModels;
using Vatowiec.SharedContracts;

namespace Vatowiec.Tests;

public sealed class TableViewTests
{
    [Theory]
    [InlineData("Contractors", "Kontrahenci")]
    [InlineData("Currencies", "Waluty")]
    [InlineData("CurrencyRates", "Kursy walut")]
    public async Task ColumnSettings_UsesPolishTitleAndSavesOriginalId(string tableId, string label)
    {
        var api = new WorkspaceTestApi(); using var model = await Open(api);
        await model.OpenColumnSettings(tableId);
        Assert.Equal("Kolumny tabeli: " + label, model.ColumnSettingsTitle);
        await model.SaveColumns();
        Assert.Equal(tableId, api.LastSavedTable);
    }

    [Fact]
    public void Catalog_HierarchyPermissionLossAndFutureColumnsAreSafe()
    {
        var table = TableCatalog.Contractors;
        Assert.Equal(new[] { "Code", "Name", "TaxId", "CountryCode", "IsActive", "AddressCity" },
            TableCatalog.EffectiveColumns(table, [], null, null));
        Assert.Equal(new[] { "Regon" }, TableCatalog.EffectiveColumns(table, [], ["Regon"], ["Name"]));
        Assert.Equal(new[] { "Name" }, TableCatalog.EffectiveColumns(table, [], null, ["Name"]));
        Assert.DoesNotContain(table.Available([]), c => c.Id == "Pesel");
        Assert.DoesNotContain("Pesel", TableCatalog.EffectiveColumns(table, [], ["Pesel", "RemovedInFuture"], null));
        Assert.Equal(new[] { "Pesel" }, TableCatalog.EffectiveColumns(table, ["ManageContractorPesel"], ["Pesel"], null));
        Assert.Equal(new[] { "Name" }, TableCatalog.EffectiveColumns(table, [], ["Name"], null));
        Assert.All(new[] { table, TableCatalog.Currencies, TableCatalog.CurrencyRates }, t =>
            Assert.Equal(t.Columns.Length, t.Columns.Select(c => c.Id).Distinct(StringComparer.Ordinal).Count()));
    }

    [Fact]
    public void EverySearchColumnTranslatesToParameterizedSql_BeforePaging()
    {
        using var db = new CompanyDbContext(new DbContextOptionsBuilder<CompanyDbContext>().UseSqlServer().Options, new CompanyContext(Guid.NewGuid()));
        foreach (var column in TableCatalog.Contractors.Columns)
        {
            var sql = ContractorTableQuery.Search(ContractorTableQuery.Rows(db), [column.Id], "fictional-search")
                .OrderBy(x => x.Contractor.Code).ThenBy(x => x.Contractor.Id).Skip(25).Take(26).ToQueryString();
            Assert.Contains("OFFSET", sql); Assert.Contains("FETCH NEXT", sql);
            Assert.Contains("AddressTypeId] = 1", sql);
            Assert.Contains("DECLARE @", sql);
            Assert.DoesNotContain("fictional-search", sql[sql.IndexOf("SELECT", StringComparison.Ordinal)..]);
        }
    }

    [Fact]
    public async Task Workspace_AutomaticallyLoads_ReadOnlyCannotWrite_ExplicitEditUsesOriginalVersion()
    {
        var api = new WorkspaceTestApi(); using var model = await Open(api);
        Assert.Single(model.Contractors); Assert.Equal(1, api.SearchCalls); Assert.True(api.LastSearch!.Active);
        Assert.Equal(51, api.LastSearch.Limit); Assert.Contains("AddressCity", api.LastSearch.VisibleColumns!);
        Assert.Contains("Użytkownik: fixture", model.StatusBarText); Assert.Contains("Firma: Firma A", model.StatusBarText);
        Assert.Contains("Sesja do:", model.StatusBarText);
        Assert.False(model.CanShowSelectedContractor); Assert.False(model.CanEditSelectedContractor);
        model.SelectedContractor = model.Contractors[0]; await model.ShowContractor();
        Assert.True(model.IsContractorReadOnly); Assert.False(model.CanModifyContractor); Assert.False(model.CanSaveContractor);
        Assert.True(model.CanStartContractorEdit); var version = model.Editor!.RowVersion;
        await model.SaveContractor(); await model.AcceptIdentifierWarning(); model.NewDetail(); await model.AddAddressType();
        Assert.Equal(0, api.Writes); Assert.False(model.IsDetailEditing);
        await model.StartContractorEdit(); Assert.False(model.IsContractorReadOnly); Assert.True(model.CanSaveContractor);
        Assert.Equal(version, model.Editor!.RowVersion);
        await model.Logout(); Assert.Empty(model.StatusBarText); Assert.Empty(model.Contractors);
    }

    [Fact]
    public async Task Reader_HasPreview_ButCannotExecuteEditingOperations()
    {
        var api = new WorkspaceTestApi { Editable = false }; using var model = await Open(api);
        model.SelectedContractor = model.Contractors[0]; await model.ShowContractor();
        Assert.True(model.IsContractorReadOnly); Assert.False(model.CanStartContractorEdit);
        await model.StartContractorEdit(); await model.SaveContractor(); Assert.Equal(0, api.Writes);
        model.CancelEditor(); model.NewContractor(); Assert.False(model.IsEditing);
    }

    [Theory]
    [InlineData(25)]
    [InlineData(50)]
    [InlineData(100)]
    [InlineData(200)]
    public async Task PageSize_IsPersistedPerTable_AndFiltersReload(int size)
    {
        var api = new WorkspaceTestApi(); using var model = await Open(api);
        await model.SetContractorPageSize(size); Assert.Equal(size, model.PageSize); Assert.Equal(size + 1, api.LastSearch!.Limit);
        Assert.Equal("Contractors", api.LastSavedTable); Assert.Equal(size, api.SavedView!.PageSize);
        model.SearchText = "nowy filtr"; Assert.Equal("nowy filtr", api.LastSearch.Query); Assert.Equal(0, api.LastSearch.Offset);
        model.FilterActivity = 2; Assert.False(api.LastSearch.Active);
        await model.OpenColumnSettings("Contractors"); foreach (var c in model.ColumnChoices) c.IsSelected = c.Column.Id == "Regon";
        await model.SaveColumns(); Assert.Equal(new[] { "Regon" }, api.LastSearch.VisibleColumns);
    }

    [Fact]
    public async Task ObsoleteResponsesCannotRestoreOldCompanyOrOldFilter()
    {
        var api = new WorkspaceTestApi(); using var model = await Open(api);
        var old = new TaskCompletionSource<ContractorResponse[]>(); api.PendingSearch = old.Task;
        var load = model.LoadContractors(); Assert.True(model.IsContractorLoading);
        api.PendingSearch = null; model.SearchText = "new"; Assert.Single(model.Contractors);
        old.SetResult([new(99, WorkspaceTestApi.A, "OLD", "Old", "PL")]); await load;
        Assert.DoesNotContain(model.Contractors, c => c.Code == "OLD");
        old = new(); api.PendingSearch = old.Task; load = model.LoadContractors();
        await model.ChangeCompany(); Assert.Empty(model.Contractors); Assert.Empty(model.CurrentCompany);
        api.PendingSearch = null; model.SelectedCompany = model.Companies[1]; await model.OpenCompany();
        old.SetResult([new(99, WorkspaceTestApi.A, "OLD", "Old", "PL")]); await load;
        Assert.Equal(WorkspaceTestApi.B, Assert.Single(model.Contractors).CompanyId);
    }
    [Theory]
    [InlineData(401, null)]
    [InlineData(403, null)]
    [InlineData(403, "user_password_change_required")]
    public async Task LoadingAuthorizationFailureNotifiesBindingsAfterRemovingPreviousContext(int status, string? code)
    {
        var api = new WorkspaceTestApi(); using var model = await Open(api);
        var notifiedAfterClear = false;
        model.PropertyChanged += (_, _) => { if (!model.IsWorkspace) notifiedAfterClear = true; };
        api.SearchError = new ApiException(status, "Odmowa") { Code = code };
        await model.LoadContractors();
        Assert.True(notifiedAfterClear); Assert.Empty(model.Contractors); Assert.False(model.IsWorkspace);
        Assert.Equal(status == 401, model.IsLogin);
        Assert.Equal(code != null, model.IsOwnPasswordChange);
    }
    internal static async Task<MainViewModel> Open(WorkspaceTestApi api)
    {
        var model = new MainViewModel(_ => api) { UserName = "fixture" }; await model.Login("memory-only");
        model.SelectedCompany = model.Companies[0]; await model.OpenCompany(); return model;
    }
}

internal sealed class WorkspaceTestApi : IVatowiecApiClient
{
    public static readonly Guid A = Guid.Parse("cccccccc-0000-4000-8000-000000000001");
    public static readonly Guid B = Guid.Parse("cccccccc-0000-4000-8000-000000000002");
    public bool Editable = true;
    public string[] AdditionalPermissions = [];
    public CurrencyResponse[] CurrencyData = [new(A, "EUR", "Waluta testowa", true, true, false, false, "AAAAAAAAAAE=")];
    public Func<CurrencyActivityRequest, CurrencyActivityResponse>? CurrencyActivity;
    public int Writes, SearchCalls;
    public ContractorSearchRequest? LastSearch;
    public TableViewRequest? SavedView;
    public string? LastSavedTable;
    public Task<ContractorResponse[]>? PendingSearch;
    public ApiException? SearchError;
    private readonly Dictionary<string, TableViewResponse> views = [];
    public Task<SessionResponse> Login(string u, string p) => Task.FromResult(new SessionResponse(u, DateTimeOffset.UtcNow.AddHours(1), true, UserId: Guid.NewGuid()));
    public Task<CompanyResponse[]> Companies() => Task.FromResult(new[] { A, B }.Select((id, i) =>
        new CompanyResponse(id, "Firma " + (i == 0 ? "A" : "B"), Editable ? ["ViewProfile", "ViewContractors", "EditContractors", "ViewExchangeRates", .. AdditionalPermissions] : ["ViewProfile", "ViewContractors"])).ToArray());
    public Task<CompanyProfileResponse> Profile(Guid id) => Task.FromResult(new CompanyProfileResponse(id, id == A ? "Firma A" : "Firma B", null, null));
    public Task<ContractorResponse[]> Contractors(Guid id, string? search = null, string? country = null, bool? active = null, int offset = 0, int limit = 50) =>
        Task.FromResult<ContractorResponse[]>([new(1, id, "FIXTURE", "Dane fikcyjne", "PL", RowVersion: "AAAAAAAAAAE=")]);
    public Task<ContractorResponse[]> SearchContractors(Guid companyId, ContractorSearchRequest value, CancellationToken ct)
    { SearchCalls++; LastSearch = value; if (SearchError != null) throw SearchError; return PendingSearch ?? Contractors(companyId); } // Deliberately ignores cancellation: generation guards must still work.
    public Task<ContractorResponse> Contractor(Guid id, int contractor) => Task.FromResult(new ContractorResponse(contractor, id, "FIXTURE", "Dane fikcyjne", "PL", RowVersion: "AAAAAAAAAAE="));
    public Task<ContractorResponse> SaveContractor(Guid id, ContractorWriteRequest value, bool create) { Writes++; throw new InvalidOperationException("Unexpected write"); }
    public Task<TableViewResponse> TableView(Guid id, string table) => Task.FromResult(views.GetValueOrDefault(table) ?? TableCatalog.Factory(table, []));
    public async Task<TableViewResponse> SaveTableView(Guid id, string table, TableViewRequest value, bool global)
    {
        SavedView = value; LastSavedTable = table;
        var current = await TableView(id, table);
        return views[table] = current with { PageSize = value.PageSize ?? current.PageSize, VisibleColumns = value.Columns ?? current.VisibleColumns, UserVersion = Guid.NewGuid().ToString() };
    }
    public Task Logout() => Task.CompletedTask;
    public Task<CurrencyResponse[]> Currencies(Guid companyId) => Task.FromResult(CurrencyData.Select(c => c with { CompanyId = companyId }).ToArray());
    public Task<CurrencyActivityResponse> ChangeCurrencyActivity(Guid companyId, CurrencyActivityRequest value) =>
        Task.FromResult(CurrencyActivity?.Invoke(value) ?? throw new InvalidOperationException("Unexpected currency activity write"));
    public Task<CurrencyHistoryResponse[]> CurrencyHistory(Guid companyId, int offset) => Task.FromResult<CurrencyHistoryResponse[]>([]);
    public Task<RatePage> Rates(Guid companyId, string? code, string? table, DateOnly? from, DateOnly? to, int offset, bool descending) =>
        Task.FromResult(new RatePage(companyId,
            [new(1, companyId, "EUR", "Mock", "A", "fixture", new DateOnly(2026, 10, 23), null, 4m, null, null, DateTimeOffset.UtcNow, "AAAAAAAAAAE=")], null, null, null));
    public void Dispose() { }
}
