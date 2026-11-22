using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Vatowiec.Core.Application;
using Vatowiec.Core.Domain;
using Vatowiec.Core.Infrastructure.ExchangeRates;
using Vatowiec.Core.Infrastructure.Identity;
using Vatowiec.Core.Infrastructure.Persistence;
using Vatowiec.Core.Infrastructure.Registries;
using Vatowiec.Desktop.Services;
using Vatowiec.Desktop.ViewModels;
using Vatowiec.SharedContracts;

namespace Vatowiec.Tests;

// ADR-048: waluty bazy firmy (aktywność) i wspólne kursy NBP w System pobierane przez usługę Operations.
// NBP wyłącznie na atrapie HTTP; czas usługi kontrolowany (strefa Europe/Warsaw).
public sealed partial class AuthenticationIntegrationTests
{
    // Środa 18.11.2026, 13:30 czasu polskiego (CET = UTC+1).
    private static readonly DateTimeOffset Wednesday1330 = new(2026, 11, 18, 12, 30, 0, TimeSpan.Zero);
    private static DateTimeOffset Warsaw(int year, int month, int day, int hour, int minute = 0) =>
        new DateTimeOffset(year, month, day, hour, minute, 0, TimeSpan.FromHours(1)).ToUniversalTime();

    private readonly NbpFixture nbpFixture = new();
    private sealed class NbpFixture : INbpClient
    {
        public ConcurrentQueue<(string Table, DateOnly? From, DateOnly? To)> Calls { get; } = new();
        public decimal Mid { get; set; } = 4.123456789012m;
        public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;
        public DateOnly PublishFrom { get; set; } = new(2026, 11, 9);
        public DateOnly PublishTo { get; set; } = DateOnly.MaxValue;
        public bool IncludeUnknownCode { get; set; }
        public int? FailOnCall { get; set; }
        // Awaria NBP wstrzykiwana w wywołaniu FailOnCall; 404 = strona HTML serwera WWW (nie „Brak danych”).
        public HttpStatusCode FailStatus { get; set; } = HttpStatusCode.InternalServerError;
        public int FailTimes { get; set; } = 1;
        public Task<NbpReply> Fetch(string table, DateOnly? from, DateOnly? to, CancellationToken ct)
        {
            Calls.Enqueue((table, from, to));
            var failing = FailOnCall is { } first && Calls.Count >= first && Calls.Count < first + FailTimes;
            var status = failing ? FailStatus : Status;
            var days = PublicationDays(table, from!.Value, to!.Value).ToArray();
            if (days.Length == 0 && status == HttpStatusCode.OK) status = HttpStatusCode.NotFound;
            // Prawdziwy brak publikacji NBP: tekst „Brak danych” (nie strona HTML 404).
            var json = failing ? "<html><title>404 - File or directory not found.</title></html>" : status == HttpStatusCode.NotFound ? NbpTests.NoData : RangeJson(table, Mid, days, IncludeUnknownCode);
            var handler = new DataPortTests.Stub(_ => new(status) { Content = new StringContent(json) });
            return new NbpClient(new DataPortTests.FreshFactory(handler)).Fetch(table, from, to, ct);
        }
        public IEnumerable<DateOnly> PublicationDays(string table, DateOnly from, DateOnly to)
        {
            for (var day = from > PublishFrom ? from : PublishFrom; day <= to && day <= PublishTo; day = day.AddDays(1))
                if (day.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday) && (table != "B" || day.DayOfWeek == DayOfWeek.Wednesday))
                    yield return day;
        }
    }

    // Numer tabeli zależy wyłącznie od daty, więc zakładka ponownie pobiera identyczne publikacje.
    internal static string RangeJson(string table, decimal mid, IEnumerable<DateOnly> days, bool unknownCode = false) =>
        JsonSerializer.Serialize(days.Select(day => new
        {
            table,
            no = $"{day.DayOfYear:000}/{table}/NBP/{day.Year}",
            effectiveDate = day.ToString("yyyy-MM-dd"),
            tradingDate = table == "C" ? day.AddDays(-1).ToString("yyyy-MM-dd") : null,
            rates = table switch
            {
                "C" => new object[] { new { code = "EUR", currency = "euro", bid = 4.100000000001m, ask = 4.200000000002m } },
                "B" => [new { code = "AFN", currency = "afgani", mid }],
                _ => unknownCode
                    ? [new { code = "EUR", currency = "euro", mid }, new { code = "USD", currency = "dolar", mid }, new { code = "GBP", currency = "funt", mid }, new { code = "CHF", currency = "frank", mid }, new { code = "XYZ", currency = "nieznana", mid }]
                    : [new { code = "EUR", currency = "euro", mid }, new { code = "USD", currency = "dolar", mid }, new { code = "GBP", currency = "funt", mid }, new { code = "CHF", currency = "frank", mid }]
            }
        }).ToArray());

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private async Task<ExchangeRateRunResult> RunSync(DateTimeOffset now)
    {
        using var scope = application!.Services.CreateScope();
        var system = scope.ServiceProvider.GetRequiredService<SystemDbContext>();
        return await new ExchangeRateSyncService(system, nbpFixture, new FixedClock(now), new ExchangeRateScheduleOptions(RetryDelaySeconds: 0)).RunIfDue(default);
    }

    private async Task<T> InSystem<T>(Func<SystemDbContext, Task<T>> read)
    {
        using var scope = application!.Services.CreateScope();
        return await read(scope.ServiceProvider.GetRequiredService<SystemDbContext>());
    }

    private async Task<HttpClient> RateAdministrator()
    {
        using (var scope = application!.Services.CreateScope())
        {
            var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
            if (!await roles.RoleExistsAsync(InstallationRoles.Administrator))
                Assert.True((await roles.CreateAsync(new IdentityRole<Guid>(InstallationRoles.Administrator) { Id = Guid.NewGuid() })).Succeeded);
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = await users.FindByIdAsync(operatorId.ToString());
            if (!await users.IsInRoleAsync(user!, InstallationRoles.Administrator))
                Assert.True((await users.AddToRoleAsync(user!, InstallationRoles.Administrator)).Succeeded);
        }
        return await DetailOperator();
    }

    private const string AdminRates = "/api/v1/installation/exchange-rates";
    private string CurrencyPath(Guid? company = null) => $"/api/v1/companies/{company ?? companyA}/currencies";
    private string RatePath(Guid? company = null) => $"/api/v1/companies/{company ?? companyA}/exchange-rates";
    private async Task<CurrencyResponse[]> ReadCurrencies(HttpClient client) => (await client.GetFromJsonAsync<CurrencyResponse[]>(CurrencyPath()))!;

    [SqlServerFact]
    public async Task Currencies_IsoCodeIsPhysicalPrimaryKeyInCompanyAndSystemAndCannotBeChanged()
    {
        await GrantRegistryRights((CompanyPermission)255); using var client = await DetailOperator();
        var rows = await ReadCurrencies(client); var eur = rows.Single(x => x.Code == "EUR");
        Assert.Equal(CurrencySeed.Rows().Length, rows.Length);
        Assert.Equal(CurrencySeed.DefaultActive.Order(), rows.Where(x => x.IsActive).Select(x => x.Code).Order());
        using var json = JsonDocument.Parse(await client.GetStringAsync(CurrencyPath()));
        Assert.All(json.RootElement.EnumerateArray(), x => { Assert.False(x.TryGetProperty("id", out _)); Assert.False(x.TryGetProperty("downloadA", out _)); });
        var request = new CurrencyWriteRequest(Guid.NewGuid(), eur.RowVersion, true);
        foreach (var invalid in new[] { "9", "eur", "EURO" })
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync(CurrencyPath() + "/" + invalid, request)).StatusCode);
        await using var db = Company(false, companyA);
        await db.Database.OpenConnectionAsync();
        using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = "SELECT c.name FROM sys.indexes i JOIN sys.index_columns ic ON ic.object_id=i.object_id AND ic.index_id=i.index_id JOIN sys.columns c ON c.object_id=ic.object_id AND c.column_id=ic.column_id WHERE i.object_id=OBJECT_ID('ref.Currencies') AND i.is_primary_key=1";
        Assert.Equal("Code", await command.ExecuteScalarAsync());
        command.CommandText = "SELECT COUNT(*) FROM sys.columns WHERE object_id=OBJECT_ID('ref.Currencies') AND name IN ('Id','DownloadA','DownloadB','DownloadC')";
        Assert.Equal(0, (int)(await command.ExecuteScalarAsync())!);
        command.CommandText = "SELECT COUNT(*) FROM sys.tables WHERE object_id=OBJECT_ID('core.ExchangeRates')";
        Assert.Equal(0, (int)(await command.ExecuteScalarAsync())!);
        await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlRawAsync("UPDATE ref.Currencies SET Code='ZZZ' WHERE Code='EUR';"));
        await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlRawAsync("INSERT INTO ref.Currencies(Code,Name,IsActive) VALUES('eur',N'bad',1);"));
        await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlRawAsync("UPDATE ref.Currencies SET IsActive=0 WHERE Code='PLN';"));
        await InSystem(async system =>
        {
            Assert.Equal(CurrencySeed.ReferenceRows().Select(x => x.Code).Order(), (await system.Currencies.Select(x => x.Code).ToArrayAsync()).Order());
            await Assert.ThrowsAsync<SqlException>(() => system.Database.ExecuteSqlRawAsync("UPDATE ref.Currencies SET Code='ZZZ' WHERE Code='EUR';"));
            foreach (var invalid in new[] { "ZZZ", "eur" })
                await Assert.ThrowsAsync<SqlException>(() => system.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO ref.ExchangeRates(CurrencyCode,Source,TableType,TableNumber,EffectiveDate,Mid,RetrievedAtUtc) VALUES({invalid},'NBP','A','001/A/NBP/2026','2026-01-02',4,SYSUTCDATETIME());"));
            return 0;
        });
    }

    [SqlServerFact]
    public async Task Currencies_ActivityUsesCasPlnGuardAndAtomicActorAudit()
    {
        await GrantRegistryRights((CompanyPermission)255); using var client = await DetailOperator();
        var eur = (await ReadCurrencies(client)).Single(x => x.Code == "EUR");
        var value = new CurrencyWriteRequest(Guid.NewGuid(), eur.RowVersion, false);
        var result = await client.PutAsJsonAsync(CurrencyPath() + "/" + eur.Code, value); Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        var changed = (await result.Content.ReadFromJsonAsync<CurrencyResponse>())!; Assert.False(changed.IsActive); Assert.NotEqual(eur.RowVersion, changed.RowVersion);
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
    public async Task Currencies_AuditFailureRollsBackActivity()
    {
        await GrantRegistryRights((CompanyPermission)255); using var client = await DetailOperator();
        await using var db = Company(false, companyA);
        var eur = (await ReadCurrencies(client)).Single(x => x.Code == "EUR");
        await db.Database.ExecuteSqlRawAsync("ALTER TABLE audit.CurrencyOperations WITH NOCHECK ADD CONSTRAINT CK_Test_CurrencyAuditFail CHECK (Kind='Impossible');");
        try
        {
            Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.PutAsJsonAsync(CurrencyPath() + "/" + eur.Code, new CurrencyWriteRequest(Guid.NewGuid(), eur.RowVersion, false))).StatusCode);
            Assert.True((await db.Currencies.AsNoTracking().SingleAsync(x => x.Code == eur.Code)).IsActive);
            Assert.Empty(await db.CurrencyOperations.ToListAsync());
        }
        finally { await db.Database.ExecuteSqlRawAsync("ALTER TABLE audit.CurrencyOperations DROP CONSTRAINT CK_Test_CurrencyAuditFail;"); }
    }

    [SqlServerFact]
    public async Task Rates_ApiHasNoNbpClientAndEveryAuthorizedCompanyReadsTheSameInstallationRates()
    {
        Assert.Null(application!.Services.GetService<INbpClient>());
        Assert.True((await RunSync(Wednesday1330)).Ran);
        using var client = await DetailOperator(); Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(RatePath())).StatusCode);
        await GrantRegistryRights((CompanyPermission)255);
        using (var scope = application.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<SystemDbContext>().CompanyAccesses.Where(x => x.UserId == operatorId && x.CompanyId == companyB)
                .ExecuteUpdateAsync(x => x.SetProperty(p => p.Permissions, (CompanyPermission)67));
        var a = (await client.GetFromJsonAsync<RatePage>(RatePath()))!; var b = (await client.GetFromJsonAsync<RatePage>(RatePath(companyB)))!;
        Assert.NotEmpty(a.Items); Assert.Equal(a.Items.Select(x => x.Id), b.Items.Select(x => x.Id));
        Assert.Equal(companyB, b.CompanyId); Assert.All(b.Items, x => Assert.Equal(companyB, x.CompanyId));
        Assert.Equal(new DateOnly(2026, 11, 18), a.LastA); Assert.Equal(new DateOnly(2026, 11, 18), a.LastB); Assert.Equal(new DateOnly(2026, 11, 18), a.LastC);
        Assert.NotNull(a.Sync!.LastSuccessUtc); Assert.Null(a.Sync.LastErrorCode);
        var eur = (await client.GetFromJsonAsync<RatePage>(RatePath() + "?code=EUR&table=C"))!;
        Assert.All(eur.Items, x => { Assert.Equal("EUR", x.CurrencyCode); Assert.Null(x.Mid); Assert.Equal(x.EffectiveDate.AddDays(-1), x.TradingDate); });
        var lookup = await client.GetFromJsonAsync<PublishedRate>(RatePath() + "/lookup?code=USD&table=A&date=2026-11-18&before=true");
        Assert.Equal(new DateOnly(2026, 11, 17), lookup!.EffectiveDate);
        var withRates = (await client.GetFromJsonAsync<CurrencyResponse[]>(CurrencyPath() + "?withRatesOnly=true"))!;
        Assert.Equal(new[] { "AFN", "CHF", "EUR", "GBP", "USD" }, withRates.Select(x => x.Code).Order());
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PutAsJsonAsync(CurrencyPath(companyB) + "/EUR", new CurrencyWriteRequest(Guid.NewGuid(), "AA==", false))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync(RatePath() + "?companyId=" + companyB)).StatusCode);
        using var reader = await Login("reader.test"); Assert.Equal(HttpStatusCode.Forbidden, (await reader.GetAsync(RatePath())).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(AdminRates)).StatusCode);
        client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync(CurrencyPath() + "/EUR", new CurrencyWriteRequest(Guid.NewGuid(), "AA==", false))).StatusCode);
        await InSystem(async system =>
        {
            await Assert.ThrowsAsync<SqlException>(() => system.Database.ExecuteSqlRawAsync("INSERT INTO ref.ExchangeRates(CurrencyCode,Source,TableType,TableNumber,EffectiveDate,Mid,RetrievedAtUtc) SELECT CurrencyCode,Source,TableType,TableNumber,EffectiveDate,Mid,RetrievedAtUtc FROM ref.ExchangeRates;"));
            await Assert.ThrowsAsync<SqlException>(() => system.Database.ExecuteSqlRawAsync("UPDATE ref.ExchangeRates SET Bid=1 WHERE TableType='A';"));
            await Assert.ThrowsAsync<SqlException>(() => system.Database.ExecuteSqlRawAsync("UPDATE audit.ExchangeRateOperations SET Kind='X';"));
            return 0;
        });
    }

    [SqlServerFact]
    public async Task Rates_FirstRunCatchesUpFromDefaultStartInChunksStoresAllCurrenciesAndThenRunsDaily()
    {
        nbpFixture.IncludeUnknownCode = true;
        var first = await RunSync(Wednesday1330);
        Assert.True(first.Ran); Assert.Null(first.ErrorCode);
        var calls = nbpFixture.Calls.ToArray();
        Assert.Equal(24, calls.Length);
        foreach (var table in new[] { "A", "B", "C" })
        {
            var parts = calls.Where(x => x.Table == table).ToArray();
            Assert.Equal(new DateOnly(2025, 1, 1), parts[0].From); Assert.Equal(new DateOnly(2026, 11, 18), parts[^1].To);
            Assert.All(parts, x => Assert.True(x.To!.Value.DayNumber - x.From!.Value.DayNumber <= 92));
            Assert.All(parts.Skip(1).Zip(parts), x => Assert.Equal(x.Second.To!.Value.AddDays(1), x.First.From));
        }
        await InSystem(async system =>
        {
            var rates = await system.ExchangeRates.AsNoTracking().ToListAsync();
            Assert.Equal(8 * 4, rates.Count(x => x.TableType == "A")); Assert.Equal(2, rates.Count(x => x.TableType == "B")); Assert.Equal(8, rates.Count(x => x.TableType == "C"));
            Assert.DoesNotContain(rates, x => x.CurrencyCode == "XYZ");
            var sync = await system.ExchangeRateSyncs.AsNoTracking().SingleAsync();
            Assert.Equal(new DateOnly(2025, 1, 1), sync.HistoryStartDate);
            Assert.All(new[] { sync.CheckedThroughA, sync.CheckedThroughB, sync.CheckedThroughC }, x => Assert.Equal(new DateOnly(2026, 11, 17), x));
            Assert.NotNull(sync.LastSuccessUtc); Assert.Null(sync.LastErrorCode); Assert.Null(sync.RunRequestedAtUtc);
            var history = await system.ExchangeRateOperations.AsNoTracking().ToListAsync();
            Assert.All(history, x => { Assert.Equal(ExchangeRateOperationKinds.Download, x.Kind); Assert.Null(x.ActorId); Assert.Equal(ExchangeRateSyncService.ActorName, x.ActorName); });
            Assert.Contains(history, x => x.DetailsJson.Contains("XYZ"));
            return 0;
        });
        nbpFixture.Calls.Clear();
        Assert.False((await RunSync(Wednesday1330.AddMinutes(10))).Ran);
        Assert.False((await RunSync(Warsaw(2026, 11, 19, 9))).Ran); // przed 13:00, wczorajszy przebieg się odbył
        Assert.Empty(nbpFixture.Calls);
        var daily = await RunSync(Warsaw(2026, 11, 19, 13, 5));
        Assert.True(daily.Ran); Assert.Equal(4 + 1, daily.Added);
        Assert.Equal(3, nbpFixture.Calls.Count);
        Assert.All(nbpFixture.Calls, x => { Assert.Equal(new DateOnly(2026, 11, 10), x.From); Assert.Equal(new DateOnly(2026, 11, 19), x.To); });
        Assert.Equal(new DateOnly(2026, 11, 18), await InSystem(system => system.ExchangeRateSyncs.Select(x => x.CheckedThroughA).SingleAsync()));
    }

    [SqlServerFact]
    public async Task Rates_OutageKeepsCheckpointRetriesAfterAnHourAndCatchesUpAfterDowntime()
    {
        nbpFixture.PublishFrom = new(2026, 11, 2);
        Assert.Null((await RunSync(Wednesday1330)).ErrorCode);
        // Serwer nie działał od czwartku do poniedziałku 23.11; zaległość jest pobierana od razu, przed 13:00.
        nbpFixture.Calls.Clear(); nbpFixture.FailOnCall = 2; nbpFixture.FailTimes = 3; nbpFixture.FailStatus = HttpStatusCode.NotFound;
        var failed = await RunSync(Warsaw(2026, 11, 23, 10));
        Assert.True(failed.Ran); Assert.Equal("nbp_unavailable", failed.ErrorCode);
        Assert.Equal(3, nbpFixture.Calls.Count(x => x.Table == "B")); // trzy próby fałszywego 404, potem awaria
        var sync = await InSystem(system => system.ExchangeRateSyncs.AsNoTracking().SingleAsync());
        Assert.Equal(new DateOnly(2026, 11, 22), sync.CheckedThroughA); Assert.Equal(new DateOnly(2026, 11, 17), sync.CheckedThroughB);
        Assert.Equal(new DateOnly(2026, 11, 22), sync.CheckedThroughC); Assert.Equal("nbp_unavailable", sync.LastErrorCode);
        Assert.Equal(1, await InSystem(system => system.ExchangeRateOperations.CountAsync(x => x.Kind == ExchangeRateOperationKinds.DownloadFailed)));
        Assert.False((await RunSync(Warsaw(2026, 11, 23, 10, 30))).Ran);
        // Pojedyncze fałszywe 404 jest ponawiane w ramach przebiegu i nie jest awarią.
        nbpFixture.FailOnCall = 1; nbpFixture.FailTimes = 1; nbpFixture.Calls.Clear();
        var resumed = await RunSync(Warsaw(2026, 11, 23, 11, 1));
        Assert.True(resumed.Ran); Assert.Null(resumed.ErrorCode);
        Assert.Equal(2, nbpFixture.Calls.Count(x => x.Table == "A"));
        Assert.Equal(1, await InSystem(system => system.ExchangeRateOperations.CountAsync(x => x.Kind == ExchangeRateOperationKinds.DownloadFailed)));
        Assert.Equal(new DateOnly(2026, 11, 10), nbpFixture.Calls.Single(x => x.Table == "B").From);
        await InSystem(async system =>
        {
            var state = await system.ExchangeRateSyncs.AsNoTracking().SingleAsync();
            Assert.Equal(new DateOnly(2026, 11, 22), state.CheckedThroughB); Assert.Null(state.LastErrorCode);
            Assert.Equal(nbpFixture.PublicationDays("A", new(2026, 11, 2), new(2026, 11, 23)).Count() * 4, await system.ExchangeRates.CountAsync(x => x.TableType == "A"));
            return 0;
        });
    }

    [SqlServerFact]
    public async Task Rates_ConflictsNeverOverwriteAreRecordedOnceAndOnlyInstallationAdministratorResolves()
    {
        nbpFixture.PublishFrom = new(2026, 11, 16);
        Assert.Null((await RunSync(Wednesday1330)).ErrorCode);
        using var admin = await RateAdministrator();
        using (var reader = await Login("reader.test"))
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await reader.GetAsync(AdminRates)).StatusCode);
            await Csrf(reader); Assert.Equal(HttpStatusCode.Forbidden, (await reader.PostAsJsonAsync(AdminRates + "/run", new { })).StatusCode);
        }
        var requested = await admin.PostAsJsonAsync(AdminRates + "/run", new { }); Assert.Equal(HttpStatusCode.OK, requested.StatusCode);
        Assert.True((await requested.Content.ReadFromJsonAsync<ExchangeRateAdministrationResponse>())!.Status.RunRequested);
        nbpFixture.Mid = 4.5m;
        var run = await RunSync(Wednesday1330.AddMinutes(10));
        Assert.True(run.Ran); Assert.Equal(3 * 4 + 1, run.Conflicts); Assert.Equal(0, run.Added);
        Assert.Null(await InSystem(system => system.ExchangeRateSyncs.Select(x => x.RunRequestedAtUtc).SingleAsync()));
        Assert.All(await InSystem(system => system.ExchangeRates.Where(x => x.TableType != "C").ToListAsync()), x => Assert.Equal(4.123456789012m, x.Mid));
        await admin.PostAsJsonAsync(AdminRates + "/run", new { });
        Assert.Equal(0, (await RunSync(Wednesday1330.AddMinutes(20))).Conflicts);
        var state = (await admin.GetFromJsonAsync<ExchangeRateAdministrationResponse>(AdminRates))!;
        Assert.Equal(13, state.Conflicts.Length);
        var item = state.Conflicts[0]; var url = $"{AdminRates}/conflicts/{item.Id}/resolve";
        Assert.Equal(4.123456789012m, item.Existing.Mid ?? item.Existing.Bid); Assert.Equal(4.5m, item.Incoming.Mid);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync(url, new ExchangeRateConflictResolution(true, item.RowVersion, item.RateRowVersion, false))).StatusCode);
        var accepted = await admin.PostAsJsonAsync(url, new ExchangeRateConflictResolution(true, item.RowVersion, item.RateRowVersion, true));
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        Assert.Equal(12, (await accepted.Content.ReadFromJsonAsync<ExchangeRateAdministrationResponse>())!.Conflicts.Length);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsJsonAsync(url, new ExchangeRateConflictResolution(true, item.RowVersion, item.RateRowVersion, true))).StatusCode);
        var other = state.Conflicts[1];
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsJsonAsync($"{AdminRates}/conflicts/{other.Id}/resolve", new ExchangeRateConflictResolution(false, other.RowVersion, other.RateRowVersion, true))).StatusCode);
        await InSystem(async system =>
        {
            var changed = await system.ExchangeRates.AsNoTracking().SingleAsync(x => x.CurrencyCode == item.CurrencyCode && x.TableType == item.TableType && x.EffectiveDate == item.Existing.EffectiveDate);
            Assert.Equal(4.5m, changed.Mid);
            var kept = await system.ExchangeRates.AsNoTracking().SingleAsync(x => x.CurrencyCode == other.CurrencyCode && x.TableType == other.TableType && x.EffectiveDate == other.Existing.EffectiveDate);
            Assert.Equal(4.123456789012m, kept.Mid); Assert.NotEqual(other.RateRowVersion, Convert.ToBase64String(kept.RowVersion));
            var decisions = await system.ExchangeRateOperations.Where(x => x.Kind == ExchangeRateOperationKinds.ResolveConflict).ToListAsync();
            Assert.Equal(2, decisions.Count); Assert.All(decisions, x => Assert.Equal(operatorId, x.ActorId));
            Assert.Contains("4.123456789012", decisions.Single(x => x.DetailsJson.Contains("AcceptIncoming")).DetailsJson);
            return 0;
        });
        var history = (await admin.GetFromJsonAsync<ExchangeRateOperationResponse[]>(AdminRates + "/history"))!;
        Assert.Contains(history, x => x.Kind == ExchangeRateOperationKinds.RunRequested && x.ActorName == "operator.test");
        admin.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync(AdminRates + "/run", new { })).StatusCode);
    }

    [SqlServerFact]
    public async Task Rates_EarlierHistoryStartRequiresCurrentVersionAndRefetchesMissingHistory()
    {
        nbpFixture.PublishFrom = new(2024, 12, 2); nbpFixture.PublishTo = new(2024, 12, 31);
        Assert.Null((await RunSync(Wednesday1330)).ErrorCode);
        Assert.Equal(0, await InSystem(system => system.ExchangeRates.CountAsync()));
        using var admin = await RateAdministrator();
        var state = (await admin.GetFromJsonAsync<ExchangeRateAdministrationResponse>(AdminRates))!;
        Assert.False(state.HistoryStartIsDefault); Assert.Equal(new DateOnly(2025, 1, 1), state.HistoryStartDate);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync(AdminRates + "/history-start", new ExchangeRateHistoryStartRequest(new(2001, 12, 31), state.RowVersion))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PutAsJsonAsync(AdminRates + "/history-start", new ExchangeRateHistoryStartRequest(new(2024, 12, 1), "AAAAAAAAAAA="))).StatusCode);
        var saved = await admin.PutAsJsonAsync(AdminRates + "/history-start", new ExchangeRateHistoryStartRequest(new(2024, 12, 1), state.RowVersion));
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        Assert.Equal(new DateOnly(2024, 12, 1), (await saved.Content.ReadFromJsonAsync<ExchangeRateAdministrationResponse>())!.HistoryStartDate);
        var sync = await InSystem(system => system.ExchangeRateSyncs.AsNoTracking().SingleAsync());
        Assert.Null(sync.CheckedThroughA); Assert.Null(sync.CheckedThroughB); Assert.Null(sync.CheckedThroughC);
        Assert.Equal(1, await InSystem(system => system.ExchangeRateOperations.CountAsync(x => x.Kind == ExchangeRateOperationKinds.HistoryStartChanged && x.ActorId == operatorId)));
        nbpFixture.Calls.Clear();
        var refetch = await RunSync(Wednesday1330.AddMinutes(10));
        Assert.True(refetch.Ran); Assert.Equal(new DateOnly(2024, 12, 1), nbpFixture.Calls.First().From);
        Assert.Equal(nbpFixture.PublicationDays("A", new(2024, 12, 1), new(2024, 12, 31)).Count() * 4, await InSystem(system => system.ExchangeRates.CountAsync(x => x.TableType == "A")));
    }

    [SqlServerFact]
    public async Task Rates_DesktopActivityRatesAndAdministrationWorkThroughRealApi()
    {
        Assert.Null((await RunSync(Wednesday1330)).ErrorCode);
        await GrantRegistryRights((CompanyPermission)255);
        using (var model = new MainViewModel(_ => new VatowiecApiClient(Client())) { UserName = "operator.test" })
        {
            await model.Login(password); model.SelectedCompany = model.Companies.Single(x => x.CompanyId == companyA); await model.OpenCompany();
            await model.OpenCurrencies(true); Assert.True(model.IsCurrencySettings, model.Message);
            Assert.Equal("EUR", model.CurrencyEditor!.Code); model.CurrencyEditor.IsActive = false; await model.SaveCurrencySettings();
            Assert.False(model.CurrencyEditor.IsActive, model.Message); Assert.False(model.CurrencyRows.Single(x => x.Code == "EUR").IsActive);
            model.ShowCurrencySettings(false); await model.LoadRates();
            Assert.NotEmpty(model.RateRows); Assert.Contains("18.11.2026", model.StoredRateDates);
            model.CloseCurrencies(); Assert.False(model.IsCurrencyScreen); Assert.Empty(model.RateRows);
            await model.ChangeCompany(); Assert.Empty(model.CurrencyRows);
            Assert.False(model.IsInstallationAdministrator);
        }
        using (var admin = await RateAdministrator()) { }
        using var adminModel = new MainViewModel(_ => new VatowiecApiClient(Client())) { UserName = "operator.test" };
        await adminModel.Login(password); Assert.True(adminModel.IsInstallationAdministrator, adminModel.Message);
        await adminModel.OpenExchangeRateAdministration();
        Assert.True(adminModel.IsExchangeRateAdministrationScreen, adminModel.Message);
        Assert.True(adminModel.ExchangeRateAdministration!.IsLoaded); Assert.False(adminModel.ExchangeRateAdministration.HasConflicts);
        Assert.Contains("18.11.2026", adminModel.ExchangeRateAdministration.StatusSummary);
        await adminModel.RequestExchangeRateRun(); Assert.Contains("Zlecono", adminModel.Message);
        Assert.True(await InSystem(system => system.ExchangeRateSyncs.Select(x => x.RunRequestedAtUtc != null).SingleAsync()));
        adminModel.CloseExchangeRateAdministration(); Assert.False(adminModel.IsExchangeRateAdministrationScreen);
    }
}
