using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Vatowiec.Desktop.Services;
using Vatowiec.Desktop.ViewModels;
using Vatowiec.SharedContracts;

namespace Vatowiec.Tests;

public sealed partial class AuthenticationIntegrationTests
{
    private readonly EwibFixture ewib = new();

    [SqlServerFact]
    public async Task Ewib_OnlyExplicitValidPolishPreviewCallsHttpAndNeverWritesSql()
    {
        ewib.Body = EwibTests.Reply("11402105", "TESTPLPW");
        using var client = await DetailOperator();
        var path = DetailsPath("bank-accounts") + "/validate";
        foreach (var number in new[] { "39-1140-2105-0000-5616-4000-1001", "PL39-1140-2105-0000-5616-4000-1001" })
        {
            using var response = await client.PostAsJsonAsync(path, new BankAccountValidationRequest(number));
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var result = (await response.Content.ReadFromJsonAsync<BankAccountValidationResponse>())!;
            Assert.Equal("11402105", result.BankLookup!.ClearingNumber);
            Assert.Equal("Bank demonstracyjny EWIB", result.BankLookup.BankName);
            Assert.Equal("TESTPLPW", Assert.Single(result.BankLookup.Bics));
            Assert.DoesNotContain("-", result.NormalizedNumber);
        }
        foreach (var number in new[] { "38-1140-2105-0000-5616-4000-1001", "DE89370400440532013000", "LOCAL-123" })
        {
            using var response = await client.PostAsJsonAsync(path, new BankAccountValidationRequest(number));
            Assert.Null((await response.Content.ReadFromJsonAsync<BankAccountValidationResponse>())!.BankLookup);
        }
        Assert.Equal(2, ewib.Requests.Count);
        Assert.All(ewib.Requests, x => Assert.EndsWith("nrRozliczeniowy=11402105&format=json", x));
        await using var db = Company(false, companyA);
        Assert.Empty(await db.ContractorBankAccounts.ToListAsync());
        Assert.Empty(await db.ContractorChanges.ToListAsync());
        Assert.Empty(await db.ContractorCreateOperations.ToListAsync());
    }

    [SqlServerFact]
    public async Task Ewib_SaveWithHyphensWithoutPreviewIsNormalizedAuditedAndIdempotentWithoutHttp()
    {
        using var client = await DetailOperator();
        var request = NewDetail(new() { AccountNumber = "39-1140-2105-0000-5616-4000-1001", BankName = "Bank wpisany ręcznie", Bic = "TESTPLPW" });
        using var response = await client.PostAsJsonAsync(DetailsPath("bank-accounts"), request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var saved = (await response.Content.ReadFromJsonAsync<DetailResponse>())!;
        Assert.Equal("39114021050000561640001001", saved.Data.AccountNumber);
        Assert.Equal("Bank wpisany ręcznie", saved.Data.BankName);
        using var retry = await client.PostAsJsonAsync(DetailsPath("bank-accounts"), request);
        Assert.Equal(saved.Id, (await retry.Content.ReadFromJsonAsync<DetailResponse>())!.Id);
        Assert.Single((await client.GetFromJsonAsync<DetailResponse[]>(DetailsPath("bank-accounts")))!);
        Assert.Empty(ewib.Requests);
        await using var a = Company(false, companyA);
        await using var b = Company(true, companyB);
        Assert.Single(await a.ContractorBankAccounts.ToListAsync()); Assert.Empty(await b.ContractorBankAccounts.ToListAsync());
        Assert.Contains("39114021050000561640001001", Assert.Single(await a.ContractorChanges.ToListAsync()).AfterJson);
        Assert.Single(await a.ContractorCreateOperations.ToListAsync());
    }

    [SqlServerFact]
    public async Task Ewib_DeniedRequestsNeverSendBankDataExternally()
    {
        using var client = await Login("operator.test");
        var path = DetailsPath("bank-accounts") + "/validate";
        var body = new BankAccountValidationRequest("39114021050000561640001001");
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(path, body)).StatusCode);
        await Csrf(client);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsJsonAsync(DetailsPath("bank-accounts", int.MaxValue) + "/validate", body)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(path + "?companyId=" + companyB, body)).StatusCode);
        using var reader = await Login("reader.test"); await Csrf(reader);
        Assert.Equal(HttpStatusCode.Forbidden, (await reader.PostAsJsonAsync(path, body)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await reader.PostAsJsonAsync(DetailsPath("bank-accounts", contractorIdB, companyB) + "/validate", body)).StatusCode);
        Assert.Empty(ewib.Requests);
    }

    [SqlServerFact]
    public async Task Ewib_DesktopKeepsManualValuesRequiresChoiceAndPersistsOnlyOnSave()
    {
        ewib.Body = EwibTests.Reply("11402105", "TESTPLPW", "TESTPLPW001");
        using var model = new MainViewModel(_ => new VatowiecApiClient(Client())) { UserName = "operator.test" };
        await model.Login(password); model.SelectedCompany = model.Companies.Single(x => x.CompanyId == companyA);
        await model.OpenCompany(); await model.LoadContractors(); model.SelectedContractor = Assert.Single(model.Contractors);
        await model.EditContractor(); model.SelectedTab = 3;
        for (var i = 0; model.IsBusy && i < 100; i++) await Task.Delay(10);
        model.NewDetail();
        model.DetailEditor!.AccountNumber = "PL39-1140-2105-0000-5616-4000-1001";
        model.DetailEditor.BankName = "Moja nazwa banku"; model.DetailEditor.Bic = "MYOWNBIC";
        await model.ValidateBankAccount();
        Assert.Equal("PL39114021050000561640001001", model.DetailEditor.AccountNumber);
        Assert.Equal("Moja nazwa banku", model.DetailEditor.BankName); Assert.Equal("MYOWNBIC", model.DetailEditor.Bic);
        Assert.True(model.HasBankNameProposal); Assert.Equal(2, model.BankBicChoices.Length); Assert.Null(model.SelectedBankBic);
        model.ApplyBankBicProposal(); Assert.Equal("MYOWNBIC", model.DetailEditor.Bic);
        await using (var db = Company(false, companyA)) { Assert.Empty(await db.ContractorBankAccounts.ToListAsync()); Assert.Empty(await db.ContractorChanges.ToListAsync()); }
        model.ApplyBankNameProposal(); model.SelectedBankBic = "TESTPLPW001"; model.ApplyBankBicProposal();
        Assert.Equal("Bank demonstracyjny EWIB", model.DetailEditor.BankName); Assert.Equal("TESTPLPW001", model.DetailEditor.Bic);
        await model.SaveDetail();
        Assert.Equal("TESTPLPW001", Assert.Single(model.DetailRows).Data.Bic);
        Assert.Single(ewib.Requests);
        await using var a = Company(false, companyA); await using var b = Company(true, companyB);
        Assert.Single(await a.ContractorChanges.ToListAsync()); Assert.Empty(await b.ContractorBankAccounts.ToListAsync());
    }
}
