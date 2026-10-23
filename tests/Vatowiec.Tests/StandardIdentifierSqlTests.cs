using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Vatowiec.Core.Application;
using Vatowiec.Core.Domain;
using Vatowiec.Core.Infrastructure.Identity;
using Vatowiec.Core.Infrastructure.Persistence;
using Vatowiec.Core.Infrastructure.Registries;
using Vatowiec.SharedContracts;

namespace Vatowiec.Tests;

[Collection("LocalSql")]
[Trait("Category", "SqlIntegration")]
public sealed class StandardIdentifierSqlTests
{
    private static readonly Guid A = Guid.Parse("aaaaaaaa-0000-4000-8000-000000000001");
    private static readonly Guid B = Guid.Parse("bbbbbbbb-0000-4000-8000-000000000002");
    private const string Pesel = "00000000017"; // Synthetic checksum fixture, impossible birth date; no person.

    [PreservedVatSqlFact]
    public async Task ApiIdentifiersPermissionsWarningsConflictsAuditIsolationAndRollback_NoReset()
    {
        await using var central = await PreservedSystemTransaction.Open();
        await using var first = await PreservedSystemTransaction.Open("Vatowiec_Company_A_Dev");
        await using var second = await PreservedSystemTransaction.Open("Vatowiec_Company_B_Dev");
        var failure = new FailAudit();
        var contexts = new FixtureContexts(first, second, failure);
        using var app = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development"); builder.ConfigureLogging(log => log.ClearProviders());
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<SystemDbContext>(); services.AddScoped(_ => central.Context());
                services.RemoveAll<ICompanyDatabaseContextFactory>(); services.AddSingleton<ICompanyDatabaseContextFactory>(contexts);
                services.AddDataProtection().UseEphemeralDataProtectionProvider();
                services.AddScoped(_ => ProductionRegistryTests.Settings("Mock"));
            });
        });
        using var scope = app.Services.CreateScope(); var system = scope.ServiceProvider.GetRequiredService<SystemDbContext>();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var password = "Fixture!a1-" + Guid.NewGuid().ToString("N");
        var owner = new ApplicationUser { Id = Guid.NewGuid(), UserName = "fixture-identifiers-owner-" + Guid.NewGuid().ToString("N") };
        var ordinary = new ApplicationUser { Id = Guid.NewGuid(), UserName = "fixture-identifiers-editor-" + Guid.NewGuid().ToString("N") };
        Assert.True((await users.CreateAsync(owner, password)).Succeeded); Assert.True((await users.CreateAsync(ordinary, password)).Succeeded);
        var protectedAccess = new CompanyAccess(owner.Id, A, (CompanyPermission)1023);
        system.CompanyAccesses.Add(protectedAccess);
        system.CompanyAccesses.Add(new(owner.Id, B, (CompanyPermission)1023));
        var access = new CompanyAccess(ordinary.Id, A, (CompanyPermission)511); system.CompanyAccesses.Add(access);
        await system.SaveChangesAsync();
        using var admin = app.CreateClient(new() { BaseAddress = new("https://localhost"), AllowAutoRedirect = false });
        using var editor = app.CreateClient(new() { BaseAddress = new("https://localhost"), AllowAutoRedirect = false });
        await GusConfigurationSqlTests.Login(admin, owner.UserName!, password); await GusConfigurationSqlTests.Login(editor, ordinary.UserName!, password);
        var route = $"/api/v1/companies/{A}/contractors";
        var draft = new ContractorWriteRequest(0, "fixture-standard-" + Guid.NewGuid().ToString("N"), "Fikcyjny test identyfikatorów", "PL", true,
            OperationId: Guid.NewGuid(), Regon: "000.000-023", Krs: "0000000023", Bdo: "000000023", Pesel: Pesel);
        using (var denied = await editor.PostAsJsonAsync(route, draft))
        { Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode); Assert.DoesNotContain(Pesel, await denied.Content.ReadAsStringAsync()); }
        Assert.Equal(HttpStatusCode.Forbidden, (await editor.PostAsJsonAsync(route + "/identifiers/validate", draft)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await editor.PostAsJsonAsync(route, draft with { AcceptWarning = true, Pesel = "" })).StatusCode);
        admin.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync(route, draft)).StatusCode);
        await GusConfigurationSqlTests.Csrf(admin);
        using var created = await admin.PostAsJsonAsync(route, draft); Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var row = (await created.Content.ReadFromJsonAsync<ContractorResponse>())!;
        Assert.Equal("000000023", row.Regon); Assert.Equal(draft.Krs, row.Krs); Assert.Equal(Pesel, row.Pesel); Assert.True(row.Id > 0);
        using var db = contexts.Create("", A);
        Assert.Equal(1, await db.ContractorChanges.CountAsync(x => x.ContractorId == row.Id));
        protectedAccess.ChangePermissions((CompanyPermission)511); await system.SaveChangesAsync();
        using (var revokedReplay = await admin.PostAsJsonAsync(route, draft))
        { Assert.Equal(HttpStatusCode.Forbidden, revokedReplay.StatusCode); Assert.DoesNotContain(Pesel, await revokedReplay.Content.ReadAsStringAsync()); }
        protectedAccess.ChangePermissions((CompanyPermission)1023); await system.SaveChangesAsync();
        var audit = await db.ContractorChanges.SingleAsync(x => x.ContractorId == row.Id);
        Assert.Equal(Pesel, JsonDocument.Parse(audit.AfterJson).RootElement.GetProperty("Pesel").GetString());
        Assert.Equal(owner.Id, audit.ActorId); Assert.NotEqual(Guid.Empty, audit.ActorIssuerId); Assert.Equal(owner.UserName, audit.ActorName);
        Assert.Equal(3, audit.SnapshotVersion);
        using var replay = await admin.PostAsJsonAsync(route, draft);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode); Assert.Equal(row.Id, (await replay.Content.ReadFromJsonAsync<ContractorResponse>())!.Id);
        Assert.Equal(1, await db.ContractorChanges.CountAsync(x => x.ContractorId == row.Id));
        foreach (var uri in new[] { route, route + "/" + row.Id })
        { var text = await editor.GetStringAsync(uri); Assert.DoesNotContain(Pesel, text); Assert.DoesNotContain("\"pesel\"", text); }
        Assert.Equal(HttpStatusCode.Forbidden, (await editor.GetAsync($"/api/v1/companies/{B}/contractors/{row.Id}")).StatusCode);
        var toEdit = draft with { Id = row.Id, OperationId = null, RowVersion = row.RowVersion, Pesel = null, Name = "Fikcyjna zmiana bez dostępu do PESEL" };
        using var saved = await editor.PutAsJsonAsync(route + "/" + row.Id, toEdit); Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        Assert.DoesNotContain(Pesel, await saved.Content.ReadAsStringAsync());
        var updated = (await admin.GetFromJsonAsync<ContractorResponse>(route + "/" + row.Id))!;
        Assert.Equal(Pesel, updated.Pesel); Assert.Equal(toEdit.Name, updated.Name);
        using var conflict = await editor.PutAsJsonAsync(route + "/" + row.Id, toEdit);
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode); Assert.DoesNotContain(Pesel, await conflict.Content.ReadAsStringAsync());
        var found = (await (await admin.PostAsJsonAsync(route + "/search", new ContractorSearchRequest(Pesel))).Content.ReadFromJsonAsync<ContractorResponse[]>())!;
        Assert.Contains(found, x => x.Id == row.Id);
        var hidden = (await (await editor.PostAsJsonAsync(route + "/search", new ContractorSearchRequest(Pesel))).Content.ReadFromJsonAsync<ContractorResponse[]>())!;
        Assert.DoesNotContain(hidden, x => x.Id == row.Id);
        foreach (var number in new[] { draft.Regon, draft.Krs, draft.Bdo })
        {
            var matches = (await (await editor.PostAsJsonAsync(route + "/search", new ContractorSearchRequest(number))).Content.ReadFromJsonAsync<ContractorResponse[]>())!;
            Assert.Contains(matches, x => x.Id == row.Id);
        }
        var invalid = draft with { Code = "fixture-warning-" + Guid.NewGuid().ToString("N"), OperationId = Guid.NewGuid(), Regon = "000000024", Pesel = null };
        using var warning = await editor.PostAsJsonAsync(route, invalid); Assert.Equal((HttpStatusCode)422, warning.StatusCode);
        var preview = (await editor.PostAsJsonAsync(route + "/identifiers/validate", invalid)).Content;
        var assessment = (await preview.ReadFromJsonAsync<IdentifierPreview>())!;
        Assert.True(assessment.NeedsWarning); Assert.Contains(assessment.Checks, x => x.Field == "Regon" && x.Status == "Invalid");
        Assert.Equal((HttpStatusCode)422, (await editor.PostAsJsonAsync(route, invalid with { AcceptWarning = true, WarningHash = "stale-rule-version" })).StatusCode);
        access.ChangePermissions(CompanyPermission.ViewContractors | CompanyPermission.EditContractors); await system.SaveChangesAsync();
        Assert.Equal(HttpStatusCode.Forbidden, (await editor.PostAsJsonAsync(route, invalid with { AcceptWarning = true, WarningHash = assessment.WarningHash })).StatusCode);
        access.ChangePermissions((CompanyPermission)511); await system.SaveChangesAsync();
        using var accepted = await editor.PostAsJsonAsync(route, invalid with { AcceptWarning = true, WarningHash = assessment.WarningHash });
        Assert.Equal(HttpStatusCode.Created, accepted.StatusCode);
        var acceptedRow = (await accepted.Content.ReadFromJsonAsync<ContractorResponse>())!;
        var warningAudit = await db.ContractorChanges.SingleAsync(x => x.ContractorId == acceptedRow.Id);
        Assert.Contains(StandardIdentifiers.Version, warningAudit.WarningJson); Assert.Contains("000000024", warningAudit.WarningJson);
        // Fail the audit after the first SaveChanges has already inserted the contractor.
        var count = await db.Contractors.CountAsync(); failure.Next = true;
        using var failed = await editor.PostAsJsonAsync(route, invalid with { Regon = null, Code = "fixture-failed-" + Guid.NewGuid().ToString("N"), OperationId = Guid.NewGuid() });
        Assert.Equal(HttpStatusCode.ServiceUnavailable, failed.StatusCode); Assert.Equal(count, await db.Contractors.CountAsync());
        Assert.False(await db.Contractors.AnyAsync(x => x.Code.StartsWith("fixture-failed-")));
        // Changing another field preserves the protected value; its history remains complete.
        var history = await db.ContractorChanges.Where(x => x.ContractorId == row.Id).ToArrayAsync();
        Assert.Equal(2, history.Length);
        var updateAudit = Assert.Single(history, x => x.BeforeJson != null);
        Assert.Contains(Pesel, updateAudit.BeforeJson); Assert.Contains(Pesel, updateAudit.AfterJson);
        var bRoute = $"/api/v1/companies/{B}/contractors";
        using var inB = await admin.PostAsJsonAsync(bRoute, draft with { Pesel = null }); Assert.Equal(HttpStatusCode.Created, inB.StatusCode);
        var bRow = (await inB.Content.ReadFromJsonAsync<ContractorResponse>())!; Assert.Equal(B, bRow.CompanyId); Assert.Null(bRow.Pesel);
        // Clear PESEL explicitly by an authorized editor, without changing ParentId.
        using var cleared = await admin.PutAsJsonAsync(route + "/" + row.Id, toEdit with { RowVersion = updated.RowVersion, Pesel = null });
        Assert.Equal(HttpStatusCode.OK, cleared.StatusCode);
        Assert.Null((await admin.GetFromJsonAsync<ContractorResponse>(route + "/" + row.Id))!.Pesel);
        access.ChangePermissions(CompanyPermission.ViewContractors); await system.SaveChangesAsync();
        Assert.Equal(HttpStatusCode.Forbidden, (await editor.PostAsJsonAsync(route, invalid)).StatusCode);
        // A durable operation saved by the previous release still replays after an additive upgrade.
        var legacyRequest = draft with { Code = "fixture-legacy-" + Guid.NewGuid().ToString("N"), OperationId = Guid.NewGuid(), Regon = null, Krs = null, Bdo = null, Pesel = null };
        var legacyRow = new Contractor(legacyRequest.Code, legacyRequest.Name, "PL"); db.Contractors.Add(legacyRow); await db.SaveChangesAsync();
        var legacySnapshot = JsonSerializer.Serialize(new { legacyRow.Code, legacyRow.Name, legacyRow.TaxId, legacyRow.VatEuId, legacyRow.KsefIdWew, legacyRow.ParentId, legacyRow.CountryCode, legacyRow.IsActive });
        var legacyResponse = new ContractorResponse(legacyRow.Id, A, legacyRow.Code, legacyRow.Name, "PL", RowVersion: Convert.ToBase64String(legacyRow.RowVersion));
        db.ContractorChanges.Add(new(legacyRow.Id, owner.Id, audit.ActorIssuerId, owner.UserName!, DateTimeOffset.UtcNow, "legacy-fixture", null, legacySnapshot));
        db.ContractorCreateOperations.Add(new(legacyRequest.OperationId!.Value, owner.Id, audit.ActorIssuerId,
            Vatowiec.Api.Security.IdentifierChecks.Hash(legacySnapshot), legacyRow.Id, JsonSerializer.Serialize(legacyResponse, new JsonSerializerOptions(JsonSerializerDefaults.Web))));
        await db.SaveChangesAsync();
        using var oldReplay = await admin.PostAsJsonAsync(route, legacyRequest); Assert.Equal(HttpStatusCode.OK, oldReplay.StatusCode);
        Assert.Equal(legacyRow.Id, (await oldReplay.Content.ReadFromJsonAsync<ContractorResponse>())!.Id);
        Assert.Equal(1, await db.ContractorChanges.CountAsync(x => x.ContractorId == legacyRow.Id));
    }

    [PreservedVatSqlFact]
    public async Task PhysicalColumnsAreNullableTextInBothCompanies()
    {
        foreach (var database in new[] { "Vatowiec_Company_A_Dev", "Vatowiec_Company_B_Dev" })
        {
            await using var transaction = await PreservedSystemTransaction.Open(database);
            await using var company = new CompanyDbContext(transaction.CompanyOptions, new CompanyContext(database.Contains("_A_", StringComparison.Ordinal) ? A : B));
            var columns = await company.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS Value FROM sys.columns WHERE object_id=OBJECT_ID('core.Contractors') AND name IN ('Regon','Krs','Bdo','Pesel') AND is_nullable=1 AND TYPE_NAME(user_type_id)='nvarchar' AND max_length=200").ToArrayAsync();
            Assert.Equal(4, Assert.Single(columns));
        }
    }
    private sealed class FixtureContexts(PreservedSystemTransaction a, PreservedSystemTransaction b, FailAudit failure) : ICompanyDatabaseContextFactory
    {
        public CompanyDbContext Create(string connectionString, Guid companyId) => new(
            new DbContextOptionsBuilder<CompanyDbContext>((companyId == A ? a : b).CompanyOptions).AddInterceptors(failure).Options, new CompanyContext(companyId));
    }
    private sealed class FailAudit : SaveChangesInterceptor
    {
        public bool Next { get; set; }
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (Next && eventData.Context!.ChangeTracker.Entries<ContractorChange>().Any(x => x.State == EntityState.Added))
            { Next = false; throw new DbUpdateException("Synthetic audit failure; no sensitive data"); }
            return ValueTask.FromResult(result);
        }
    }
}
