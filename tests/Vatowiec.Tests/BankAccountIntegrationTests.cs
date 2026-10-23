using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Vatowiec.Core.Application;
using Vatowiec.Core.Domain;
using Vatowiec.Core.Infrastructure;
using Vatowiec.Core.Infrastructure.Persistence;
using Vatowiec.Desktop.Services;
using Vatowiec.Desktop.ViewModels;
using Vatowiec.SharedContracts;

namespace Vatowiec.Tests;

public sealed partial class AuthenticationIntegrationTests
{
    private readonly VersionedBankValidator bankValidator = new();
    private sealed class VersionedBankValidator : IBankAccountValidator
    {
        public string Revision { get; set; } = "fixture-v1";
        private readonly IbanNetBankAccountValidator inner = new();
        public BankAccountCheck Validate(string number)
        {
            var result = inner.Validate(number);
            return result with { RuleVersion = result.RuleVersion + "/" + Revision };
        }
    }
    private async Task AcceptBankWarnings()
    {
        using var scope = application!.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<SystemDbContext>().CompanyAccesses
            .Where(x => x.UserId == operatorId && x.CompanyId == companyA)
            .ExecuteUpdateAsync(x => x.SetProperty(a => a.Permissions, (CompanyPermission)15));
    }
    private async Task<string> BankWarning(HttpClient client, DetailWriteRequest request)
    {
        using var response = await client.PostAsJsonAsync(DetailsPath("bank-accounts"), request);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.NotEqual("ValidIban", json.GetProperty("bankValidation").GetProperty("status").GetString());
        return json.GetProperty("warningHash").GetString()!;
    }

    [SqlServerFact]
    public async Task BankValidation_PreviewRequiresCsrfViewOwnerAndCompanyAndNeverSaves()
    {
        var path = DetailsPath("bank-accounts") + "/validate";
        var request = new BankAccountValidationRequest("pl70 5064 0533 5016 0963 1294 5164");
        using var anonymous = Client();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsJsonAsync(path, request)).StatusCode);
        using var client = await Login("operator.test");
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(path, request)).StatusCode);
        await Csrf(client);
        using var valid = await client.PostAsJsonAsync(path, request);
        var result = (await valid.Content.ReadFromJsonAsync<BankAccountValidationResponse>())!;
        Assert.Equal(HttpStatusCode.OK, valid.StatusCode);
        Assert.Equal("ValidIban", result.Status);
        Assert.Equal("PL70506405335016096312945164", result.NormalizedNumber);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsJsonAsync(DetailsPath("bank-accounts", int.MaxValue) + "/validate", request)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(path + "?companyId=" + companyB, request)).StatusCode);
        using var reader = await Login("reader.test");
        await Csrf(reader);
        Assert.Equal(HttpStatusCode.Forbidden, (await reader.PostAsJsonAsync(path, request)).StatusCode);
        using (var scope = application!.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<SystemDbContext>().CompanyAccesses
                .Where(x => x.UserId == operatorId && x.CompanyId == companyB).ExecuteDeleteAsync();
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync(DetailsPath("bank-accounts", contractorIdB, companyB) + "/validate", request)).StatusCode);
        await using var db = Company(false, companyA);
        Assert.Empty(await db.ContractorBankAccounts.ToListAsync());
        Assert.Empty(await db.ContractorChanges.ToListAsync());
    }

    [SqlServerFact]
    public async Task BankValidation_RuleChangeInvalidatesConfirmationAndAcceptedRetryIsIdempotent()
    {
        using var client = await DetailOperator();
        await AcceptBankWarnings();
        var request = NewDetail(Data("bank-accounts") with { AccountNumber = "PL71506405335016096312945164" });
        var oldHash = await BankWarning(client, request);
        bankValidator.Revision = "fixture-v2";
        var hash = await BankWarning(client, request with { AcceptWarning = true, WarningHash = oldHash });
        Assert.NotEqual(oldHash, hash);
        // Potwierdzenie nie przenosi się na inny klucz operacji, firmę ani dane.
        Assert.NotEqual(hash, await BankWarning(client, request with { OperationId = Guid.NewGuid(), AcceptWarning = true, WarningHash = hash }));
        var accepted = request with { AcceptWarning = true, WarningHash = hash };
        using var created = await client.PostAsJsonAsync(DetailsPath("bank-accounts"), accepted);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var row = (await created.Content.ReadFromJsonAsync<DetailResponse>())!;
        Assert.Equal("invalid_checksum", row.BankValidation!.Code);
        bankValidator.Revision = "fixture-v3";
        using var retry = await client.PostAsJsonAsync(DetailsPath("bank-accounts"), accepted);
        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
        Assert.Equal(row.Id, (await retry.Content.ReadFromJsonAsync<DetailResponse>())!.Id);
        await using var a = Company(false, companyA);
        await using var b = Company(true, companyB);
        Assert.Single(await a.ContractorBankAccounts.ToListAsync());
        Assert.Empty(await b.ContractorBankAccounts.ToListAsync());
        Assert.Single(await a.ContractorCreateOperations.ToListAsync());
        var audit = Assert.Single(await a.ContractorChanges.ToListAsync());
        Assert.Equal(operatorId, audit.ActorId);
        Assert.Contains("fixture-v2", audit.WarningJson);
        Assert.Contains("invalid_checksum", audit.WarningJson);
        Assert.Contains(row.Data.AccountNumber!, audit.AfterJson);
        // Odczyt pokazuje kontrolę bieżącą, retry zwraca historyczny wynik operacji.
        Assert.Contains("fixture-v3", (await client.GetFromJsonAsync<DetailResponse>($"{DetailsPath("bank-accounts")}/{row.Id}"))!.BankValidation!.RuleVersion);
    }

    [SqlServerFact]
    public async Task BankValidation_AcceptedWarningAndAuditRollBackTogether()
    {
        using var client = await DetailOperator();
        await AcceptBankWarnings();
        var request = NewDetail(Data("bank-accounts") with { AccountNumber = "LOCAL-123" });
        var hash = await BankWarning(client, request);
        await using var db = Company(false, companyA);
        await db.Database.ExecuteSqlRawAsync("ALTER TABLE audit.ContractorChanges WITH NOCHECK ADD CONSTRAINT CK_IbanAuditFailure CHECK (ActorName <> N'operator.test');");
        try
        {
            using var failed = await client.PostAsJsonAsync(DetailsPath("bank-accounts"), request with { AcceptWarning = true, WarningHash = hash });
            Assert.Equal(HttpStatusCode.ServiceUnavailable, failed.StatusCode);
            Assert.Empty(await db.ContractorBankAccounts.ToListAsync());
            Assert.Empty(await db.ContractorChanges.ToListAsync());
            Assert.Empty(await db.ContractorCreateOperations.ToListAsync());
        }
        finally { await db.Database.ExecuteSqlRawAsync("ALTER TABLE audit.ContractorChanges DROP CONSTRAINT CK_IbanAuditFailure;"); }
    }

    [SqlServerFact]
    public async Task BankValidation_NrbNormalizationEditConflictAndCompanyIsolation()
    {
        using var client = await DetailOperator();
        var row = await CreateDetail(client, "bank-accounts", Data("bank-accounts") with { AccountNumber = "70 5064 0533 5016 0963 1294 5164" });
        Assert.Equal("70506405335016096312945164", row.Data.AccountNumber);
        Assert.True(row.BankValidation!.IsPolishNrb);
        using var edit = await client.PutAsJsonAsync($"{DetailsPath("bank-accounts")}/{row.Id}", ChangeDetail(row, row.Data with { AccountNumber = "de89 3704 0044 0532 0130 00" }));
        Assert.Equal(HttpStatusCode.OK, edit.StatusCode);
        var changed = (await edit.Content.ReadFromJsonAsync<DetailResponse>())!;
        Assert.Equal("DE89370400440532013000", changed.Data.AccountNumber);
        Assert.NotEqual(row.RowVersion, changed.RowVersion);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PutAsJsonAsync($"{DetailsPath("bank-accounts")}/{row.Id}", ChangeDetail(row, row.Data))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"{DetailsPath("bank-accounts", contractorIdB, companyB)}/{row.Id}")).StatusCode);
        await using var a = Company(false, companyA);
        await using var b = Company(true, companyB);
        Assert.Equal(2, await a.ContractorChanges.CountAsync());
        Assert.Empty(await b.ContractorBankAccounts.ToListAsync());
    }

    [SqlServerFact]
    public async Task BankValidation_DesktopShowsPreviewClearsStaleResultAndAcceptsWarningWithRealApi()
    {
        await AcceptBankWarnings();
        using var model = new MainViewModel(_ => new VatowiecApiClient(Client())) { UserName = "operator.test" };
        await model.Login(password);
        model.SelectedCompany = model.Companies.Single(x => x.CompanyId == companyA);
        await model.OpenCompany();
        await model.LoadContractors();
        model.SelectedContractor = Assert.Single(model.Contractors);
        await model.EditContractor();
        model.SelectedTab = 3;
        for (var i = 0; model.IsBusy && i < 100; i++) await Task.Delay(10);
        model.NewDetail();
        model.DetailEditor!.AccountNumber = "70506405335016096312945164";
        await model.ValidateBankAccount();
        Assert.Contains("Poprawny IBAN", model.BankValidationMessage);
        model.DetailEditor.AccountNumber = "PL71506405335016096312945164";
        Assert.Contains("jeszcze sprawdzony", model.BankValidationMessage);
        await model.SaveDetail();
        Assert.True(model.HasBankWarning);
        Assert.Contains("suma kontrolna", model.BankValidationMessage);
        model.DetailEditor.AccountNumber = "LOCAL-123";
        Assert.False(model.HasBankWarning);
        await model.SaveDetail();
        Assert.Contains("spoza IBAN", model.BankValidationMessage);
        await model.SaveDetail(true);
        Assert.False(model.HasBankWarning);
        Assert.Contains("spoza IBAN", Assert.Single(model.DetailRows).BankValidation!.Message);
    }
}
