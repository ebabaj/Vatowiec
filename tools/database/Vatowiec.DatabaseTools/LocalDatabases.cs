using System.Diagnostics;
using System.Xml.Linq;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Vatowiec.Core.Application;
using Vatowiec.Core.Domain;
using Vatowiec.Core.Infrastructure.Persistence;
using Vatowiec.Core.Infrastructure.Identity;

namespace Vatowiec.DatabaseTools;

internal static class LocalDatabases
{
    private const string Server = @"localhost\SQLEXPRESS";
    private const string Marker = "Vatowiec.LocalTestPurpose";
    private const string SystemDatabase = "Vatowiec_System";
    private static readonly string[] TestDatabases =
        ["Vatowiec_System", "Vatowiec_Company_A_Dev", "Vatowiec_Company_B_Dev"];
    private static readonly (Guid Id, string Name, string Database)[] DemoCompanies =
    [
        (Guid.Parse("aaaaaaaa-0000-4000-8000-000000000001"), "Firma demonstracyjna A — dane fikcyjne", "Vatowiec_Company_A_Dev"),
        (Guid.Parse("bbbbbbbb-0000-4000-8000-000000000002"), "Firma demonstracyjna B — dane fikcyjne", "Vatowiec_Company_B_Dev")
    ];


    private static string Connection(string database) => new SqlConnectionStringBuilder
    {
        DataSource = Server,
        InitialCatalog = database,
        IntegratedSecurity = true,
        Encrypt = SqlConnectionEncryptOption.Mandatory,
        TrustServerCertificate = true,
        ConnectTimeout = 5,
        Pooling = false
    }.ConnectionString;

    private static string Purpose(string database) => "VATowiec M0 fictional company demonstration v1";

    private static void CheckName(string database)
    {
        if (database != SystemDatabase &&
            !DemoCompanies.Any(x => x.Database == database) && !TestDatabases.Contains(database, StringComparer.Ordinal))
            throw new InvalidOperationException("Baza spoza jawnej listy lokalnych baz testowych VATowca.");
    }

    private static string Identifier(string database)
    {
        CheckName(database);
        return $"[{database}]"; // Wyłącznie stałe nazwy po sprawdzeniu allowlisty.
    }

    private static async Task<int> Execute(string database, string sql, params SqlParameter[] parameters)
    {
        await using var connection = new SqlConnection(Connection(database));
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection) { CommandTimeout = 60 };
        command.Parameters.AddRange(parameters);
        return await command.ExecuteNonQueryAsync();
    }

    private static async Task<object?> Scalar(string database, string sql, params SqlParameter[] parameters)
    {
        await using var connection = new SqlConnection(Connection(database));
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection) { CommandTimeout = 15 };
        command.Parameters.AddRange(parameters);
        return await command.ExecuteScalarAsync();
    }

    public static async Task EnsureLocalServer()
    {
        var local = await Scalar("master", """
            SELECT CASE WHEN CONVERT(nvarchar(128), SERVERPROPERTY('MachineName')) = @machine
                AND CONVERT(nvarchar(128), SERVERPROPERTY('InstanceName')) = N'SQLEXPRESS'
                AND CONVERT(int, SERVERPROPERTY('ProductMajorVersion')) = 17
                AND CONVERT(nvarchar(128), SERVERPROPERTY('Edition')) LIKE N'Express%'
                AND CONVERT(nvarchar(128), CONNECTIONPROPERTY('auth_scheme')) IN (N'NTLM', N'KERBEROS')
                THEN 1 ELSE 0 END;
            """, new SqlParameter("@machine", Environment.MachineName));
        if (!Equals(local, 1)) throw new InvalidOperationException("Wymagana lokalna instancja SQL Server 2025 Express i Windows Authentication.");
        Console.WriteLine($"Serwer: {Server}; SQL Server 2025 Express; Windows Authentication; TLS.");
    }

    private static async Task<bool> Exists(string database)
    {
        CheckName(database);
        return Equals(await Scalar("master", "SELECT COUNT(*) FROM sys.databases WHERE name = @name;",
            new SqlParameter("@name", database)), 1);
    }

    private static async Task CheckOwnership(string database)
    {
        CheckName(database);
        var marker = await Scalar(database,
            "SELECT CONVERT(nvarchar(256), value) FROM sys.extended_properties WHERE class = 0 AND name = @marker;",
            new SqlParameter("@marker", Marker));
        if (!Equals(marker, Purpose(database)))
            throw new InvalidOperationException($"Niepotwierdzone przeznaczenie {database}; nie będzie zmieniana ani usuwana.");
    }

    public static async Task Inspect()
    {
        await using var connection = new SqlConnection(Connection("master"));
        await connection.OpenAsync();
        await using var command = new SqlCommand("SELECT name, state_desc FROM sys.databases ORDER BY database_id;", connection);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync()) Console.WriteLine($"{reader.GetString(0)} | {reader.GetString(1)}");
        foreach (var database in TestDatabases)
            if (await Exists(database)) await CheckDemoData(database);
    }

    private static async Task CheckDemoData(string database)
    {
        await EnsureLocalServer();
        await CheckOwnership(database);
        // Odczyt bez EF: obsługuje zatwierdzony stary demonstrator oraz nowy model.
        // Nie wystarcza nazwa bazy. Nieznany schemat lub nieoznaczone dane blokują DROP.
        var company = database != SystemDatabase;
        var expectedOld = company ? 4 : 13;
        var expectedNew = company ? 9 : 13;
        var tableCount = Convert.ToInt32(await Scalar(database, "SELECT COUNT(*) FROM sys.tables WHERE is_ms_shipped=0;"));
        if (tableCount != expectedOld && tableCount != expectedNew)
            throw new InvalidOperationException($"Nieznany schemat {database}; odmowa odtworzenia.");
        var unknownTables = await Scalar(database, """
            SELECT COUNT(*) FROM sys.tables t JOIN sys.schemas s ON s.schema_id=t.schema_id
            WHERE t.is_ms_shipped=0 AND s.name+'.'+t.name NOT IN (
                'core.Companies','core.CompanyDatabases','core.CompanyProfile','core.Contractors',
                'core.ContractorAddresses','core.ContractorContacts','core.ContractorBankAccounts',
                'core.ContractorCreateOperations','ref.AddressTypes','audit.ContractorChanges',
                'dbo.__EFMigrationsHistory','security.Users','security.Roles','security.UserRoles',
                'security.UserClaims','security.UserLogins','security.UserTokens','security.RoleClaims',
                'security.CompanyAccess','security.UserSessions','security.Installation');
            """);
        if (Convert.ToInt32(unknownTables) != 0) throw new InvalidOperationException($"Obce tabele w {database}.");
        var unknownHistory = await Scalar(database, """
            SELECT COUNT(*) FROM dbo.__EFMigrationsHistory WHERE MigrationId NOT IN (
                '20261021102912_InitialCompany','20261022060000_ContractorAudit','20261022143310_CompanyModelV2','20261022161050_ContractorDetailAudit',
                '20261021102909_InitialSystem','20261021114324_IdentityAndCompanyAccess','20261022055958_InstallationIdentity');
            """);
        // Rzeczywiste identyfikatory migracji systemowych weryfikuje niżej kontekst.
        if (company && Convert.ToInt32(unknownHistory) != 0) throw new InvalidOperationException($"Obca historia migracji w {database}.");
        if (!company) await using (var system = SystemContext(database)) await CheckKnownHistory(system);
        if (company)
        {
            var profileOk = await Scalar(database, """
                SELECT COUNT(*) FROM core.CompanyProfile WHERE Nip IS NOT NULL OR Regon IS NOT NULL
                    OR (Name NOT LIKE N'%fikcyjn%' AND Name NOT LIKE N'%demonstracyjn%' AND Name NOT LIKE N'Testowa firma%');
                """);
            if (Convert.ToInt32(profileOk) != 0) throw new InvalidOperationException($"Niepotwierdzone fikcyjne dane profilu w {database}.");
            var taxColumn = Equals(await Scalar(database, "SELECT COUNT(*) FROM sys.columns WHERE object_id=OBJECT_ID('core.Contractors') AND name='TaxIdentifier';"), 1)
                ? "TaxIdentifier" : "TaxId";
            var unknownTax = await Scalar(database, $"""
                SELECT COUNT(*) FROM core.Contractors WHERE [{taxColumn}] IS NOT NULL
                    AND [{taxColumn}] NOT LIKE N'DEMO%' AND [{taxColumn}] NOT LIKE N'TEST%'
                    AND [{taxColumn}] NOT LIKE N'FIKCYJN%';
                """);
            if (Convert.ToInt32(unknownTax) != 0) throw new InvalidOperationException($"Niepotwierdzone identyfikatory podatkowe w {database}.");
            var unknownNames = await Scalar(database, """
                SELECT COUNT(*) FROM core.Contractors WHERE Name NOT LIKE N'%fikcyjn%'
                    AND Name NOT LIKE N'%demonstracyjn%' AND Name NOT LIKE N'Kontrahent [AB]'
                    AND Name NOT LIKE N'%UIAUTO%' AND Name NOT LIKE N'%UI-AUTO%'
                    AND Name NOT LIKE N'%test%' AND Name NOT LIKE N'%automatyzac%';
                """);
            if (Convert.ToInt32(unknownNames) != 0) throw new InvalidOperationException($"Niepotwierdzone nazwy demonstracyjne w {database}; odmowa odtworzenia.");
            if (tableCount == expectedNew)
            {
                var unknownAccounts = await Scalar(database, """
                    SELECT COUNT(*) FROM core.ContractorBankAccounts WHERE AccountNumber NOT LIKE N'DEMO%'
                        AND AccountNumber NOT LIKE N'TEST%' AND AccountNumber NOT LIKE N'FIKCYJN%'
                        AND AccountNumber NOT IN (N'00000000000000000000000000',N'00000000000000000000000001',
                            N'00000000000000000000000002',N'00000000000000000000000003',N'00000000000000000000000004');
                    """);
                if (Convert.ToInt32(unknownAccounts) != 0) throw new InvalidOperationException($"Niepotwierdzone numery rachunków w {database}.");
            }
        }
        else
        {
            var unknownCompanies = await Scalar(database, """
                SELECT COUNT(*) FROM core.Companies WHERE Name NOT LIKE N'%demonstracyjn%' AND Name NOT LIKE N'%test%' AND Name NOT LIKE N'%fikcyjn%';
                """);
            var unknownUsers = await Scalar(database, """
                SELECT COUNT(*) FROM security.Users WHERE UserName NOT IN (N'operator.m0',N'czytelnik.m0',N'operator.test',N'reader.test');
                """);
            if (Convert.ToInt32(unknownCompanies) != 0 || Convert.ToInt32(unknownUsers) != 0)
                throw new InvalidOperationException($"Niepotwierdzone dane demonstracyjne w {database}.");
        }
        Console.WriteLine($"Preflight {database}: dokładna nazwa, znacznik demonstratora, {tableCount} znanych tabel; brak nieoznaczonych danych w kontrolowanych kartotekach.");
    }

    private static async Task CreateOrCheck(string database)
    {
        CheckName(database);
        if (await Exists(database))
        {
            await CheckOwnership(database);
            Console.WriteLine($"Istniejąca własna baza testowa: {database}.");
            return;
        }
        await Execute("master", $"CREATE DATABASE {Identifier(database)};");
        await Execute(database,
            "EXEC sys.sp_addextendedproperty @name = @marker, @value = @purpose;",
            new SqlParameter("@marker", Marker), new SqlParameter("@purpose", Purpose(database)));
        Console.WriteLine($"Utworzono: {database} ({Purpose(database)}).");
    }

    private static async Task Drop(string database)
    {
        CheckName(database);
        if (!await Exists(database)) return;
        await CheckDemoData(database);
        Console.WriteLine($"Usuwanie potwierdzonej bazy testowej: {database}.");
        await Execute("master", $"ALTER DATABASE {Identifier(database)} SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE {Identifier(database)};");
    }

    private static SystemDbContext SystemContext(string database)
    {
        DatabaseConnectionPolicy.ValidateSystem(Connection(database), true);
        return new SystemDbContext(new DbContextOptionsBuilder<SystemDbContext>().UseSqlServer(Connection(database)).Options);
    }

    private static CompanyDbContext CompanyContext(string database, Guid? companyId)
    {
        DatabaseConnectionPolicy.ValidateCompany(Connection(database), true);
        return new CompanyDbContext(new DbContextOptionsBuilder<CompanyDbContext>().UseSqlServer(Connection(database)).Options,
            new CompanyContext(companyId));
    }

    public static async Task Initialize()
    {
        // Sprawdź wszystkie zastane cele przed pierwszą operacją zmieniającą dane.
        foreach (var database in new[] { SystemDatabase }.Concat(DemoCompanies.Select(x => x.Database)))
            if (await Exists(database)) await CheckOwnership(database);
        await CreateOrCheck(SystemDatabase);
        await using var system = SystemContext(SystemDatabase);
        await system.Database.MigrateAsync();
        foreach (var (id, name, database) in DemoCompanies)
        {
            await CreateOrCheck(database);
            await using var company = CompanyContext(database, id);
            await company.Database.MigrateAsync();
            await using var transaction = await company.Database.BeginTransactionAsync();
            var profile = await company.CompanyProfiles.IgnoreQueryFilters().SingleOrDefaultAsync();
            if (profile is null) company.CompanyProfiles.Add(new CompanyProfile(id, name));
            else if (profile.CompanyId != id) throw new InvalidOperationException($"Obcy profil w {database}.");
            if (!await company.Contractors.AnyAsync(x => x.Code == "DEMO"))
                company.Contractors.Add(new Contractor("DEMO", $"Kontrahent {name} — fikcyjny", "PL"));
            await company.SaveChangesAsync();
            var demoId = await company.Contractors.Where(x => x.Code == "DEMO").Select(x => x.Id).SingleAsync();
            if (!await company.ContractorAddresses.AnyAsync(x => x.ContractorId == demoId))
                company.ContractorAddresses.Add(new ContractorAddress(demoId, 1)
                { Name = "Adres demonstracyjny", Street = "Fikcyjna", City = "Miejscowość testowa", CountryCode = "PL", IsDefault = true });
            if (!await company.ContractorBankAccounts.AnyAsync(x => x.ContractorId == demoId))
                company.ContractorBankAccounts.Add(new ContractorBankAccount(demoId, "DEMO-RACHUNEK-NIE-DO-PLATNOSCI", "PLN")
                { Name = "Fikcyjny rachunek", IsDefault = true });
            if (!await company.ContractorContacts.AnyAsync(x => x.ContractorId == demoId))
                company.ContractorContacts.Add(new ContractorContact(demoId, "Fikcyjny kontakt")
                { Email = "demo@example.invalid", Street = "Fikcyjna" });
            await company.SaveChangesAsync();
            await transaction.CommitAsync();
        }
        // Katalog publikujemy po poprawnym zainicjalizowaniu obu profili.
        await using (var transaction = await system.Database.BeginTransactionAsync())
        {
            foreach (var (id, name, database) in DemoCompanies)
            {
                if (!await system.Companies.AnyAsync(x => x.Id == id)) system.Companies.Add(new Company(id, name));
                var registration = await system.CompanyDatabases.SingleOrDefaultAsync(x => x.CompanyId == id);
                if (registration is null)
                    system.CompanyDatabases.Add(new CompanyDatabaseRegistration(id, Server, database, "LocalWindows"));
                else if (registration.ServerName != Server || registration.DatabaseName != database || registration.ConnectionProfileKey != "LocalWindows")
                    throw new InvalidOperationException("Niezgodna lokalizacja przykładowej firmy w katalogu.");
            }
            await system.SaveChangesAsync();
            await transaction.CommitAsync();
        }
        await Verify();
    }

    public static async Task PrepareTests()
    {
        foreach (var database in TestDatabases)
            await CheckDemoData(database);
        // Fixture wymagają pustych kartotek, ale nie wymagają DROP/CREATE baz ani tabel.
        await using (var system = SystemContext(SystemDatabase))
        {
            if ((await system.Database.GetPendingMigrationsAsync()).Any()) throw new InvalidOperationException("Najpierw wykonaj migrate-contractors.");
        }
        foreach (var (id, _, database) in DemoCompanies)
        {
            await using var company = CompanyContext(database, id);
            if ((await company.Database.GetPendingMigrationsAsync()).Any()) throw new InvalidOperationException("Najpierw wykonaj migrate-contractors.");
        }
        foreach (var (_, _, database) in DemoCompanies)
            await Execute(database, """
                DELETE FROM core.ContractorCreateOperations;
                DELETE FROM audit.ContractorChanges;
                DELETE FROM core.ContractorAddresses;
                DELETE FROM core.ContractorContacts;
                DELETE FROM core.ContractorBankAccounts;
                UPDATE core.Contractors SET ParentId=NULL;
                DELETE FROM core.Contractors;
                DELETE FROM core.CompanyProfile;
                DELETE FROM ref.AddressTypes WHERE Id NOT IN (1,2);
                """);
        await Execute(SystemDatabase, """
            DELETE FROM security.UserSessions;
            DELETE FROM security.CompanyAccess;
            DELETE FROM security.Users;
            DELETE FROM security.Roles;
            DELETE FROM core.CompanyDatabases;
            DELETE FROM core.Companies;
            """);
        Console.WriteLine("Wyczyszczono fikcyjne fixture w istniejących bazach; bez DROP/CREATE. Po testach nastąpi inicjalizacja demonstratora.");
    }

    public static async Task CleanupTests()
    {
        foreach (var database in TestDatabases)
            if (await Exists(database)) await CheckOwnership(database);
        foreach (var database in TestDatabases) await Drop(database);
    }

    public static async Task Test()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Vatowiec.slnx"))) root = root.Parent;
        if (root is null) throw new InvalidOperationException("Nie znaleziono Vatowiec.slnx.");
        await PrepareTests();
        try
        {
            var reportName = $"database-per-company-{Guid.NewGuid():N}.trx";
            var reportDirectory = Path.Combine(root.FullName, "TestResults");
            var start = new ProcessStartInfo("dotnet")
            {
                WorkingDirectory = root.FullName,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            foreach (var argument in new[] { "test", "Vatowiec.slnx", "--no-build", "--logger", "console;verbosity=normal",
                         "--logger", $"trx;LogFileName={reportName}", "--results-directory", reportDirectory })
                start.ArgumentList.Add(argument);
            start.Environment["DOTNET_ENVIRONMENT"] = "Development";
            start.Environment["VATOWIEC_DATABASE_PER_COMPANY_SQL"] = "1";
            start.Environment["VATOWIEC_DEMO_RESET_AUTHORIZED"] = "1";
            start.Environment["ConnectionStrings__VatowiecSystemTest"] = Connection(TestDatabases[0]);
            start.Environment["ConnectionStrings__VatowiecCompanyATest"] = Connection(TestDatabases[1]);
            start.Environment["ConnectionStrings__VatowiecCompanyBTest"] = Connection(TestDatabases[2]);
            using var process = Process.Start(start) ?? throw new InvalidOperationException("Nie uruchomiono testów.");
            await Task.WhenAll(process.StandardOutput.BaseStream.CopyToAsync(Console.OpenStandardOutput()),
                process.StandardError.BaseStream.CopyToAsync(Console.OpenStandardError()), process.WaitForExitAsync());
            if (process.ExitCode != 0) throw new InvalidOperationException($"Testy zakończyły się kodem {process.ExitCode}.");
            var report = XDocument.Load(Path.Combine(reportDirectory, reportName));
            var counters = report.Descendants().Single(x => x.Name.LocalName == "Counters");
            var passed = (int?)counters.Attribute("passed") ?? 0;
            var total = (int?)counters.Attribute("total") ?? 0;
            if (passed == 0 || total != passed)
                throw new InvalidOperationException("Pełny przebieg musi mieć wszystkie testy zaliczone, bez pominięć; sprawdź TRX.");
            Console.WriteLine($"TRX: {total} testów, {passed} zaliczonych, bez pominięć. {Path.Combine(reportDirectory, reportName)}");
            await CheckEmptyFixtures();
        }
        finally
        {
            await Initialize();
            await SeedUsers();
        }
    }

    private static async Task CheckEmptyFixtures()
    {
        await using var system = SystemContext(TestDatabases[0]);
        if (await system.Companies.AnyAsync() || await system.CompanyDatabases.AnyAsync() ||
            await system.Users.AnyAsync() || await system.Roles.AnyAsync() || await system.CompanyAccesses.AnyAsync() ||
            await system.UserSessions.AnyAsync())
            throw new InvalidOperationException("Testy pozostawiły rekordy w katalogu demonstracyjnym.");
        if (!(await system.Database.GetAppliedMigrationsAsync()).SequenceEqual(system.Database.GetMigrations()))
            throw new InvalidOperationException("Testy zmieniły historię bazy systemowej.");
        foreach (var database in TestDatabases.Skip(1))
        {
            await using var company = CompanyContext(database, null);
            if (await company.CompanyProfiles.IgnoreQueryFilters().AnyAsync() ||
                await company.Contractors.IgnoreQueryFilters().AnyAsync() ||
                await company.ContractorChanges.IgnoreQueryFilters().AnyAsync() ||
                !(await company.Database.GetAppliedMigrationsAsync()).SequenceEqual(company.Database.GetMigrations()))
                throw new InvalidOperationException($"Testy nie wycofały danych lub historii w {database}.");
        }
        Console.WriteLine("Potwierdzono sprzątanie fixture w trzech bazach demonstracyjnych; historie migracji zachowane.");
    }

    private static async Task ShowSchema(DbContext context, string database, string[] expectedTables, string migrationSuffix)
    {
        await CheckOwnership(database);
        var applied = (await context.Database.GetAppliedMigrationsAsync()).ToArray();
        if (applied.Length == 0 || !applied[^1].EndsWith(migrationSuffix, StringComparison.Ordinal) ||
            (await context.Database.GetPendingMigrationsAsync()).Any() || context.Database.HasPendingModelChanges())
            throw new InvalidOperationException($"Niezgodna historia migracji w {database}.");
        await using var connection = new SqlConnection(Connection(database));
        await connection.OpenAsync();
        await using var command = new SqlCommand("""
            SELECT s.name + '.' + t.name FROM sys.tables t JOIN sys.schemas s ON s.schema_id = t.schema_id
            WHERE t.is_ms_shipped = 0 ORDER BY s.name, t.name;
            """, connection);
        await using var reader = await command.ExecuteReaderAsync();
        var tables = new List<string>();
        while (await reader.ReadAsync()) tables.Add(reader.GetString(0));
        if (!tables.Order().SequenceEqual(expectedTables.Order()))
            throw new InvalidOperationException($"Nieoczekiwane tabele w {database}.");
        Console.WriteLine($"{database}: {string.Join(", ", tables)}; migracje {string.Join(", ", applied)}.");
    }

    public static async Task Verify()
    {
        await using var system = SystemContext(SystemDatabase);
        await ShowSchema(system, SystemDatabase, ["core.Companies", "core.CompanyDatabases", "dbo.__EFMigrationsHistory",
            "security.Users", "security.Roles", "security.UserRoles", "security.UserClaims", "security.UserLogins",
            "security.UserTokens", "security.RoleClaims", "security.CompanyAccess", "security.UserSessions", "security.Installation"], "_WarningPermission");
        if (await system.InstallationIdentities.CountAsync() != 1 || (await system.InstallationIdentities.SingleAsync()).Id == Guid.Empty)
            throw new InvalidOperationException("Brak trwałej identyfikacji instalacji.");
        if (await system.Companies.CountAsync() != 2 || await system.CompanyDatabases.CountAsync() != 2)
            throw new InvalidOperationException("Katalog demonstratora powinien zawierać dokładnie dwie firmy i dwa wskazania baz.");
        var now = DateTimeOffset.UtcNow;
        Console.WriteLine($"  Identity: użytkownicy={await system.Users.CountAsync()}, role={await system.Roles.CountAsync()}, przypisania={await system.CompanyAccesses.CountAsync()}, aktywne sesje={await system.UserSessions.CountAsync(x => x.RevokedAtUtc == null && x.ExpiresAtUtc > now)}.");
        foreach (var (id, _, database) in DemoCompanies)
        {
            var registration = await system.CompanyDatabases.SingleAsync(x => x.CompanyId == id);
            if (registration.ServerName != Server || registration.DatabaseName != database)
                throw new InvalidOperationException("Błędna lokalizacja firmy.");
            await using var company = CompanyContext(database, id);
            await ShowSchema(company, database, ["core.CompanyProfile", "core.Contractors", "core.ContractorAddresses", "core.ContractorContacts", "core.ContractorBankAccounts", "core.ContractorCreateOperations", "ref.AddressTypes", "audit.ContractorChanges", "dbo.__EFMigrationsHistory"], "_ContractorDetailAudit");
            var profile = await company.CompanyProfiles.IgnoreQueryFilters().SingleAsync();
            if (profile.CompanyId != id)
                throw new InvalidOperationException("Niezgodna tożsamość danych firmy.");
            Console.WriteLine($"  CompanyId={id}; profil=1; kontrahenci={await company.Contractors.CountAsync()}; NIP/REGON puste, dane fikcyjne.");
        }
    }

    public static async Task SeedUsers()
    {
        await CheckOwnership(SystemDatabase);
        var config = new ConfigurationBuilder().AddUserSecrets("vatowiec-api-development")
            .AddEnvironmentVariables().Build();
        var operatorName = config["DemoUsers:Operator:UserName"];
        var operatorPassword = config["DemoUsers:Operator:Password"];
        var readerName = config["DemoUsers:Reader:UserName"];
        var readerPassword = config["DemoUsers:Reader:Password"];
        if (new[] { operatorName, operatorPassword, readerName, readerPassword }.Any(string.IsNullOrWhiteSpace) ||
            string.Equals(operatorName, readerName, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Ustaw lokalnie odrębne DemoUsers:Operator/Reader:UserName i Password (user-secrets API lub zmienne środowiskowe).");
        await using var system = SystemContext(SystemDatabase);
        if ((await system.Database.GetPendingMigrationsAsync()).Any())
            throw new InvalidOperationException("Najpierw wykonaj migracje poleceniem initialize.");
        var services = new ServiceCollection().AddLogging().AddSingleton(system);
        services.AddVatowiecIdentity();
        await using var provider = services.BuildServiceProvider();
        var users = provider.GetRequiredService<UserManager<ApplicationUser>>();
        var roles = provider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
        foreach (var role in new[] { "Operator", "Reader" })
            if (!await roles.RoleExistsAsync(role))
                Ensure(await roles.CreateAsync(new IdentityRole<Guid>(role) { Id = Guid.NewGuid() }));
        foreach (var (name, password, role) in new[]
                 { (operatorName!, operatorPassword!, "Operator"), (readerName!, readerPassword!, "Reader") })
        {
            var user = await users.FindByNameAsync(name);
            if (user is null)
            {
                user = new ApplicationUser { Id = Guid.NewGuid(), UserName = name };
                Ensure(await users.CreateAsync(user, password));
            }
            else if (!await users.CheckPasswordAsync(user, password))
                Ensure(await users.ResetPasswordAsync(user, await users.GeneratePasswordResetTokenAsync(user), password));
            if (!await users.IsInRoleAsync(user, role)) Ensure(await users.AddToRoleAsync(user, role));
            foreach (var (id, _, _) in DemoCompanies)
            {
                var permissions = role == "Operator"
                    ? (id == DemoCompanies[0].Id ? (CompanyPermission)15 : (CompanyPermission)3)
                    : (id == DemoCompanies[0].Id ? CompanyPermission.ViewProfile : CompanyPermission.None);
                var access = await system.CompanyAccesses.SingleOrDefaultAsync(x => x.UserId == user.Id && x.CompanyId == id);
                if (permissions == CompanyPermission.None)
                {
                    if (access is not null) system.CompanyAccesses.Remove(access);
                }
                else if (access is null) system.CompanyAccesses.Add(new CompanyAccess(user.Id, id, permissions));
                else access.ChangePermissions(permissions);
            }
            await system.SaveChangesAsync();
            Console.WriteLine($"Konto demonstracyjne: {name}; rola {role}; hasło wyłącznie z lokalnej konfiguracji.");
        }
    }

    private static void Ensure(IdentityResult result)
    {
        if (!result.Succeeded) throw new InvalidOperationException(string.Join(", ", result.Errors.Select(x => x.Code)));
    }

    public static async Task BackupSystem()
        => await Backup(SystemDatabase);

    private static async Task Backup(string database)
    {
        await CheckOwnership(database);
        var directory = (string?)await Scalar("master", "SELECT CONVERT(nvarchar(4000), SERVERPROPERTY('InstanceDefaultBackupPath'));");
        if (string.IsNullOrWhiteSpace(directory) || !Path.IsPathFullyQualified(directory))
            throw new InvalidOperationException("Brak jawnego katalogu backupów lokalnej instancji SQL.");
        var backup = Path.Combine(directory, $"{database}_before_contractors_{Guid.NewGuid():N}.bak");
        await Execute("master", $"BACKUP DATABASE {Identifier(database)} TO DISK = @path WITH COPY_ONLY, CHECKSUM;",
            new SqlParameter("@path", backup));
        await Execute("master", "RESTORE VERIFYONLY FROM DISK = @path WITH CHECKSUM;", new SqlParameter("@path", backup));
        Console.WriteLine($"Backup {database} COPY_ONLY i VERIFYONLY z CHECKSUM: {backup}");
    }

    public static async Task MigrateContractors()
    {
        // Wyłącznie trzy konkretne lokalne bazy demonstracyjne. Nigdy API startup.
        foreach (var database in new[] { SystemDatabase }.Concat(DemoCompanies.Select(x => x.Database)))
            await CheckOwnership(database);
        await using (var system = SystemContext(SystemDatabase)) await CheckKnownHistory(system);
        foreach (var (id, _, database) in DemoCompanies)
        {
            await using var company = CompanyContext(database, id);
            await CheckKnownHistory(company);
            if ((await company.CompanyProfiles.IgnoreQueryFilters().SingleAsync()).CompanyId != id)
                throw new InvalidOperationException($"Obcy profil w {database}; odmowa migracji.");
        }
        foreach (var database in new[] { SystemDatabase }.Concat(DemoCompanies.Select(x => x.Database))) await Backup(database);
        await using (var system = SystemContext(SystemDatabase)) await system.Database.MigrateAsync();
        foreach (var (id, _, database) in DemoCompanies)
        {
            await using var company = CompanyContext(database, id);
            await company.Database.MigrateAsync();
        }
        await Verify();
    }

    private static async Task CheckKnownHistory(DbContext context)
    {
        var applied = (await context.Database.GetAppliedMigrationsAsync()).ToArray();
        var known = context.Database.GetMigrations().ToArray();
        if (applied.Length == 0 || !applied.SequenceEqual(known.Take(applied.Length)))
            throw new InvalidOperationException("Nieznana historia migracji. Przerwano przed zmianą schematów.");
    }

    public static async Task MigrateSystem()
    {
        await CheckOwnership(SystemDatabase);
        await using var system = SystemContext(SystemDatabase);
        await system.Database.MigrateAsync();
        await Verify();
    }
}
