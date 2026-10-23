using Vatowiec.SharedContracts;

namespace Vatowiec.Tests;

public sealed partial class DesktopTests
{
    private static BankAccountValidationResponse BankReply(params string[] bics) => new("ValidIban", "valid_iban", "Poprawny IBAN.",
        "39114021050000561640001001", true, "bank-iban-v3/test")
        { BankLookup = new("Found", "Dane banku pobrano z NBP EWIB", "11402105", "Bank demonstracyjny EWIB", bics) };

    [Theory]
    [InlineData(0)][InlineData(1)][InlineData(2)]
    public async Task BankLookup_BlankFieldsAutoFillOnlyUnambiguousValues(int bicCount)
    {
        var api = new FakeApi { Permissions = ["ViewProfile", "ViewContractors", "EditContractors"],
            BankCheck = BankReply(new[] { "TESTPLPW", "TESTPLPW001" }.Take(bicCount).ToArray()) };
        using var model = await OpenDetailForm(api); model.SelectedTab = 3; model.NewDetail();
        model.DetailEditor!.AccountNumber = "39-1140-2105-0000-5616-4000-1001";
        await model.ValidateBankAccount();
        Assert.Equal("39114021050000561640001001", model.DetailEditor.AccountNumber);
        Assert.Equal("Bank demonstracyjny EWIB", model.DetailEditor.BankName);
        Assert.Equal(bicCount == 1 ? "TESTPLPW" : null, model.DetailEditor.Bic);
        if (bicCount > 1) { Assert.Null(model.SelectedBankBic); Assert.False(model.CanApplyBankBic); }
        Assert.Empty(api.DetailWrites);
        model.DetailEditor.AccountNumber = "70506405335016096312945164";
        Assert.False(model.HasBicProposal); Assert.Empty(model.BankLookupMessage);
        model.ApplyBankNameProposal(); model.ApplyBankBicProposal();
    }
    [Fact]
    public async Task BankLookup_ChangingDraftDuringResponseNeverAppliesStaleBank()
    {
        var completion = new TaskCompletionSource<BankAccountValidationResponse>();
        var api = new FakeApi { Permissions = ["ViewProfile", "ViewContractors", "EditContractors"], PendingBankCheck = completion.Task };
        using var model = await OpenDetailForm(api); model.SelectedTab = 3; model.NewDetail();
        model.DetailEditor!.AccountNumber = "39114021050000561640001001";
        var work = model.ValidateBankAccount();
        model.DetailEditor.AccountNumber = "70506405335016096312945164";
        completion.SetResult(BankReply("TESTPLPW")); await work;
        Assert.Null(model.DetailEditor.BankName); Assert.Null(model.DetailEditor.Bic);
        Assert.Empty(model.BankLookupMessage); Assert.Empty(api.DetailWrites);
    }
    [Fact]
    public async Task BankLookup_UnavailableDoesNotBlockManualSaveOrReplaceFields()
    {
        var api = new FakeApi { Permissions = ["ViewProfile", "ViewContractors", "EditContractors"],
            BankCheck = BankReply() with { BankLookup = new("Unavailable", "NBP EWIB: timeout", "11402105", null, []) } };
        using var model = await OpenDetailForm(api); model.SelectedTab = 3; model.NewDetail();
        model.DetailEditor!.AccountNumber = "39114021050000561640001001";
        model.DetailEditor.BankName = "Własny bank"; model.DetailEditor.Bic = "MYOWNBIC";
        await model.ValidateBankAccount();
        Assert.Equal("Własny bank", model.DetailEditor.BankName); Assert.Equal("MYOWNBIC", model.DetailEditor.Bic);
        Assert.True(model.CanSaveDetail); await model.SaveDetail(); Assert.Single(api.DetailWrites);
    }
}
