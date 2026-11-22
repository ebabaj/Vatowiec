using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Vatowiec.Core.Domain;
using Vatowiec.Core.Infrastructure.Identity;
using Vatowiec.Core.Infrastructure.Operations;
using Vatowiec.Core.Infrastructure.Persistence;
using Vatowiec.Operations;
using Vatowiec.SharedContracts;
using static Vatowiec.Tests.CompanyProvisioningSqlTests;

namespace Vatowiec.Tests;

// ADR-045: pełny cykl na lokalnym SQLEXPRESS — utworzenie firmy, kopia, usunięcie z archiwum i czyszczenie.
// Zmiany w Vatowiec_System są wycofywane; bazy i pliki utworzone przez test są usuwane w finally.
[Collection("LocalSql")]
[Trait("Category", "SqlIntegration")]
public sealed class CompanyDeletionSqlTests
{
    private static string BackupBase => Environment.GetEnvironmentVariable("VATOWIEC_BACKUP_ROOT") ?? @"C:\ProgramData\Vatowiec\Backups";

    private static async Task<CompanyOperation> Provision(PreservedSystemTransaction tx, ApplicationUser admin, MutableClock clock, string name)
    {
        CompanyOperation operation;
        await using (var db = tx.Context())
            (operation, _) = await new CompanyOperationRequests(db, clock).RequestProvision(new(admin.Id, admin.UserName!), Guid.NewGuid(), name,
                "LocalWindows", Server, default);
        await using (var db = tx.Context()) Assert.True(await Executor(db, clock, "worker").RunNext(default));
        await using (var db = tx.Context()) Assert.True(await db.Companies.AnyAsync(x => x.Id == operation.CompanyId && x.IsActive));
        return operation;
    }

    [ProvisioningSqlFact]
    public async Task Delete_WithArchive_DropsDatabase_PurgesEverything_KeepsOnlyRegistryAndArchive()
    {
        await using var tx = await PreservedSystemTransaction.Open(); using var app = App(tx);
        using var scope = app.Services.CreateScope();
        var password = "Fixture!1a-" + Guid.NewGuid().ToString("N");
        var admin = await Account(scope.ServiceProvider, true, password);
        var plain = await Account(scope.ServiceProvider, false, password);
        var root = Path.Combine(BackupBase, "tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var clock = new MutableClock(DateTimeOffset.UtcNow);
        var name = "Firma do usuniecia " + Guid.NewGuid().ToString("N")[..8];
        CompanyOperation? provisioned = null;
        try
        {
            provisioned = await Provision(tx, admin, clock, name);
            var company = provisioned.CompanyId;
            // Dane towarzyszące: kopia, harmonogram z audytem, odłożone przypomnienie, prawa drugiego użytkownika.
            await using (var db = tx.Context())
            {
                var backups = new CompanyBackupService(db, clock);
                await backups.Request(new(admin.Id, admin.UserName!), company, Guid.NewGuid(), default);
                await backups.SaveSchedule(new(admin.Id, admin.UserName!), company, new("Daily", "Automatic", 3, null), default);
                await backups.Snooze(admin.Id, company, default);
                db.CompanyAccesses.Add(new CompanyAccess(plain.Id, company, CompanyPermission.ViewProfile));
                db.UserAdministrationChanges.Add(new UserAdministrationChange(admin.Id, provisioned.ActorIssuerId, admin.UserName!, plain.Id,
                    plain.UserName!, clock.GetUtcNow(), "ChangeCompanyAccess", company, CompanyPermission.None, CompanyPermission.ViewProfile));
                await db.SaveChangesAsync();
            }
            await using (var db = tx.Context()) Assert.True(await Executor(db, clock, "worker", backupRoot: root).RunNext(default));
            string regularBackup;
            await using (var db = tx.Context()) regularBackup = Path.Combine(root, (await db.CompanyBackups.SingleAsync(x => x.CompanyId == company)).FileName);
            Assert.True(File.Exists(regularBackup));

            // API: tylko administrator instalacji; bez archiwum wymagane jawne potwierdzenie.
            using var user = Client(app); using var adminClient = Client(app);
            await GusConfigurationSqlTests.Login(user, plain.UserName!, password);
            await GusConfigurationSqlTests.Login(adminClient, admin.UserName!, password);
            Assert.Equal(HttpStatusCode.Forbidden, (await user.PostAsJsonAsync("/api/v1/installation/company-operations/delete",
                new DeleteCompanyRequest(Guid.NewGuid(), company, true, false))).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await user.GetAsync("/api/v1/installation/companies")).StatusCode);
            Assert.Contains((await adminClient.GetFromJsonAsync<InstallationCompanyResponse[]>("/api/v1/installation/companies"))!, x => x.CompanyId == company);
            using (var unconfirmed = await adminClient.PostAsJsonAsync("/api/v1/installation/company-operations/delete", new DeleteCompanyRequest(Guid.NewGuid(), company, false, false)))
            {
                Assert.Equal(HttpStatusCode.BadRequest, unconfirmed.StatusCode);
                Assert.Contains("archive_confirmation_required", await unconfirmed.Content.ReadAsStringAsync());
            }
            using var accepted = await adminClient.PostAsJsonAsync("/api/v1/installation/company-operations/delete", new DeleteCompanyRequest(Guid.NewGuid(), company, true, false));
            Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
            var deletion = (await accepted.Content.ReadFromJsonAsync<CompanyOperationResponse>())!;
            Assert.Equal(("Delete", true), (deletion.Kind, deletion.CreateArchive));
            Assert.Equal(HttpStatusCode.Conflict, (await adminClient.PostAsJsonAsync("/api/v1/installation/company-operations/delete",
                new DeleteCompanyRequest(Guid.NewGuid(), company, true, false))).StatusCode); // Drugie usuwanie tej firmy.

            string archive;
            await using (var db = tx.Context())
            {
                var queued = await db.CompanyOperations.SingleAsync(x => x.Id == deletion.Id);
                archive = Path.Combine(root, queued.BackupFileName!);
                Assert.Null(queued.NextAttemptAtUtc); // Z archiwum bez zwłoki.
                Assert.False((await db.Companies.AsNoTracking().SingleAsync(x => x.Id == company)).IsActive); // Niedostępna od zlecenia.
            }
            Assert.StartsWith(Path.Combine(root, "_archive"), archive);
            await using (var db = tx.Context()) Assert.True(await Executor(db, clock, "worker", backupRoot: root).RunNext(default));

            Assert.False(await DatabaseExists(provisioned.DatabaseName));
            Assert.False(File.Exists(regularBackup)); Assert.False(File.Exists(BackupStorage.ManifestPath(regularBackup)));
            Assert.True(File.Exists(archive));
            var manifest = JsonDocument.Parse(await File.ReadAllTextAsync(BackupStorage.ManifestPath(archive))).RootElement;
            Assert.Equal(("Archive", company), (manifest.GetProperty("kind").GetString(), manifest.GetProperty("companyId").GetGuid()));
            // Usuwana historia z System zachowana obok archiwum bazy.
            var history = JsonDocument.Parse(await File.ReadAllTextAsync(CompanyOperationExecutor.HistoryPath(archive))).RootElement;
            Assert.Equal((CompanyOperationExecutor.HistoryFormat, company), (history.GetProperty("format").GetString(), history.GetProperty("companyId").GetGuid()));
            Assert.Contains(history.GetProperty("operations").EnumerateArray(), x => x.GetProperty("id").GetGuid() == provisioned.Id);
            Assert.Contains(history.GetProperty("operations").EnumerateArray(), x => x.GetProperty("id").GetGuid() == deletion.Id);
            Assert.True(history.GetProperty("operationEvents").GetArrayLength() > 0);
            Assert.Contains(history.GetProperty("userAdministrationChanges").EnumerateArray(), x => x.GetProperty("targetId").GetGuid() == plain.Id);
            Assert.Contains(history.GetProperty("userAdministrationChanges").EnumerateArray(), x => x.GetProperty("operation").GetString() == "CompanyCreatorAccessGranted");
            Assert.Equal(2, history.GetProperty("companyAccess").GetArrayLength());
            Assert.Equal("Daily", history.GetProperty("backupSchedule").GetProperty("frequency").GetString());
            Assert.Single(history.GetProperty("backups").EnumerateArray());
            Assert.Single(history.GetProperty("backupSettingChanges").EnumerateArray());

            await using (var db = tx.Context())
            {
                Assert.False(await db.Companies.AnyAsync(x => x.Id == company));
                Assert.False(await db.CompanyDatabases.AnyAsync(x => x.CompanyId == company));
                Assert.False(await db.CompanyAccesses.AnyAsync(x => x.CompanyId == company));
                Assert.False(await db.CompanyOperations.AnyAsync(x => x.CompanyId == company));
                Assert.False(await db.CompanyOperationEvents.AnyAsync(x => x.OperationId == provisioned.Id || x.OperationId == deletion.Id));
                Assert.False(await db.CompanyBackups.AnyAsync(x => x.CompanyId == company));
                Assert.False(await db.CompanyBackupSchedules.AnyAsync(x => x.CompanyId == company));
                Assert.False(await db.BackupReminderSnoozes.AnyAsync(x => x.CompanyId == company));
                Assert.False(await db.BackupSettingChanges.AnyAsync(x => x.CompanyId == company));
                Assert.False(await db.UserAdministrationChanges.AnyAsync(x => x.CompanyId == company));
                var deleted = await db.DeletedCompanies.AsNoTracking().SingleAsync(x => x.CompanyId == company);
                Assert.Equal((name, admin.Id, admin.UserName), (deleted.CompanyName, deleted.DeletedById, deleted.DeletedByName));
                Assert.Equal(manifest.GetProperty("sha256").GetString(), deleted.ArchiveSha256);
                Assert.Equal(new FileInfo(archive).Length, deleted.ArchiveSizeBytes);

                // Nazwa firmy i bazy znów wolna; identyfikator pozostaje zablokowany w rejestrze.
                var (again, created) = await new CompanyOperationRequests(db, clock).RequestProvision(new(admin.Id, admin.UserName!), Guid.NewGuid(), name,
                    "LocalWindows", Server, default);
                Assert.True(created); Assert.Equal(provisioned.DatabaseName, again.DatabaseName); Assert.NotEqual(company, again.CompanyId);
            }
            var listed = (await adminClient.GetFromJsonAsync<DeletedCompanyResponse[]>("/api/v1/installation/deleted-companies"))!;
            Assert.Contains(listed, x => x.CompanyId == company && x.HasArchive);

            // Wyjątek od niezmienności działa tylko w kontekście czyszczenia i tylko dla wierszy tej firmy.
            await using (var db = tx.Context())
            {
                var other = await db.UserAdministrationChanges.AsNoTracking().Where(x => x.CompanyId != null).Select(x => x.Id).FirstOrDefaultAsync();
                if (other != 0)
                {
                    var error = await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync(
                        $"DELETE FROM audit.UserAdministrationChanges WHERE Id = {other}"));
                    Assert.Equal(51000, error.Number);
                    await db.Database.ExecuteSqlInterpolatedAsync($"EXEC sys.sp_set_session_context @key = N'Vatowiec.PurgeCompanyId', @value = {company};");
                    Assert.Equal(51000, (await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync(
                        $"DELETE FROM audit.UserAdministrationChanges WHERE Id = {other}"))).Number);
                    await db.Database.ExecuteSqlRawAsync("EXEC sys.sp_set_session_context @key = N'Vatowiec.PurgeCompanyId', @value = NULL;");
                }
            }
        }
        finally
        {
            if (provisioned is not null) await DropOwned(provisioned);
            Directory.Delete(root, recursive: true);
        }
    }

    [ProvisioningSqlFact]
    public async Task Delete_WithoutArchiveWaitsTenMinutes_CancelRestoresAccess_FailureCanBeCancelled_ThenDeletesWithoutHistoryFile()
    {
        await using var tx = await PreservedSystemTransaction.Open(); using var app = App(tx);
        using var scope = app.Services.CreateScope();
        var admin = await Account(scope.ServiceProvider, true, "Fixture!1a-" + Guid.NewGuid().ToString("N"));
        var clock = new MutableClock(DateTimeOffset.UtcNow);
        CompanyOperation? provisioned = null;
        var root = Path.Combine(BackupBase, "tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var requester = new OperationRequester(admin.Id, admin.UserName!);
        async Task Cancel(Guid operation)
        {
            await using var db = tx.Context();
            var state = await db.CompanyOperations.AsNoTracking().SingleAsync(x => x.Id == operation);
            await new CompanyOperationRequests(db, clock).Cancel(requester, operation, Convert.ToBase64String(state.RowVersion), default);
        }
        async Task<bool> Active() { await using var db = tx.Context(); return (await db.Companies.AsNoTracking().SingleAsync(x => x.Id == provisioned!.CompanyId)).IsActive; }
        try
        {
            provisioned = await Provision(tx, admin, clock, "Firma anulowana " + Guid.NewGuid().ToString("N")[..8]);
            CompanyOperation deletion;

            // 1. Bez archiwum: zlecenie czeka 10 minut; firma od razu niedostępna; anulowanie w tym czasie przywraca dostęp.
            await using (var db = tx.Context())
            {
                var requests = new CompanyOperationRequests(db, clock);
                Assert.Equal(400, (await Assert.ThrowsAsync<CompanyOperationException>(() =>
                    requests.RequestDelete(requester, Guid.NewGuid(), provisioned.CompanyId, false, false, default))).Status);
                (deletion, _) = await requests.RequestDelete(requester, Guid.NewGuid(), provisioned.CompanyId, false, true, default);
                Assert.Equal(clock.GetUtcNow() + CompanyOperationRequests.DeleteWithoutArchiveGrace, deletion.NextAttemptAtUtc);
            }
            Assert.False(await Active());
            clock.Advance(TimeSpan.FromMinutes(9));
            await using (var db = tx.Context()) Assert.False(await Executor(db, clock, "worker", backupRoot: root).RunNext(default));
            await Cancel(deletion.Id);
            Assert.True(await Active());
            clock.Advance(TimeSpan.FromHours(1));
            await using (var db = tx.Context()) Assert.False(await Executor(db, clock, "worker", backupRoot: root).RunNext(default)); // Anulowane nie wraca.

            // 2. Z archiwum: awaria po dezaktywacji, anulowanie przed usunięciem bazy.
            await using (var db = tx.Context())
                (deletion, _) = await new CompanyOperationRequests(db, clock).RequestDelete(requester, Guid.NewGuid(), provisioned.CompanyId, true, false, default);
            await using (var db = tx.Context())
                Assert.True(await Executor(db, clock, "worker", stage => stage == CompanyOperationStages.CompanyDeactivated
                    ? throw new InvalidOperationException("injected") : Task.CompletedTask, root).RunNext(default));
            await using (var db = tx.Context())
                Assert.Equal((CompanyOperationStages.CompanyDeactivated, CompanyOperationStatuses.Pending),
                    await db.CompanyOperations.AsNoTracking().Where(x => x.Id == deletion.Id).Select(x => ValueTuple.Create(x.Stage, x.Status)).SingleAsync());
            await Cancel(deletion.Id);
            Assert.True(await Active());
            Assert.True(await DatabaseExists(provisioned.DatabaseName));

            // 3. Bez archiwum po upływie zwłoki: baza usunięta, bez pliku historii.
            await using (var db = tx.Context())
                (deletion, _) = await new CompanyOperationRequests(db, clock).RequestDelete(requester, Guid.NewGuid(), provisioned.CompanyId, false, true, default);
            clock.Advance(CompanyOperationRequests.DeleteWithoutArchiveGrace + TimeSpan.FromSeconds(1));
            await using (var db = tx.Context()) Assert.True(await Executor(db, clock, "worker", backupRoot: root).RunNext(default));
            Assert.False(await DatabaseExists(provisioned.DatabaseName));
            Assert.False(Directory.Exists(Path.Combine(root, "_archive")) && Directory.EnumerateFiles(Path.Combine(root, "_archive"), "*", SearchOption.AllDirectories).Any());
            await using (var db = tx.Context())
            {
                Assert.False(await db.Companies.AnyAsync(x => x.Id == provisioned.CompanyId));
                var deleted = await db.DeletedCompanies.AsNoTracking().SingleAsync(x => x.CompanyId == provisioned.CompanyId);
                Assert.Null(deleted.ArchiveFileName);
            }
        }
        finally
        {
            if (provisioned is not null) await DropOwned(provisioned);
            Directory.Delete(root, recursive: true);
        }
    }
}
