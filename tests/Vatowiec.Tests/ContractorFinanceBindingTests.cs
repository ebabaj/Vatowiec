using Vatowiec.Desktop.ViewModels;
using Vatowiec.SharedContracts;

namespace Vatowiec.Tests;

public sealed partial class DesktopTests
{
    private static FakeApi SavedFinanceApi(bool editable = true)
    {
        var api = new FakeApi { Permissions = editable ? ["ViewProfile", "ViewContractors", "EditContractors"] : ["ViewProfile", "ViewContractors"] };
        api.termData["Sale"] = new(10, A, 4132, "Sale", new(30, "TRANSFER", "PLN"), "AAAAAAAAAAE=");
        api.termData["Purchase"] = new(11, A, 4132, "Purchase", new(45, "TRANSFER", "PLN"), "AAAAAAAAAAI=");
        return api;
    }
    private static async Task<MainViewModel> OpenSavedFinance(FakeApi api)
    {
        var model = await Open(api);
        model.SelectedContractor = new(4132, A, "DEMO", "Fikcyjny kontrahent testu wiązań", "PL");
        await model.EditContractor();
        return model;
    }
    private static void AssertSavedFinance(MainViewModel model, string days, string version)
    {
        Assert.Equal(days, model.TradeTermsEditor!.Days);
        Assert.Equal("TRANSFER", model.TradeTermsEditor.PaymentMethodCode); Assert.Equal("PLN", model.TradeTermsEditor.CurrencyCode);
        Assert.Equal(version, model.TradeTermsEditor.RowVersion);
        Assert.Equal("TRANSFER", model.SelectedTradePayment!.Code); Assert.Equal("PLN", model.SelectedTradeCurrency!.Code);
        Assert.Same(model.TradePaymentChoices, model.TradePaymentChoices); Assert.Same(model.TradeCurrencyChoices, model.TradeCurrencyChoices);
        Assert.Contains(model.TradePaymentChoices, x => ReferenceEquals(x, model.SelectedTradePayment));
        Assert.Contains(model.TradeCurrencyChoices, x => ReferenceEquals(x, model.SelectedTradeCurrency));
        Assert.False(model.HasFinanceChanges); Assert.False(model.DiscardPrompt);
    }

    [Fact]
    public async Task SavedSale30AndPurchase45RestoreCodesWithoutDirtyDraftOrWrites()
    {
        var api = SavedFinanceApi(); using var model = await OpenSavedFinance(api); model.SelectedTab = 5;
        AssertSavedFinance(model, "30", "AAAAAAAAAAE=");
        model.FinanceDirectionIndex = 1; AssertSavedFinance(model, "45", "AAAAAAAAAAI=");
        model.FinanceDirectionIndex = 0; AssertSavedFinance(model, "30", "AAAAAAAAAAE=");
        model.RefreshFinance(); AssertSavedFinance(model, "30", "AAAAAAAAAAE=");
        model.SelectedTab = 0; model.SelectedTab = 5; AssertSavedFinance(model, "30", "AAAAAAAAAAE=");
        model.SelectedTradeCurrency = null; model.SelectedTradePayment = null;
        AssertSavedFinance(model, "30", "AAAAAAAAAAE="); Assert.Empty(api.TermWrites); Assert.Empty(api.Writes);
    }

    [Fact]
    public async Task ChoicePublicationIgnoresTransientPlaceholderCallbacksAndKeepsCodesAfterDictionaryRefresh()
    {
        var api = SavedFinanceApi(); using var model = await OpenSavedFinance(api);
        model.PropertyChanged += (_, e) =>
        {
            // ComboBox can report its previous/placeholder selection during ItemsSource replacement.
            if (e.PropertyName == nameof(model.TradePaymentChoices)) model.SelectedTradePayment = model.TradePaymentChoices[0];
            if (e.PropertyName == nameof(model.TradeCurrencyChoices)) model.SelectedTradeCurrency = model.TradeCurrencyChoices[0];
        };
        model.SelectedTab = 5; AssertSavedFinance(model, "30", "AAAAAAAAAAE=");
        model.NewPaymentMethod(); model.PaymentMethodEditor!.Name = "Testowy sposób"; await model.SavePaymentMethod();
        AssertSavedFinance(model, "30", "AAAAAAAAAAE="); Assert.Empty(api.TermWrites);
        // Selection is resolved by a stable code even if the UI passes a different instance.
        model.SelectedTradeCurrency = new("PLN", "inna instancja tego samego kodu", true);
        model.SelectedTradePayment = new(1, "TRANSFER", "inna instancja", true, "");
        AssertSavedFinance(model, "30", "AAAAAAAAAAE=");
    }

    [Fact]
    public async Task DelayedLookupsDoNotPublishEmptyDefaultsOrMakeInitialLoadAnEdit()
    {
        var api = SavedFinanceApi(); var gate = new TaskCompletionSource<FinanceLookups>();
        api.PendingFinanceLookups = gate.Task;
        using var model = await OpenSavedFinance(api); model.SelectedTab = 5;
        Assert.True(model.IsBusy); Assert.Null(model.TradeTermsEditor); Assert.Empty(model.TradePaymentChoices);
        model.SelectedTradePayment = new(0, "", "przejściowa pusta opcja", true, ""); model.SelectedTradeCurrency = new("", "pusta", true);
        gate.SetResult(new([new("PLN", "złoty", true)], [new(1, "TRANSFER", "Przelew", true, "AAAAAAAAAAE=")]));
        await WaitFinanceIdle(model);
        AssertSavedFinance(model, "30", "AAAAAAAAAAE=");
        model.FinanceDirectionIndex = 1; await WaitFinanceIdle(model); AssertSavedFinance(model, "45", "AAAAAAAAAAI=");
        Assert.Empty(api.TermWrites);
    }
    private static async Task WaitFinanceIdle(MainViewModel model)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (model.IsBusy) await Task.Delay(5, timeout.Token);
    }

    [Fact]
    public async Task RealEditGuardsDirectionAndRetainsIndependentRowVersions()
    {
        var api = SavedFinanceApi(); using var model = await OpenSavedFinance(api); model.SelectedTab = 5;
        model.SelectedTradeCurrency = model.TradeCurrencyChoices.Single(x => x.Code == "EUR");
        Assert.True(model.HasFinanceChanges); model.FinanceDirectionIndex = 1;
        Assert.True(model.DiscardPrompt); Assert.Equal("Sale", model.FinanceDirection);
        model.KeepEditing(); Assert.Equal("EUR", model.SelectedTradeCurrency!.Code); Assert.Equal("AAAAAAAAAAE=", model.TradeTermsEditor!.RowVersion);
        model.FinanceDirectionIndex = 1; model.ConfirmDiscard(); AssertSavedFinance(model, "45", "AAAAAAAAAAI=");
        model.FinanceDirectionIndex = 0; AssertSavedFinance(model, "30", "AAAAAAAAAAE="); Assert.Empty(api.TermWrites);
    }

    [Fact]
    public async Task SaveAndReopenRetainsCodesAndNeverSavesMainDraftOrOtherDirection()
    {
        var api = SavedFinanceApi(); using var model = await OpenSavedFinance(api); model.SelectedTab = 5;
        model.Editor!.Name = "Niezapisana nazwa"; model.TradeTermsEditor!.Notes = "Testowy zapis";
        await model.SaveTradeTerms(); AssertSavedFinance(model, "30", "AAAAAAAAAAM=");
        var write = Assert.Single(api.TermWrites); Assert.Equal("AAAAAAAAAAE=", write.Request.RowVersion);
        Assert.Equal(new TradeTermsValues(30, "TRANSFER", "PLN", Notes: "Testowy zapis"), write.Request.Data);
        Assert.Empty(api.Writes); Assert.True(model.Editor.IsDirty);
        model.FinanceDirectionIndex = 1; AssertSavedFinance(model, "45", "AAAAAAAAAAI=");
        model.SelectedTab = 0; model.SelectedTab = 5; AssertSavedFinance(model, "30", "AAAAAAAAAAM=");
    }

    [Fact]
    public async Task UnknownCodesAreExplicitPreservedAndCannotSilentlyBecomeNullOnSave()
    {
        var api = SavedFinanceApi(); api.FinanceLookupOverride = new([], []);
        using var model = await OpenSavedFinance(api); model.SelectedTab = 5;
        AssertSavedFinance(model, "30", "AAAAAAAAAAE=");
        Assert.True(model.HasUnknownTradeCodes); Assert.False(model.CanSaveTradeTerms); Assert.Contains("Zachowano", model.FinanceLookupWarning);
        Assert.Contains("niedostępny", model.SelectedTradePayment!.Name); Assert.Contains("niedostępny", model.SelectedTradeCurrency!.Name);
        await model.SaveTradeTerms(); Assert.Empty(api.TermWrites);
        Assert.Equal("TRANSFER", model.TradeTermsEditor!.Request().Data.PaymentMethodCode); Assert.Equal("PLN", model.TradeTermsEditor.Request().Data.CurrencyCode);
        model.SelectedTradePayment = model.TradePaymentChoices.Single(x => x.Code == "");
        model.SelectedTradeCurrency = model.TradeCurrencyChoices.Single(x => x.Code == "");
        Assert.True(model.HasFinanceChanges); Assert.False(model.HasUnknownTradeCodes); Assert.True(model.CanSaveTradeTerms);
    }

    [Fact]
    public async Task SavedCodesRenderReadOnlyAndLimitsStayIndependentOfTerms()
    {
        var api = SavedFinanceApi(false);
        api.limitData.Add(new(9, A, 4132, "Sale", "EUR", 2500m, true, "AAAAAAAAAAQ="));
        api.limitData.Add(new(10, A, 4132, "Purchase", "PLN", 10000m, true, "AAAAAAAAAAU="));
        using var model = await OpenSavedFinance(api); model.SelectedTab = 5;
        AssertSavedFinance(model, "30", "AAAAAAAAAAE="); Assert.False(model.CanSaveTradeTerms);
        model.SelectedTradeCurrency = model.TradeCurrencyChoices[0]; model.SelectedTradePayment = model.TradePaymentChoices[0];
        AssertSavedFinance(model, "30", "AAAAAAAAAAE="); Assert.Equal(2500m, Assert.Single(model.CreditLimits).Amount);
        model.FinanceDirectionIndex = 1; AssertSavedFinance(model, "45", "AAAAAAAAAAI=");
        Assert.Equal(10000m, Assert.Single(model.CreditLimits).Amount); await model.SaveTradeTerms(); Assert.Empty(api.TermWrites);
    }
}
