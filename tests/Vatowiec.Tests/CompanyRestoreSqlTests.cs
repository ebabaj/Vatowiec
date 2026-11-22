using System.Net;
using System.Net.Http.Json;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Vatowiec.Core.Application;
using Vatowiec.Core.Domain;
using Vatowiec.Core.Infrastructure.Identity;
using Vatowiec.Core.Infrastructure.Operations;
using Vatowiec.Core.Infrastructure.Persistence;
using Vatowiec.Operations;
using Vatowiec.SharedContracts;
using static Vatowiec.Tests.CompanyProvisioningSqlTests;

namespace Vatowiec.Tests;

// ADR-044 krok 3 na lokalnym SQLEXPRESS: odtworzenie istniejącej, usuniętej i przeniesionej firmy.
// Zmiany w Vatowiec_System są wycofywane; bazy i pliki utworzone przez test są usuwane w finally.
[Collection("LocalSql")]
[Trait("Category", "SqlIntegration")]
public sealed class CompanyRestoreSqlTests
{
    private static string BackupBase => Environment.GetEnvironmentVariable("VATOWIEC_BACKUP_ROOT") ?? @"C:\ProgramData\Vatowiec\Backups";

    private sealed class Scenario : IAsyncDisposable
    {
        public required PreservedSystemTransaction Tx { get; init; }
        public required Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> App { get; init; }
        public required ApplicationUser Admin { get; init; }
        public required string Password { get; init; }
        public required string Root { get; init; }
        public MutableClock Clock { get; } = new(DateTimeOffset.UtcNow);
        public List<string> Prefixes { get; } = [];
        public OperationRequester Requester => new(Admin.Id, Admin.UserName!);
        public CompanyOperationExecutor Executor(SystemDbContext db) => CompanyProvisioningSqlTests.Executor(db, Clock, "worker", backupRoot: Root);

        public async ValueTask DisposeAsync()
        {
            // Wyłącznie bazy tego testu: nazwy z unikalnym sufiksem przebiegu.
            foreach (var prefix in Prefixes)
                foreach (var database in await Databases(prefix))
                    if (CompanyDatabaseNames.IsProjectDatabase(database)) await Drop(database);
            if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true);
            App.Dispose();
            await Tx.DisposeAsync();
        }
    }

    private static async Task<Scenario> Start()
    {
        var tx = await PreservedSystemTransaction.Open();
        var app = App(tx);
        using var scope = app.Services.CreateScope();
        var password = "Fixture!1a-" + Guid.NewGuid().ToString("N");
        var admin = await Account(scope.ServiceProvider, true, password);
        var root = Path.Combine(BackupBase, "tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return new Scenario { Tx = tx, App = app, Admin = admin, Password = password, Root = root };
    }

    private static async Task<CompanyOperation> Provision(Scenario s, string name)
    {
        CompanyOperation operation;
        await using (var db = s.Tx.Context())
            (operation, _) = await new CompanyOperationRequests(db, s.Clock).RequestProvision(s.Requester, Guid.NewGuid(), name, "LocalWindows", Server, default);
        s.Prefixes.Add(operation.DatabaseName);
        await using (var db = s.Tx.Context()) Assert.True(await s.Executor(db).RunNext(default));
        return operation;
    }

    private static CompanyDbContext Company(string database, Guid companyId) => new(
        new DbContextOptionsBuilder<CompanyDbContext>().UseSqlServer(Local + ";Database=" + database).Options, new CompanyContext(companyId));

    private static async Task AddContractor(string database, Guid companyId, string code)
    {
        await using var company = Company(database, companyId);
        company.Contractors.Add(new Contractor(code, "Kontrahent fikcyjny " + code, "PL"));
        await company.SaveChangesAsync();
    }

    private static async Task<string[]> Codes(string database, Guid companyId)
    {
        await using var company = Company(database, companyId);
        return await company.Contractors.OrderBy(x => x.Code).Select(x => x.Code).ToArrayAsync();
    }

    private static async Task<long> Backup(Scenario s, Guid companyId)
    {
        Guid operation;
        await using (var db = s.Tx.Context()) operation = (await new CompanyBackupService(db, s.Clock).Request(s.Requester, companyId, Guid.NewGuid(), default)).Operation.Id;
        await using (var db = s.Tx.Context()) Assert.True(await s.Executor(db).RunNext(default));
        await using (var db = s.Tx.Context()) return (await db.CompanyBackups.SingleAsync(x => x.OperationId == operation)).Id;
    }

    private static async Task<CompanyOperation> Run(Scenario s, Guid operationId)
    {
        await using (var db = s.Tx.Context()) Assert.True(await s.Executor(db).RunNext(default));
        await using (var db = s.Tx.Context()) return await db.CompanyOperations.AsNoTracking().SingleAsync(x => x.Id == operationId);
    }

    private static async Task<string[]> Databases(string prefix)
    {
        await using var connection = new SqlConnection(Local + ";Database=master");
        await connection.OpenAsync();
        await using var command = new SqlCommand("SELECT name FROM sys.databases WHERE LEFT(name, LEN(@prefix)) = @prefix", connection);
        command.Parameters.AddWithValue("@prefix", prefix);
        var names = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync()) names.Add(reader.GetString(0));
        return names.ToArray();
    }

    [ProvisioningSqlFact]
    public async Task Existing_RestoreKeepsPreviousDatabaseAndSafetyCopy_ThenDeletedCompanyComesBackFromArchive()
    {
        await using var s = await Start();
        var name = "Odtwarzana " + Guid.NewGuid().ToString("N")[..8];
        var provisioned = await Provision(s, name);
        var company = provisioned.CompanyId;
        await AddContractor(provisioned.DatabaseName, company, "PRZED");
        var backup = await Backup(s, company);
        await AddContractor(provisioned.DatabaseName, company, "PO");

        // API: tylko administrator instalacji; zastąpienie danych wymaga potwierdzenia.
        using var admin = Client(s.App);
        await GusConfigurationSqlTests.Login(admin, s.Admin.UserName!, s.Password);
        var sources = (await admin.GetFromJsonAsync<RestoreSourceResponse[]>("/api/v1/installation/restore-sources"))!;
        Assert.Contains(sources, x => x.Source == "Backup" && x.BackupId == backup && x.CompanyExists);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/v1/installation/company-operations/restore",
            new RestoreCompanyRequest(Guid.NewGuid(), "Backup", backup, null, null, false))).StatusCode);
        using var accepted = await admin.PostAsJsonAsync("/api/v1/installation/company-operations/restore",
            new RestoreCompanyRequest(Guid.NewGuid(), "Backup", backup, null, null, true));
        Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
        var requested = (await accepted.Content.ReadFromJsonAsync<CompanyOperationResponse>())!;

        var restore = await Run(s, requested.Id);
        Assert.Equal((CompanyOperationStatuses.Completed, RestoreModes.Existing, (string?)null), (restore.Status, restore.RestoreMode, restore.LastErrorCode));
        Assert.Equal(["PRZED"], await Codes(provisioned.DatabaseName, company)); // Stan z kopii pod właściwą nazwą.
        Assert.Equal(["PO", "PRZED"], await Codes(restore.PreviousDatabaseName!, company)); // Dotychczasowa baza zachowana obok.
        Assert.False((await Databases(restore.WorkingDatabaseName!)).Any());
        await using (var db = s.Tx.Context())
        {
            Assert.True((await db.Companies.AsNoTracking().SingleAsync(x => x.Id == company)).IsActive);
            Assert.Equal(provisioned.DatabaseName, (await db.CompanyDatabases.AsNoTracking().SingleAsync(x => x.CompanyId == company)).DatabaseName);
            var safety = await db.CompanyBackups.AsNoTracking().SingleAsync(x => x.OperationId == restore.Id);
            Assert.Equal(CompanyBackup.PreRestoreKind, safety.Kind);
            Assert.True(File.Exists(Path.Combine(s.Root, safety.FileName)));
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["CompanyConnections:LocalWindows"] = Local }).Build();
            await using var resolved = await new CompanyDatabaseResolver(db, configuration, NullLogger<CompanyDatabaseResolver>.Instance)
                .Resolve(s.Admin.Id, company, CompanyPermission.ViewProfile, isDevelopment: true);
            Assert.Equal(["PRZED"], await resolved.Context.Contractors.Select(x => x.Code).ToArrayAsync());
        }

        // Usunięcie z archiwum usuwa też bazę …_przed_…; przywrócenie z archiwum pod tym samym CompanyId.
        Guid deletion;
        await using (var db = s.Tx.Context())
            deletion = (await new CompanyOperationRequests(db, s.Clock).RequestDelete(s.Requester, Guid.NewGuid(), company, true, false, default)).Operation.Id;
        await using (var db = s.Tx.Context()) Assert.True(await s.Executor(db).RunNext(default));
        Assert.Empty(await Databases(provisioned.DatabaseName));
        await using (var db = s.Tx.Context()) Assert.True(await db.DeletedCompanies.AnyAsync(x => x.CompanyId == company));

        CompanyOperation back;
        await using (var db = s.Tx.Context())
            (back, _) = await new CompanyOperationRequests(db, s.Clock).RequestRestore(s.Requester,
                new RestoreCompanyRequest(Guid.NewGuid(), "Deleted", null, company, null, false), Server, "LocalWindows", default);
        Assert.Equal(RestoreModes.Deleted, back.RestoreMode);
        back = await Run(s, back.Id);
        Assert.Equal((CompanyOperationStatuses.Completed, (string?)null), (back.Status, back.LastErrorCode));
        Assert.Equal(["PRZED"], await Codes(provisioned.DatabaseName, company));
        await using (var db = s.Tx.Context())
        {
            Assert.Equal(name, (await db.Companies.AsNoTracking().SingleAsync(x => x.Id == company && x.IsActive)).Name);
            Assert.Equal(provisioned.DatabaseName, (await db.CompanyDatabases.AsNoTracking().SingleAsync(x => x.CompanyId == company)).DatabaseName);
            Assert.Equal(CompanyPermissionCatalog.All, (await db.CompanyAccesses.AsNoTracking().SingleAsync(x => x.CompanyId == company && x.UserId == s.Admin.Id)).Permissions);
            Assert.Contains(await db.UserAdministrationChanges.AsNoTracking().Where(x => x.CompanyId == company).Select(x => x.Operation).ToArrayAsync(), x => x == "CompanyRestorerAccessGranted");
            Assert.False(await db.DeletedCompanies.AnyAsync(x => x.CompanyId == company));
        }
    }

    [ProvisioningSqlFact]
    public async Task Import_FromAnotherInstallationWithOlderSchema_IsMigratedAndRegisteredWithTheSameCompanyId()
    {
        await using var s = await Start();
        var provisioned = await Provision(s, "Przenoszona " + Guid.NewGuid().ToString("N")[..8]);
        var company = provisioned.CompanyId;
        await AddContractor(provisioned.DatabaseName, company, "IMPORT");
        // Starsza wersja programu: baza bez ostatniej migracji.
        await using (var older = Company(provisioned.DatabaseName, company))
        {
            var migrations = older.Database.GetMigrations().ToArray();
            await older.GetService<IMigrator>().MigrateAsync(migrations[^2]);
        }
        var backupId = await Backup(s, company);
        string backupFile;
        await using (var db = s.Tx.Context()) backupFile = Path.Combine(s.Root, (await db.CompanyBackups.SingleAsync(x => x.Id == backupId)).FileName);
        var import = Path.Combine(s.Root, BackupImportScanner.ImportFolder);
        Directory.CreateDirectory(import);
        File.Copy(backupFile, Path.Combine(import, "obca_kopia.bak"));
        File.Copy(BackupStorage.ManifestPath(backupFile), Path.Combine(import, "obca_kopia.manifest.json"));

        // „Inna instalacja”: firmy tu nie ma i nie ma jej w rejestrze usuniętych.
        Guid deletion;
        await using (var db = s.Tx.Context())
            deletion = (await new CompanyOperationRequests(db, s.Clock).RequestDelete(s.Requester, Guid.NewGuid(), company, false, true, default)).Operation.Id;
        s.Clock.Advance(CompanyOperationRequests.DeleteWithoutArchiveGrace + TimeSpan.FromSeconds(1));
        await using (var db = s.Tx.Context()) Assert.True(await s.Executor(db).RunNext(default));
        await using (var db = s.Tx.Context()) await db.DeletedCompanies.Where(x => x.CompanyId == company).ExecuteDeleteAsync();
        Assert.Empty(await Databases(provisioned.DatabaseName));

        await using (var db = s.Tx.Context())
        {
            var storage = new BackupStorage(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Operations:BackupRoot"] = s.Root }).Build());
            Assert.Equal(1, await new BackupImportScanner(db, storage, s.Clock, NullLogger<BackupImportScanner>.Instance).Scan(default));
        }
        RestoreSourceResponse source;
        await using (var db = s.Tx.Context())
            source = Assert.Single(await new CompanyOperationRequests(db, s.Clock).RestoreSources(default), x => x.Source == "Import");
        Assert.Equal(("_import/obca_kopia.bak", company, false), (source.FileName, source.CompanyId, source.CompanyExists));

        CompanyOperation restore;
        await using (var db = s.Tx.Context())
            (restore, _) = await new CompanyOperationRequests(db, s.Clock).RequestRestore(s.Requester,
                new RestoreCompanyRequest(Guid.NewGuid(), "Import", null, null, source.FileName, false), Server, "LocalWindows", default);
        Assert.Equal(RestoreModes.Import, restore.RestoreMode);
        restore = await Run(s, restore.Id);
        Assert.Equal((CompanyOperationStatuses.Completed, (string?)null), (restore.Status, restore.LastErrorCode));
        Assert.Equal(["IMPORT"], await Codes(provisioned.DatabaseName, company));
        await using (var restored = Company(provisioned.DatabaseName, company))
            Assert.Equal(restored.Database.GetMigrations(), await restored.Database.GetAppliedMigrationsAsync()); // Schemat zaktualizowany.
        await using (var db = s.Tx.Context())
            Assert.True(await db.CompanyAccesses.AnyAsync(x => x.CompanyId == company && x.UserId == s.Admin.Id));
    }

    [ProvisioningSqlFact]
    public async Task DamagedBackupFile_IsRejectedBeforeTouchingTheCompany()
    {
        await using var s = await Start();
        var provisioned = await Provision(s, "Uszkodzona " + Guid.NewGuid().ToString("N")[..8]);
        await AddContractor(provisioned.DatabaseName, provisioned.CompanyId, "ZOSTAJE");
        var backupId = await Backup(s, provisioned.CompanyId);
        await using (var db = s.Tx.Context())
        {
            // SQL Server zakłada na plikach kopii własne uprawnienia; podmiana pliku zamiast dopisywania.
            var path = Path.Combine(s.Root, (await db.CompanyBackups.SingleAsync(x => x.Id == backupId)).FileName);
            var bytes = await File.ReadAllBytesAsync(path);
            File.Delete(path);
            bytes[^1] ^= 0xFF;
            await File.WriteAllBytesAsync(path, bytes);
        }
        CompanyOperation restore;
        await using (var db = s.Tx.Context())
            (restore, _) = await new CompanyOperationRequests(db, s.Clock).RequestRestore(s.Requester,
                new RestoreCompanyRequest(Guid.NewGuid(), "Backup", backupId, null, null, true), Server, "LocalWindows", default);
        restore = await Run(s, restore.Id);
        Assert.Equal((CompanyOperationStatuses.NeedsReconciliation, "backup_checksum_mismatch"), (restore.Status, restore.LastErrorCode));
        Assert.Equal(["ZOSTAJE"], await Codes(provisioned.DatabaseName, provisioned.CompanyId));
        Assert.Empty(await Databases(restore.WorkingDatabaseName!));
        await using (var db = s.Tx.Context()) Assert.True((await db.Companies.AsNoTracking().SingleAsync(x => x.Id == provisioned.CompanyId)).IsActive);
    }
}
