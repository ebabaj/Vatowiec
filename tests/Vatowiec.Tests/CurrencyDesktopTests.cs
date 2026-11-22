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
        CurrencyData = [new(A, "PLN", "złoty", true, "AAAAAAAAAAE="),
            new(A, "EUR", "euro", true, "AAAAAAAAAAE="),
            new(A, "USD", "dolar", true, "AAAAAAAAAAE="),
            new(A, "JPY", "jen", false, "AAAAAAAAAAE=")]
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
        Assert.All(model.CurrencyRows.Where(x => x.Code != "PLN"), x => Assert.False(x.IsActive));
        model.ClearCurrencySelection(); Assert.Contains("0 z 4", model.CurrencySelectionCount);
        model.BeginCurrencyActivity(true, true); await model.ConfirmCurrencyActivity();
        Assert.True(api.ActivityWrites.Last().All); Assert.Equal(4, api.ActivityWrites.Last().Currencies.Length);
        Assert.All(model.CurrencyRows, x => Assert.True(x.IsActive));
        model.BeginCurrencyActivity(false, true); Assert.Contains("PLN zostanie pominięty", model.CurrencyActivityConfirmation);
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
    }

    [Fact]
    public async Task BulkCurrencies_DirtyEditorReadOnlyAndExpiryPreventWrites()
    {
        var api = BulkApi(); using var model = await Open(api); await model.OpenCurrencies(true);
        model.CurrencyEditor!.IsActive = !model.CurrencyEditor.IsActive; model.BeginCurrencyActivity(false, true);
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
        model.CurrencyEditor!.IsActive = !model.CurrencyEditor.IsActive;
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
    public async Task CurrencyViews_UnsavedActivityRequiresDecisionAndCompanyScreenHasNoImport()
    {
        var api = new FakeApi { Permissions = ["ViewProfile", "ViewContractors", "EditContractors", "ViewExchangeRates", "ManageCurrencies"] };
        using var model = await Open(api); await model.OpenCurrencies(true);
        Assert.True(model.IsCurrencySettings); Assert.False(model.IsBrowsingWorkspace);
        model.CurrencyEditor!.IsActive = !model.CurrencyEditor.IsActive; Assert.True(model.HasUnsavedChanges);
        model.CloseCurrencies(); Assert.True(model.DiscardPrompt); model.KeepEditing();
        await model.SaveCurrencySettings(); Assert.Single(api.CurrencyWrites); Assert.False(model.HasUnsavedChanges);
        model.ShowCurrencySettings(false); await model.LoadRates();
        Assert.Contains("VATowiec Operations", model.StoredRateDates);
        model.NewContractor(); Assert.False(model.IsEditing);
        model.CloseCurrencies(); Assert.False(model.IsCurrencyScreen); Assert.Empty(model.CurrencyRows);
        Assert.DoesNotContain(typeof(MainViewModel).GetMethods(), x => x.Name.Contains("Import", StringComparison.Ordinal) && x.Name.Contains("Nbp", StringComparison.Ordinal));
    }
    [Fact]
    public async Task CurrencyReadOnlyUser_CannotTriggerWritesEvenByCallingCommands()
    {
        var api = new FakeApi { Permissions = ["ViewProfile", "ViewExchangeRates"] };
        using var model = await Open(api); await model.OpenCurrencies(true);
        Assert.False(model.CanConfigureCurrencies);
        await model.SaveCurrencySettings();
        Assert.Empty(api.CurrencyWrites);
        Assert.False(model.IsInstallationAdministrator); await model.OpenExchangeRateAdministration();
        Assert.False(model.IsExchangeRateAdministrationScreen); Assert.Equal(0, api.ExchangeRateAdministrationReads);
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
    public async Task ExchangeRateAdministration_ShowsStatusResolvesWithConfirmationAndGuardsUnsavedStart()
    {
        var api = new FakeApi { InstallationAdmin = true, Permissions = ["ViewProfile"] };
        using var model = new MainViewModel(_ => api) { UserName = "admin" };
        await model.Login("temporary"); Assert.True(model.IsInstallationAdministrator);
        await model.OpenExchangeRateAdministration();
        var screen = model.ExchangeRateAdministration!;
        Assert.True(model.IsExchangeRateAdministrationScreen); Assert.True(screen.IsLoaded); Assert.False(model.IsCompanySelection);
        Assert.Contains("18.11.2026", screen.StatusSummary); Assert.Single(screen.History);
        Assert.True(screen.HasConflicts); Assert.False(screen.CanResolve);
        screen.SelectedConflict = screen.Conflicts[0]; Assert.Contains("NBP opublikował", screen.ConflictComparison);
        await model.ResolveExchangeRateConflict(true); Assert.Empty(api.ExchangeRateResolutions);
        screen.ConfirmResolution = true; await model.ResolveExchangeRateConflict(true);
        var resolution = Assert.Single(api.ExchangeRateResolutions);
        Assert.True(resolution.Value.AcceptIncoming); Assert.True(resolution.Value.Confirm); Assert.Equal("AAAAAAAAAAE=", resolution.Value.RateRowVersion);
        Assert.False(screen.HasConflicts);
        await model.RequestExchangeRateRun(); Assert.Equal(1, api.ExchangeRateRuns); Assert.Contains("Zlecono", model.Message);
        screen.HistoryStart = new DateTimeOffset(new DateTime(2024, 12, 1)); Assert.True(model.HasUnsavedChanges);
        model.CloseExchangeRateAdministration(); Assert.True(model.DiscardPrompt); model.KeepEditing();
        await model.SaveExchangeRateHistoryStart();
        Assert.Equal(new DateOnly(2024, 12, 1), Assert.Single(api.HistoryStartWrites).HistoryStartDate);
        Assert.False(model.HasUnsavedChanges);
        model.CloseExchangeRateAdministration(); Assert.False(model.IsExchangeRateAdministrationScreen); Assert.True(model.IsGusSettings);
    }

    private sealed partial class FakeApi
    {
        public int ExchangeRateAdministrationReads { get; private set; }
        public int ExchangeRateRuns { get; private set; }
        public List<(long Id, ExchangeRateConflictResolution Value)> ExchangeRateResolutions { get; } = [];
        public List<ExchangeRateHistoryStartRequest> HistoryStartWrites { get; } = [];
        private DateOnly rateHistoryStart = new(2025, 1, 1);
        private bool rateConflictOpen = true;
        private ExchangeRateAdministrationResponse RateAdministration() => new(rateHistoryStart, false,
            new(DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, null, new(2026, 11, 18), new(2026, 11, 18), new(2026, 11, 18), ExchangeRateRuns > 0),
            rateConflictOpen ? [new(5, "EUR", "A", new("222/A/NBP/2026", new(2026, 11, 18), null, 4.1m, null, null),
                new("222/A/NBP/2026", new(2026, 11, 18), null, 4.2m, null, null), DateTimeOffset.UtcNow, "AAAAAAAAAAI=", "AAAAAAAAAAE=")] : [],
            "AAAAAAAAAAM=");
        public Task<ExchangeRateAdministrationResponse> ExchangeRateAdministration() { ExchangeRateAdministrationReads++; return Task.FromResult(RateAdministration()); }
        public Task<ExchangeRateAdministrationResponse> RequestExchangeRateRun() { ExchangeRateRuns++; return Task.FromResult(RateAdministration()); }
        public Task<ExchangeRateAdministrationResponse> SaveExchangeRateHistoryStart(ExchangeRateHistoryStartRequest value)
        { HistoryStartWrites.Add(value); rateHistoryStart = value.HistoryStartDate; return Task.FromResult(RateAdministration()); }
        public Task<ExchangeRateAdministrationResponse> ResolveExchangeRateConflict(long conflictId, ExchangeRateConflictResolution value)
        { ExchangeRateResolutions.Add((conflictId, value)); rateConflictOpen = false; return Task.FromResult(RateAdministration()); }
        public Task<ExchangeRateOperationResponse[]> ExchangeRateHistory(int offset) =>
            Task.FromResult<ExchangeRateOperationResponse[]>([new(1, "Download", DateTimeOffset.UtcNow, "VATowiec Operations (kursy NBP)",
                """{"table":"A","from":"2026-11-11","to":"2026-11-18","tables":6,"added":4,"skipped":20,"conflicts":0,"unknownCodes":[]}""")]);
    }
}
