using System.Globalization;
using Vatowiec.Core.Domain;
using Vatowiec.Desktop.Converters;
using Vatowiec.Desktop.Services;
using Vatowiec.SharedContracts;

namespace Vatowiec.Tests;

public sealed partial class DesktopTests
{
    [Fact]
    public async Task ArchiveTransport_UsesExistingCurrencyDtoAndCompanyRouteWithoutChangingOrdinaryDictionary()
    {
        var paths = new List<string>(); var data = ActiveRateApi().CurrencyData!;
        using var client = new VatowiecApiClient(new HttpClient(new Handler(request =>
        {
            Assert.Equal(HttpMethod.Get, request.Method); paths.Add(request.RequestUri!.PathAndQuery);
            return Task.FromResult(Json(paths.Count == 1 ? data : data.Where(x => x.Code == "USD").ToArray()));
        }))
        { BaseAddress = new("https://localhost") });
        Assert.Equal(4, (await client.Currencies(A)).Length);
        Assert.Equal("USD", Assert.Single(await client.CurrenciesWithRates(A)).Code);
        Assert.Equal(new[] { $"/api/v1/companies/{A}/currencies", $"/api/v1/companies/{A}/currencies?withRatesOnly=true" }, paths);
    }

    private static FinanceLookups ActiveFinanceLookups() => new([
        new("PLN", "złoty", true), new("EUR", "euro", true), new("USD", "dolar", false), new("JPY", "jen", false)],
        [new(1, "TRANSFER", "Przelew", true, "AAAAAAAAAAE=")]);

    [Fact]
    public async Task FinanceCurrencyChoices_OnlyActiveForNewValuesAndSavedInactiveIsPreservedWithoutDirtyBinding()
    {
        var api = SavedFinanceApi(); api.FinanceLookupOverride = ActiveFinanceLookups();
        api.termData["Sale"] = api.termData["Sale"] with { Data = new(30, "TRANSFER", "USD") };
        api.limitData.Add(new(9, A, 4132, "Sale", "USD", 2500m, true, "AAAAAAAAAAQ="));
        using var model = await OpenSavedFinance(api); model.SelectedTab = 5;
        Assert.Equal(new[] { "", "PLN", "EUR", "USD" }, model.TradeCurrencyChoices.Select(x => x.Code));
        var usd = model.SelectedTradeCurrency!; Assert.False(usd.IsActive); Assert.Contains("nieaktywna", usd.ToString());
        var choices = model.TradeCurrencyChoices;
        model.SelectedTradeCurrency = null; model.SelectedTradeCurrency = usd;
        Assert.Same(choices, model.TradeCurrencyChoices); Assert.False(model.HasFinanceChanges);
        model.NewCreditLimit(); Assert.Equal(new[] { "PLN", "EUR" }, model.LimitCurrencyChoices.Select(x => x.Code));
        model.SelectedLimitCurrency = usd; Assert.Equal("", model.CreditLimitEditor!.CurrencyCode);
        model.SelectedCreditLimit = model.CreditLimits.Single(); model.EditCreditLimit();
        Assert.Equal("USD", model.SelectedLimitCurrency!.Code); Assert.False(model.SelectedLimitCurrency.IsActive);
        Assert.False(model.CreditLimitEditor!.IsDirty); Assert.Equal(2500m, model.CreditLimitEditor.Request("Sale").Amount);
        model.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(model.LimitCurrencyChoices)) model.SelectedLimitCurrency = new("PLN", "rebind", true); };
        model.NewPaymentMethod(); model.PaymentMethodEditor!.Name = "Testowy sposób"; await model.SavePaymentMethod();
        Assert.Equal("USD", model.CreditLimitEditor.CurrencyCode); Assert.False(model.CreditLimitEditor.IsDirty);
        model.SelectedLimitCurrency = null; Assert.Equal("USD", model.CreditLimitEditor.CurrencyCode);
        model.SelectedLimitCurrency = model.LimitCurrencyChoices.Single(x => x.Code == "EUR");
        model.SelectedLimitCurrency = usd; Assert.Equal("EUR", model.CreditLimitEditor.CurrencyCode);
        model.CancelCreditLimit(); Assert.True(model.DiscardPrompt); model.ConfirmDiscard();
        Assert.Equal("USD", model.TradeTermsEditor!.CurrencyCode); Assert.False(model.TradeTermsEditor.IsDirty);
        model.SelectedTradeCurrency = model.TradeCurrencyChoices.Single(x => x.Code == "EUR");
        model.SelectedTradeCurrency = usd; Assert.Equal("EUR", model.TradeTermsEditor.CurrencyCode);
        model.SelectedTab = 0; Assert.True(model.DiscardPrompt); model.KeepEditing();
        Assert.Equal("EUR", model.TradeTermsEditor.CurrencyCode);
        Assert.Empty(api.TermWrites); Assert.Empty(api.Writes);
        var converter = new FinanceCurrencyLabelConverter();
        Assert.Equal("USD (nieaktywna)", converter.Convert(["USD", model.FinanceCurrencies], typeof(string), null, CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task SavedInactiveTradeCurrency_SurvivesSavingOtherFieldsAndReopening()
    {
        var api = SavedFinanceApi(); api.FinanceLookupOverride = ActiveFinanceLookups();
        api.termData["Sale"] = api.termData["Sale"] with { Data = new(30, "TRANSFER", "USD") };
        using var model = await OpenSavedFinance(api); model.SelectedTab = 5;
        Assert.False(model.HasUnknownTradeCodes); Assert.True(model.CanSaveTradeTerms);
        model.TradeTermsEditor!.Notes = "Zmieniono wyłącznie uwagi"; await model.SaveTradeTerms();
        Assert.Equal("USD", Assert.Single(api.TermWrites).Request.Data.CurrencyCode);
        model.SelectedTab = 0; model.SelectedTab = 5;
        Assert.Equal("USD", model.SelectedTradeCurrency!.Code); Assert.False(model.SelectedTradeCurrency.IsActive);
        Assert.False(model.HasFinanceChanges); Assert.False(model.DiscardPrompt);
        Assert.Empty(api.Writes);
    }

    [Fact]
    public async Task FinanceActiveCurrencyChoices_AreStableOnRefreshAndDoNotCreateFalseChanges()
    {
        var api = SavedFinanceApi(); api.FinanceLookupOverride = ActiveFinanceLookups();
        using var model = await OpenSavedFinance(api); model.SelectedTab = 5;
        Assert.Equal(new[] { "", "PLN", "EUR" }, model.TradeCurrencyChoices.Select(x => x.Code));
        var choices = model.TradeCurrencyChoices;
        model.TradeTermsEditor!.Notes = "zmiana"; Assert.Same(choices, model.TradeCurrencyChoices);
        model.CancelTradeTerms(); model.ConfirmDiscard();
        model.RefreshFinance(); Assert.False(model.HasFinanceChanges); Assert.Equal("PLN", model.SelectedTradeCurrency!.Code);
        model.NewCreditLimit(); Assert.Same(model.LimitCurrencyChoices, model.LimitCurrencyChoices);
        Assert.Equal(new[] { "PLN", "EUR" }, model.LimitCurrencyChoices.Select(x => x.Code));
        Assert.False(model.HasFinanceChanges); Assert.Empty(api.TermWrites); Assert.Empty(api.Writes);
    }

    private static FakeApi ActiveRateApi()
    {
        var api = BulkApi(); api.CurrencyData = api.CurrencyData!.Select(x => x with { IsActive = x.Code is "PLN" or "EUR", DownloadB = false }).ToArray();
        api.CurrenciesWithRatesData = api.CurrencyData.Where(x => x.Code == "USD").ToArray();
        api.StoredRates = [new(1, A, "USD", "NBP", "A", "001/A/NBP/2026", new(2026, 1, 2), null, 4.1m, null, null, DateTimeOffset.UtcNow, "AAAAAAAAAAE=")];
        return api;
    }

    [Fact]
    public async Task RateChoices_ActiveEurOnlyAndArchiveFindsUsdWithoutImportOrChangingOtherFilters()
    {
        var api = ActiveRateApi(); using var model = await Open(api); await model.OpenCurrencies();
        Assert.Equal(new[] { "", "EUR" }, model.RateCurrencyChoices.Select(x => x.Code));
        Assert.Equal("", model.SelectedRateCurrency!.Code); Assert.Single(model.RateRows); Assert.Equal(0, api.ArchiveCurrencyReads);
        model.RateTableFilter = 1; model.RateFromFilter = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        model.RateToFilter = new(2026, 1, 31, 0, 0, 0, TimeSpan.Zero); model.RateDescending = false;
        var stable = model.RateCurrencyChoices; await model.LoadRates(); Assert.Same(stable, model.RateCurrencyChoices);
        await model.ShowInactiveCurrencies(true);
        Assert.Equal(new[] { "", "EUR", "USD" }, model.RateCurrencyChoices.Select(x => x.Code));
        var usd = model.RateCurrencyChoices.Single(x => x.Code == "USD"); Assert.Contains("nieaktywna", usd.ToString());
        model.SelectedRateCurrency = usd; await model.LoadRates();
        Assert.Equal("USD", api.LastRateQuery.Code); Assert.Equal("A", api.LastRateQuery.Table);
        Assert.Equal(new DateOnly(2026, 1, 1), api.LastRateQuery.From); Assert.Equal(new DateOnly(2026, 1, 31), api.LastRateQuery.To);
        Assert.False(api.LastRateQuery.Descending); Assert.Equal("USD", Assert.Single(model.RateRows).CurrencyCode);
        await model.ShowInactiveCurrencies(false); Assert.Equal("USD", model.RateCurrencyFilter!.Code);
        model.SelectedRateCurrency = model.RateCurrencyChoices[0]; await model.LoadRates();
        Assert.Null(api.LastRateQuery.Code); Assert.Single(model.RateRows);
        Assert.Equal(new[] { "", "EUR" }, model.RateCurrencyChoices.Select(x => x.Code));
        model.ShowCurrencySettings(true); Assert.Equal(4, model.CurrencyRows.Length); Assert.Contains(model.CurrencyRows, x => x.Code == "USD" && !x.IsActive);
        Assert.Equal(0, api.NbpImports); Assert.Empty(api.ActivityWrites);
    }

    [Fact]
    public async Task ActivityRefresh_PreservesExplicitCurrencyDateTableAndSortFiltersDuringChoiceRebinding()
    {
        var api = ActiveRateApi(); using var model = await Open(api); await model.OpenCurrencies(true);
        model.SelectedRateCurrency = model.RateCurrencyChoices.Single(x => x.Code == "EUR");
        model.RateTableFilter = 3; model.RateDescending = false;
        var date = DateTimeOffset.Now.Date; model.RateFromFilter = date; model.RateToFilter = date;
        model.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(model.RateCurrencyChoices)) { model.SelectedRateCurrency = model.RateCurrencyChoices[0]; model.RateCurrencyFilter = null; }
        };
        api.CurrencyData = api.CurrencyData!.Select(x => x.Code == "EUR" ? x with { IsActive = false, DownloadA = false } : x).ToArray();
        await model.RefreshCurrencies();
        Assert.Equal("EUR", model.SelectedRateCurrency!.Code); Assert.False(model.SelectedRateCurrency.IsActive);
        Assert.Equal("EUR", model.RateCurrencyFilter!.Code); Assert.Equal(3, model.RateTableFilter);
        Assert.Equal(date, model.RateFromFilter); Assert.Equal(date, model.RateToFilter); Assert.False(model.RateDescending);
        Assert.Equal(0, api.NbpImports); Assert.False(model.HasUnsavedChanges);
    }

    [Theory]
    [InlineData(true, "PLN", true, false)]
    [InlineData(false, "USD", true, false)]
    [InlineData(true, "EUR", false, false)]
    [InlineData(true, "EUR", true, true)]
    public void NbpSelection_RequiresActiveForeignCurrencyAndExplicitDownloadFlag(bool active, string code, bool download, bool expected)
    {
        var row = new Currency { Code = code, IsActive = active, DownloadA = download, DownloadB = download, DownloadC = download };
        foreach (var table in new[] { "A", "B", "C" }) Assert.Equal(expected, row.Downloads(table));
    }
}
