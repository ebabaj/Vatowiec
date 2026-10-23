using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Vatowiec.Api.Security;
using Vatowiec.Core.Application;
using Vatowiec.Core.Infrastructure.Persistence;
using Vatowiec.SharedContracts;

namespace Vatowiec.Tests;

// Deliberately independent of the destructive M0 fixtures. Never seeds or clears data.
public sealed class PreservedVatSqlFactAttribute : FactAttribute
{
    public PreservedVatSqlFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("VATOWIEC_PRESERVED_VAT_SQL") != "1")
            Skip = "Osobne niedestrukcyjne testy po jawnej migracji reguł; VATOWIEC_PRESERVED_VAT_SQL=1.";
    }
}

[Collection("LocalSql")]
[Trait("Category", "SqlIntegration")]
public sealed class OfficialVatRulesSqlTests
{
    private static readonly Guid CompanyA = Guid.Parse("aaaaaaaa-0000-4000-8000-000000000001");
    private static readonly Guid CompanyB = Guid.Parse("bbbbbbbb-0000-4000-8000-000000000002");
    private static string Connection(string database) => $"Server=localhost\\SQLEXPRESS;Database={database};Integrated Security=True;Encrypt=True;TrustServerCertificate=True";
    private static SystemDbContext System() => new(new DbContextOptionsBuilder<SystemDbContext>().UseSqlServer(Connection("Vatowiec_System")).Options);
    private static CompanyDbContext Company(Guid id) => new(new DbContextOptionsBuilder<CompanyDbContext>()
        .UseSqlServer(Connection(id == CompanyA ? "Vatowiec_Company_A_Dev" : "Vatowiec_Company_B_Dev")).Options, new CompanyContext(id));

    private static async Task VerifyLocalTarget(DbContext db)
    {
        await using var connection = new SqlConnection(db.Database.GetConnectionString());
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT CASE WHEN CONVERT(nvarchar(128),SERVERPROPERTY('MachineName'))=@machine
                AND CONVERT(nvarchar(128),SERVERPROPERTY('InstanceName'))=N'SQLEXPRESS'
                AND CONVERT(int,SERVERPROPERTY('ProductMajorVersion'))=17
                AND CONVERT(nvarchar(128),SERVERPROPERTY('Edition')) LIKE N'Express%'
                AND CONVERT(nvarchar(128),CONNECTIONPROPERTY('auth_scheme')) IN (N'NTLM',N'KERBEROS')
                AND EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=0
                    AND name=N'Vatowiec.LocalTestPurpose'
                    AND CONVERT(nvarchar(256),value)=N'VATowiec M0 fictional company demonstration v1')
                THEN 1 ELSE 0 END
            """;
        command.Parameters.AddWithValue("@machine", Environment.MachineName);
        Assert.Equal(1, await command.ExecuteScalarAsync());
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
    }

    [PreservedVatSqlFact]
    public async Task Sql_StoresHistoryAndExactVersionedSeed_WithoutCompanyMigrations()
    {
        await using var system = System(); await VerifyLocalTarget(system);
        var stored = await system.TaxIdentifierSchemes.AsNoTracking().OrderBy(x => x.Id).ToArrayAsync();
        Assert.Equal(JsonSerializer.Serialize(TaxSchemeSeed.Rules().OrderBy(x => x.Id)), JsonSerializer.Serialize(stored));
        Assert.Equal(8, (await system.Database.GetAppliedMigrationsAsync()).Count());
        foreach (var id in new[] { CompanyA, CompanyB })
        {
            await using var company = Company(id); await VerifyLocalTarget(company);
            Assert.Equal(id, (await company.CompanyProfiles.SingleAsync()).CompanyId);
            Assert.Equal(7, (await company.Database.GetAppliedMigrationsAsync()).Count());
        }
    }

    [PreservedVatSqlFact]
    public async Task Sql_PolicyBoundaryChangesWarningHashAndPreservesForeignTaxIdAsUnknown()
    {
        await using var system = System(); await VerifyLocalTarget(system);
        await using var company = Company(CompanyA); await VerifyLocalTarget(company);
        var actor = await system.Users.OrderBy(x => x.Id).Select(x => x.Id).FirstAsync();
        var request = Draft("FI12345672") with { TaxId = "12345671", CountryCode = "FI" };
        var before = await new IdentifierChecks(system, OfficialVatRulesTests.Validator(), new DayClock(22))
            .Check(company, CompanyA, actor, request, default);
        var after = await new IdentifierChecks(system, OfficialVatRulesTests.Validator(), new DayClock(23))
            .Check(company, CompanyA, actor, request, default);
        Assert.True(before.NeedsWarning); Assert.True(after.NeedsWarning);
        Assert.NotEqual(before.WarningHash, after.WarningHash);
        Assert.Equal("Unknown", before.Checks.Single(x => x.Field == "VatEuId").Status);
        Assert.Equal("Invalid", after.Checks.Single(x => x.Field == "VatEuId").Status);
        var tax = after.Checks.Single(x => x.Field == "TaxId");
        Assert.Equal("Unknown", tax.Status); Assert.False(tax.FormatChecked); Assert.False(tax.ChecksumChecked);
    }

    [PreservedVatSqlFact]
    public async Task Api_NewRulesCsrfPermissionsCompanyIsolationAndStaleWarningHash_NoContractorWrites()
    {
        await using var system = System(); await VerifyLocalTarget(system);
        foreach (var id in new[] { CompanyA, CompanyB }) { await using var db = Company(id); await VerifyLocalTarget(db); }
        using var app = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureLogging(logging => logging.ClearProviders());
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:VatowiecSystem"] = Connection("Vatowiec_System"),
                ["CompanyConnections:LocalWindows"] = "Server=localhost\\SQLEXPRESS;Integrated Security=True;Encrypt=True;TrustServerCertificate=True",
                ["Registries:MF:Environment"] = "Mock",
                ["Registries:GUS:Environment"] = "Mock",
                ["Registries:VIES:Environment"] = "Mock"
            }));
            builder.ConfigureServices(services =>
            {
                services.AddSingleton<TimeProvider>(new DayClock(23));
                services.AddDataProtection().UseEphemeralDataProtectionProvider();
            });
        });
        using var op = app.CreateClient(new() { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
        using var reader = app.CreateClient(new() { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
        var config = new ConfigurationBuilder().AddUserSecrets("vatowiec-api-development").AddEnvironmentVariables().Build();
        await Login(op, config, "Operator");
        try
        {
            await Login(reader, config, "Reader");
            try
            {
                foreach (var id in new[] { CompanyA, CompanyB })
                {
                    await using var company = Company(id);
                    var counts = await Counts(company);
                    if (id == CompanyB)
                    {
                        // Existing demo policy: operator can read B but cannot edit it.
                        Assert.Equal(HttpStatusCode.OK, (await op.GetAsync(Path(id))).StatusCode);
                        Assert.Equal(HttpStatusCode.Forbidden, (await op.PostAsJsonAsync($"{Path(id)}/identifiers/validate", Draft("FI12345671"))).StatusCode);
                        Assert.Equal(HttpStatusCode.Forbidden, (await op.PostAsJsonAsync(Path(id), Draft("FI12345671"))).StatusCode);
                        Assert.Equal(counts, await Counts(company));
                        continue;
                    }
                    foreach (var (number, status, checksum) in new[] { (" fi 1234567-1 ", "Valid", true),
                                 ("FI12345672", "Invalid", true), ("HR69435151530", "Valid", true),
                                 ("HR69435151531", "Invalid", true), ("BE2123456789", "Invalid", false),
                                 ("BE1123456789", "Unknown", false), ("EL123456789", "Unknown", false), ("XI123456789", "Unknown", false) })
                    {
                        using var response = await op.PostAsJsonAsync($"{Path(id)}/identifiers/validate", Draft(number));
                        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                        var check = (await response.Content.ReadFromJsonAsync<IdentifierPreview>())!.Checks.Single(x => x.Field == "VatEuId");
                        Assert.Equal(status, check.Status); Assert.Equal(checksum, check.ChecksumChecked);
                    }
                    var request = Draft("FI12345672");
                    var actor = await system.Users.Where(x => x.UserName == config["DemoUsers:Operator:UserName"]).Select(x => x.Id).SingleAsync();
                    var oldPreview = await new IdentifierChecks(system, OfficialVatRulesTests.Validator(), new DayClock(22))
                        .Check(company, id, actor, request, default);
                    using var stale = await op.PostAsJsonAsync(Path(id), request with { AcceptWarning = true, WarningHash = oldPreview.WarningHash });
                    Assert.Equal(HttpStatusCode.UnprocessableEntity, stale.StatusCode);
                    var error = await stale.Content.ReadFromJsonAsync<JsonElement>();
                    Assert.Equal("identifiers_warning", error.GetProperty("code").GetString());
                    Assert.NotEqual(oldPreview.WarningHash, error.GetProperty("warningHash").GetString());
                    Assert.Equal(counts, await Counts(company)); // No contractor, audit, operation or registry writes.
                }
                foreach (var id in new[] { CompanyA, CompanyB })
                    Assert.Equal(HttpStatusCode.Forbidden, (await reader.PostAsJsonAsync($"{Path(id)}/identifiers/validate", Draft("FI12345671"))).StatusCode);
                op.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
                Assert.Equal(HttpStatusCode.BadRequest, (await op.PostAsJsonAsync($"{Path(CompanyA)}/identifiers/validate", Draft("FI12345671"))).StatusCode);
            }
            finally { await Logout(reader); }
        }
        finally { await Logout(op); }
    }

    private static ContractorWriteRequest Draft(string vat) => new(0, "VAT-TEST-" + Guid.NewGuid().ToString("N"),
        "Fikcyjny podgląd bez zapisu", "PL", true, OperationId: Guid.NewGuid(), VatEuId: vat);
    private static string Path(Guid id) => $"/api/v1/companies/{id}/contractors";
    private static async Task<(int, int, int, int)> Counts(CompanyDbContext company) =>
        (await company.Contractors.CountAsync(), await company.ContractorChanges.CountAsync(),
            await company.ContractorCreateOperations.CountAsync(), await company.ExternalVerifications.CountAsync());
    private static async Task Csrf(HttpClient client)
    {
        var token = (await client.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf")).GetProperty("token").GetString();
        client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN"); client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", token);
    }
    private static async Task Login(HttpClient client, IConfiguration config, string kind)
    {
        var user = config[$"DemoUsers:{kind}:UserName"]; var password = config[$"DemoUsers:{kind}:Password"];
        Assert.False(string.IsNullOrWhiteSpace(user), "Brak lokalnej nazwy konta demonstracyjnego.");
        Assert.False(string.IsNullOrWhiteSpace(password), "Brak lokalnego hasła; nie zapisuj go w repozytorium.");
        await Csrf(client);
        using var response = await client.PostAsJsonAsync("/api/v1/auth/login", new { userName = user, password });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await Csrf(client);
    }
    private static async Task Logout(HttpClient client)
    {
        await Csrf(client);
        using var response = await client.PostAsync("/api/v1/auth/logout", null);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }
    private sealed class DayClock(int day) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 10, day, 12, 0, 0, TimeSpan.Zero);
    }
}
