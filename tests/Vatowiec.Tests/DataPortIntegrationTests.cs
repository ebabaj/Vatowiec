using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Vatowiec.Core.Domain;
using Vatowiec.Core.Infrastructure.Registries;
using Vatowiec.Desktop.Services;
using Vatowiec.Desktop.ViewModels;
using Vatowiec.SharedContracts;

namespace Vatowiec.Tests;

public sealed partial class AuthenticationIntegrationTests
{
    private readonly ConcurrentQueue<string> dataPortRequests = new();
    private string DataPortPath(Guid? company = null) => $"/api/v1/companies/{company ?? companyA}/integrations/dataport";
    private async Task<DataPortConfigurationResponse> SaveKey(HttpClient client, string key, string? version = null, Guid? company = null)
    {
        using var response = await client.PutAsJsonAsync(DataPortPath(company), new SaveDataPortKeyRequest { ApiKey = key, RowVersion = version });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var text = await response.Content.ReadAsStringAsync(); Assert.DoesNotContain(key, text);
        return JsonSerializer.Deserialize<DataPortConfigurationResponse>(text, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
    }
    private static Task<HttpResponseMessage> DeleteKey(HttpClient client, string path, string? version, bool confirm = true) =>
        client.SendAsync(new HttpRequestMessage(HttpMethod.Delete, path) { Content = JsonContent.Create(new DeleteDataPortKeyRequest(version, confirm)) });
    private sealed class MockDataPort(ConcurrentQueue<string> requests) : IDataPortClient
    {
        public async Task<RegistryReply> Lookup(string nip, string apiKey, CancellationToken cancellationToken)
        {
            using var handler = new DataPortTests.Stub(request =>
            {
                requests.Enqueue(request.Headers.GetValues("X-API-Key").Single());
                return new(HttpStatusCode.OK) { Content = new StringContent(DataPortTests.Success) };
            });
            var reply = await new DataPortClient(new DataPortTests.FreshFactory(handler)).Lookup(nip, apiKey, cancellationToken);
            return reply with { Environment = "Mock", Executed = false, Message = "MOCK — atrapa HTTP DataPort, bez zapytania do rzeczywistej usługi. " + reply.Message };
        }
    }
    [SqlServerFact]
    public async Task DataPortKey_EncryptedRotationRemovalAndStaleWritesNeverReturnSecret()
    {
        await GrantRegistryRights((CompanyPermission)63); using var client = await DetailOperator();
        var status = (await client.GetFromJsonAsync<DataPortConfigurationResponse>(DataPortPath()))!;
        Assert.False(status.IsConfigured); Assert.Null(status.RowVersion);
        var firstKey = "fake-" + Guid.NewGuid().ToString("N"); var nextKey = "fake-" + Guid.NewGuid().ToString("N");
        var first = await SaveKey(client, firstKey);
        await using var db = Company(false, companyA);
        var encrypted = (await db.DataPortConfigurations.AsNoTracking().SingleAsync()).ProtectedApiKey!;
        Assert.DoesNotContain(firstKey, encrypted);
        using var scope = application!.Services.CreateScope();
        var protector = scope.ServiceProvider.GetRequiredService<IDataPortKeyProtector>();
        Assert.Equal(firstKey, protector.Unprotect(companyA, encrypted));
        var next = await SaveKey(client, nextKey, first.RowVersion);
        Assert.NotEqual(first.RowVersion, next.RowVersion);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PutAsJsonAsync(DataPortPath(), new SaveDataPortKeyRequest { ApiKey = firstKey, RowVersion = first.RowVersion })).StatusCode);
        var read = await client.GetStringAsync(DataPortPath()); Assert.DoesNotContain(firstKey, read); Assert.DoesNotContain(nextKey, read); Assert.DoesNotContain(encrypted, read);
        Assert.Equal(HttpStatusCode.BadRequest, (await DeleteKey(client, DataPortPath(), next.RowVersion, false)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await DeleteKey(client, DataPortPath(), next.RowVersion)).StatusCode);
        var removed = (await client.GetFromJsonAsync<DataPortConfigurationResponse>(DataPortPath()))!;
        Assert.False(removed.IsConfigured); Assert.NotEqual(next.RowVersion, removed.RowVersion);
        Assert.Null((await db.DataPortConfigurations.AsNoTracking().SingleAsync()).ProtectedApiKey);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PutAsJsonAsync(DataPortPath(), new SaveDataPortKeyRequest { ApiKey = nextKey, RowVersion = next.RowVersion })).StatusCode);
        Assert.Empty(dataPortRequests);
    }
    [SqlServerFact]
    public async Task DataPortSettings_RequireCompanyAdministratorCsrfAndCurrentProfile()
    {
        using var client = await DetailOperator();
        var request = new SaveDataPortKeyRequest { ApiKey = "fake-" + Guid.NewGuid().ToString("N") };
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PutAsJsonAsync(DataPortPath(), request)).StatusCode);
        await GrantRegistryRights((CompanyPermission)63);
        client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync(DataPortPath(), request)).StatusCode);
        await Csrf(client);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PutAsJsonAsync(DataPortPath(companyB), request)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await DeleteKey(client, DataPortPath(companyB), null)).StatusCode);
        using var reader = await Login("reader.test"); await Csrf(reader);
        Assert.Equal(HttpStatusCode.OK, (await reader.GetAsync(DataPortPath())).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await reader.PutAsJsonAsync(DataPortPath(), request)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await DeleteKey(reader, DataPortPath(), null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await reader.GetAsync(DataPortPath(companyB))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync(DataPortPath() + "?companyId=" + companyB)).StatusCode);
        await using var a = Company(false, companyA); await using var b = Company(true, companyB);
        Assert.Empty(await a.DataPortConfigurations.ToListAsync()); Assert.Empty(await b.DataPortConfigurations.ToListAsync());
        Assert.Empty(dataPortRequests);
    }
    [SqlServerFact]
    public async Task DataPortSettings_ConcurrentInitialWritesHaveExactlyOneWinner()
    {
        await GrantRegistryRights((CompanyPermission)63); using var client = await DetailOperator();
        var responses = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => client.PutAsJsonAsync(DataPortPath(),
            new SaveDataPortKeyRequest { ApiKey = "fake-" + Guid.NewGuid().ToString("N") })));
        Assert.Single(responses, x => x.StatusCode == HttpStatusCode.OK);
        Assert.Single(responses, x => x.StatusCode == HttpStatusCode.Conflict);
        foreach (var response in responses) response.Dispose();
        await using var db = Company(false, companyA); Assert.Single(await db.DataPortConfigurations.ToListAsync());
        Assert.Empty(dataPortRequests);
    }
    [SqlServerFact]
    public async Task DataPort_TwoCompanyKeysAndConcurrentResultsRemainSeparate()
    {
        await GrantRegistryRights((CompanyPermission)63); using var client = await DetailOperator(); await SetRegistryInput();
        var keyA = "fake-A-" + Guid.NewGuid().ToString("N"); var keyB = "fake-B-" + Guid.NewGuid().ToString("N");
        await SaveKey(client, keyA);
        using (var scope = application!.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<Vatowiec.Core.Infrastructure.Persistence.SystemDbContext>().CompanyAccesses
                .Where(x => x.UserId == operatorId && x.CompanyId == companyB).ExecuteUpdateAsync(x => x.SetProperty(p => p.Permissions, (CompanyPermission)31));
            await using var company = Company(true, companyB);
            var configuration = new DataPortConfiguration();
            configuration.SetKey(scope.ServiceProvider.GetRequiredService<IDataPortKeyProtector>().Protect(companyB, keyB));
            company.DataPortConfigurations.Add(configuration);
            var row = await company.Contractors.SingleAsync(); row.SetIdentifiers("3245174504", null, null); await company.SaveChangesAsync();
        }
        var a = (await Read(client, contractorId))!;
        var b = (await client.GetFromJsonAsync<ContractorResponse>($"{ContractorsPath(companyB)}/{contractorIdB}"))!;
        var responses = await Task.WhenAll(client.PostAsJsonAsync(VerificationsPath(), new VerificationRequest(Guid.NewGuid(), "DataPort", a.RowVersion)),
            client.PostAsJsonAsync(VerificationsPath(contractorIdB, companyB), new VerificationRequest(Guid.NewGuid(), "DataPort", b.RowVersion)));
        Assert.All(responses, x => Assert.Equal(HttpStatusCode.OK, x.StatusCode));
        var first = (await responses[0].Content.ReadFromJsonAsync<VerificationResponse>())!;
        var second = (await responses[1].Content.ReadFromJsonAsync<VerificationResponse>())!;
        foreach (var response in responses) response.Dispose();
        Assert.Equal(companyA, first.CompanyId); Assert.Equal(companyB, second.CompanyId); Assert.NotEqual(first.Id, second.Id);
        Assert.Equal(new[] { keyA, keyB }.Order(), dataPortRequests.Order());
        await using var da = Company(false, companyA); await using var db = Company(true, companyB);
        Assert.Equal(first.Id, (await da.ExternalVerifications.SingleAsync()).Id);
        Assert.Equal(second.Id, (await db.ExternalVerifications.SingleAsync()).Id);
        Assert.DoesNotContain(keyA, await client.GetStringAsync(VerificationsPath()));
        Assert.DoesNotContain(keyB, await client.GetStringAsync(VerificationsPath(contractorIdB, companyB)));
    }
    [SqlServerFact]
    public async Task DataPort_KeysAndHistoryAreIsolatedAndCopiedCiphertextCannotBeUsed()
    {
        await GrantRegistryRights((CompanyPermission)63); using var client = await DetailOperator(); await SetRegistryInput();
        var key = "fake-" + Guid.NewGuid().ToString("N"); await SaveKey(client, key);
        Assert.False((await client.GetFromJsonAsync<DataPortConfigurationResponse>(DataPortPath(companyB)))!.IsConfigured);
        var row = (await Read(client, contractorId))!;
        var value = new VerificationRequest(Guid.NewGuid(), "DataPort", row.RowVersion);
        using var response = await client.PostAsJsonAsync(VerificationsPath(), value); Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = (await response.Content.ReadFromJsonAsync<VerificationResponse>())!;
        Assert.Equal("DataPort", result.Source); Assert.Equal("Mock", result.Environment); Assert.False(result.Executed);
        Assert.Equal("3245174504", result.TaxId); Assert.Equal(key, Assert.Single(dataPortRequests));
        Assert.Equal("Fikcyjna nazwa DataPort", result.Data.Name);
        Assert.DoesNotContain(key, await client.GetStringAsync(VerificationsPath()));
        Assert.Empty((await client.GetFromJsonAsync<VerificationResponse[]>(VerificationsPath(contractorIdB, companyB)))!);
        using var replay = await client.PostAsJsonAsync(VerificationsPath(), value);
        Assert.Equal(result.Id, (await replay.Content.ReadFromJsonAsync<VerificationResponse>())!.Id); Assert.Single(dataPortRequests);
        await using var a = Company(false, companyA); await using var b = Company(true, companyB);
        Assert.Equal(operatorId, (await a.ExternalVerifications.SingleAsync()).ActorId);
        var copied = new DataPortConfiguration(); copied.SetKey((await a.DataPortConfigurations.SingleAsync()).ProtectedApiKey);
        b.DataPortConfigurations.Add(copied); var rb = await b.Contractors.SingleAsync(); rb.SetIdentifiers("3245174504", null, null); await b.SaveChangesAsync();
        using (var scope = application!.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<Vatowiec.Core.Infrastructure.Persistence.SystemDbContext>().CompanyAccesses
                .Where(x => x.UserId == operatorId && x.CompanyId == companyB).ExecuteUpdateAsync(x => x.SetProperty(p => p.Permissions, (CompanyPermission)31));
        var requestB = new VerificationRequest(Guid.NewGuid(), "DataPort", Convert.ToBase64String(rb.RowVersion));
        using var responseB = await client.PostAsJsonAsync(VerificationsPath(contractorIdB, companyB), requestB);
        var blocked = (await responseB.Content.ReadFromJsonAsync<VerificationResponse>())!;
        Assert.Equal("dataport_key_unavailable", blocked.ErrorCode); Assert.False(blocked.Executed); Assert.Single(dataPortRequests);
        Assert.DoesNotContain(key, (await b.ExternalVerifications.SingleAsync()).DataJson);
    }
    [SqlServerFact]
    public async Task DataPort_InvalidNipAndMissingKeyNeverQueryService_AndPermissionsAreEnforced()
    {
        using var client = await DetailOperator(); await SetRegistryInput("3245174505");
        var row = (await Read(client, contractorId))!; var value = new VerificationRequest(Guid.NewGuid(), "DataPort", row.RowVersion);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync(VerificationsPath(), value)).StatusCode);
        await GrantRegistryRights();
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(VerificationsPath(), value)).StatusCode);
        await SetRegistryInput("PL 324-517-45-04"); row = (await Read(client, contractorId))!;
        using var response = await client.PostAsJsonAsync(VerificationsPath(), value with { OperationId = Guid.NewGuid(), RowVersion = row.RowVersion });
        var missing = (await response.Content.ReadFromJsonAsync<VerificationResponse>())!;
        Assert.Equal("NotConfigured", missing.ServiceStatus); Assert.Equal("3245174504", missing.TaxId); Assert.False(missing.Executed);
        Assert.Contains("ustawieniach", missing.Message); Assert.Empty(dataPortRequests);
        client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(VerificationsPath(), value with { RowVersion = row.RowVersion })).StatusCode);
    }
    [SqlServerFact]
    public async Task DataPort_DesktopSettingsAndSelectiveImportUseNormalAtomicAudit()
    {
        await SetRegistryInput(); await GrantRegistryRights((CompanyPermission)63);
        using var model = new MainViewModel(_ => new VatowiecApiClient(Client())) { UserName = "operator.test" };
        await model.Login(password); model.SelectedCompany = model.Companies.Single(x => x.CompanyId == companyA); await model.OpenCompany();
        await model.OpenDataPortSettings(); Assert.True(model.CanManageCompanyIntegrations);
        var key = "fake-" + Guid.NewGuid().ToString("N"); await model.SaveDataPortKey(key); Assert.Equal("Skonfigurowano", model.DataPortStatus);
        model.CloseDataPortSettings(); await model.LoadContractors(); model.SelectedContractor = Assert.Single(model.Contractors); await model.EditContractor();
        var original = model.Editor!.Name;
        await model.VerifyRegistry("DataPort"); Assert.True(model.CanImportGus, model.Message); Assert.Equal(original, model.Editor.Name);
        Assert.Contains("DataPort (dane GUS)", model.VerificationSummary);
        await using (var db = Company(false, companyA)) Assert.Empty(await db.ContractorChanges.ToListAsync());
        model.UseGusName = true; model.UseGusStreet = true; model.ApplyGusSelection();
        Assert.Equal("Fikcyjna nazwa DataPort", model.Editor.Name); Assert.Equal("Testowa", model.DetailEditor!.Street);
        Assert.Null(model.DetailEditor.City); Assert.Null(model.DetailEditor.CountryCode);
        model.DetailEditor.AddressTypeId = 1;
        await model.SaveDetail(); Assert.False(model.IsDetailEditing, model.Message);
        model.SelectedTab = 0; await model.SaveContractor();
        Assert.True(model.HasIdentifierWarning); // Imported data must not bypass warnings about existing identifiers.
        await model.AcceptIdentifierWarning(); Assert.False(model.IsEditing, model.Message);
        await using (var db = Company(false, companyA))
        {
            Assert.Equal("Fikcyjna nazwa DataPort", (await db.Contractors.SingleAsync()).Name);
            Assert.Equal("Testowa", (await db.ContractorAddresses.SingleAsync()).Street);
            Assert.Equal(2, await db.ContractorChanges.CountAsync());
            Assert.All(await db.ContractorChanges.ToListAsync(), x => Assert.Equal(operatorId, x.ActorId));
            Assert.DoesNotContain(key, JsonSerializer.Serialize(await db.ExternalVerifications.ToListAsync()));
        }
        await model.OpenDataPortSettings(); model.RequestDataPortDelete(); await model.DeleteDataPortKey(); Assert.Equal("Nie skonfigurowano", model.DataPortStatus);
        await model.ChangeCompany(); model.SelectedCompany = model.Companies.Single(x => x.CompanyId == companyB); await model.OpenCompany(); await model.OpenDataPortSettings();
        Assert.False(model.CanManageCompanyIntegrations); Assert.Equal("Nie skonfigurowano", model.DataPortStatus);
        await model.SaveDataPortKey(key); Assert.Equal("Nie skonfigurowano", model.DataPortStatus); Assert.Single(dataPortRequests);
    }
}
