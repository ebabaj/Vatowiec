using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Vatowiec.Core.Domain;
using Vatowiec.Core.Infrastructure.Persistence;
using Vatowiec.SharedContracts;

namespace Vatowiec.Tests;

public sealed partial class AuthenticationIntegrationTests
{
    private ContractorWriteRequest NewContractor(string code = "NEW") => new(0, code, "Fikcyjny kontrahent", "pl", true, OperationId: Guid.NewGuid());
    private string ContractorsPath(Guid? company = null) => $"/api/v1/companies/{company ?? companyA}/contractors";
    private static ContractorWriteRequest Edit(ContractorResponse value, string name, bool active = true) =>
        new(value.Id, value.Code, name, value.CountryCode, active, value.RowVersion, ParentId: value.ParentId);
    private Task<ContractorResponse?> Read(HttpClient client, int id) => client.GetFromJsonAsync<ContractorResponse>($"{ContractorsPath()}/{id}");
    private async Task GrantReaderEdit()
    {
        using var scope = application!.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<SystemDbContext>().CompanyAccesses.Where(x => x.UserId == readerId)
            .ExecuteUpdateAsync(x => x.SetProperty(a => a.Permissions, (CompanyPermission)7));
    }

    [SqlServerFact]
    public async Task ContractorCreate_IsAuditedAndReplayDoesNotDuplicate()
    {
        using var op = await Login("operator.test");
        await Csrf(op);
        var value = NewContractor(" new ");
        using var created = await op.PostAsJsonAsync(ContractorsPath(), value);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var row = (await created.Content.ReadFromJsonAsync<ContractorResponse>())!;
        Assert.Equal("NEW", row.Code);
        Assert.Equal("PL", row.CountryCode);
        Assert.Equal(8, Convert.FromBase64String(row.RowVersion).Length);
        using var replay = await op.PostAsJsonAsync(ContractorsPath(), value);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        await using var company = Company(false, companyA);
        var audit = Assert.Single(await company.ContractorChanges.ToListAsync());
        Assert.Null(audit.BeforeJson);
        Assert.Equal("Create", audit.Operation);
        Assert.Equal(operatorId, audit.ActorId);
        Assert.Equal("operator.test", audit.ActorName);
        Assert.Equal(clock.GetUtcNow(), audit.OccurredAtUtc);
        Assert.False(string.IsNullOrWhiteSpace(audit.CorrelationId));
        using var scope = application!.Services.CreateScope();
        Assert.Equal((await scope.ServiceProvider.GetRequiredService<SystemDbContext>().InstallationIdentities.SingleAsync()).Id, audit.ActorIssuerId);
        Assert.Equal("NEW", JsonDocument.Parse(audit.AfterJson).RootElement.GetProperty("Code").GetString());
        await using var other = Company(true, companyB);
        Assert.False(await other.Contractors.AnyAsync(x => x.Code == value.Code.Trim().ToUpper()));
        Assert.Empty(await other.ContractorChanges.ToListAsync());
    }

    [SqlServerFact]
    public async Task ContractorUpdate_ChangesAllFieldsAndDeactivatesWithoutDeleting()
    {
        using var op = await Login("operator.test");
        await Csrf(op);
        var before = (await Read(op, contractorId))!;
        var value = Edit(before, "Nowa fikcyjna nazwa", false) with { Code = "EDITED", CountryCode = "de" };
        using var update = await op.PutAsJsonAsync($"{ContractorsPath()}/{contractorId}", value);
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        var after = (await Read(op, contractorId))!;
        Assert.NotEqual(before.RowVersion, after.RowVersion);
        Assert.False(after.IsActive);
        Assert.Equal("DE", after.CountryCode);
        Assert.Equal(before.TaxId, after.TaxId);
        await using var company = Company(false, companyA);
        var audit = Assert.Single(await company.ContractorChanges.ToListAsync());
        Assert.Equal("Update", audit.Operation);
        Assert.Equal(before.Name, JsonDocument.Parse(audit.BeforeJson!).RootElement.GetProperty("Name").GetString());
        Assert.False(JsonDocument.Parse(audit.AfterJson).RootElement.GetProperty("IsActive").GetBoolean());
        Assert.Equal(1, await company.Contractors.CountAsync());
        using var delete = await op.DeleteAsync($"{ContractorsPath()}/{contractorId}");
        Assert.Equal(HttpStatusCode.MethodNotAllowed, delete.StatusCode);
        await using var b = Company(true, companyB);
        Assert.Equal("Kontrahent B", (await b.Contractors.SingleAsync()).Name);
    }

    [SqlServerFact]
    public async Task ContractorWrite_RequiresPermissionEvenForDirectCalls()
    {
        using var reader = await Login("reader.test");
        await Csrf(reader);
        var value = NewContractor();
        Assert.Equal(HttpStatusCode.Forbidden, (await reader.PostAsJsonAsync(ContractorsPath(), value)).StatusCode);
        using (var scope = application!.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<SystemDbContext>().CompanyAccesses.Where(x => x.UserId == readerId)
                .ExecuteUpdateAsync(x => x.SetProperty(a => a.Permissions, (CompanyPermission)3));
        Assert.Equal(HttpStatusCode.OK, (await reader.GetAsync(ContractorsPath())).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await reader.PostAsJsonAsync(ContractorsPath(), value)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await reader.PutAsJsonAsync($"{ContractorsPath()}/{contractorId}", value)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await reader.PostAsJsonAsync(ContractorsPath(companyB), value)).StatusCode);
        using var anonymous = Client();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsJsonAsync(ContractorsPath(), value)).StatusCode);
        await using var company = Company(false, companyA);
        Assert.Empty(await company.ContractorChanges.ToListAsync());
    }

    [SqlServerFact]
    public async Task ContractorWrite_RequiresAuthenticatedCsrfAndRejectsAlternativeCompanyId()
    {
        using var op = await Login("operator.test");
        var value = NewContractor();
        Assert.Equal(HttpStatusCode.BadRequest, (await op.PostAsJsonAsync(ContractorsPath(), value)).StatusCode); // Token anonimowy.
        await Csrf(op);
        Assert.Equal(HttpStatusCode.BadRequest, (await op.PostAsJsonAsync(ContractorsPath() + "?companyId=" + companyB, value)).StatusCode);
        op.DefaultRequestHeaders.Add("X-Company-Id", companyB.ToString());
        Assert.Equal(HttpStatusCode.BadRequest, (await op.PostAsJsonAsync(ContractorsPath(), value)).StatusCode);
        op.DefaultRequestHeaders.Remove("X-Company-Id");
        Assert.Equal(HttpStatusCode.BadRequest, (await op.PostAsJsonAsync(ContractorsPath(), new
        { value.Id, value.Code, value.Name, value.OperationId, value.CountryCode, value.IsActive, companyId = companyB })).StatusCode);
        var row = (await Read(op, contractorId))!;
        op.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        Assert.Equal(HttpStatusCode.BadRequest, (await op.PutAsJsonAsync($"{ContractorsPath()}/{contractorId}", Edit(row, "Fikcyjna zmiana"))).StatusCode);
        await using var company = Company(false, companyA);
        Assert.Empty(await company.ContractorChanges.ToListAsync());
    }

    [SqlServerFact]
    public async Task ContractorValidation_DuplicateCodesAndVersionsGiveBusinessErrors()
    {
        using var op = await Login("operator.test");
        await Csrf(op);
        foreach (var value in new[] { NewContractor(""), NewContractor() with { Name = " ", CountryCode = "P1", Code = new string('x', 65) } })
        {
            using var response = await op.PostAsJsonAsync(ContractorsPath(), value);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Contains("validation_failed", await response.Content.ReadAsStringAsync());
        }
        using var duplicate = await op.PostAsJsonAsync(ContractorsPath(), NewContractor("same"));
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.Contains("contractor_code_exists", await duplicate.Content.ReadAsStringAsync());
        var row = (await Read(op, contractorId))!;
        foreach (var version in new string?[] { null, "malformed", Convert.ToBase64String([1, 2]) })
            Assert.Equal(HttpStatusCode.BadRequest, (await op.PutAsJsonAsync($"{ContractorsPath()}/{contractorId}", Edit(row, "Test") with { RowVersion = version })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await op.PutAsJsonAsync($"{ContractorsPath()}/{int.MaxValue}", Edit(row, "Test"))).StatusCode);
        var unknown = int.MaxValue;
        Assert.Equal(HttpStatusCode.NotFound, (await op.PutAsJsonAsync($"{ContractorsPath()}/{unknown}", Edit(row, "Test") with { Id = unknown })).StatusCode);
        await using var company = Company(false, companyA);
        Assert.Empty(await company.ContractorChanges.ToListAsync());
    }

    [SqlServerFact]
    public async Task TwoUsers_StaleVersionCannotOverwriteAndExplicitResolutionCanSave()
    {
        await GrantReaderEdit();
        using var first = await Login("operator.test");
        using var second = await Login("reader.test");
        await Csrf(first);
        await Csrf(second);
        var original = (await Read(first, contractorId))!;
        var firstChange = Edit(original, "Zmiana pierwszej osoby");
        Assert.Equal(HttpStatusCode.OK, (await first.PutAsJsonAsync($"{ContractorsPath()}/{contractorId}", firstChange)).StatusCode);
        var secondChange = Edit(original, "Zmiana drugiej osoby");
        using var conflict = await second.PutAsJsonAsync($"{ContractorsPath()}/{contractorId}", secondChange);
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        var payload = await conflict.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("contractor_conflict", payload.GetProperty("code").GetString());
        var current = payload.GetProperty("current").Deserialize<ContractorResponse>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        Assert.Equal(firstChange.Name, current.Name);
        await using var company = Company(false, companyA);
        Assert.Single(await company.ContractorChanges.ToListAsync());
        Assert.Equal(HttpStatusCode.OK, (await second.PutAsJsonAsync($"{ContractorsPath()}/{contractorId}", secondChange with { RowVersion = current.RowVersion })).StatusCode);
        Assert.Equal(secondChange.Name, (await Read(second, contractorId))!.Name);
        var history = await company.ContractorChanges.OrderBy(x => x.CreatedAtUtc).ToListAsync();
        Assert.Equal(2, history.Count);
        Assert.Equal(operatorId, history[0].ActorId);
        Assert.Equal(readerId, history[1].ActorId);
        Assert.Equal("Zmiana pierwszej osoby", JsonDocument.Parse(history[1].BeforeJson!).RootElement.GetProperty("Name").GetString());
    }

    [SqlServerFact]
    public async Task SimultaneousWrites_OnlyOneVersionAndAuditCommit()
    {
        using var first = await Login("operator.test");
        using var second = await Login("operator.test");
        await Csrf(first);
        await Csrf(second);
        var original = (await Read(first, contractorId))!;
        var responses = await Task.WhenAll(first.PutAsJsonAsync($"{ContractorsPath()}/{contractorId}", Edit(original, "Fikcyjna A")),
            second.PutAsJsonAsync($"{ContractorsPath()}/{contractorId}", Edit(original, "Fikcyjna B")));
        Assert.Single(responses, x => x.StatusCode == HttpStatusCode.OK);
        Assert.Single(responses, x => x.StatusCode == HttpStatusCode.Conflict);
        foreach (var response in responses) response.Dispose();
        await using var company = Company(false, companyA);
        var audit = Assert.Single(await company.ContractorChanges.ToListAsync());
        var saved = await company.Contractors.SingleAsync();
        Assert.Equal(saved.Name, JsonDocument.Parse(audit.AfterJson).RootElement.GetProperty("Name").GetString());
    }

    [SqlServerFact]
    public async Task FailedAudit_RollsBackCreateAndUpdate()
    {
        using var op = await Login("operator.test");
        await Csrf(op);
        var original = (await Read(op, contractorId))!;
        await using var company = Company(false, companyA);
        await company.Database.ExecuteSqlRawAsync("ALTER TABLE audit.ContractorChanges ADD CONSTRAINT CK_Audit_TestFailure CHECK ([ActorName] <> N'operator.test');");
        try
        {
            var value = NewContractor("ROLLBACK");
            Assert.Equal(HttpStatusCode.ServiceUnavailable, (await op.PostAsJsonAsync(ContractorsPath(), value)).StatusCode);
            Assert.Equal(HttpStatusCode.ServiceUnavailable, (await op.PutAsJsonAsync($"{ContractorsPath()}/{contractorId}", Edit(original, "Nie wolno zapisać"))).StatusCode);
            Assert.False(await company.Contractors.AnyAsync(x => x.Code == value.Code.Trim().ToUpper()));
            var current = (await Read(op, contractorId))!;
            Assert.Equal(original.Name, current.Name);
            Assert.Equal(original.RowVersion, current.RowVersion);
            Assert.Empty(await company.ContractorChanges.ToListAsync());
        }
        finally { await company.Database.ExecuteSqlRawAsync("ALTER TABLE audit.ContractorChanges DROP CONSTRAINT CK_Audit_TestFailure;"); }
    }

    [SqlServerFact]
    public async Task AuditSurvivesUserDeletionAndRejectsTrackedMutation()
    {
        using var op = await Login("operator.test");
        await Csrf(op);
        Assert.Equal(HttpStatusCode.Created, (await op.PostAsJsonAsync(ContractorsPath(), NewContractor())).StatusCode);
        using (var scope = application!.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<SystemDbContext>().Users.Where(x => x.Id == operatorId).ExecuteDeleteAsync();
        await using var company = Company(false, companyA);
        var audit = await company.ContractorChanges.SingleAsync();
        Assert.Equal(operatorId, audit.ActorId);
        Assert.Equal("operator.test", audit.ActorName);
        company.ContractorChanges.Remove(audit);
        await Assert.ThrowsAsync<InvalidOperationException>(() => company.SaveChangesAsync());
        company.ChangeTracker.Clear();
        var retained = await company.ContractorChanges.SingleAsync();
        company.Entry(retained).Property(x => x.ActorName).CurrentValue = "Nielegalna zmiana";
        await Assert.ThrowsAsync<InvalidOperationException>(() => company.SaveChangesAsync());
        company.ChangeTracker.Clear();
        Assert.Equal("operator.test", (await company.ContractorChanges.SingleAsync()).ActorName);
        await using var foreign = Company(false, companyB);
        Assert.Empty(await foreign.ContractorChanges.ToListAsync());
    }

    [SqlServerFact]
    public async Task ContractorSearchAndPaging_AreFilteredOnServer()
    {
        await using (var company = Company(false, companyA))
        {
            for (var i = 0; i < 65; i++)
            {
                var row = new Contractor($"PAGE{i:000}", $"Fikcyjna pozycja {i}", i % 2 == 0 ? "PL" : "DE", $"TEST-{i:000}");
                row.Update(row.Code, row.Name, row.CountryCode, i % 3 != 0);
                company.Contractors.Add(row);
            }
            await company.SaveChangesAsync();
        }
        using var op = await Login("operator.test");
        var page1 = (await op.GetFromJsonAsync<ContractorResponse[]>(ContractorsPath() + "?q=PAGE&limit=50"))!;
        var page2 = (await op.GetFromJsonAsync<ContractorResponse[]>(ContractorsPath() + "?q=PAGE&offset=50&limit=50"))!;
        Assert.Equal(50, page1.Length);
        Assert.Equal(15, page2.Length);
        Assert.Empty(page1.Select(x => x.Id).Intersect(page2.Select(x => x.Id)));
        var filtered = (await op.GetFromJsonAsync<ContractorResponse[]>(ContractorsPath() + "?q=TEST-0&country=de&active=false"))!;
        Assert.NotEmpty(filtered);
        Assert.All(filtered, x => { Assert.False(x.IsActive); Assert.Equal("DE", x.CountryCode); });
        Assert.Empty((await op.GetFromJsonAsync<ContractorResponse[]>(ContractorsPath() + "?q=%25"))!);
        Assert.Equal(HttpStatusCode.BadRequest, (await op.GetAsync(ContractorsPath() + "?limit=101")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await op.GetAsync(ContractorsPath() + "?offset=-1")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await op.GetAsync(ContractorsPath() + "?country=P1")).StatusCode);
        Assert.Single((await op.GetFromJsonAsync<ContractorResponse[]>(ContractorsPath(companyB)))!);
    }

    [SqlServerFact]
    public async Task ResolverMismatch_PreventsWritesToForeignPhysicalDatabase()
    {
        using var op = await Login("operator.test");
        await Csrf(op);
        using (var scope = application!.Services.CreateScope())
        {
            var system = scope.ServiceProvider.GetRequiredService<SystemDbContext>();
            await system.CompanyDatabases.Where(x => x.CompanyId == companyB).ExecuteDeleteAsync();
            await system.CompanyDatabases.Where(x => x.CompanyId == companyA).ExecuteUpdateAsync(x => x.SetProperty(a => a.DatabaseName, "Vatowiec_Company_B_Dev"));
        }
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await op.PostAsJsonAsync(ContractorsPath(), NewContractor())).StatusCode);
        await using var b = Company(true, companyB);
        Assert.Single(await b.Contractors.ToListAsync());
        Assert.Empty(await b.ContractorChanges.ToListAsync());
    }
}
