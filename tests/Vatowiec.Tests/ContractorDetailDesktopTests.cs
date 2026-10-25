using Vatowiec.Desktop.Services;
using Vatowiec.SharedContracts;

namespace Vatowiec.Tests;

public sealed partial class DesktopTests
{
    private static async Task<Vatowiec.Desktop.ViewModels.MainViewModel> OpenDetailForm(FakeApi api)
    {
        var model = await Open(api);
        model.SelectedContractor = new ContractorResponse(1, A, "TEST", "Fikcyjny kontrahent", "PL");
        if (model.CanEditContractors) await model.EditContractor(); else await model.ShowContractor();
        model.SelectedTab = 2;
        return model;
    }
    [Fact]
    public async Task DirtyDetail_RequiresConfirmationAndCancelKeepsDraft()
    {
        var api = new FakeApi { Permissions = ["ViewProfile", "ViewContractors", "EditContractors"] };
        using var model = await OpenDetailForm(api);
        model.NewDetail();
        model.DetailEditor!.Name = "Fikcyjny szkic";
        model.CancelDetail();
        Assert.True(model.DiscardPrompt);
        Assert.False(model.CanUseForm);
        Assert.Equal("Fikcyjny szkic", model.DetailEditor.Name);
        model.KeepEditing();
        Assert.False(model.DiscardPrompt);
        Assert.True(model.IsDetailEditing);
        model.SelectedTab = 3;
        Assert.True(model.DiscardPrompt);
        Assert.Equal(2, model.SelectedTab);
        model.ConfirmDiscard();
        Assert.Equal(3, model.SelectedTab);
        Assert.Null(model.DetailEditor);
        Assert.Empty(api.DetailWrites);
    }
    [Fact]
    public async Task SavingDetail_DoesNotDiscardUnsubmittedAddressTypeDraft()
    {
        var api = new FakeApi { Permissions = ["ViewProfile", "ViewContractors", "EditContractors"] };
        using var model = await OpenDetailForm(api);
        model.NewAddressTypeName = "Fikcyjny nowy rodzaj";
        model.NewDetail();
        model.DetailEditor!.Name = "Fikcyjny kontakt";
        await model.SaveDetail();
        Assert.Equal("Fikcyjny nowy rodzaj", model.NewAddressTypeName);
        model.CancelEditor();
        Assert.True(model.DiscardPrompt);
        model.ConfirmDiscard();
        Assert.Empty(model.NewAddressTypeName);
    }

    [Fact]
    public async Task DetailValidationAndUncertainResponse_PreserveStableOperationAndDraft()
    {
        var api = new FakeApi { Permissions = ["ViewProfile", "ViewContractors", "EditContractors"] };
        using var model = await OpenDetailForm(api);
        model.NewDetail();
        await model.SaveDetail();
        Assert.NotEmpty(model.DetailEditor!.Errors["Name"]);
        Assert.Empty(api.DetailWrites);
        model.DetailEditor.Name = "Fikcyjny szkic";
        api.SaveError = new HttpRequestException();
        await model.SaveDetail();
        var operation = model.DetailEditor.OperationId;
        Assert.True(model.IsDetailEditing);
        api.SaveError = null;
        await model.SaveDetail();
        Assert.Equal(new[] { operation, operation }, api.DetailWrites.Select(x => x.OperationId!.Value));
        Assert.False(model.IsDetailEditing);
    }
    [Fact]
    public async Task DetailConflict_RequiresExplicitComparisonBeforeRetry()
    {
        var current = new DetailResponse(1, A, 1, "contacts", new() { Name = "Fikcyjny serwer" }, "AAAAAAAAAAI=");
        var api = new FakeApi { Permissions = ["ViewProfile", "ViewContractors", "EditContractors"], DetailData = [current] };
        using var model = await OpenDetailForm(api);
        model.SelectedDetail = current;
        await model.EditDetail();
        model.DetailEditor!.Name = "Fikcyjna moja zmiana";
        api.SaveError = new ApiException(409, "Konflikt") { Code = "detail_conflict", DetailCurrent = current };
        await model.SaveDetail();
        Assert.True(model.HasDetailConflict);
        Assert.Contains("Fikcyjny serwer", model.DetailServerSummary);
        Assert.Contains("Fikcyjna moja zmiana", model.DetailLocalSummary);
        await model.SaveDetail();
        Assert.Single(api.DetailWrites);
        model.ResolveDetailConflict(true);
        Assert.Equal(current.RowVersion, model.DetailEditor.RowVersion);
        Assert.Single(api.DetailWrites);
        api.SaveError = null;
        await model.SaveDetail();
        Assert.Equal(2, api.DetailWrites.Count);
    }
}
