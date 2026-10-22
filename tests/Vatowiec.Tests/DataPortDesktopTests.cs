using Vatowiec.SharedContracts;

namespace Vatowiec.Tests;

public sealed partial class DesktopTests
{
    [Fact]
    public async Task DataPortImport_ExplicitSelectionOnly_NoAutomaticWrites()
    {
        var api = new FakeApi
        {
            Permissions = ["ViewProfile", "ViewContractors", "EditContractors", "VerifyRegistries"],
            VerificationResult = VerificationFixture("DataPort", "DataPort") with
            { Data = new(Name: "Fikcyjna nazwa DataPort", Street: "Testowa", City: "Miasto testowe", CountryCode: "PL") }
        };
        using var model = await Open(api); model.SelectedContractor = new(1, A, "TEST", "Fikcyjny", "PL"); await model.EditContractor();
        var original = model.Editor!.Name;
        await model.VerifyRegistry("DataPort"); Assert.Equal(original, model.Editor.Name); Assert.True(model.CanImportGus);
        Assert.Contains("DataPort (dane GUS)", model.VerificationSummary); Assert.Empty(api.Writes); Assert.Empty(api.DetailWrites);
        model.UseGusStreet = true; model.ApplyGusSelection();
        Assert.Equal(original, model.Editor.Name); Assert.Equal("Testowa", model.DetailEditor!.Street);
        Assert.Null(model.DetailEditor.City); Assert.Null(model.DetailEditor.CountryCode); Assert.Empty(api.DetailWrites);
    }
    [Fact]
    public async Task DataPortSettings_SaveRotateConfirmDeleteAndClearOnCompanyChange()
    {
        var api = new FakeApi { Permissions = ["ViewProfile", "ManageCompanyIntegrations"] };
        using var model = await Open(api); await model.OpenDataPortSettings();
        Assert.True(model.IsDataPortSettings); Assert.False(model.IsBrowsingWorkspace); Assert.True(model.CanManageCompanyIntegrations);
        Assert.Equal("Nie skonfigurowano", model.DataPortStatus);
        await model.SaveDataPortKey("fake-" + Guid.NewGuid().ToString("N")); Assert.Equal("Skonfigurowano", model.DataPortStatus);
        await model.SaveDataPortKey("fake-" + Guid.NewGuid().ToString("N")); Assert.Equal(2, api.KeySaves);
        await model.DeleteDataPortKey(); Assert.Equal(0, api.KeyDeletes);
        model.RequestDataPortDelete(); Assert.True(model.ConfirmDataPortDelete);
        model.CancelDataPortDelete(); Assert.False(model.ConfirmDataPortDelete);
        model.RequestDataPortDelete(); await model.DeleteDataPortKey(); Assert.Equal(1, api.KeyDeletes); Assert.Equal("Nie skonfigurowano", model.DataPortStatus);
        await model.ChangeCompany(); Assert.False(model.IsDataPortSettings); Assert.Equal("Stan niepobrany", model.DataPortStatus);
    }
    [Fact]
    public async Task DataPortSettings_ReadOnlyUserCannotSaveOrDelete_AndSessionExpiryClearsState()
    {
        var api = new FakeApi { Permissions = ["ViewProfile"] };
        using var model = await Open(api); await model.OpenDataPortSettings(); Assert.False(model.CanManageCompanyIntegrations);
        await model.SaveDataPortKey("fake-key"); model.RequestDataPortDelete(); await model.DeleteDataPortKey();
        Assert.Equal(0, api.KeySaves); Assert.Equal(0, api.KeyDeletes); Assert.False(model.ConfirmDataPortDelete);
        await model.Logout(); Assert.False(model.IsDataPortSettings); Assert.Equal("Stan niepobrany", model.DataPortStatus);
    }
}
