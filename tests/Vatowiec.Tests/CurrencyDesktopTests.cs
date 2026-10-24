using System.Text.Json;
using System.Net.Http.Json;
using Vatowiec.Desktop.Services;
using Vatowiec.Desktop.ViewModels;
using Vatowiec.SharedContracts;

namespace Vatowiec.Tests;

public sealed partial class DesktopTests
{
    [Fact]
    public async Task BulkCurrencyTransport_PostsOneCompanyOperationWithFreshCsrfAndUnchangedPayload()
    {
        var value = new CurrencyActivityRequest(Guid.NewGuid(), false, true, [new("PLN", "AAAAAAAAAAE="), new("EUR", "AAAAAAAAAAE=")], true);
        var calls = new List<string>();
        using var client = new VatowiecApiClient(new HttpClient(new Handler(async request =>
        {
            calls.Add(request.RequestUri!.AbsolutePath);
            if (calls.Count == 1) return Json(new CsrfResponse("fixture-token", "X-CSRF-TOKEN"));
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("fixture-token", request.Headers.GetValues("X-CSRF-TOKEN").Single());
            Assert.Equal(JsonSerializer.Serialize(value), JsonSerializer.Serialize(await request.Content!.ReadFromJsonAsync<CurrencyActivityRequest>()));
            return Json(new CurrencyActivityResponse(A, false, [], ["PLN"]));
        }))
        { BaseAddress = new("https://localhost") });
        await client.ChangeCurrencyActivity(A, value);
        Assert.Equal(new[] { "/api/v1/auth/csrf", $"/api/v1/companies/{A}/currencies/activity" }, calls);
    }

    [Theory]
    [InlineData(401)]
    [InlineData(403)]
    public async Task CompanyDoubleClick_RespectsExistingAccessDenial(int status)
    {
        var api = new FakeApi { ProfileError = new ApiException(status, "odmowa") };
        using var model = new MainViewModel(_ => api) { UserName = "operator" };
        await model.Login("temporary"); model.SelectedCompany = model.Companies[0];
        await model.OpenClickedCompany(model.Companies[1]);
        Assert.Equal(B, Assert.Single(api.ProfileCalls)); Assert.False(model.IsWorkspace); Assert.Empty(model.CurrentCompany);
        Assert.Equal(status == 401, model.IsLogin);
    }

    private static FakeApi BulkApi(bool manage = true) => new()
    {
        Permissions = manage ? ["ViewProfile", "ViewExchangeRates", "ManageCurrencies"] : ["ViewProfile", "ViewExchangeRates"],
        CurrencyData = [new(A, "PLN", "złoty", true, false, false, false, "AAAAAAAAAAE="),
            new(A, "EUR", "euro", true, true, false, true, "AAAAAAAAAAE="),
            new(A, "USD", "dolar", true, false, true, false, "AAAAAAAAAAE="),
            new(A, "JPY", "jen", false, false, false, false, "AAAAAAAAAAE=")]
    };

    [Fact]
    public async Task BulkCurrencies_SelectionIsIndependentAndAllIncludesHiddenRows_PreservesFilterAndNoNbp()
    {
        var api = BulkApi(); using var model = await Open(api); await model.OpenCurrencies(true);
        var editor = model.SelectedCurrency;
        model.CurrencySearch = "USD"; model.SelectCurrencies(false);
        Assert.Equal("USD", Assert.Single(model.VisibleCurrencySelections, x => x.IsChecked).Code);
        Assert.Same(editor, model.SelectedCurrency);
        model.SelectCurrencies(true); Assert.Contains("4 z 4", model.CurrencySelectionCount);
        model.BeginCurrencyActivity(false); Assert.True(model.CurrencyActivityPrompt);
        Assert.Contains("Do zmiany: 2", model.CurrencyActivityConfirmation); Assert.Empty(api.ActivityWrites);
        model.CancelCurrencyActivity(); Assert.Empty(api.ActivityWrites);
        model.BeginCurrencyActivity(false); await model.ConfirmCurrencyActivity();
        Assert.Equal(4, Assert.Single(api.ActivityWrites).Currencies.Length);
        Assert.Equal("USD", model.CurrencySearch); Assert.Single(model.VisibleCurrencyRows);
        Assert.True(model.CurrencyRows.Single(x => x.Code == "PLN").IsActive);
        Assert.All(model.CurrencyRows.Where(x => x.Code != "PLN"), x => { Assert.False(x.IsActive); Assert.False(x.DownloadA || x.DownloadB || x.DownloadC); });
        model.ClearCurrencySelection(); Assert.Contains("0 z 4", model.CurrencySelectionCount);
        model.BeginCurrencyActivity(true, true); await model.ConfirmCurrencyActivity();
        Assert.True(api.ActivityWrites.Last().All); Assert.Equal(4, api.ActivityWrites.Last().Currencies.Length);
        Assert.All(model.CurrencyRows, x => { Assert.True(x.IsActive); Assert.False(x.DownloadA || x.DownloadB || x.DownloadC); });
        model.BeginCurrencyActivity(false, true); Assert.Contains("PLN zostanie pominięty", model.CurrencyActivityConfirmation);
        Assert.Equal(0, api.NbpImports);
    }

    [Theory]
    [InlineData(409)]
    [InlineData(503)]
    public async Task BulkCurrencies_FailedAttemptKeepsSameIdempotencyKeyAndRequiresConfirmation(int status)
    {
        var api = BulkApi(); api.ActivityError = new Vatowiec.Desktop.Services.ApiException(status, "test");
        using var model = await Open(api); await model.OpenCurrencies(true);
        await model.ConfirmCurrencyActivity(); Assert.Empty(api.ActivityWrites);
        model.BeginCurrencyActivity(false, true); await model.ConfirmCurrencyActivity();
        Assert.True(model.CurrencyActivityPrompt); Assert.All(model.CurrencyRows, x => Assert.Equal(x.Code != "JPY", x.IsActive));
        api.ActivityError = null; await model.ConfirmCurrencyActivity();
        Assert.Equal(api.ActivityWrites[0], api.ActivityWrites[1]); Assert.False(model.CurrencyActivityPrompt);
        Assert.Equal(0, api.NbpImports);
    }

    [Fact]
    public async Task BulkCurrencies_DirtyEditorReadOnlyAndExpiryPreventWrites()
    {
        var api = BulkApi(); using var model = await Open(api); await model.OpenCurrencies(true);
        model.CurrencyEditor!.DownloadB = true; model.BeginCurrencyActivity(false, true);
        Assert.False(model.CurrencyActivityPrompt); Assert.Contains("Najpierw", model.Message);
        model.CancelCurrencyChanges(); model.BeginCurrencyActivity(false, true);
        await model.Logout(); await model.ConfirmCurrencyActivity(); Assert.Empty(api.ActivityWrites);
        using var reader = await Open(BulkApi(false)); await reader.OpenCurrencies(true);
        reader.SelectCurrencies(true); reader.BeginCurrencyActivity(false, true); await reader.ConfirmCurrencyActivity();
        Assert.False(reader.CurrencyActivityPrompt); Assert.False(reader.CanStartCurrencyActivity);
        Assert.Contains("0 z 4", reader.CurrencySelectionCount);
    }

    [Fact]
    public async Task CompanyDoubleClick_OpensClickedCompanyOnceThroughExistingAccessPath()
    {
        var api = new FakeApi(); using var model = new MainViewModel(_ => api) { UserName = "operator" };
        await model.Login("temporary"); model.SelectedCompany = model.Companies[0];
        Assert.Empty(api.ProfileCalls); // Single selection never opens a company.
        var pending = new TaskCompletionSource<CompanyProfileResponse>(); api.PendingProfile = pending.Task;
        var clicked = model.Companies[1]; var opening = model.OpenClickedCompany(clicked);
        await model.OpenClickedCompany(model.Companies[0]); await model.OpenCompany();
        Assert.Equal(B, Assert.Single(api.ProfileCalls)); Assert.Same(clicked, model.SelectedCompany);
        pending.SetResult(new(B, "Firma B", null, null)); await opening;
        await model.OpenClickedCompany(clicked); Assert.Single(api.ProfileCalls); Assert.Equal("Firma B", model.CurrentCompany);
        await model.ChangeCompany(); api.PendingProfile = null; model.SelectedCompany = model.Companies[0];
        await model.OpenCompany(); Assert.Equal(A, api.ProfileCalls.Last());
    }

    [Fact]
    public async Task CurrencyEditorNotifications_KeepFilteredListStableAndDoNotReenterDiscardPrompt()
    {
        var api = new FakeApi { Permissions = ["ViewProfile", "ViewExchangeRates", "ManageCurrencies"] };
        using var model = await Open(api); await model.OpenCurrencies(true);
        var rows = model.VisibleCurrencyRows;
        var selected = model.SelectedCurrency;
        model.CurrencyEditor!.DownloadC = true;
        Assert.Same(rows, model.VisibleCurrencyRows);
        Assert.Same(selected, model.SelectedCurrency);
        model.SelectedCurrency = null;
        Assert.True(model.DiscardPrompt);
        var notifications = 0;
        model.PropertyChanged += (_, _) => notifications++;
        model.SelectedCurrency = null;
        Assert.Equal(0, notifications);
        Assert.Same(rows, model.VisibleCurrencyRows);
        model.KeepEditing(); model.CancelCurrencyChanges();
        model.SelectedCurrency = null;
        Assert.Same(selected, model.SelectedCurrency);
        Assert.NotNull(model.CurrencyEditor);
        model.CurrencySearch = "EUR";
        Assert.NotSame(rows, model.VisibleCurrencyRows);
        Assert.All(model.VisibleCurrencyRows, x => Assert.Contains("EUR", x.Code));
    }

    [Fact]
    public async Task CurrencyViews_DoNotImportAutomaticallyAndUnsavedSettingsRequireDecision()
    {
        var api = new FakeApi { Permissions = ["ViewProfile", "ViewContractors", "EditContractors", "ViewExchangeRates", "ManageCurrencies", "ImportExchangeRates"] };
        using var model = await Open(api); await model.OpenCurrencies(true);
        Assert.True(model.IsCurrencySettings); Assert.False(model.IsBrowsingWorkspace); Assert.Equal(0, api.NbpImports);
        model.CurrencyEditor!.DownloadC = true; Assert.True(model.HasUnsavedChanges);
        await model.ImportNbpRates(); Assert.Equal(0, api.NbpImports); Assert.Contains("ustawienia", model.Message);
        model.CloseCurrencies(); Assert.True(model.DiscardPrompt); model.KeepEditing();
        await model.SaveCurrencySettings(); Assert.Single(api.CurrencyWrites); Assert.Equal(0, api.NbpImports);
        model.ShowCurrencySettings(false); await model.ImportNbpRates(); Assert.Equal(1, api.NbpImports); Assert.Contains("Brak nowych kursów", model.RateImportSummary);
        model.NewContractor(); Assert.False(model.IsEditing);
        model.CloseCurrencies(); Assert.False(model.IsCurrencyScreen); Assert.Empty(model.CurrencyRows);
    }
    [Fact]
    public async Task CurrencyReadOnlyUser_CannotTriggerWritesEvenByCallingCommands()
    {
        var api = new FakeApi { Permissions = ["ViewProfile", "ViewExchangeRates"] };
        using var model = await Open(api); await model.OpenCurrencies(true);
        Assert.False(model.CanConfigureCurrencies); Assert.False(model.CanImportRates);
        await model.SaveCurrencySettings(); await model.ImportNbpRates();
        Assert.Empty(api.CurrencyWrites); Assert.Equal(0, api.NbpImports);
    }
    [Fact]
    public async Task CurrencyState_IsRemovedOnSessionExpiry()
    {
        var clock = new Clock(); var api = new FakeApi { Permissions = ["ViewProfile", "ViewExchangeRates"], Expires = clock.Now.AddMinutes(1) };
        using var model = new MainViewModel(_ => api, clock) { UserName = "operator" };
        await model.Login("temporary"); model.SelectedCompany = model.Companies[0]; await model.OpenCompany(); await model.OpenCurrencies();
        Assert.NotEmpty(model.CurrencyRows); clock.Now = clock.Now.AddMinutes(2); model.CheckExpiry();
        Assert.True(model.IsLogin); Assert.Empty(model.CurrencyRows); Assert.Empty(model.RateRows); Assert.Empty(model.CurrencyHistoryRows); Assert.False(model.IsCurrencyScreen);
    }
    [Fact]
    public async Task CurrencyHistory_RestoresConflictProposalWithoutAnotherExternalRequest()
    {
        var rate = new RateValue("001/A/NBP/2026", new(2026, 10, 21), null, 4.1m, null, null);
        var result = new RateImportResponse(0, A, 1, 0, 0, [new(1, "EUR", "A", "AAAAAAAAAAE=", rate, rate with { Mid = 4.2m })], [], [], null, null, rate.EffectiveDate, null, null);
        var row = new CurrencyHistoryResponse(7, A, "Import", DateTimeOffset.UtcNow, "operator", JsonSerializer.Serialize(result, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        var api = new FakeApi { Permissions = ["ViewProfile", "ViewExchangeRates"], CurrencyHistoryData = [row] };
        using var model = await Open(api); await model.OpenCurrencies(); model.SelectedCurrencyHistory = model.CurrencyHistoryRows[0];
        Assert.Single(model.RateConflicts); Assert.Equal(0, api.NbpImports); Assert.Contains("historyczne", model.RateImportSummary);
        Assert.False(model.CanResolveRates);
    }
}
