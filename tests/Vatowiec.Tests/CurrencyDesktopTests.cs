using System.Text.Json;
using Vatowiec.Desktop.ViewModels;
using Vatowiec.SharedContracts;

namespace Vatowiec.Tests;

public sealed partial class DesktopTests
{
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
