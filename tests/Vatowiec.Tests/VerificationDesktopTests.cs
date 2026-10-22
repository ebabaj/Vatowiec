using Vatowiec.SharedContracts;

namespace Vatowiec.Tests;

public sealed partial class DesktopTests
{
    private static VerificationResponse VerificationFixture(string source, string operation) => new(Guid.NewGuid(), A, 1, null,
        source, operation, "Mock", "3245174504", null, null, null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow,
        "Information", "Completed", null, "MOCK — dane fikcyjne", null, new(Name: "Fikcyjna nazwa z GUS"), "test-adapter", "test-rule", false);
    [Fact]
    public async Task NewVerification_SelectionCannotBeRevertedByItemsSourceBindingDuringRefresh()
    {
        var api = new FakeApi { Permissions = ["ViewProfile", "ViewContractors", "VerifyRegistries"], VerificationResult = VerificationFixture("VIES", "Vies") };
        using var model = await Open(api); model.SelectedContractor = new(1, A, "TEST", "Fikcyjny", "PL"); await model.EditContractor();
        await model.VerifyRegistry("Vies"); var previous = model.SelectedVerification;
        Assert.NotNull(previous);
        model.PropertyChanged += (_, args) => { if (args.PropertyName == nameof(model.VerificationRows)) model.SelectedVerification = previous; };
        api.VerificationResult = VerificationFixture("MF", "MfTaxpayer");
        await model.VerifyRegistry("MfTaxpayer");
        Assert.Equal(api.VerificationResult.Id, model.SelectedVerification!.Id);
        Assert.Contains("MF/MfTaxpayer", model.VerificationSummary);
        await model.LoadVerifications();
        Assert.Equal(api.VerificationResult.Id, model.SelectedVerification!.Id);
    }
    [Fact]
    public async Task GusImport_RequiresExplicitFieldSelectionAndOnlyChangesDraft()
    {
        var api = new FakeApi { Permissions = ["ViewProfile", "ViewContractors", "EditContractors", "VerifyRegistries"], VerificationResult = VerificationFixture("GUS", "Gus") };
        using var model = await Open(api); model.SelectedContractor = new(1, A, "TEST", "Fikcyjny", "PL"); await model.EditContractor();
        var original = model.Editor!.Name;
        await model.VerifyRegistry("Gus");
        Assert.Equal(original, model.Editor!.Name); Assert.True(model.CanImportGus); Assert.Empty(api.Writes);
        model.UseGusName = true; model.ApplyGusSelection();
        Assert.Equal("Fikcyjna nazwa z GUS", model.Editor.Name); Assert.Empty(api.Writes); Assert.True(model.HasUnsavedChanges);
    }
}
