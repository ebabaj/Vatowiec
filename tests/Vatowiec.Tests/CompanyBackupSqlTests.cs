using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Vatowiec.Core.Domain;
using Vatowiec.Core.Infrastructure.Operations;
using Vatowiec.Core.Infrastructure.Persistence;
using Vatowiec.Operations;
using Vatowiec.SharedContracts;
using static Vatowiec.Tests.CompanyProvisioningSqlTests;

namespace Vatowiec.Tests;

// ADR-044 krok 2. Zmiany w Vatowiec_System są wycofywane; kopie trafiają do własnego podkatalogu testu
// w katalogu kopii (VATOWIEC_BACKUP_ROOT, domyślnie C:\ProgramData\Vatowiec\Backups) i są po teście usuwane.
[Collection("LocalSql")]
[Trait("Category", "SqlIntegration")]
public sealed class CompanyBackupSqlTests
{
    private static readonly Guid A = Guid.Parse("aaaaaaaa-0000-4000-8000-000000000001");
    private static string BackupBase => Environment.GetEnvironmentVariable("VATOWIEC_BACKUP_ROOT") ?? @"C:\ProgramData\Vatowiec\Backups";

    [ProvisioningSqlFact]
    public async Task Api_EnforcesAccessScheduleRightPolicySwitchAndSingleActiveBackup()
    {
        await using var tx = await PreservedSystemTransaction.Open(); using var app = App(tx);
        using var scope = app.Services.CreateScope();
        var password = "Fixture!1a-" + Guid.NewGuid().ToString("N");
        var admin = await Account(scope.ServiceProvider, true, password);
        var plain = await Account(scope.ServiceProvider, false, password);
        var outsider = await Account(scope.ServiceProvider, false, password);
        CompanyAccess plainAccess;
        await using (var db = tx.Context())
        {
            db.CompanyAccesses.Add(new CompanyAccess(admin.Id, A, CompanyPermission.ViewProfile));
            db.CompanyAccesses.Add(plainAccess = new CompanyAccess(plain.Id, A, CompanyPermission.ViewProfile));
            await db.SaveChangesAsync();
        }
        using var adminClient = Client(app); using var user = Client(app); using var other = Client(app);
        await GusConfigurationSqlTests.Login(adminClient, admin.UserName!, password);
        await GusConfigurationSqlTests.Login(user, plain.UserName!, password);
        await GusConfigurationSqlTests.Login(other, outsider.UserName!, password);
        var route = $"/api/v1/companies/{A}/backups";

        Assert.Equal(HttpStatusCode.Forbidden, (await other.GetAsync(route)).StatusCode); // Bez CompanyAccess.
        var status = (await user.GetFromJsonAsync<CompanyBackupStatusResponse>(route))!;
        Assert.True(status.CanRequest); Assert.False(status.CanManageSchedule);
        Assert.Equal(("Every30Days", "Reminder", true), (status.Schedule.Frequency, status.Schedule.Mode, status.Schedule.IsDefault));
        Assert.Null(status.LastSuccessAtUtc); Assert.True(status.ReminderDue); // Firma A istnieje dłużej niż 30 dni i nie ma kopii.

        var schedule = new CompanyBackupScheduleRequest("Daily", "Automatic", 21, null);
        using (var denied = await user.PutAsJsonAsync(route + "/schedule", schedule))
        {
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode); Assert.Contains("backup_schedule_denied", await denied.Content.ReadAsStringAsync());
        }
        await using (var db = tx.Context())
        {
            var access = await db.CompanyAccesses.SingleAsync(x => x.UserId == plain.Id && x.CompanyId == A);
            access.ChangePermissions(CompanyPermission.ViewProfile | CompanyPermission.ManageCompanyBackups); await db.SaveChangesAsync();
        }
        status = (await (await user.PutAsJsonAsync(route + "/schedule", schedule)).Content.ReadFromJsonAsync<CompanyBackupStatusResponse>())!;
        Assert.Equal(("Daily", "Automatic", 21, false), (status.Schedule.Frequency, status.Schedule.Mode, status.Schedule.PreferredHour, status.Schedule.IsDefault));
        Assert.Equal(HttpStatusCode.Conflict, (await user.PutAsJsonAsync(route + "/schedule", schedule with { Frequency = "Never" })).StatusCode); // Stara wersja.
        Assert.Equal(HttpStatusCode.BadRequest, (await user.PutAsJsonAsync(route + "/schedule", schedule with { Frequency = "Hourly", RowVersion = status.Schedule.RowVersion })).StatusCode);

        using (var noCsrf = Client(app))
        {
            await GusConfigurationSqlTests.Login(noCsrf, plain.UserName!, password); noCsrf.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
            Assert.Equal(HttpStatusCode.BadRequest, (await noCsrf.PostAsJsonAsync(route, new CompanyBackupRequest(Guid.NewGuid()))).StatusCode);
        }
        var key = Guid.NewGuid();
        using var created = await user.PostAsJsonAsync(route, new CompanyBackupRequest(key));
        Assert.Equal(HttpStatusCode.Accepted, created.StatusCode);
        var operation = (await created.Content.ReadFromJsonAsync<CompanyOperationResponse>())!;
        Assert.Equal(("Backup", "Pending"), (operation.Kind, operation.Status));
        Assert.Equal(operation.Id, (await (await user.PostAsJsonAsync(route, new CompanyBackupRequest(key))).Content.ReadFromJsonAsync<CompanyOperationResponse>())!.Id);
        using (var second = await user.PostAsJsonAsync(route, new CompanyBackupRequest(Guid.NewGuid())))
        {
            Assert.Equal(HttpStatusCode.OK, second.StatusCode); // Trwająca kopia firmy zamiast drugiej.
            Assert.Equal(operation.Id, (await second.Content.ReadFromJsonAsync<CompanyOperationResponse>())!.Id);
        }

        var snoozed = (await (await user.PostAsJsonAsync(route + "/reminder/snooze", new { })).Content.ReadFromJsonAsync<CompanyBackupStatusResponse>())!;
        Assert.False(snoozed.ReminderDue);

        Assert.Equal(HttpStatusCode.Forbidden, (await user.GetAsync("/api/v1/installation/backup-policy")).StatusCode);
        var policy = (await adminClient.GetFromJsonAsync<BackupPolicyResponse>("/api/v1/installation/backup-policy"))!;
        Assert.Equal((true, 7), (policy.UsersMayRequestBackups, policy.RetainedBackupsPerCompany));
        Assert.Equal(HttpStatusCode.BadRequest, (await adminClient.PutAsJsonAsync("/api/v1/installation/backup-policy", new BackupPolicyRequest(false, 0, policy.RowVersion))).StatusCode);
        policy = (await (await adminClient.PutAsJsonAsync("/api/v1/installation/backup-policy", new BackupPolicyRequest(false, 3, policy.RowVersion)))
            .Content.ReadFromJsonAsync<BackupPolicyResponse>())!;
        Assert.Equal((false, 3), (policy.UsersMayRequestBackups, policy.RetainedBackupsPerCompany));
        using (var disabled = await user.PostAsJsonAsync(route, new CompanyBackupRequest(Guid.NewGuid())))
        {
            Assert.Equal(HttpStatusCode.Forbidden, disabled.StatusCode); Assert.Contains("backup_requests_disabled", await disabled.Content.ReadAsStringAsync());
        }
        Assert.False((await user.GetFromJsonAsync<CompanyBackupStatusResponse>(route))!.CanRequest);
        Assert.NotEqual(HttpStatusCode.Forbidden, (await adminClient.PostAsJsonAsync(route, new CompanyBackupRequest(Guid.NewGuid()))).StatusCode); // Administrator zawsze.

        await using (var db = tx.Context())
        {
            var changes = await db.BackupSettingChanges.AsNoTracking().OrderBy(x => x.Id).ToArrayAsync();
            Assert.Contains(changes, x => x.CompanyId == A && x.ActorId == plain.Id && x.BeforeJson == null && x.AfterJson.Contains("Daily"));
            Assert.Contains(changes, x => x.CompanyId == null && x.ActorId == admin.Id && x.AfterJson.Contains("\"retained\":3"));
            await Assert.ThrowsAsync<InvalidOperationException>(async () => { db.Remove(changes[0]); await db.SaveChangesAsync(); });
            var stored = await db.CompanyOperations.AsNoTracking().SingleAsync(x => x.Id == operation.Id);
            Assert.StartsWith("Vatowiec_Company_A_Dev/Vatowiec_Company_A_Dev_", stored.BackupFileName);
            Assert.False(File.Exists(Path.Combine(BackupBase, stored.BackupFileName!))); // API niczego nie zapisuje na dysku.
        }
    }

    [ProvisioningSqlFact]
    public async Task Worker_BacksUpWithChecksumAndManifest_ResumesAfterFailure_AndRotatesOnlyOwnFiles()
    {
        await using var tx = await PreservedSystemTransaction.Open(); using var app = App(tx);
        using var scope = app.Services.CreateScope();
        var admin = await Account(scope.ServiceProvider, true, "Fixture!1a-" + Guid.NewGuid().ToString("N"));
        var root = Path.Combine(BackupBase, "tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var clock = new MutableClock(DateTimeOffset.UtcNow);
        try
        {
            Guid installation;
            await using (var db = tx.Context())
            {
                db.CompanyAccesses.Add(new CompanyAccess(admin.Id, A, CompanyPermission.ViewProfile));
                (await db.BackupPolicies.SingleAsync()).Change(true, 2);
                await db.SaveChangesAsync();
                installation = await db.InstallationIdentities.Select(x => x.Id).SingleAsync();
            }
            var foreign = Path.Combine(root, "Vatowiec_Company_A_Dev", "foreign-do-not-touch.bak");
            Directory.CreateDirectory(Path.GetDirectoryName(foreign)!); await File.WriteAllTextAsync(foreign, "obcy plik");

            var operations = new List<Guid>();
            for (var i = 0; i < 3; i++)
            {
                await using (var db = tx.Context())
                    operations.Add((await new CompanyBackupService(db, clock).Request(new(admin.Id, admin.UserName!), A, Guid.NewGuid(), default)).Operation.Id);
                var failed = i > 0; // Pierwsza kopia: awaria po zapisaniu pliku i wznowienie.
                await using (var db = tx.Context())
                    Assert.True(await Executor(db, clock, "worker", stage =>
                    {
                        if (stage == CompanyOperationStages.BackupWritten && !failed) { failed = true; throw new InvalidOperationException("injected"); }
                        return Task.CompletedTask;
                    }, root).RunNext(default));
                if (i == 0)
                {
                    await using (var db = tx.Context())
                    {
                        var state = await db.CompanyOperations.AsNoTracking().SingleAsync(x => x.Id == operations[0]);
                        Assert.Equal((CompanyOperationStages.BackupWritten, CompanyOperationStatuses.Pending), (state.Stage, state.Status));
                        clock.Advance(state.NextAttemptAtUtc!.Value - clock.GetUtcNow() + TimeSpan.FromSeconds(1));
                    }
                    await using (var db = tx.Context()) Assert.True(await Executor(db, clock, "worker", backupRoot: root).RunNext(default));
                }
                clock.Advance(TimeSpan.FromMinutes(1));
            }

            await using (var db = tx.Context())
            {
                var backups = await db.CompanyBackups.AsNoTracking().Where(x => x.CompanyId == A && operations.Contains(x.OperationId)).OrderBy(x => x.CreatedAtUtc).ToArrayAsync();
                Assert.Equal(3, backups.Length);
                Assert.All(operations, id => Assert.Equal(CompanyOperationStatuses.Completed, db.CompanyOperations.AsNoTracking().Single(x => x.Id == id).Status));
                Assert.Equal(("Rotation", (string?)null, (string?)null), (backups[0].RemovedReason, backups[1].RemovedReason, backups[2].RemovedReason));
                var removed = Path.Combine(root, backups[0].FileName);
                Assert.False(File.Exists(removed)); Assert.False(File.Exists(BackupStorage.ManifestPath(removed)));
                foreach (var kept in backups.Skip(1))
                {
                    var path = Path.Combine(root, kept.FileName);
                    await using (var file = File.OpenRead(path)) Assert.Equal(kept.Sha256, Convert.ToHexString(await SHA256.HashDataAsync(file)));
                    var manifest = JsonDocument.Parse(await File.ReadAllTextAsync(BackupStorage.ManifestPath(path))).RootElement;
                    Assert.Equal(CompanyOperationExecutor.ManifestFormat, manifest.GetProperty("format").GetString());
                    Assert.Equal((A, installation, kept.OperationId), (manifest.GetProperty("companyId").GetGuid(), manifest.GetProperty("installationId").GetGuid(), manifest.GetProperty("operationId").GetGuid()));
                    Assert.Equal(kept.Sha256, manifest.GetProperty("sha256").GetString());
                    Assert.Contains(manifest.GetProperty("migrations").EnumerateArray(), x => x.GetString()!.EndsWith("_CurrenciesBgnAng", StringComparison.Ordinal));
                }
                Assert.True(File.Exists(foreign)); // Rotacja nie dotyka plików spoza rejestru kopii.
                var status = await new CompanyBackupService(db, clock).Status(admin.Id, A, default);
                Assert.Equal(2, status.Backups.Length); Assert.Equal(backups[2].CreatedAtUtc, status.LastSuccessAtUtc);
            }
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [ProvisioningSqlFact]
    public async Task Scheduler_QueuesAutomaticBackupsOnlyWhenDueAndInWindow()
    {
        await using var tx = await PreservedSystemTransaction.Open();
        var clock = new MutableClock(new DateTimeOffset(2099, 1, 1, 12, 0, 0, TimeSpan.Zero), TimeZoneInfo.Utc);
        await using (var db = tx.Context())
        {
            db.CompanyBackupSchedules.Add(new CompanyBackupSchedule(A, BackupFrequencies.Daily, BackupModes.Automatic, 13));
            await db.SaveChangesAsync();
            Assert.Equal(0, await Scheduler(db, clock).EnqueueDue(default)); // Przed ustawioną godziną.
            clock.Advance(TimeSpan.FromHours(1));
            Assert.Equal(1, await Scheduler(db, clock).EnqueueDue(default));
            Assert.Equal(0, await Scheduler(db, clock).EnqueueDue(default)); // Jedna aktywna kopia firmy.
            var queued = await db.CompanyOperations.AsNoTracking().SingleAsync(x => x.CompanyId == A && x.Kind == CompanyOperationKinds.Backup && x.RequestedById == null);
            Assert.Equal(CompanyBackupService.SchedulerName, queued.RequestedByName);
            var schedule = await db.CompanyBackupSchedules.SingleAsync(x => x.CompanyId == A);
            schedule.Change(BackupFrequencies.Daily, BackupModes.Reminder, 0); await db.SaveChangesAsync();
            Assert.Equal(0, await Scheduler(db, clock).EnqueueDue(default)); // Tryb przypomnienia nie tworzy kopii.
        }
    }

    private static BackupScheduler Scheduler(SystemDbContext db, TimeProvider clock) =>
        new(db, new CompanyBackupService(db, clock), clock, NullLogger<BackupScheduler>.Instance);
}
