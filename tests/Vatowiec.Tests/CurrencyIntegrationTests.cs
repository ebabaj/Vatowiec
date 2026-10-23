using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Vatowiec.Core.Domain;
using Vatowiec.Core.Infrastructure.Persistence;
using Vatowiec.Core.Infrastructure.Registries;
using Vatowiec.Desktop.Services;
using Vatowiec.Desktop.ViewModels;
using Vatowiec.SharedContracts;

namespace Vatowiec.Tests;

public sealed partial class AuthenticationIntegrationTests
{
    [SqlServerFact]
    public async Task Nbp_IsoCodeIsPhysicalPrimaryKeyAndCannotBeChangedOrReplacedByNumericId()
    {
        await GrantRegistryRights((CompanyPermission)511); using var client = await DetailOperator();
        var rows = await ReadCurrencies(client); var eur = rows.Single(x => x.Code == "EUR");
        using var json = JsonDocument.Parse(await client.GetStringAsync(CurrencyPath()));
        Assert.All(json.RootElement.EnumerateArray(), x => Assert.False(x.TryGetProperty("id", out _)));
        var request = new CurrencyWriteRequest(Guid.NewGuid(), eur.RowVersion, true, true, false, false);
        foreach (var invalid in new[] { "9", "eur", "EURO" })
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync(CurrencyPath() + "/" + invalid, request)).StatusCode);
        await using var db = Company(false, companyA);
        await db.Database.OpenConnectionAsync();
        using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = "SELECT c.name FROM sys.indexes i JOIN sys.index_columns ic ON ic.object_id=i.object_id AND ic.index_id=i.index_id JOIN sys.columns c ON c.object_id=ic.object_id AND c.column_id=ic.column_id WHERE i.object_id=OBJECT_ID('ref.Currencies') AND i.is_primary_key=1";
        Assert.Equal("Code", await command.ExecuteScalarAsync());
        command.CommandText = "SELECT COUNT(*) FROM sys.columns WHERE (object_id=OBJECT_ID('ref.Currencies') AND name='Id') OR (object_id=OBJECT_ID('core.ExchangeRates') AND name='CurrencyId')";
        Assert.Equal(0, (int)(await command.ExecuteScalarAsync())!);
        await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlRawAsync("UPDATE ref.Currencies SET Code='ZZZ' WHERE Code='EUR';"));
        await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlRawAsync("INSERT INTO ref.Currencies(Code,Name,IsActive,DownloadA,DownloadB,DownloadC) VALUES('eur',N'bad',1,0,0,0);"));
        foreach (var invalid in new[] { "ZZZ", "eur" })
            await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO core.ExchangeRates(CurrencyCode,Source,TableType,TableNumber,EffectiveDate,Mid,RetrievedAtUtc) VALUES({invalid},'NBP','A','001/A/NBP/2026','2026-01-02',4,SYSUTCDATETIME());"));
        await ImportNbp(client);
        Assert.Equal("EUR", (await db.ExchangeRates.SingleAsync(x => x.CurrencyCode == "EUR")).CurrencyCode);
    }

    private readonly NbpFixture nbpFixture = new();
    private sealed class NbpFixture : INbpClient
    {
        public ConcurrentQueue<(string Table, DateOnly? From, DateOnly? To)> Calls { get; } = new();
        public decimal Mid { get; set; } = 4.123456789012m;
        public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;
        public bool OmitTradingDate { get; set; }
        public Task<NbpReply> Fetch(string table, DateOnly? from, DateOnly? to, CancellationToken ct)
        {
            Calls.Enqueue((table, from, to));
            var json = NbpTests.TableJson(table, Mid, from);
            if (OmitTradingDate) json = json.Replace("\"tradingDate\":\"2026-10-20\",", "");
            var handler = new DataPortTests.Stub(_ => new(Status) { Content = new StringContent(json) });
            return new NbpClient(new DataPortTests.FreshFactory(handler)).Fetch(table, from, to, ct);
        }
    }
    private string CurrencyPath(Guid? company = null) => $"/api/v1/companies/{company ?? companyA}/currencies";
    private string RatePath(Guid? company = null) => $"/api/v1/companies/{company ?? companyA}/exchange-rates";
    private async Task<CurrencyResponse[]> ReadCurrencies(HttpClient client) => (await client.GetFromJsonAsync<CurrencyResponse[]>(CurrencyPath()))!;
    private async Task<CurrencyResponse> SetCurrency(HttpClient client, CurrencyResponse row, bool a, bool b = false, bool c = false, bool active = true)
    {
        using var response = await client.PutAsJsonAsync(CurrencyPath() + "/" + row.Code, new CurrencyWriteRequest(Guid.NewGuid(), row.RowVersion, active, a, b, c));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode); return (await response.Content.ReadFromJsonAsync<CurrencyResponse>())!;
    }
    private async Task<RateImportResponse> ImportNbp(HttpClient client, RateImportRequest? request = null)
    {
        using var response = await client.PostAsJsonAsync(RatePath() + "/imports", request ?? new(Guid.NewGuid()));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode); return (await response.Content.ReadFromJsonAsync<RateImportResponse>())!;
    }
    [SqlServerFact]
    public async Task Nbp_DefaultsImportOnlyConfiguredCurrencies_DeduplicatesAndReplaysWithoutHttp()
    {
        await GrantRegistryRights((CompanyPermission)511); using var client = await DetailOperator();
        var rows = await ReadCurrencies(client); Assert.Equal(CurrencySeed.Rows().Length, rows.Length); Assert.Empty(nbpFixture.Calls);
        var eur = rows.Single(x => x.Code == "EUR");
        // Leave only EUR in A; the complete HTTP table still contains four currencies.
        foreach (var row in rows.Where(x => x.DownloadA && x.Code != "EUR")) await SetCurrency(client, row, false);
        var request = new RateImportRequest(Guid.NewGuid());
        var first = await ImportNbp(client, request); Assert.Equal(1, first.Added); Assert.Equal(1, first.Tables); Assert.Empty(first.Errors);
        var replay = await ImportNbp(client, request); Assert.Equal(first.AuditId, replay.AuditId); Assert.Single(nbpFixture.Calls);
        var repeated = await ImportNbp(client); Assert.Equal(0, repeated.Added); Assert.Equal(1, repeated.Skipped); Assert.Empty(repeated.Conflicts);
        var page = (await client.GetFromJsonAsync<RatePage>(RatePath()))!;
        Assert.Equal("EUR", Assert.Single(page.Items).CurrencyCode); Assert.Equal(4.123456789012m, page.Items[0].Mid);
        Assert.Equal(first.LastA, page.LastA); Assert.Null(page.LastB); Assert.Null(page.LastC);
        await using var db = Company(false, companyA); Assert.Equal(2, await db.CurrencyOperations.CountAsync(x => x.Kind == "Import"));
        Assert.Single(await db.ExchangeRates.ToListAsync()); Assert.Equal(eur.Code, (await db.ExchangeRates.SingleAsync()).CurrencyCode);
    }
    [SqlServerFact]
    public async Task Nbp_ConfigurationUsesCasPlnGuardAndAtomicActorAudit()
    {
        await GrantRegistryRights((CompanyPermission)511); using var client = await DetailOperator();
        var eur = (await ReadCurrencies(client)).Single(x => x.Code == "EUR");
        var value = new CurrencyWriteRequest(Guid.NewGuid(), eur.RowVersion, true, true, false, true);
        var result = await client.PutAsJsonAsync(CurrencyPath() + "/" + eur.Code, value); Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        var changed = (await result.Content.ReadFromJsonAsync<CurrencyResponse>())!; Assert.NotEqual(eur.RowVersion, changed.RowVersion);
        var replay = await client.PutAsJsonAsync(CurrencyPath() + "/" + eur.Code, value);
        Assert.Equal(changed, (await replay.Content.ReadFromJsonAsync<CurrencyResponse>())!);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PutAsJsonAsync(CurrencyPath() + "/" + eur.Code, value with { OperationId = Guid.NewGuid() })).StatusCode);
        var pln = (await ReadCurrencies(client)).Single(x => x.Code == "PLN");
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync(CurrencyPath() + "/" + pln.Code, value with { OperationId = Guid.NewGuid(), RowVersion = pln.RowVersion })).StatusCode);
        await using var db = Company(false, companyA); var audit = await db.CurrencyOperations.SingleAsync();
        Assert.Equal(operatorId, audit.ActorId); Assert.NotEqual(Guid.Empty, audit.ActorIssuerId); Assert.Equal("operator.test", audit.ActorName);
        Assert.Contains("before", audit.DetailsJson); Assert.Contains("after", audit.DetailsJson); Assert.Empty(nbpFixture.Calls);
        await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlRawAsync("DELETE FROM audit.CurrencyOperations;"));
        audit.DetailsJson = "tamper"; await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
    }
    [SqlServerFact]
    public async Task Nbp_ConflictsNeverOverwriteWithoutConfirmedAuditedResolutionAndStaleProposalFails()
    {
        await GrantRegistryRights((CompanyPermission)511); using var client = await DetailOperator();
        var first = await ImportNbp(client); Assert.Equal(4, first.Added);
        nbpFixture.Mid = 4.5m;
        var conflict = await ImportNbp(client); Assert.Equal(4, conflict.Conflicts.Length); Assert.Equal(0, conflict.Added);
        var item = conflict.Conflicts[0]; var url = RatePath() + $"/imports/{conflict.AuditId}/resolve";
        clock.Advance(TimeSpan.FromMinutes(1));
        var request = new RateResolutionRequest(Guid.NewGuid(), 0, true, false);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(url, request)).StatusCode);
        await using var db = Company(false, companyA); Assert.All(await db.ExchangeRates.ToListAsync(), x => Assert.Equal(4.123456789012m, x.Mid));
        request = request with { Confirm = true };
        var accepted = await client.PostAsJsonAsync(url, request); Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        var response = (await accepted.Content.ReadFromJsonAsync<CurrencyHistoryResponse>())!;
        var replay = await client.PostAsJsonAsync(url, request); Assert.Equal(response.Id, (await replay.Content.ReadFromJsonAsync<CurrencyHistoryResponse>())!.Id);
        Assert.Equal(4.5m, (await db.ExchangeRates.AsNoTracking().SingleAsync(x => x.Id == item.RateId)).Mid);
        Assert.Equal(item.RetrievedAtUtc, (await db.ExchangeRates.AsNoTracking().SingleAsync(x => x.Id == item.RateId)).RetrievedAtUtc);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync(url, request with { OperationId = Guid.NewGuid() })).StatusCode);
        var kept = await client.PostAsJsonAsync(url, new RateResolutionRequest(Guid.NewGuid(), 1, false, true)); Assert.Equal(HttpStatusCode.OK, kept.StatusCode);
        Assert.Equal(4.123456789012m, (await db.ExchangeRates.AsNoTracking().SingleAsync(x => x.Id == conflict.Conflicts[1].RateId)).Mid);
        Assert.Equal(2, await db.CurrencyOperations.CountAsync(x => x.Kind == "ResolveConflict"));
        Assert.Contains("4.123456789012", (await db.CurrencyOperations.SingleAsync(x => x.Id == response.Id)).DetailsJson);
    }
    [SqlServerFact]
    public async Task Nbp_BAndCDateSemanticsMissingCurrencyAnd93DayChunksArePreserved()
    {
        await GrantRegistryRights((CompanyPermission)511); using var client = await DetailOperator();
        var currencies = await ReadCurrencies(client);
        foreach (var row in currencies.Where(x => x.DownloadA)) await SetCurrency(client, row, false);
        currencies = await ReadCurrencies(client);
        await SetCurrency(client, currencies.Single(x => x.Code == "EUR"), false, false, true);
        await SetCurrency(client, currencies.Single(x => x.Code == "AFN"), false, true);
        await SetCurrency(client, currencies.Single(x => x.Code == "JPY"), false, true); // absent in B mock, never invent a rate.
        var from = new DateOnly(2026, 1, 2); var to = from.AddDays(93);
        var result = await ImportNbp(client, new(Guid.NewGuid(), from, to));
        Assert.Equal(4, result.Tables); Assert.Equal(4, result.Added); Assert.Contains(result.Notices, x => x.Contains("JPY"));
        Assert.Equal(4, nbpFixture.Calls.Count); Assert.All(nbpFixture.Calls, x => Assert.True(x.To!.Value.DayNumber - x.From!.Value.DayNumber <= 92));
        await using var db = Company(false, companyA); var all = await db.ExchangeRates.ToListAsync();
        Assert.All(all.Where(x => x.TableType == "C"), x => { Assert.Null(x.Mid); Assert.Equal(x.EffectiveDate.AddDays(-1), x.TradingDate); Assert.NotNull(x.Bid); Assert.NotNull(x.Ask); });
        Assert.All(all.Where(x => x.TableType == "B"), x => { Assert.NotNull(x.Mid); Assert.Null(x.TradingDate); });
        Assert.Equal(new[] { from, from.AddDays(93) }, all.Select(x => x.EffectiveDate).Distinct().Order());
        var reader = new ExchangeRateReader(db);
        Assert.NotNull(await reader.OnDate("EUR", "C", from)); Assert.Null(await reader.Before("EUR", "C", from));
        Assert.Equal(from, (await reader.Before("EUR", "C", from.AddDays(93)))!.EffectiveDate);
        Assert.Null(await reader.OnDate("PLN", "A", from));
    }
    [SqlServerFact]
    public async Task Nbp_NoPublicationIsSuccessfulButOutageIsAnAuditedErrorAndDatesValidateBeforeHttp()
    {
        await GrantRegistryRights((CompanyPermission)511); using var client = await DetailOperator(); nbpFixture.Status = HttpStatusCode.NotFound;
        var result = await ImportNbp(client, new(Guid.NewGuid(), new(2026, 10, 18), new(2026, 10, 18)));
        Assert.Equal(0, result.Added); Assert.Empty(result.Errors); Assert.NotEmpty(result.Notices); Assert.Equal(0, result.Tables);
        nbpFixture.Status = HttpStatusCode.InternalServerError; var failed = await ImportNbp(client); Assert.Single(failed.Errors); Assert.Empty(failed.Notices);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(RatePath() + "/imports", new RateImportRequest(Guid.NewGuid(), new(2026, 2, 1), new(2026, 1, 1)))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(RatePath() + "/imports", new RateImportRequest(Guid.NewGuid(), new(2026, 1, 1), null))).StatusCode);
        Assert.Equal(2, nbpFixture.Calls.Count);
        await using var db = Company(false, companyA); Assert.Empty(await db.ExchangeRates.ToListAsync()); Assert.Equal(2, await db.CurrencyOperations.CountAsync());
    }
    [SqlServerFact]
    public async Task Nbp_AuditFailureRollsBackRatesAndConfiguration()
    {
        await GrantRegistryRights((CompanyPermission)511); using var client = await DetailOperator();
        await using var db = Company(false, companyA);
        var eur = (await ReadCurrencies(client)).Single(x => x.Code == "EUR");
        await db.Database.ExecuteSqlRawAsync("ALTER TABLE audit.CurrencyOperations WITH NOCHECK ADD CONSTRAINT CK_Test_CurrencyAuditFail CHECK (Kind='Impossible');");
        try
        {
            Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.PostAsJsonAsync(RatePath() + "/imports", new RateImportRequest(Guid.NewGuid()))).StatusCode);
            Assert.Empty(await db.ExchangeRates.ToListAsync()); Assert.Empty(await db.CurrencyOperations.ToListAsync());
            Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.PutAsJsonAsync(CurrencyPath() + "/" + eur.Code, new CurrencyWriteRequest(Guid.NewGuid(), eur.RowVersion, true, false, true, false))).StatusCode);
            Assert.True((await db.Currencies.AsNoTracking().SingleAsync(x => x.Code == eur.Code)).DownloadA);
        }
        finally { await db.Database.ExecuteSqlRawAsync("ALTER TABLE audit.CurrencyOperations DROP CONSTRAINT CK_Test_CurrencyAuditFail;"); }
    }
    [SqlServerFact]
    public async Task Nbp_PermissionsCsrfCompanyIsolationAndPhysicalSqlConstraintsAreEnforced()
    {
        using var client = await DetailOperator(); Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(CurrencyPath())).StatusCode);
        await GrantRegistryRights((CompanyPermission)511);
        using (var scope = application!.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<SystemDbContext>().CompanyAccesses.Where(x => x.UserId == operatorId && x.CompanyId == companyB)
                .ExecuteUpdateAsync(x => x.SetProperty(p => p.Permissions, (CompanyPermission)67));
        var eur = (await ReadCurrencies(client)).Single(x => x.Code == "EUR");
        client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(RatePath() + "/imports", new RateImportRequest(Guid.NewGuid()))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync(CurrencyPath() + "/" + eur.Code, new CurrencyWriteRequest(Guid.NewGuid(), eur.RowVersion, true, true, false, true))).StatusCode);
        await Csrf(client); await ImportNbp(client);
        Assert.Empty((await client.GetFromJsonAsync<RatePage>(RatePath(companyB)))!.Items);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync(RatePath(companyB) + "/imports", new RateImportRequest(Guid.NewGuid()))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PutAsJsonAsync(CurrencyPath(companyB) + "/" + eur.Code, new CurrencyWriteRequest(Guid.NewGuid(), eur.RowVersion, true, true, false, false))).StatusCode);
        using var reader = await Login("reader.test"); await Csrf(reader);
        Assert.Equal(HttpStatusCode.Forbidden, (await reader.PostAsJsonAsync(RatePath() + "/imports", new RateImportRequest(Guid.NewGuid()))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync(RatePath() + "?companyId=" + companyB)).StatusCode);
        await using var db = Company(false, companyA); var rate = await db.ExchangeRates.AsNoTracking().FirstAsync();
        await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlRawAsync("INSERT INTO core.ExchangeRates(CurrencyCode,Source,TableType,TableNumber,EffectiveDate,Mid,RetrievedAtUtc) SELECT CurrencyCode,Source,TableType,TableNumber,EffectiveDate,Mid,RetrievedAtUtc FROM core.ExchangeRates;"));
        await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlRawAsync("UPDATE ref.Currencies SET DownloadA=1 WHERE Code='PLN';"));
        await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlRawAsync("UPDATE core.ExchangeRates SET Bid=1 WHERE TableType='A';"));
        Assert.Equal(4.123456789012m, rate.Mid);
    }
    [SqlServerFact]
    public async Task Nbp_ConcurrentSameOperationCommitsOneImportAndOneAudit()
    {
        await GrantRegistryRights((CompanyPermission)511); using var client = await DetailOperator();
        var value = new RateImportRequest(Guid.NewGuid());
        var replies = await Task.WhenAll(client.PostAsJsonAsync(RatePath() + "/imports", value), client.PostAsJsonAsync(RatePath() + "/imports", value));
        Assert.All(replies, x => Assert.Equal(HttpStatusCode.OK, x.StatusCode));
        var a = (await replies[0].Content.ReadFromJsonAsync<RateImportResponse>())!;
        var b = (await replies[1].Content.ReadFromJsonAsync<RateImportResponse>())!;
        Assert.Equal(a.AuditId, b.AuditId);
        foreach (var reply in replies) reply.Dispose();
        await using var db = Company(false, companyA); Assert.Equal(4, await db.ExchangeRates.CountAsync()); Assert.Single(await db.CurrencyOperations.ToListAsync());
    }
    [SqlServerFact]
    public async Task Nbp_CWithoutTradingDatePersistsNullableDateAndBidAsk()
    {
        await GrantRegistryRights((CompanyPermission)511); using var client = await DetailOperator();
        var eur = (await ReadCurrencies(client)).Single(x => x.Code == "EUR"); await SetCurrency(client, eur, true, false, true);
        nbpFixture.OmitTradingDate = true; var result = await ImportNbp(client); Assert.Empty(result.Errors); Assert.Equal(5, result.Added);
        await using var db = Company(false, companyA); var row = await db.ExchangeRates.SingleAsync(x => x.TableType == "C");
        Assert.Null(row.TradingDate); Assert.Null(row.Mid); Assert.Equal(4.100000000001m, row.Bid);
    }
    [SqlServerFact]
    public async Task Nbp_DesktopConfigImportFilterConflictAndReadOnlyCompanyWorkThroughRealApi()
    {
        await GrantRegistryRights((CompanyPermission)511);
        using var model = new MainViewModel(_ => new VatowiecApiClient(Client())) { UserName = "operator.test" };
        await model.Login(password); model.SelectedCompany = model.Companies.Single(x => x.CompanyId == companyA); await model.OpenCompany();
        await model.OpenCurrencies(true); Assert.True(model.IsCurrencySettings, model.Message); Assert.Empty(nbpFixture.Calls);
        model.CurrencyEditor!.DownloadC = true; await model.SaveCurrencySettings(); Assert.Empty(nbpFixture.Calls); Assert.True(model.CurrencyEditor.DownloadC, model.Message);
        await model.ImportNbpRates(); Assert.Equal(5, model.RateRows.Length); Assert.Contains("nowe kursy: 5", model.RateImportSummary);
        model.ShowCurrencySettings(false); model.RateCurrencyFilter = model.CurrencyRows.Single(x => x.Code == "EUR"); await model.LoadRates(); Assert.Equal(2, model.RateRows.Length);
        nbpFixture.Mid = 4.5m; await model.ImportNbpRates(); Assert.Equal(4, model.RateConflicts.Length);
        model.SelectedRateConflict = model.RateConflicts[0]; await model.ResolveNbpConflict(true); Assert.Contains("potwierdzenie", model.Message);
        model.ConfirmRateResolution = true; await model.ResolveNbpConflict(true); Assert.Equal(3, model.RateConflicts.Length);
        model.CloseCurrencies(); Assert.False(model.IsCurrencyScreen); Assert.Empty(model.RateRows);
        await model.ChangeCompany(); Assert.Empty(model.CurrencyRows); Assert.Empty(model.RateConflicts);
    }
}
