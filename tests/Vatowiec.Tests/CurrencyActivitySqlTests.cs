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

// Independent of the destructive SQL fixtures. Existing data and all test writes
// stay inside real outer transactions; each endpoint uses a rollback savepoint.
[Collection("LocalSql")]
[Trait("Category", "SqlIntegration")]
public sealed class CurrencyActivitySqlTests
{
    private static readonly Guid A = Guid.Parse("aaaaaaaa-0000-4000-8000-000000000001");
    private static readonly Guid B = Guid.Parse("bbbbbbbb-0000-4000-8000-000000000002");

    [PreservedVatSqlFact]
    public async Task BulkActivity_ApiPermissionsCasRollbackReplayAuditAndAllCurrencies_WithoutPersistentChanges()
    {
        await using var central = await PreservedSystemTransaction.Open();
        await using var first = await PreservedSystemTransaction.Open("Vatowiec_Company_A_Dev");
        await using var second = await PreservedSystemTransaction.Open("Vatowiec_Company_B_Dev");
        var failure = new FailSave(); var contexts = new Contexts(first, second, failure); var nbp = new NoNbp();
        using var app = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development"); builder.ConfigureLogging(x => x.ClearProviders());
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<SystemDbContext>(); services.AddScoped(_ => central.Context());
                services.RemoveAll<ICompanyDatabaseContextFactory>(); services.AddSingleton<ICompanyDatabaseContextFactory>(contexts);
                services.RemoveAll<INbpClient>(); services.AddSingleton<INbpClient>(nbp);
                services.AddDataProtection().UseEphemeralDataProtectionProvider();
                services.AddScoped(_ => ProductionRegistryTests.Settings("Mock"));
            });
        });
        using var scope = app.Services.CreateScope(); var system = scope.ServiceProvider.GetRequiredService<SystemDbContext>();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var password = "Fixture!a1-" + Guid.NewGuid().ToString("N");
        var owner = new ApplicationUser { Id = Guid.NewGuid(), UserName = "fixture-currencies-" + Guid.NewGuid().ToString("N") };
        var reader = new ApplicationUser { Id = Guid.NewGuid(), UserName = "fixture-currencies-reader-" + Guid.NewGuid().ToString("N") };
        Assert.True((await users.CreateAsync(owner, password)).Succeeded); Assert.True((await users.CreateAsync(reader, password)).Succeeded);
        system.CompanyAccesses.Add(new(owner.Id, A, CompanyPermission.ViewProfile | CompanyPermission.ViewExchangeRates | CompanyPermission.ManageCurrencies));
        system.CompanyAccesses.Add(new(owner.Id, B, CompanyPermission.ViewProfile | CompanyPermission.ViewExchangeRates));
        system.CompanyAccesses.Add(new(reader.Id, A, CompanyPermission.ViewProfile | CompanyPermission.ViewExchangeRates));
        await system.SaveChangesAsync();
        using var db = contexts.Create("", A); using var dbB = contexts.Create("", B);
        var bBefore = JsonSerializer.Serialize(await dbB.Currencies.AsNoTracking().OrderBy(x => x.Code).ToArrayAsync());
        var ratesBefore = JsonSerializer.Serialize(await db.ExchangeRates.AsNoTracking().OrderBy(x => x.Id).ToArrayAsync());
        // A known baseline only inside the outer rollback; no seed, deletes or DDL.
        await db.Currencies.Where(x => x.Code == "EUR" || x.Code == "USD").ExecuteUpdateAsync(s => s
            .SetProperty(x => x.IsActive, true).SetProperty(x => x.DownloadA, true).SetProperty(x => x.DownloadB, true).SetProperty(x => x.DownloadC, true));
        var path = $"/api/v1/companies/{A}/currencies";
        using var admin = app.CreateClient(new() { BaseAddress = new("https://localhost"), AllowAutoRedirect = false });
        using var read = app.CreateClient(new() { BaseAddress = new("https://localhost"), AllowAutoRedirect = false });
        async Task<CurrencyResponse[]> Rows() => (await admin.GetFromJsonAsync<CurrencyResponse[]>(path))!;
        static CurrencyActivityRequest Request(CurrencyResponse[] rows, bool active, bool all = false) =>
            new(Guid.NewGuid(), active, all, rows.Select(x => new CurrencyVersion(x.Code, x.RowVersion)).ToArray(), true);
        async Task<CurrencyActivityResponse> Save(CurrencyActivityRequest request)
        {
            using var response = await admin.PostAsJsonAsync(path + "/activity", request);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return (await response.Content.ReadFromJsonAsync<CurrencyActivityResponse>())!;
        }
        Assert.Equal(HttpStatusCode.Unauthorized, (await admin.PostAsJsonAsync(path + "/activity", Request([], false))).StatusCode);
        await GusConfigurationSqlTests.Login(admin, owner.UserName!, password);
        await GusConfigurationSqlTests.Login(read, reader.UserName!, password);
        var rows = await Rows(); var pair = rows.Where(x => x.Code is "EUR" or "USD").ToArray();
        var request = Request(pair, false);
        Assert.Equal(HttpStatusCode.Forbidden, (await read.PostAsJsonAsync(path + "/activity", request)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.PostAsJsonAsync($"/api/v1/companies/{B}/currencies/activity", request)).StatusCode);
        admin.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync(path + "/activity", request)).StatusCode);
        await GusConfigurationSqlTests.Csrf(admin);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync(path + "/activity", request with { Confirm = false })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync(path + "/activity", request with { Currencies = [request.Currencies[0], request.Currencies[0]] })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync(path + "/activity", request with { OperationId = Guid.Empty })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsJsonAsync(path + "/activity", request with { All = true })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync(path + "/activity", new { request.OperationId, request.Currencies, request.IsActive, request.All, request.Confirm, DownloadA = true })).StatusCode);
        var baselineAudits = await db.CurrencyOperations.CountAsync();
        var unchanged = await Save(Request(pair, true)); Assert.Empty(unchanged.Changed);
        Assert.Equal(JsonSerializer.Serialize(pair), JsonSerializer.Serialize((await Rows()).Where(x => x.Code is "EUR" or "USD").ToArray()));
        baselineAudits++;
        var changed = await Save(request); Assert.Equal(2, changed.Changed.Length); Assert.Empty(changed.Skipped);
        Assert.All(changed.Changed, x => { Assert.False(x.IsActive); Assert.False(x.DownloadA || x.DownloadB || x.DownloadC); Assert.Equal(A, x.CompanyId); });
        var replay = await Save(request); Assert.Equal(JsonSerializer.Serialize(changed), JsonSerializer.Serialize(replay));
        Assert.Equal(baselineAudits + 1, await db.CurrencyOperations.CountAsync());
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsJsonAsync(path + "/activity", request with { IsActive = true })).StatusCode);
        var audit = await db.CurrencyOperations.AsNoTracking().SingleAsync(x => x.OperationId == request.OperationId);
        Assert.Equal(owner.Id, audit.ActorId); Assert.NotEqual(Guid.Empty, audit.ActorIssuerId); Assert.Equal(owner.UserName, audit.ActorName);
        var details = JsonSerializer.Deserialize<ExchangeRateService.ActivityAudit>(audit.DetailsJson, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        Assert.All(details.Changes, x => { Assert.True(x.Before.DownloadA && x.Before.DownloadB && x.Before.DownloadC); Assert.False(x.After.IsActive); });
        Assert.Equal("BulkActivity", audit.Kind);
        var active = await Save(Request(changed.Changed, true));
        Assert.Equal(2, active.Changed.Length); Assert.All(active.Changed, x => { Assert.True(x.IsActive); Assert.False(x.DownloadA || x.DownloadB || x.DownloadC); });
        var noOp = await Save(Request(active.Changed, true)); Assert.Empty(noOp.Changed); Assert.Equal(2, noOp.Skipped.Length);
        Assert.Equal(JsonSerializer.Serialize(active.Changed.OrderBy(x => x.Code)), JsonSerializer.Serialize((await Rows()).Where(x => x.Code is "EUR" or "USD").OrderBy(x => x.Code)));

        // One stale member aborts both; a stale no-op and PLN never get UPDATEd.
        var stale = Request(active.Changed, false) with { Currencies = [new("EUR", active.Changed.Single(x => x.Code == "EUR").RowVersion), new("USD", pair.Single(x => x.Code == "USD").RowVersion)] };
        var beforeConflict = JsonSerializer.Serialize(await Rows()); var beforeCount = await db.CurrencyOperations.CountAsync();
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsJsonAsync(path + "/activity", stale)).StatusCode);
        Assert.Equal(beforeConflict, JsonSerializer.Serialize(await Rows())); Assert.Equal(beforeCount, await db.CurrencyOperations.CountAsync());
        failure.Race = true;
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsJsonAsync(path + "/activity", Request(active.Changed, false))).StatusCode);
        // SQL rowversions may advance during rolled-back transactions; compare business state after this injected race.
        rows = await Rows(); Assert.All(rows.Where(x => x.Code is "EUR" or "USD"), x => Assert.True(x.IsActive));
        Assert.Equal(beforeCount, await db.CurrencyOperations.CountAsync());
        failure.Audit = true; beforeConflict = JsonSerializer.Serialize(rows);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await admin.PostAsJsonAsync(path + "/activity", Request(rows.Where(x => x.Code is "EUR" or "USD").ToArray(), false))).StatusCode);
        Assert.Equal(beforeConflict, JsonSerializer.Serialize(await Rows())); Assert.Equal(beforeCount, await db.CurrencyOperations.CountAsync());

        rows = await Rows(); var pln = rows.Single(x => x.Code == "PLN");
        var allOff = await Save(Request(rows, false, true));
        Assert.Contains("PLN", allOff.Skipped); Assert.DoesNotContain(allOff.Changed, x => x.Code == "PLN");
        rows = await Rows(); Assert.Equal(pln, rows.Single(x => x.Code == "PLN"));
        Assert.All(rows.Where(x => x.Code != "PLN"), x => { Assert.False(x.IsActive); Assert.False(x.DownloadA || x.DownloadB || x.DownloadC); });
        var allOn = await Save(Request(rows, true, true)); Assert.Equal(rows.Length - 1, allOn.Changed.Length);
        Assert.All(await Rows(), x => { Assert.True(x.IsActive); Assert.False(x.DownloadA || x.DownloadB || x.DownloadC); });
        Assert.Equal(ratesBefore, JsonSerializer.Serialize(await db.ExchangeRates.AsNoTracking().OrderBy(x => x.Id).ToArrayAsync()));
        Assert.Equal(bBefore, JsonSerializer.Serialize(await dbB.Currencies.AsNoTracking().OrderBy(x => x.Code).ToArrayAsync()));
        Assert.Equal(0, nbp.Calls);
    }

    private sealed class Contexts(PreservedSystemTransaction a, PreservedSystemTransaction b, FailSave failure) : ICompanyDatabaseContextFactory
    {
        public CompanyDbContext Create(string connectionString, Guid companyId) => new(new DbContextOptionsBuilder<CompanyDbContext>((companyId == A ? a : b).CompanyOptions)
            .AddInterceptors(failure).Options, new CompanyContext(companyId));
    }
    private sealed class NoNbp : INbpClient
    {
        public int Calls { get; private set; }
        public Task<NbpReply> Fetch(string table, DateOnly? from, DateOnly? to, CancellationToken ct)
        { Calls++; throw new InvalidOperationException("Activity must never call NBP"); }
    }
    private sealed class FailSave : SaveChangesInterceptor
    {
        public bool Audit { get; set; }
        public bool Race { get; set; }
        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken ct = default)
        {
            var db = eventData.Context!;
            if (Race && db.ChangeTracker.Entries<Currency>().Any(x => x.State == EntityState.Modified))
            {
                Race = false;
                await db.Database.ExecuteSqlRawAsync("UPDATE ref.Currencies SET IsActive=IsActive WHERE Code='USD'", ct);
            }
            if (Audit && db.ChangeTracker.Entries<CurrencyOperation>().Any(x => x.State == EntityState.Added))
            { Audit = false; throw new DbUpdateException("Synthetic currency audit failure"); }
            return result;
        }
    }
}
