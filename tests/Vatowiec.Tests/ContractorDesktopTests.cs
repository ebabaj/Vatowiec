using System.Net;
using System.Net.Http.Json;
using Vatowiec.Core.Domain;
using Vatowiec.Desktop.Services;
using Vatowiec.SharedContracts;

namespace Vatowiec.Tests;

public sealed partial class DesktopTests
{
    [Fact]
    public async Task FailedSave_PreservesDraftAndStableIdForSafeRetry()
    {
        var api = new FakeApi { Permissions = ["ViewProfile", "ViewContractors", "EditContractors"], SaveError = new HttpRequestException() };
        using var model = await Open(api);
        model.NewContractor();
        model.Editor!.Code = "RETRY";
        model.Editor.Name = "Fikcyjna pozycja";
        var id = model.Editor.Id;
        await model.SaveContractor();
        Assert.True(model.IsEditing);
        Assert.Equal(id, model.Editor.Id);
        Assert.Equal("RETRY", model.Editor.Code);
        api.SaveError = null;
        await model.SaveContractor();
        Assert.Equal(new[] { id, id }, api.Writes.Select(x => x.Id));
        Assert.False(model.IsEditing);
    }

    [Fact]
    public async Task Editor_ValidatesBeforeSendingAndClearsAfterSaved()
    {
        var api = new FakeApi { Permissions = ["ViewProfile", "ViewContractors", "EditContractors"] };
        using var model = await Open(api);
        model.NewContractor();
        Assert.True(model.IsEditing);
        await model.SaveContractor();
        Assert.Empty(api.Writes);
        Assert.Contains("Kod", model.Message);
        model.Editor!.Code = "FICTIONAL";
        model.Editor.Name = "Fikcyjny kontrahent";
        model.Editor.TaxIdentifier = "DEMO-IDENTIFIER";
        model.Editor.IsActive = false;
        await model.SaveContractor();
        Assert.False(model.IsEditing);
        Assert.False(Assert.Single(api.Writes).IsActive);
        Assert.Contains("Zapisano", model.Message);
    }

    [Fact]
    public async Task ReadOnlyUser_CannotOpenEditorOrSendWrite()
    {
        using var model = await Open(new FakeApi());
        Assert.False(model.CanEditContractors);
        model.NewContractor();
        Assert.False(model.IsEditing);
        await model.SaveContractor();
        Assert.False(model.IsEditing);
    }

    [Fact]
    public async Task Conflict_PreservesLocalFieldsAndRequiresExplicitDecisionBeforeRetry()
    {
        var current = new ContractorResponse(Guid.NewGuid(), A, "DEMO", "Zmiana na serwerze", "DE", "TEST", false, "AAAAAAAAAAI=");
        var api = new FakeApi { Permissions = ["ViewProfile", "ViewContractors", "EditContractors"] };
        using var model = await Open(api);
        model.SelectedContractor = current;
        await model.EditContractor();
        model.Editor!.Name = "Moja zmiana";
        var originalVersion = model.Editor.RowVersion;
        api.SaveError = new ApiException(409, "Konflikt") { Code = "contractor_conflict", Current = current };
        await model.SaveContractor();
        Assert.True(model.HasConflict);
        Assert.False(model.CanSaveContractor);
        Assert.Equal("Moja zmiana", model.Editor.Name);
        Assert.Equal(originalVersion, model.Editor.RowVersion);
        await model.SaveContractor();
        Assert.Single(api.Writes);
        model.ResolveConflict(true);
        Assert.Equal(current.RowVersion, model.Editor.RowVersion);
        Assert.Equal("Moja zmiana", model.Editor.Name);
        Assert.Single(api.Writes); // Rozstrzygnięcie nie wykonuje automatycznego zapisu.
        api.SaveError = null;
        await model.SaveContractor();
        Assert.Equal(2, api.Writes.Count);
        Assert.False(model.IsEditing);
    }

    [Fact]
    public async Task Conflict_ReloadServerVersionDiscardsLocalFields()
    {
        var api = new FakeApi { Permissions = ["ViewProfile", "ViewContractors", "EditContractors"] };
        using var model = await Open(api);
        var row = new ContractorResponse(Guid.NewGuid(), A, "NEW", "Aktualne dane", "DE", IsActive: false, RowVersion: "AAAAAAAAAAI=");
        model.SelectedContractor = row;
        await model.EditContractor();
        api.SaveError = new ApiException(409, "Konflikt") { Code = "contractor_conflict", Current = row };
        await model.SaveContractor();
        model.ResolveConflict(false);
        Assert.Equal(row.Name, model.Editor!.Name);
        Assert.False(model.Editor.IsActive);
        Assert.Equal(row.RowVersion, model.Editor.RowVersion);
        Assert.False(model.HasConflict);
    }

    [Theory]
    [InlineData(401)]
    [InlineData(403)]
    public async Task WritePermissionOrSessionRevoked_RemovesEditorAndSensitiveData(int status)
    {
        var api = new FakeApi { Permissions = ["ViewProfile", "ViewContractors", "EditContractors"] };
        using var model = await Open(api);
        model.NewContractor();
        model.Editor!.Code = "TEST";
        model.Editor.Name = "Fikcyjna nazwa";
        api.SaveError = new ApiException(status, "Odmowa");
        await model.SaveContractor();
        Assert.False(model.IsEditing);
        Assert.Empty(model.Contractors);
        Assert.Equal(status == 401, model.IsLogin);
    }

    [Fact]
    public async Task Search_PagesAreBoundedAndFilterChangeRestartsFirstPage()
    {
        var api = new FakeApi
        {
            Pending = Task.FromResult(Enumerable.Range(0, 51)
            .Select(i => new ContractorResponse(Guid.NewGuid(), A, $"T{i}", "Fikcyjny", "PL")).ToArray())
        };
        using var model = await Open(api);
        model.SearchText = "demo & test";
        model.FilterCountry = "PL";
        model.FilterActivity = 2;
        await model.LoadContractors();
        Assert.Equal(50, model.Contractors.Length);
        Assert.True(model.HasNextPage);
        Assert.Equal(("demo & test", "PL", false, 0, 51), api.LastQuery);
        await model.NextPage();
        Assert.Equal(50, api.LastQuery.Offset);
        model.SearchText = "nowe";
        await model.NextPage();
        Assert.Equal(0, api.LastQuery.Offset);
    }

    [Fact]
    public async Task SaveTransport_PostAndPutUseFreshCsrfAndPreserveVersion()
    {
        var calls = 0;
        using var client = new VatowiecApiClient(new HttpClient(new Handler(async request =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/csrf", StringComparison.Ordinal))
                return Json(new CsrfResponse($"token-{++calls}", "X-CSRF-TOKEN"));
            Assert.Equal($"token-{calls}", request.Headers.GetValues("X-CSRF-TOKEN").Single());
            var value = (await request.Content!.ReadFromJsonAsync<ContractorWriteRequest>())!;
            Assert.Equal(calls == 1 ? HttpMethod.Post : HttpMethod.Put, request.Method);
            Assert.Equal(calls == 1 ? null : "AAAAAAAAAAE=", value.RowVersion);
            return Json(new ContractorResponse(value.Id, A, value.Code, value.Name, value.CountryCode, RowVersion: "AAAAAAAAAAE="));
        }))
        { BaseAddress = new Uri("https://localhost") });
        var request = new ContractorWriteRequest(Guid.NewGuid(), "TEST", "Fikcyjny", null, "PL", true);
        await client.SaveContractor(A, request, true);
        await client.SaveContractor(A, request with { RowVersion = "AAAAAAAAAAE=" }, false);
        Assert.Equal(2, calls);
    }

    [Fact]
    public void DomainUpdate_ValidatesAllFieldsBeforeChangingState()
    {
        var row = new Contractor(A, Guid.NewGuid(), "OLD", "Fikcyjny", "PL");
        Assert.Throws<ArgumentException>(() => row.Update("NEW", "Zmieniony", null, "P1", false));
        Assert.Equal("OLD", row.Code);
        Assert.True(row.IsActive);
        row.Update(" new ", " Zmieniony ", " TEST ", "de", false);
        Assert.Equal("NEW", row.Code);
        Assert.Equal("TEST", row.TaxIdentifier);
        Assert.Equal("DE", row.CountryCode);
        Assert.False(row.IsActive);
    }
}
