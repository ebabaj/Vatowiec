using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
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
public sealed class ContractorFinanceSqlTests
{
    private static readonly Guid A = Guid.Parse("aaaaaaaa-0000-4000-8000-000000000001");
    private static readonly Guid B = Guid.Parse("bbbbbbbb-0000-4000-8000-000000000002");
    [PreservedVatSqlFact]
    public async Task ApiFinanceDirectionsCurrenciesPermissionsVersionsIdempotenceAndAtomicAudit_AllRolledBack()
    {
        await using var central = await PreservedSystemTransaction.Open();
        await using var first = await PreservedSystemTransaction.Open("Vatowiec_Company_A_Dev");
        await using var second = await PreservedSystemTransaction.Open("Vatowiec_Company_B_Dev");
        var failure = new FailAudit(); var factory = new Contexts(first, second, failure);
        using var app = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development"); builder.ConfigureLogging(log => log.ClearProviders());
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<SystemDbContext>(); services.AddScoped(_ => central.Context());
                services.RemoveAll<ICompanyDatabaseContextFactory>(); services.AddSingleton<ICompanyDatabaseContextFactory>(factory);
                services.AddDataProtection().UseEphemeralDataProtectionProvider();
                services.AddScoped(_ => ProductionRegistryTests.Settings("Mock"));
            });
        });
        using var scope = app.Services.CreateScope(); var system = scope.ServiceProvider.GetRequiredService<SystemDbContext>();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var password = "Fixture!a1-" + Guid.NewGuid().ToString("N");
        var owner = new ApplicationUser { Id = Guid.NewGuid(), UserName = "fixture-finance-owner-" + Guid.NewGuid().ToString("N") };
        var reader = new ApplicationUser { Id = Guid.NewGuid(), UserName = "fixture-finance-reader-" + Guid.NewGuid().ToString("N") };
        Assert.True((await users.CreateAsync(owner, password)).Succeeded); Assert.True((await users.CreateAsync(reader, password)).Succeeded);
        foreach (var company in new[] { A, B }) system.CompanyAccesses.Add(new(owner.Id, company, CompanyPermission.ViewContractors | CompanyPermission.EditContractors));
        system.CompanyAccesses.Add(new(reader.Id, A, CompanyPermission.ViewContractors)); await system.SaveChangesAsync();
        using var db = factory.Create("", A); using var dbB = factory.Create("", B);
        // Normalize mutable lookup availability only in the outer rollback fixture.
        await db.Currencies.Where(x => x.Code == "EUR" || x.Code == "PLN").ExecuteUpdateAsync(s => s.SetProperty(x => x.IsActive, true));
        await db.PaymentMethods.Where(x => x.Code == "TRANSFER" || x.Code == "CASH").ExecuteUpdateAsync(s => s.SetProperty(x => x.IsActive, true));
        var code = "fixture-finance-" + Guid.NewGuid().ToString("N");
        var contractor = new Contractor(code, "Fikcyjny kontrahent finansowy", "PL"); db.Contractors.Add(contractor); await db.SaveChangesAsync();
        var bContractor = new Contractor(code, "Fikcyjny kontrahent B", "PL"); dbB.Contractors.Add(bContractor); await dbB.SaveChangesAsync();
        var originalVersion = Convert.ToBase64String(contractor.RowVersion);
        using var admin = app.CreateClient(new() { BaseAddress = new("https://localhost"), AllowAutoRedirect = false });
        using var read = app.CreateClient(new() { BaseAddress = new("https://localhost"), AllowAutoRedirect = false });
        var root = $"/api/v1/companies/{A}"; var path = root + $"/contractors/{contractor.Id}";
        Assert.Equal(HttpStatusCode.Unauthorized, (await admin.GetAsync(path + "/trade-terms/Sale")).StatusCode);
        await GusConfigurationSqlTests.Login(admin, owner.UserName!, password); await GusConfigurationSqlTests.Login(read, reader.UserName!, password);
        var lookups = (await admin.GetFromJsonAsync<FinanceLookups>(root + "/financial-lookups"))!;
        Assert.True(lookups.PaymentMethods.Length >= 5); Assert.Contains(lookups.PaymentMethods, x => x.Code == "TRANSFER");
        Assert.Contains(lookups.Currencies, x => x.Code == "EUR");
        var empty = (await read.GetFromJsonAsync<TradeTermsResponse>(path + "/trade-terms/Sale"))!;
        Assert.Equal(0, empty.Id); Assert.Null(empty.Data.CurrencyCode); Assert.Null(empty.Data.PaymentDays); Assert.Null(empty.Data.DiscountPercent);
        var sale = new TradeTermsRequest(new(14, "TRANSFER", "EUR", 2.1250m, "Fikcyjne warunki sprzedaży"), OperationId: Guid.NewGuid());
        Assert.Equal(HttpStatusCode.Forbidden, (await read.PutAsJsonAsync(path + "/trade-terms/Sale", sale)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await read.PostAsJsonAsync(root + "/payment-methods", new PaymentMethodRequest("FIXTURE", "Test", true, OperationId: Guid.NewGuid()))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await read.PostAsJsonAsync(path + "/credit-limits", new CreditLimitRequest(0, "Sale", "EUR", 100m, true, OperationId: Guid.NewGuid()))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await read.GetAsync($"/api/v1/companies/{B}/financial-lookups")).StatusCode);
        admin.DefaultRequestHeaders.Remove("X-CSRF-TOKEN"); Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync(path + "/trade-terms/Sale", sale)).StatusCode); await GusConfigurationSqlTests.Csrf(admin);
        foreach (var invalid in new[] { sale.Data with { PaymentDays = -1 }, sale.Data with { PaymentDays = 3651 }, sale.Data with { DiscountPercent = 100.0001m }, sale.Data with { DiscountPercent = 0.00001m }, sale.Data with { CurrencyCode = "QQQ" }, sale.Data with { PaymentMethodCode = "UNKNOWN" } })
            Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync(path + "/trade-terms/Sale", sale with { Data = invalid })).StatusCode);
        using var savedSale = await admin.PutAsJsonAsync(path + "/trade-terms/Sale", sale); Assert.Equal(HttpStatusCode.Created, savedSale.StatusCode);
        var terms = (await savedSale.Content.ReadFromJsonAsync<TradeTermsResponse>())!; Assert.Equal(sale.Data, terms.Data); Assert.Equal(A, terms.CompanyId);
        using var replay = await admin.PutAsJsonAsync(path + "/trade-terms/Sale", sale); Assert.Equal(HttpStatusCode.OK, replay.StatusCode); Assert.Equal(terms.Id, (await replay.Content.ReadFromJsonAsync<TradeTermsResponse>())!.Id);
        Assert.Equal(1, await db.ContractorChanges.CountAsync(x => x.ContractorId == contractor.Id && x.RecordType == "ContractorTradeTerms"));
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PutAsJsonAsync(path + "/trade-terms/Sale", sale with { Data = new(15) })).StatusCode);
        var purchase = sale with { Data = new(30, "CASH", "PLN", 0m, null), OperationId = Guid.NewGuid() };
        Assert.Equal(HttpStatusCode.Created, (await admin.PutAsJsonAsync(path + "/trade-terms/Purchase", purchase)).StatusCode);
        Assert.Equal("EUR", (await admin.GetFromJsonAsync<TradeTermsResponse>(path + "/trade-terms/Sale"))!.Data.CurrencyCode);
        Assert.Equal(30, (await admin.GetFromJsonAsync<TradeTermsResponse>(path + "/trade-terms/Purchase"))!.Data.PaymentDays);
        Assert.Equal(HttpStatusCode.Created, (await admin.PutAsJsonAsync($"/api/v1/companies/{B}/contractors/{bContractor.Id}/trade-terms/Sale", new TradeTermsRequest(new(), OperationId: Guid.NewGuid()))).StatusCode);
        Assert.Null((await admin.GetFromJsonAsync<TradeTermsResponse>($"/api/v1/companies/{B}/contractors/{bContractor.Id}/trade-terms/Sale"))!.Data.CurrencyCode);
        var edit = new TradeTermsRequest(terms.Data with { PaymentDays = 21 }, terms.RowVersion);
        Assert.Equal(HttpStatusCode.OK, (await admin.PutAsJsonAsync(path + "/trade-terms/Sale", edit)).StatusCode);
        using var conflict = await admin.PutAsJsonAsync(path + "/trade-terms/Sale", edit);
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        var conflictBody = await conflict.Content.ReadFromJsonAsync<JsonElement>(); Assert.Equal(21, conflictBody.GetProperty("financeCurrent").GetProperty("terms").GetProperty("data").GetProperty("paymentDays").GetInt32());
        var currentTerms = (await admin.GetFromJsonAsync<TradeTermsResponse>(path + "/trade-terms/Sale"))!;
        Assert.Equal(HttpStatusCode.OK, (await admin.PutAsJsonAsync(path + "/trade-terms/Sale", new TradeTermsRequest(new(), currentTerms.RowVersion))).StatusCode);
        Assert.Null((await admin.GetFromJsonAsync<TradeTermsResponse>(path + "/trade-terms/Sale"))!.Data.CurrencyCode);
        var limits = new List<CreditLimitResponse>();
        foreach (var (direction, currency) in new[] { ("Sale", "PLN"), ("Sale", "EUR"), ("Purchase", "EUR") })
        {
            var value = new CreditLimitRequest(0, direction, currency, 12345.12345678m, true, OperationId: Guid.NewGuid());
            using var result = await admin.PostAsJsonAsync(path + "/credit-limits", value); Assert.Equal(HttpStatusCode.Created, result.StatusCode);
            var row = (await result.Content.ReadFromJsonAsync<CreditLimitResponse>())!; limits.Add(row); Assert.Equal(value.Amount, row.Amount);
            using var repeated = await admin.PostAsJsonAsync(path + "/credit-limits", value); Assert.Equal(HttpStatusCode.OK, repeated.StatusCode); Assert.Equal(row.Id, (await repeated.Content.ReadFromJsonAsync<CreditLimitResponse>())!.Id);
        }
        foreach (var invalid in new[] { new CreditLimitRequest(0, "Sale", "EUR", -1m, true, OperationId: Guid.NewGuid()), new CreditLimitRequest(0, "Sale", "QQQ", 10m, true, OperationId: Guid.NewGuid()), new CreditLimitRequest(0, "Sale", "EUR", .123456789m, true, OperationId: Guid.NewGuid()) })
            Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync(path + "/credit-limits", invalid)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsJsonAsync(path + "/credit-limits", new CreditLimitRequest(0, "Sale", "EUR", 20m, true, OperationId: Guid.NewGuid()))).StatusCode);
        var eur = limits.Single(x => x.CurrencyCode == "EUR" && x.Direction == "Sale");
        var deactivate = new CreditLimitRequest(eur.Id, eur.Direction, eur.CurrencyCode, eur.Amount, false, eur.RowVersion);
        Assert.Equal(HttpStatusCode.OK, (await admin.PutAsJsonAsync(path + "/credit-limits/" + eur.Id, deactivate)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PutAsJsonAsync(path + "/credit-limits/" + eur.Id, deactivate)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await admin.PostAsJsonAsync(path + "/credit-limits", new CreditLimitRequest(0, "Sale", "EUR", 200m, true, OperationId: Guid.NewGuid()))).StatusCode);
        Assert.Equal(3, (await admin.GetFromJsonAsync<CreditLimitResponse[]>(path + "/credit-limits?direction=Sale"))!.Length);
        Assert.Single((await admin.GetFromJsonAsync<CreditLimitResponse[]>(path + "/credit-limits?direction=Purchase"))!);
        var newMethod = new PaymentMethodRequest("FIXTURE_" + Guid.NewGuid().ToString("N")[..16].ToUpperInvariant(), "Fikcyjny sposób", true, OperationId: Guid.NewGuid());
        using var methodSaved = await admin.PostAsJsonAsync(root + "/payment-methods", newMethod); Assert.Equal(HttpStatusCode.Created, methodSaved.StatusCode);
        var method = (await methodSaved.Content.ReadFromJsonAsync<PaymentMethodResponse>())!;
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsJsonAsync(root + "/payment-methods", newMethod)).StatusCode);
        var beforeRename = method.Code;
        var rename = new PaymentMethodRequest(method.Code, "Fikcyjna nowa nazwa", false, method.RowVersion);
        using var renamed = await admin.PutAsJsonAsync(root + "/payment-methods/" + method.Code, rename); Assert.Equal(HttpStatusCode.OK, renamed.StatusCode);
        Assert.Equal(beforeRename, (await renamed.Content.ReadFromJsonAsync<PaymentMethodResponse>())!.Code);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PutAsJsonAsync(root + "/payment-methods/" + method.Code, rename)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync(root + "/payment-methods/" + method.Code, rename with { Code = "CHANGED" })).StatusCode);
        var transfer = lookups.PaymentMethods.Single(x => x.Code == "CASH");
        Assert.Equal(HttpStatusCode.OK, (await admin.PutAsJsonAsync(root + "/payment-methods/CASH", new PaymentMethodRequest("CASH", "Gotówka — po zmianie nazwy", false, transfer.RowVersion))).StatusCode);
        var purchaseTerms = (await admin.GetFromJsonAsync<TradeTermsResponse>(path + "/trade-terms/Purchase"))!;
        Assert.Equal(HttpStatusCode.OK, (await admin.PutAsJsonAsync(path + "/trade-terms/Purchase", new TradeTermsRequest(purchaseTerms.Data with { PaymentDays = 31 }, purchaseTerms.RowVersion))).StatusCode);
        currentTerms = (await admin.GetFromJsonAsync<TradeTermsResponse>(path + "/trade-terms/Sale"))!;
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync(path + "/trade-terms/Sale", new TradeTermsRequest(new(10, "CASH"), currentTerms.RowVersion))).StatusCode);
        failure.Next = true;
        using var failed = await admin.PutAsJsonAsync(path + "/trade-terms/Sale", new TradeTermsRequest(new(99), currentTerms.RowVersion)); Assert.Equal(HttpStatusCode.ServiceUnavailable, failed.StatusCode);
        Assert.Equal(currentTerms, await admin.GetFromJsonAsync<TradeTermsResponse>(path + "/trade-terms/Sale"));
        // The finance write never changes main data, its rowversion, or its audit type.
        Assert.Equal(originalVersion, (await admin.GetFromJsonAsync<ContractorResponse>(path))!.RowVersion);
        Assert.False(await db.ContractorChanges.AnyAsync(x => x.ContractorId == contractor.Id && x.RecordType == "Contractor"));
        var audit = await db.ContractorChanges.AsNoTracking().Where(x => x.ActorId == owner.Id).ToArrayAsync();
        Assert.Contains(audit, x => x.RecordType == "ContractorCreditLimit" && x.BeforeJson != null && x.AfterJson.Contains("\"IsActive\":false"));
        Assert.Contains(audit, x => x.RecordType == "PaymentMethod" && x.ContractorId == null);
        Assert.All(audit, x => { Assert.Equal(owner.UserName, x.ActorName); Assert.NotEqual(Guid.Empty, x.ActorIssuerId); Assert.Equal(3, x.SnapshotVersion); });
        // Physical FKs/constraints, executed in savepoints without disabling protection.
        foreach (var sql in new[] {
            $"UPDATE core.ContractorTradeTerms SET PaymentMethodCode='MISSING' WHERE Id={terms.Id}",
            $"UPDATE core.ContractorTradeTerms SET CurrencyCode='QQQ' WHERE Id={terms.Id}",
            $"UPDATE core.ContractorTradeTerms SET PaymentDays=3651 WHERE Id={terms.Id}",
            $"UPDATE core.ContractorTradeTerms SET DiscountPercent=101 WHERE Id={terms.Id}",
            "DELETE FROM ref.PaymentMethods WHERE Code='CASH'" })
        {
            await using var savepoint = await db.Database.BeginTransactionAsync();
            var error = await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlRawAsync(sql)); Assert.Equal(547, error.Number);
        }
    }
    [PreservedVatSqlFact]
    public async Task ConcurrentInsertsWaitForUniqueCurrencyKey_WithoutCommittingFixtures()
    {
        await using var verified = await PreservedSystemTransaction.Open("Vatowiec_Company_A_Dev");
        using var db = new CompanyDbContext(verified.CompanyOptions, new CompanyContext(A));
        var contractor = await db.Contractors.OrderBy(x => x.Id).Select(x => x.Id).FirstAsync();
        var currency = await db.Currencies.Where(x => !db.ContractorCreditLimits.Any(l => l.ContractorId == contractor && l.Direction == "Sale" && l.CurrencyCode == x.Code && l.IsActive)).OrderBy(x => x.Code).Select(x => x.Code).FirstAsync();
        const string connection = "Server=localhost\\SQLEXPRESS;Database=Vatowiec_Company_A_Dev;Integrated Security=True;Encrypt=True;TrustServerCertificate=True";
        await using var a = new SqlConnection(connection); await using var b = new SqlConnection(connection); await a.OpenAsync(); await b.OpenAsync();
        await using var one = (SqlTransaction)await a.BeginTransactionAsync(); await using var two = (SqlTransaction)await b.BeginTransactionAsync();
        using var first = Insert(a, one, contractor, currency); using var second = Insert(b, two, contractor, currency);
        await first.ExecuteNonQueryAsync();
        var competing = second.ExecuteNonQueryAsync();
        Assert.NotSame(competing, await Task.WhenAny(competing, Task.Delay(150)));
        await one.RollbackAsync(); Assert.Equal(1, await competing); await two.RollbackAsync();
    }
    private static SqlCommand Insert(SqlConnection connection, SqlTransaction transaction, int contractor, string currency)
    {
        var cmd = new SqlCommand("INSERT core.ContractorCreditLimits (ContractorId,Direction,CurrencyCode,Amount,IsActive,CreatedAtUtc,UpdatedAtUtc) VALUES (@contractor,'Sale',@currency,1,1,SYSUTCDATETIME(),SYSUTCDATETIME())", connection, transaction) { CommandTimeout = 5 };
        cmd.Parameters.AddWithValue("@contractor", contractor); cmd.Parameters.AddWithValue("@currency", currency); return cmd;
    }
    private sealed class Contexts(PreservedSystemTransaction a, PreservedSystemTransaction b, FailAudit failure) : ICompanyDatabaseContextFactory
    {
        public CompanyDbContext Create(string connectionString, Guid companyId) => new(new DbContextOptionsBuilder<CompanyDbContext>((companyId == A ? a : b).CompanyOptions).AddInterceptors(failure).Options, new CompanyContext(companyId));
    }
    private sealed class FailAudit : SaveChangesInterceptor
    {
        public bool Next { get; set; }
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (Next && eventData.Context!.ChangeTracker.Entries<ContractorChange>().Any(x => x.State == EntityState.Added)) { Next = false; throw new DbUpdateException("Synthetic finance audit failure"); }
            return ValueTask.FromResult(result);
        }
    }
}
