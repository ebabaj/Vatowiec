using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Vatowiec.Core.Domain;
using Vatowiec.Core.Infrastructure.Persistence;
using Vatowiec.Desktop.ViewModels;
using Vatowiec.SharedContracts;

namespace Vatowiec.Tests;

public sealed class ContractorFinanceTests
{
    [Theory]
    [InlineData(null, true)]
    [InlineData(0, true)]
    [InlineData(3650, true)]
    [InlineData(-1, false)]
    [InlineData(3651, false)]
    public void OptionalDaysHaveExactBounds(int? days, bool valid) => Assert.Equal(valid, FinanceValidation.Terms(new(PaymentDays: days)).Count == 0);
    [Theory]
    [InlineData("0", true)]
    [InlineData("100", true)]
    [InlineData("12.3456", true)]
    [InlineData("-0.0001", false)]
    [InlineData("100.0001", false)]
    [InlineData("1.00001", false)]
    public void DiscountNeverRoundsImplicitly(string number, bool valid) => Assert.Equal(valid, FinanceValidation.Terms(new(DiscountPercent: decimal.Parse(number, System.Globalization.CultureInfo.InvariantCulture))).Count == 0);
    [Theory]
    [InlineData("0", true)]
    [InlineData("100000.12345678", true)]
    [InlineData("-0.01", false)]
    [InlineData("1.123456789", false)]
    [InlineData("10000000000000000", false)]
    public void LimitPrecisionAndNonnegativeAmount(string number, bool valid) => Assert.Equal(valid, FinanceValidation.Limit(new(0, "Sale", "EUR", decimal.Parse(number, System.Globalization.CultureInfo.InvariantCulture), true)).Count == 0);
    [Fact]
    public void EmptyTermsNeverDefaultToPlnAndEditorsKeepInvalidDrafts()
    {
        var editor = new TradeTermsEditor(new(0, Guid.NewGuid(), 1, "Sale", new(), null));
        Assert.False(editor.IsDirty); Assert.Null(editor.Request().Data.CurrencyCode); Assert.Null(editor.Request().Data.PaymentDays); Assert.Null(editor.Request().Data.DiscountPercent);
        editor.Days = "0"; editor.Discount = "12,3456"; Assert.Equal(12.3456m, editor.Request().Data.DiscountPercent); Assert.Equal(0, editor.Request().Data.PaymentDays);
        editor.Days = "1,5"; Assert.Throws<ArgumentException>(() => editor.Request()); Assert.Equal("1,5", editor.Days);
        editor.Days = "14"; editor.Discount = "1,12345"; Assert.Throws<ArgumentException>(() => editor.Request());
        var limit = new CreditLimitEditor { Amount = "123,12", CurrencyCode = "EUR" }; Assert.Equal(123.12m, limit.Request("Purchase").Amount);
    }
    [Fact]
    public void SchemaHasNaturalUniquenessRealCurrencyFksAndConcurrency()
    {
        using var db = new CompanyDbContextFactory().CreateDbContext([]);
        var model = db.GetService<IDesignTimeModel>().Model;
        var terms = model.FindEntityType(typeof(ContractorTradeTerms))!;
        var limit = model.FindEntityType(typeof(ContractorCreditLimit))!;
        Assert.Contains(terms.GetIndexes(), x => x.IsUnique && x.Properties.Select(p => p.Name).SequenceEqual(new[] { "ContractorId", "Direction" }));
        var index = Assert.Single(limit.GetIndexes(), x => x.IsUnique); Assert.Equal("[IsActive] = 1", index.GetFilter());
        Assert.Equal(new[] { "ContractorId", "Direction", "CurrencyCode" }, index.Properties.Select(x => x.Name));
        foreach (var entity in new[] { terms, limit })
        {
            Assert.Equal("Latin1_General_100_BIN2", entity.FindProperty("CurrencyCode")!.GetCollation());
            Assert.Null(entity.FindProperty("CompanyId")); Assert.True(entity.FindProperty("RowVersion")!.IsConcurrencyToken);
            Assert.Contains(entity.GetForeignKeys(), x => x.PrincipalEntityType.ClrType == typeof(Currency) && x.Properties.Single().Name == "CurrencyCode");
        }
        Assert.True(terms.FindProperty("CurrencyCode")!.IsNullable); Assert.False(limit.FindProperty("CurrencyCode")!.IsNullable);
        Assert.Contains(terms.GetForeignKeys(), x => x.PrincipalEntityType.ClrType == typeof(PaymentMethod) && x.PrincipalKey.Properties.Single().Name == "Code");
        var seed = model.FindEntityType(typeof(PaymentMethod))!.GetSeedData().ToArray();
        Assert.Equal(new[] { "Przelew", "Gotówka", "Karta", "Kompensata", "Inny" }, seed.OrderBy(x => (int)x["Id"]!).Select(x => x["Name"]));
        Assert.Null(new SystemDbContextFactory().CreateDbContext([]).Model.FindEntityType(typeof(ContractorTradeTerms)));
    }
}

public sealed partial class DesktopTests
{
    private static async Task<MainViewModel> FinanceForm(FakeApi api)
    {
        var model = await OpenDetailForm(api); model.SelectedTab = 5;
        // Fake transport completes synchronously; avoids fire-and-forget timing in this test.
        Assert.NotNull(model.TradeTermsEditor); return model;
    }
    [Fact]
    public async Task FinanceSaveDoesNotSaveMainDraftAndDirectionsAreIndependent()
    {
        var api = new FakeApi { Permissions = ["ViewProfile", "ViewContractors", "EditContractors"] };
        using var model = await FinanceForm(api);
        var main = model.Editor!; main.Name = "Niezapisana nazwa";
        model.TradeTermsEditor!.Days = "14"; await model.SaveTradeTerms();
        Assert.Empty(api.Writes); Assert.Same(main, model.Editor); Assert.Equal("Niezapisana nazwa", main.Name); Assert.True(main.IsDirty);
        await model.SaveContractor(); Assert.Empty(api.Writes); // Root shortcut cannot save while on Finance.
        model.FinanceDirectionIndex = 1; Assert.Equal("Purchase", model.FinanceDirection); Assert.Empty(model.TradeTermsEditor!.Days);
        model.TradeTermsEditor.Days = "30"; await model.SaveTradeTerms();
        model.FinanceDirectionIndex = 0; Assert.Equal("14", model.TradeTermsEditor!.Days);
        Assert.Equal(new[] { "Sale", "Purchase" }, api.TermWrites.Select(x => x.Direction));
    }
    [Fact]
    public async Task DirtyFinanceGuardsDirectionTabsRefreshAndClose()
    {
        using var model = await FinanceForm(new FakeApi { Permissions = ["ViewProfile", "ViewContractors", "EditContractors"] });
        model.TradeTermsEditor!.Notes = "Niezapisane warunki";
        model.FinanceDirectionIndex = 1; Assert.True(model.DiscardPrompt); Assert.Equal("Sale", model.FinanceDirection);
        model.KeepEditing(); Assert.Equal("Niezapisane warunki", model.TradeTermsEditor.Notes);
        model.SelectedTab = 1; Assert.True(model.DiscardPrompt); Assert.Equal(5, model.SelectedTab); model.KeepEditing();
        model.RefreshFinance(); Assert.True(model.DiscardPrompt); model.KeepEditing();
        model.CancelEditor(); Assert.True(model.DiscardPrompt); model.KeepEditing();
        model.FinanceDirectionIndex = 1; model.ConfirmDiscard(); Assert.Equal("Purchase", model.FinanceDirection); Assert.Empty(model.TradeTermsEditor!.Notes);
    }
    [Fact]
    public async Task FinanceConflictsKeepBothVersionsAndRequireExplicitRetry()
    {
        var api = new FakeApi { Permissions = ["ViewProfile", "ViewContractors", "EditContractors"] };
        using var model = await FinanceForm(api);
        model.TradeTermsEditor!.Days = "14";
        api.FinanceError = new Vatowiec.Desktop.Services.ApiException(409, "Konflikt") { Code = "finance_conflict", FinanceCurrent = new(new(42, A, model.Editor!.Id, "Sale", new(30), "AAAAAAAAAAI="), null, null) };
        await model.SaveTradeTerms(); Assert.True(model.HasFinanceConflict); Assert.Equal("14", model.TradeTermsEditor.Days); Assert.False(model.CanSaveFinance);
        model.ResolveFinanceConflict(true); Assert.Equal("AAAAAAAAAAI=", model.TradeTermsEditor.RowVersion); Assert.Equal("14", model.TradeTermsEditor.Days); Assert.False(model.HasFinanceConflict);
        api.FinanceError = null; await model.SaveTradeTerms(); Assert.False(model.TradeTermsEditor.IsDirty); Assert.Empty(api.Writes);
        model.NewCreditLimit(); model.CreditLimitEditor!.CurrencyCode = "EUR"; model.CreditLimitEditor.Amount = "100,25";
        await model.SaveCreditLimit(); Assert.Null(model.CreditLimitEditor); Assert.Single(model.CreditLimits); Assert.Equal(100.25m, model.CreditLimits[0].Amount);
        model.NewPaymentMethod(); var code = model.PaymentMethodEditor!.Code; model.PaymentMethodEditor.Name = "Płatność testowa"; await model.SavePaymentMethod();
        Assert.Contains(model.PaymentMethods, x => x.Code == code && x.Name == "Płatność testowa"); Assert.Equal("14", model.TradeTermsEditor.Days);
    }
    [Fact]
    public async Task FinanceReaderCannotWriteAndUnsavedContractorHasNoFinanceApiCalls()
    {
        var api = new FakeApi { Permissions = ["ViewProfile", "ViewContractors"] };
        using var model = await FinanceForm(api); Assert.False(model.CanEditFinance);
        model.TradeTermsEditor!.Days = "20"; await model.SaveTradeTerms(); model.NewCreditLimit(); model.NewPaymentMethod();
        Assert.Empty(api.TermWrites); Assert.Null(model.CreditLimitEditor); Assert.Null(model.PaymentMethodEditor);
        using var other = await Open(new FakeApi { Permissions = ["ViewProfile", "ViewContractors", "EditContractors"] });
        other.NewContractor(); other.SelectedTab = 5; Assert.False(other.HasSavedContractor); Assert.Null(other.TradeTermsEditor); Assert.False(other.CanEditFinance);
    }
    private sealed partial class FakeApi
    {
        public Task<FinanceLookups>? PendingFinanceLookups { get; set; }
        public FinanceLookups? FinanceLookupOverride { get; set; }
        public List<(string Direction, TradeTermsRequest Request)> TermWrites { get; } = [];
        public readonly Dictionary<string, TradeTermsResponse> termData = [];
        public readonly List<CreditLimitResponse> limitData = [];
        private readonly List<PaymentMethodResponse> methodData = [new(1, "TRANSFER", "Przelew", true, "AAAAAAAAAAE=")];
        public Vatowiec.Desktop.Services.ApiException? FinanceError { get; set; }
        public Task<FinanceLookups> FinanceLookups(Guid companyId) => PendingFinanceLookups ?? Task.FromResult(FinanceLookupOverride ?? new FinanceLookups([new("PLN", "złoty", true), new("EUR", "euro", true)], methodData.ToArray()));
        public Task<TradeTermsResponse> TradeTerms(Guid companyId, int contractorId, string direction) => Task.FromResult(termData.GetValueOrDefault(direction) ?? new(0, companyId, contractorId, direction, new(), null));
        public Task<TradeTermsResponse> SaveTradeTerms(Guid companyId, int contractorId, string direction, TradeTermsRequest value)
        {
            if (FinanceError != null) throw FinanceError;
            TermWrites.Add((direction, value)); var row = new TradeTermsResponse(42, companyId, contractorId, direction, value.Data, "AAAAAAAAAAM="); termData[direction] = row; return Task.FromResult(row);
        }
        public Task<CreditLimitResponse[]> CreditLimits(Guid companyId, int contractorId, string direction, int offset) => Task.FromResult(limitData.Where(x => x.Direction == direction).Skip(offset).Take(51).ToArray());
        public Task<CreditLimitResponse> SaveCreditLimit(Guid companyId, int contractorId, CreditLimitRequest value)
        {
            var row = new CreditLimitResponse(1, companyId, contractorId, value.Direction, value.CurrencyCode, value.Amount, value.IsActive, "AAAAAAAAAAI="); limitData.Add(row); return Task.FromResult(row);
        }
        public Task<PaymentMethodResponse> SavePaymentMethod(Guid companyId, PaymentMethodRequest value, bool create)
        {
            var row = new PaymentMethodResponse(8, value.Code, value.Name, value.IsActive, "AAAAAAAAAAI="); methodData.Add(row); return Task.FromResult(row);
        }
    }
}
