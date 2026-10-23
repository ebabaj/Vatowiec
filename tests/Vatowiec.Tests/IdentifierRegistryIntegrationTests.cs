using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Vatowiec.Core.Domain;
using Vatowiec.Core.Infrastructure.Persistence;
using Vatowiec.Desktop.Services;
using Vatowiec.Desktop.ViewModels;
using Vatowiec.SharedContracts;

namespace Vatowiec.Tests;

public sealed partial class AuthenticationIntegrationTests
{
    private async Task GrantRegistryRights(CompanyPermission permissions = (CompanyPermission)31)
    {
        using var scope = application!.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<SystemDbContext>().CompanyAccesses.Where(x => x.UserId == operatorId && x.CompanyId == companyA)
            .ExecuteUpdateAsync(x => x.SetProperty(a => a.Permissions, permissions));
    }
    private async Task<IdentifierPreview> Preview(HttpClient client, ContractorWriteRequest request)
    {
        using var response = await client.PostAsJsonAsync(ContractorsPath() + "/identifiers/validate", request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<IdentifierPreview>())!;
    }
    private string VerificationsPath(int? id = null, Guid? company = null) => $"{ContractorsPath(company)}/{id ?? contractorId}/verifications";
    private async Task SetRegistryInput(string nip = "3245174504", string vat = "PL100", string country = "PL")
    {
        await using var db = Company(false, companyA);
        var row = await db.Contractors.SingleAsync(x => x.Id == contractorId);
        row.Update(row.Code, row.Name, country, true); row.SetIdentifiers(nip, vat, null); await db.SaveChangesAsync();
    }
    [SqlServerFact]
    public async Task Identifiers_NormalizedValuesPersistAndReplayIsDurable()
    {
        using var client = await DetailOperator();
        var request = NewContractor("NORMALIZED") with { TaxId = "PL 324-517-45-04", VatEuId = "pl 324 5174504" };
        var preview = await Preview(client, request); Assert.False(preview.NeedsWarning);
        Assert.All(preview.Checks.Where(x => x.Field != "KsefIdWew"), x => Assert.Equal("Valid", x.Status));
        using var created = await client.PostAsJsonAsync(ContractorsPath(), request);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var row = (await created.Content.ReadFromJsonAsync<ContractorResponse>())!;
        Assert.Equal("3245174504", row.TaxId); Assert.Equal("PL3245174504", row.VatEuId);
        using var replay = await client.PostAsJsonAsync(ContractorsPath(), request);
        Assert.Equal(row.Id, (await replay.Content.ReadFromJsonAsync<ContractorResponse>())!.Id);
        await using var db = Company(false, companyA); Assert.Single(await db.ContractorChanges.ToListAsync());
        Assert.Equal(HttpStatusCode.Conflict, (await client.PutAsJsonAsync($"{ContractorsPath()}/{row.Id}", Edit(row, "Zmiana") with { RowVersion = "AAAAAAAAAAA=" })).StatusCode);
    }
    [SqlServerFact]
    public async Task IdentifierWarning_RequiresSeparateRightAndExactHash_AndIsAuditedAtomically()
    {
        using var client = await DetailOperator();
        var request = NewContractor("WARN") with { TaxId = "DEMO-?BAD" };
        var preview = await Preview(client, request); Assert.True(preview.NeedsWarning);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await client.PostAsJsonAsync(ContractorsPath(), request)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync(ContractorsPath(), request with { AcceptWarning = true, WarningHash = preview.WarningHash })).StatusCode);
        await GrantRegistryRights();
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await client.PostAsJsonAsync(ContractorsPath(), request with { AcceptWarning = true, WarningHash = "wrong" })).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await client.PostAsJsonAsync(ContractorsPath(), request with { Name = "Fikcyjna zmiana", AcceptWarning = true, WarningHash = preview.WarningHash })).StatusCode);
        var accepted = request with { AcceptWarning = true, WarningHash = preview.WarningHash };
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync(ContractorsPath(), accepted)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync(ContractorsPath(), accepted)).StatusCode);
        await using var db = Company(false, companyA);
        var audit = await db.ContractorChanges.SingleAsync(); Assert.NotNull(audit.WarningJson);
        Assert.Contains(preview.WarningHash, audit.WarningJson); Assert.Equal(operatorId, audit.ActorId);
        Assert.Equal(1, await db.ContractorCreateOperations.CountAsync());
    }
    [SqlServerFact]
    public async Task Duplicates_AreListedFromThisCompanyAndMayBeConsciouslyAccepted()
    {
        await GrantRegistryRights(); using var client = await DetailOperator();
        var first = NewContractor("DUP-FIRST") with { TaxId = "3245174504" };
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync(ContractorsPath(), first)).StatusCode);
        var second = NewContractor("DUP-SECOND") with { TaxId = "3245174504" };
        var preview = await Preview(client, second); Assert.Equal("DUP-FIRST", Assert.Single(preview.Duplicates).Code);
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync(ContractorsPath(), second with { AcceptWarning = true, WarningHash = preview.WarningHash })).StatusCode);
        await using var a = Company(false, companyA); await using var b = Company(true, companyB);
        Assert.Equal(2, await a.Contractors.CountAsync(x => x.TaxId == "3245174504")); Assert.False(await b.Contractors.AnyAsync(x => x.TaxId == "3245174504"));
    }
    [SqlServerFact]
    public async Task IdentifierWarning_IsBoundToRuleVersionAndCurrentRowVersion()
    {
        await GrantRegistryRights(); using var client = await DetailOperator();
        var row = (await Read(client, contractorId))!;
        var request = Edit(row, "Fikcyjny") with { VatEuId = "EL123456789" };
        var preview = await Preview(client, request);
        using var scope = application!.Services.CreateScope(); var system = scope.ServiceProvider.GetRequiredService<SystemDbContext>();
        var rule = await system.TaxIdentifierSchemes.SingleAsync(x => x.VatPrefix == "EL"); var originalVersion = rule.Version;
        try
        {
            rule.Version = "test-rule-v2"; await system.SaveChangesAsync();
            Assert.Equal(HttpStatusCode.UnprocessableEntity, (await client.PutAsJsonAsync($"{ContractorsPath()}/{row.Id}", request with { AcceptWarning = true, WarningHash = preview.WarningHash })).StatusCode);
            var currentPreview = await Preview(client, request);
            Assert.NotEqual(preview.WarningHash, currentPreview.WarningHash);
            Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync($"{ContractorsPath()}/{row.Id}", request with { AcceptWarning = true, WarningHash = currentPreview.WarningHash })).StatusCode);
            Assert.Equal(HttpStatusCode.Conflict, (await client.PutAsJsonAsync($"{ContractorsPath()}/{row.Id}", request with { AcceptWarning = true, WarningHash = currentPreview.WarningHash })).StatusCode);
        }
        finally { rule.Version = originalVersion; await system.SaveChangesAsync(); }
    }
    [SqlServerFact]
    public async Task Registries_RequirePermissionCsrfAndCurrentVersions_AndNeverFallbackToProduction()
    {
        await SetRegistryInput(); using var client = await DetailOperator(); var row = (await Read(client, contractorId))!;
        var request = new VerificationRequest(Guid.NewGuid(), "Vies", row.RowVersion);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync(VerificationsPath(), request)).StatusCode);
        await GrantRegistryRights(); using var noCsrf = await Login("operator.test");
        Assert.Equal(HttpStatusCode.BadRequest, (await noCsrf.PostAsJsonAsync(VerificationsPath(), request)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync(VerificationsPath(), request with { RowVersion = "old" })).StatusCode);
        using var response = await client.PostAsJsonAsync(VerificationsPath(), request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = (await response.Content.ReadFromJsonAsync<VerificationResponse>())!;
        Assert.Equal("Mock", result.Environment); Assert.False(result.Executed); Assert.Equal("PL100", result.VatEuId);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsJsonAsync(VerificationsPath(int.MaxValue), request with { OperationId = Guid.NewGuid() })).StatusCode);
        using var reader = await Login("reader.test"); await Csrf(reader);
        Assert.Equal(HttpStatusCode.Forbidden, (await reader.PostAsJsonAsync(VerificationsPath(company: companyB), request)).StatusCode);
    }
    [SqlServerFact]
    public async Task VerificationHistory_SnapshotIsImmutableIndependentOfEditsAndAccountCurrentState()
    {
        await SetRegistryInput(); await GrantRegistryRights(); using var client = await DetailOperator();
        var bank = await CreateDetail(client, "bank-accounts", new() { AccountNumber = "PL70506405335016096312945164" });
        var row = (await Read(client, contractorId))!;
        var request = new VerificationRequest(Guid.NewGuid(), "MfAccount", row.RowVersion, bank.Id, bank.RowVersion, new(2026, 10, 21));
        using var response = await client.PostAsJsonAsync(VerificationsPath(), request); Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = (await response.Content.ReadFromJsonAsync<VerificationResponse>())!;
        Assert.Equal("70506405335016096312945164", result.AccountNumber); Assert.Equal(request.StateDate, result.StateDate);
        Assert.Equal(result.Id, (await (await client.PostAsJsonAsync(VerificationsPath(), request)).Content.ReadFromJsonAsync<VerificationResponse>())!.Id);
        await SetRegistryInput("5261040828");
        var history = (await client.GetFromJsonAsync<VerificationResponse[]>(VerificationsPath()))!;
        Assert.Equal("3245174504", Assert.Single(history).TaxId);
        Assert.Empty((await client.GetFromJsonAsync<VerificationResponse[]>(VerificationsPath(contractorIdB, companyB)))!);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync(VerificationsPath(contractorIdB, companyB), request)).StatusCode);
        await using var db = Company(false, companyA);
        Assert.Single(await db.ContractorChanges.ToListAsync()); // Tylko wcześniejsze dodanie rachunku, bez edycji przez sprawdzenie.
        var stored = await db.ExternalVerifications.SingleAsync();
        db.Remove(stored); await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync()); db.ChangeTracker.Clear();
        await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlRawAsync("UPDATE audit.ExternalVerifications SET Message=N'zmiana';"));
        await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlRawAsync("DELETE FROM audit.ExternalVerifications;"));
        Assert.Single(await db.ExternalVerifications.ToListAsync());
        using var scope = application!.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<SystemDbContext>().Users.Where(x => x.Id == operatorId).ExecuteDeleteAsync();
        Assert.Equal(operatorId, (await db.ExternalVerifications.SingleAsync()).ActorId);
    }
    [SqlServerFact]
    public async Task AcceptedIdentifierWarning_AuditFailureRollsBackDataAndOperationResult()
    {
        await GrantRegistryRights(); using var client = await DetailOperator();
        var request = NewContractor("WARN-ROLLBACK") with { TaxId = "DEMO?INVALID" };
        var preview = await Preview(client, request);
        await using var db = Company(false, companyA);
        await db.Database.ExecuteSqlRawAsync("ALTER TABLE audit.ContractorChanges WITH NOCHECK ADD CONSTRAINT CK_Test_IdentifierWarningFailure CHECK (WarningJson IS NULL);");
        try
        {
            var response = await client.PostAsJsonAsync(ContractorsPath(), request with { AcceptWarning = true, WarningHash = preview.WarningHash });
            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
            Assert.False(await db.Contractors.AnyAsync(x => x.Code == request.Code));
            Assert.Empty(await db.ContractorChanges.ToListAsync()); Assert.Empty(await db.ContractorCreateOperations.ToListAsync());
        }
        finally { await db.Database.ExecuteSqlRawAsync("ALTER TABLE audit.ContractorChanges DROP CONSTRAINT CK_Test_IdentifierWarningFailure;"); }
    }
    [SqlServerFact]
    public async Task RegistryInputs_RejectForeignTaxIdWrongOwnerAndNonPolishAccountWithoutNegativeResult()
    {
        await SetRegistryInput(country: "DE"); await GrantRegistryRights(); using var client = await DetailOperator();
        var row = (await Read(client, contractorId))!;
        var request = new VerificationRequest(Guid.NewGuid(), "MfTaxpayer", row.RowVersion);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(VerificationsPath(), request)).StatusCode);
        await SetRegistryInput(); row = (await Read(client, contractorId))!;
        var bank = await CreateDetail(client, "bank-accounts", new() { AccountNumber = "DE89370400440532013000" });
        request = new(Guid.NewGuid(), "MfAccount", row.RowVersion, bank.Id, bank.RowVersion);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(VerificationsPath(), request)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsJsonAsync(VerificationsPath(), request with { BankAccountId = int.MaxValue })).StatusCode);
        await using var db = Company(false, companyA); Assert.Empty(await db.ExternalVerifications.ToListAsync());
    }
    [SqlServerFact]
    public async Task DesktopRegistryFlow_EditsIdentifiersShowsHistoryAndDoesNotAutomaticallyImportGus()
    {
        await SetRegistryInput(); await GrantRegistryRights();
        using var model = new MainViewModel(_ => new VatowiecApiClient(Client())) { UserName = "operator.test" };
        await model.Login(password); model.SelectedCompany = model.Companies.Single(x => x.CompanyId == companyA);
        await model.OpenCompany(); await model.LoadContractors(); model.SelectedContractor = Assert.Single(model.Contractors); await model.EditContractor();
        var originalName = model.Editor!.Name;
        await model.VerifyRegistry("Gus"); Assert.True(model.IsVerificationsTab); Assert.Contains("Mock", model.VerificationSummary);
        Assert.Equal(originalName, model.Editor!.Name); Assert.False(model.CanImportGus); // Atrapa nie udaje danych urzędu.
        model.SelectedTab = 0; model.Editor.TaxId = "DEMO?WARNING"; await model.CheckIdentifiers();
        Assert.True(model.HasIdentifierWarning); await model.SaveContractor(); Assert.True(model.IsEditing);
        await model.AcceptIdentifierWarning(); Assert.False(model.IsEditing);
    }
}
