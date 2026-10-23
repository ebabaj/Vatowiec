using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
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
    private string DetailsPath(string kind, int? owner = null, Guid? company = null) => $"{ContractorsPath(company)}/{owner ?? contractorId}/{kind}";
    private static DetailWriteRequest NewDetail(DetailValues data) => new(0, data, OperationId: Guid.NewGuid());
    private static DetailWriteRequest ChangeDetail(DetailResponse row, DetailValues data) => new(row.Id, data, row.RowVersion);
    private async Task<HttpClient> DetailOperator()
    {
        var client = await Login("operator.test");
        await Csrf(client);
        return client;
    }
    private async Task<DetailResponse> CreateDetail(HttpClient client, string kind, DetailValues data)
    {
        using var response = await client.PostAsJsonAsync(DetailsPath(kind), NewDetail(data));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<DetailResponse>())!;
    }
    private static DetailValues Data(string kind) => kind switch
    {
        "contacts" => new() { Name = "Fikcyjny kontakt" },
        "addresses" => new() { Street = "Fikcyjna", City = "Miasto testowe" },
        _ => new() { AccountNumber = "70506405335016096312945164", CurrencyCode = "PLN" }
    };

    [SqlServerFact]
    public async Task DetailAddresses_CustomTypeMultipleSameTypeDefaultSwitchAndEdit()
    {
        using var client = await DetailOperator();
        var typeRequest = new AddressTypeWriteRequest(Guid.NewGuid(), "Fikcyjny adres dostawy");
        var typesPath = $"/api/v1/companies/{companyA}/address-types";
        using var typeCreated = await client.PostAsJsonAsync(typesPath, typeRequest);
        Assert.Equal(HttpStatusCode.Created, typeCreated.StatusCode);
        var type = (await typeCreated.Content.ReadFromJsonAsync<AddressTypeResponse>())!;
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync(typesPath, typeRequest)).StatusCode);
        var first = await CreateDetail(client, "addresses", Data("addresses") with { AddressTypeId = type.Id, IsDefault = true });
        var second = await CreateDetail(client, "addresses", Data("addresses") with { AddressTypeId = type.Id, IsDefault = true, City = "Druga fikcyjna miejscowość" });
        var third = await CreateDetail(client, "addresses", Data("addresses") with { AddressTypeId = 2, IsDefault = true });
        var rows = (await client.GetFromJsonAsync<DetailResponse[]>(DetailsPath("addresses")))!;
        Assert.Equal(3, rows.Length);
        Assert.False(rows.Single(x => x.Id == first.Id).Data.IsDefault);
        Assert.True(rows.Single(x => x.Id == second.Id).Data.IsDefault);
        Assert.True(rows.Single(x => x.Id == third.Id).Data.IsDefault);
        using var edited = await client.PutAsJsonAsync($"{DetailsPath("addresses")}/{second.Id}", ChangeDetail(second, second.Data with { Name = "Fikcyjny magazyn", IsActive = false }));
        Assert.Equal(HttpStatusCode.OK, edited.StatusCode);
        var current = (await edited.Content.ReadFromJsonAsync<DetailResponse>())!;
        Assert.False(current.Data.IsActive);
        Assert.NotEqual(second.RowVersion, current.RowVersion);
        await using var db = Company(false, companyA);
        Assert.Single(await db.ContractorChanges.Where(x => x.RecordType == "AddressType").ToListAsync());
        Assert.Equal(5, await db.ContractorChanges.CountAsync(x => x.RecordType == "ContractorAddress"));
        Assert.Equal(type.Id, (await db.ContractorChanges.SingleAsync(x => x.RecordType == "AddressType")).RecordId);
    }

    [SqlServerFact]
    public async Task DetailContacts_NameOnlyEmailOnlyAndFullAddressAreIndependent()
    {
        using var client = await DetailOperator();
        var nameOnly = await CreateDetail(client, "contacts", new() { Name = "Fikcyjna Pani Ewa" });
        Assert.Null(nameOnly.Data.Email);
        var email = await CreateDetail(client, "contacts", new() { Name = "Księgowość", Email = "demo@example.invalid" });
        Assert.Null(email.Data.Street);
        var postal = await CreateDetail(client, "contacts", new() { Name = "Magazyn", Street = "Fikcyjna", BuildingNo = "1", ApartmentNo = "2", City = "Miasto testowe", CountryCode = "PL", Notes = "Dane fikcyjne" });
        Assert.Null(postal.Data.Email);
        using var edit = await client.PutAsJsonAsync($"{DetailsPath("contacts")}/{email.Id}", ChangeDetail(email, email.Data with { Phone = "TEST", Position = "Fikcyjna funkcja" }));
        Assert.Equal(HttpStatusCode.OK, edit.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(DetailsPath("contacts"), NewDetail(new() { Email = "demo@example.invalid" }))).StatusCode);
        await using var db = Company(false, companyA);
        Assert.Equal(3, await db.ContractorContacts.CountAsync());
        Assert.Empty(await db.ContractorAddresses.ToListAsync());
        Assert.Equal(4, await db.ContractorChanges.CountAsync(x => x.RecordType == "ContractorContact"));
    }

    [SqlServerFact]
    public async Task DetailBankAccounts_PlN_Eur_NullSwitchDefaultsAndAllowDuplicateNumbers()
    {
        using var client = await DetailOperator();
        foreach (var currency in new string?[] { "PLN", "EUR", null })
        {
            var first = await CreateDetail(client, "bank-accounts", Data("bank-accounts") with { CurrencyCode = currency, IsDefault = true });
            var second = await CreateDetail(client, "bank-accounts", Data("bank-accounts") with { CurrencyCode = currency, IsDefault = true });
            using var updated = await client.PutAsJsonAsync($"{DetailsPath("bank-accounts")}/{second.Id}", ChangeDetail(second, second.Data with { BankName = "Fikcyjny bank", IsActive = false }));
            Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
            await using var db = Company(false, companyA);
            Assert.False((await db.ContractorBankAccounts.SingleAsync(x => x.Id == first.Id)).IsDefault);
            Assert.True((await db.ContractorBankAccounts.SingleAsync(x => x.Id == second.Id)).IsDefault);
        }
        var rows = (await client.GetFromJsonAsync<DetailResponse[]>(DetailsPath("bank-accounts")))!;
        Assert.Equal(6, rows.Length);
        Assert.Equal(3, rows.Count(x => x.Data.IsDefault));
        Assert.Single(rows.Select(x => x.Data.AccountNumber).Distinct());
        await using var verify = Company(false, companyA);
        Assert.Equal(12, await verify.ContractorChanges.CountAsync(x => x.RecordType == "ContractorBankAccount"));
    }

    [SqlServerFact]
    public async Task DetailWrites_RequireCsrfPermissionsAndRejectForeignOwnerOrCompany()
    {
        using var op = await Login("operator.test");
        foreach (var kind in new[] { "addresses", "contacts", "bank-accounts" })
        {
            var value = NewDetail(Data(kind));
            Assert.Equal(HttpStatusCode.BadRequest, (await op.PostAsJsonAsync(DetailsPath(kind), value)).StatusCode);
        }
        await Csrf(op);
        foreach (var kind in new[] { "addresses", "contacts", "bank-accounts" })
        {
            using (var scope = application!.Services.CreateScope())
                await scope.ServiceProvider.GetRequiredService<SystemDbContext>().CompanyAccesses.Where(x => x.UserId == operatorId && x.CompanyId == companyB)
                    .ExecuteUpdateAsync(x => x.SetProperty(a => a.Permissions, (CompanyPermission)3));
            Assert.Equal(HttpStatusCode.Forbidden, (await op.PostAsJsonAsync(DetailsPath(kind, company: companyB), NewDetail(Data(kind)))).StatusCode);
            var saved = await CreateDetail(op, kind, Data(kind));
            Assert.Equal(HttpStatusCode.NotFound, (await op.GetAsync($"{DetailsPath(kind, int.MaxValue)}/{saved.Id}")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await op.PutAsJsonAsync($"{DetailsPath(kind, int.MaxValue)}/{saved.Id}", ChangeDetail(saved, saved.Data))).StatusCode);
            Assert.Empty((await op.GetFromJsonAsync<DetailResponse[]>(DetailsPath(kind, contractorIdB, companyB)))!);
            Assert.Equal(HttpStatusCode.BadRequest, (await op.PostAsJsonAsync(DetailsPath(kind) + "?companyId=" + companyB, NewDetail(Data(kind)))).StatusCode);
        }
        Assert.Equal(HttpStatusCode.Forbidden, (await op.PostAsJsonAsync($"/api/v1/companies/{companyB}/address-types", new AddressTypeWriteRequest(Guid.NewGuid(), "Fikcyjny"))).StatusCode);
    }

    [SqlServerFact]
    public async Task DetailConcurrency_StaleVersionCannotOverwriteAndExplicitResolutionWorks()
    {
        using var first = await DetailOperator();
        using var second = await DetailOperator();
        foreach (var kind in new[] { "addresses", "contacts", "bank-accounts" })
        {
            var original = await CreateDetail(first, kind, Data(kind));
            var path = $"{DetailsPath(kind)}/{original.Id}";
            var results = await Task.WhenAll(first.PutAsJsonAsync(path, ChangeDetail(original, original.Data with { Name = "Fikcyjna zmiana A" })),
                second.PutAsJsonAsync(path, ChangeDetail(original, original.Data with { Name = "Fikcyjna zmiana B" })));
            Assert.Single(results, x => x.StatusCode == HttpStatusCode.OK);
            var conflict = Assert.Single(results, x => x.StatusCode == HttpStatusCode.Conflict);
            var payload = await conflict.Content.ReadFromJsonAsync<JsonElement>();
            var current = payload.GetProperty("detailCurrent").Deserialize<DetailResponse>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
            Assert.Equal(HttpStatusCode.OK, (await second.PutAsJsonAsync(path, ChangeDetail(current, current.Data with { Name = "Świadoma fikcyjna zmiana" }))).StatusCode);
            foreach (var result in results) result.Dispose();
        }
        await using var db = Company(false, companyA);
        Assert.Equal(9, await db.ContractorChanges.CountAsync());
    }

    [SqlServerFact]
    public async Task DetailCreateConcurrentReplay_DoesNotDuplicateAndIsBoundToPayloadAndOwner()
    {
        using var first = await DetailOperator();
        using var second = await DetailOperator();
        foreach (var kind in new[] { "addresses", "contacts", "bank-accounts" })
        {
            var request = NewDetail(Data(kind));
            var responses = await Task.WhenAll(first.PostAsJsonAsync(DetailsPath(kind), request), second.PostAsJsonAsync(DetailsPath(kind), request));
            Assert.Single(responses, x => x.StatusCode == HttpStatusCode.Created);
            Assert.Single(responses, x => x.StatusCode == HttpStatusCode.OK);
            var a = (await responses[0].Content.ReadFromJsonAsync<DetailResponse>())!;
            var b = (await responses[1].Content.ReadFromJsonAsync<DetailResponse>())!;
            Assert.Equal(a, b);
            Assert.Equal(HttpStatusCode.Conflict, (await first.PostAsJsonAsync(DetailsPath(kind), request with { Data = request.Data with { Name = "Inne fikcyjne dane" } })).StatusCode);
            foreach (var response in responses) response.Dispose();
        }
        await using var db = Company(false, companyA);
        Assert.Equal(3, await db.ContractorCreateOperations.CountAsync());
        Assert.Equal(3, await db.ContractorChanges.CountAsync());
    }

    [SqlServerFact]
    public async Task DetailDefaultConcurrentChanges_KeepOneDefaultAndAuditBothAffectedRecords()
    {
        using var first = await DetailOperator();
        using var second = await DetailOperator();
        foreach (var (kind, currency) in new[] { ("addresses", (string?)null), ("bank-accounts", "PLN"), ("bank-accounts", "EUR"), ("bank-accounts", null) })
        {
            var a = await CreateDetail(first, kind, Data(kind) with { CurrencyCode = currency });
            var b = await CreateDetail(first, kind, Data(kind) with { CurrencyCode = currency });
            var responses = await Task.WhenAll(
                first.PutAsJsonAsync($"{DetailsPath(kind)}/{a.Id}", ChangeDetail(a, a.Data with { IsDefault = true })),
                second.PutAsJsonAsync($"{DetailsPath(kind)}/{b.Id}", ChangeDetail(b, b.Data with { IsDefault = true })));
            Assert.All(responses, response => Assert.Equal(HttpStatusCode.OK, response.StatusCode));
            foreach (var response in responses) response.Dispose();
            var rows = (await first.GetFromJsonAsync<DetailResponse[]>(DetailsPath(kind)))!;
            Assert.Single(rows, x => (x.Id == a.Id || x.Id == b.Id) && x.Data.IsDefault);
        }
        await using var db = Company(false, companyA);
        Assert.Equal(20, await db.ContractorChanges.CountAsync());
        Assert.Equal(4, await db.ContractorChanges.CountAsync(x => x.BeforeJson != null && x.BeforeJson.Contains("\"IsDefault\":true")));
    }

    [SqlServerFact]
    public async Task DetailAuditFailure_RollsBackNewRowsDefaultSwitchAndOperationResult()
    {
        using var client = await DetailOperator();
        var original = await CreateDetail(client, "addresses", Data("addresses") with { IsDefault = true });
        await using var db = Company(false, companyA);
        await db.Database.ExecuteSqlRawAsync("ALTER TABLE audit.ContractorChanges WITH NOCHECK ADD CONSTRAINT CK_DetailFailure CHECK (ActorName <> N'operator.test');");
        try
        {
            foreach (var kind in new[] { "addresses", "contacts", "bank-accounts" })
                Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.PostAsJsonAsync(DetailsPath(kind), NewDetail(Data(kind) with { IsDefault = true }))).StatusCode);
            Assert.Single(await db.ContractorAddresses.ToListAsync());
            Assert.True((await db.ContractorAddresses.SingleAsync(x => x.Id == original.Id)).IsDefault);
            Assert.Empty(await db.ContractorContacts.ToListAsync());
            Assert.Empty(await db.ContractorBankAccounts.ToListAsync());
            Assert.Single(await db.ContractorCreateOperations.ToListAsync());
            Assert.Single(await db.ContractorChanges.ToListAsync());
        }
        finally { await db.Database.ExecuteSqlRawAsync("ALTER TABLE audit.ContractorChanges DROP CONSTRAINT CK_DetailFailure;"); }
    }

    [SqlServerFact]
    public async Task DetailBankWarning_RequiresExactConfirmationAndSeparatePermissionAndIsAudited()
    {
        using var client = await DetailOperator();
        var request = NewDetail(Data("bank-accounts") with { AccountNumber = " TEST-? " });
        using var warning = await client.PostAsJsonAsync(DetailsPath("bank-accounts"), request);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, warning.StatusCode);
        var hash = (await warning.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("warningHash").GetString();
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await client.PostAsJsonAsync(DetailsPath("bank-accounts"), request with { AcceptWarning = true })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync(DetailsPath("bank-accounts"), request with { AcceptWarning = true, WarningHash = hash })).StatusCode);
        using (var scope = application!.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<SystemDbContext>().CompanyAccesses.Where(x => x.UserId == operatorId && x.CompanyId == companyA)
                .ExecuteUpdateAsync(x => x.SetProperty(a => a.Permissions, (CompanyPermission)15));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await client.PostAsJsonAsync(DetailsPath("bank-accounts"), request with { Data = request.Data with { AccountNumber = "INNE-?" }, AcceptWarning = true, WarningHash = hash })).StatusCode);
        using var accepted = await client.PostAsJsonAsync(DetailsPath("bank-accounts"), request with { AcceptWarning = true, WarningHash = hash });
        Assert.Equal(HttpStatusCode.Created, accepted.StatusCode);
        Assert.Equal("TEST-?", (await accepted.Content.ReadFromJsonAsync<DetailResponse>())!.Data.AccountNumber);
        await using var db = Company(false, companyA);
        var audit = Assert.Single(await db.ContractorChanges.ToListAsync());
        Assert.Equal("ContractorBankAccount", audit.RecordType);
        Assert.Contains("bank-iban-v3", audit.WarningJson);
        Assert.Contains("Accepted", audit.WarningJson);
    }

    [SqlServerFact]
    public async Task DesktopDetailTabs_IdentifiersParentAndReadOnlyCompanyWorkWithRealApi()
    {
        await using (var db = Company(false, companyA))
        {
            var parent = new Contractor("GROUP", "Fikcyjna grupa", "PL", "TEST-TAX", "TEST-VAT", "TEST-IDWEW");
            db.Contractors.Add(parent);
            await db.SaveChangesAsync();
        }
        using var model = new MainViewModel(_ => new VatowiecApiClient(Client())) { UserName = "operator.test" };
        await model.Login(password);
        model.SelectedCompany = model.Companies.Single(x => x.CompanyId == companyA);
        await model.OpenCompany();
        await model.LoadContractors();
        model.SelectedContractor = model.Contractors.Single(x => x.Code == "GROUP");
        await model.EditContractor();
        Assert.Equal("TEST-TAX", model.Editor!.TaxId);
        Assert.Equal("TEST-VAT", model.Editor.VatEuId);
        Assert.Equal("TEST-IDWEW", model.Editor.KsefIdWew);
        model.CancelEditor();
        model.SelectedContractor = model.Contractors.Single(x => x.Id == contractorId);
        await model.EditContractor();
        model.ParentSearch = "GROUP";
        await model.SearchParents();
        model.SelectedParentCandidate = Assert.Single(model.ParentCandidates);
        model.ChooseParent();
        Assert.NotNull(model.Editor!.ParentId);
        await model.SaveContractor();
        Assert.False(model.IsEditing);
        await model.LoadContractors();
        model.SelectedContractor = model.Contractors.Single(x => x.Id == contractorId);
        await model.EditContractor();
        model.SelectedTab = 2;
        // SelectTab uruchamia asynchroniczny odczyt; poczekaj na zakończenie transportu.
        for (var i = 0; model.IsBusy && i < 100; i++) await Task.Delay(10);
        model.NewDetail();
        model.DetailEditor!.Name = "Księgowość";
        model.DetailEditor.Email = "demo@example.invalid";
        await model.SaveDetail();
        Assert.Single(model.DetailRows);
        model.CancelEditor();
        await model.ChangeCompany();
        using (var scope = application!.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<SystemDbContext>().CompanyAccesses.Where(x => x.UserId == operatorId && x.CompanyId == companyB)
                .ExecuteUpdateAsync(x => x.SetProperty(a => a.Permissions, (CompanyPermission)3));
        await model.RefreshCompanies();
        model.SelectedCompany = model.Companies.Single(x => x.CompanyId == companyB);
        await model.OpenCompany();
        await model.LoadContractors();
        model.SelectedContractor = Assert.Single(model.Contractors);
        await model.EditContractor();
        Assert.True(model.IsEditing);
        Assert.False(model.CanEditDetails);
        model.SelectedTab = 2;
        for (var i = 0; model.IsBusy && i < 100; i++) await Task.Delay(10);
        Assert.Empty(model.DetailRows);
    }
}
