using System.Diagnostics;
using System.Xml.Linq;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Vatowiec.Core.Application;
using Vatowiec.Core.Domain;
using Vatowiec.Core.Infrastructure.Persistence;

namespace Vatowiec.DatabaseTools;

internal static class LocalDatabases
{
    private const string Server = @"localhost\SQLEXPRESS";
    private const string Marker = "Vatowiec.LocalTestPurpose";
    private const string Legacy = "Vatowiec_Dev";
    private const string SystemDatabase = "Vatowiec_System";
    private static readonly string[] TestDatabases =
        ["Vatowiec_System_Test", "Vatowiec_Company_A_Test", "Vatowiec_Company_B_Test"];
    private static readonly (Guid Id, string Name, string Database)[] DemoCompanies =
    [
        (Guid.Parse("aaaaaaaa-0000-4000-8000-000000000001"), "Firma demonstracyjna A — dane fikcyjne", "Vatowiec_Company_A_Dev"),
        (Guid.Parse("bbbbbbbb-0000-4000-8000-000000000002"), "Firma demonstracyjna B — dane fikcyjne", "Vatowiec_Company_B_Dev")
    ];
    private static readonly Guid DemoContractor = Guid.Parse("cccccccc-0000-4000-8000-000000000001");

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

    private static string Purpose(string database) => TestDatabases.Contains(database, StringComparer.Ordinal)
        ? "VATowiec M0 disposable integration fixture v1" : "VATowiec M0 fictional company demonstration v1";

    private static void CheckName(string database)
    {
        if (database != SystemDatabase && database != Legacy &&
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

    private static async Task CheckLegacy()
    {
        if (!await Exists(Legacy)) return;
        // Wyjątek tylko dla jawnie wskazanej przez użytkownika starej bazy.
        // Nie wystarcza prefiks nazwy: dokładny schemat i archiwalna migracja.
        var known = await Scalar(Legacy, """
            SELECT CASE WHEN (SELECT COUNT(*) FROM sys.tables WHERE is_ms_shipped = 0) = 3
                AND OBJECT_ID(N'core.Companies', N'U') IS NOT NULL
                AND OBJECT_ID(N'core.Contractors', N'U') IS NOT NULL
                AND OBJECT_ID(N'dbo.__EFMigrationsHistory', N'U') IS NOT NULL
                THEN 1 ELSE 0 END;
            """);
        if (!Equals(known, 1)) throw new InvalidOperationException("Vatowiec_Dev nie odpowiada staremu schematowi projektu; odmowa usunięcia.");
        var migration = await Scalar(Legacy, """
            SELECT CASE WHEN COUNT(*) = 1 AND MIN(MigrationId) = N'20261021092204_InitialCompaniesAndContractors'
                THEN 1 ELSE 0 END FROM dbo.__EFMigrationsHistory;
            """);
        if (!Equals(migration, 1)) throw new InvalidOperationException("Nieznana historia Vatowiec_Dev; odmowa usunięcia.");
        var count = await Scalar(Legacy, "SELECT (SELECT COUNT_BIG(*) FROM core.Companies) + (SELECT COUNT_BIG(*) FROM core.Contractors);");
        Console.WriteLine($"Potwierdzono {Legacy}: archiwalna migracja VATowca, Companies/Contractors; rekordów: {count}. Baza testowa wskazana do usunięcia przez użytkownika.");
    }

    public static async Task Inspect()
    {
        await using var connection = new SqlConnection(Connection("master"));
        await connection.OpenAsync();
        await using var command = new SqlCommand("SELECT name, state_desc FROM sys.databases ORDER BY database_id;", connection);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync()) Console.WriteLine($"{reader.GetString(0)} | {reader.GetString(1)}");
        await CheckLegacy();
    }

    private static async Task CreateOrCheck(string database)
    {
        CheckName(database);
        if (database == Legacy) throw new InvalidOperationException("Nie tworzymy starej bazy.");
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
        if (database == Legacy) await CheckLegacy();
        else await CheckOwnership(database);
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
        await CheckLegacy();
        // Sprawdź wszystkie zastane cele przed pierwszą operacją zmieniającą dane.
        foreach (var database in new[] { SystemDatabase }.Concat(DemoCompanies.Select(x => x.Database)))
            if (await Exists(database)) await CheckOwnership(database);
        await Drop(Legacy);
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
            if (!await company.Contractors.AnyAsync(x => x.Id == DemoContractor))
                company.Contractors.Add(new Contractor(id, DemoContractor, "DEMO", $"Kontrahent {name} — fikcyjny", "PL"));
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
            if (await Exists(database)) await CheckOwnership(database);
        foreach (var database in TestDatabases)
        {
            await Drop(database);
            await CreateOrCheck(database);
            if (database == "Vatowiec_System_Test")
            {
                await using var system = SystemContext(database);
                await system.Database.MigrateAsync();
            }
            else
            {
                await using var company = CompanyContext(database, null);
                await company.Database.MigrateAsync();
            }
        }
        Console.WriteLine("Trzy puste bazy *_Test są gotowe do testów transakcyjnych.");
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
            await CleanupTests();
        }
    }

    private static async Task CheckEmptyFixtures()
    {
        await using var system = SystemContext(TestDatabases[0]);
        if (await system.Companies.AnyAsync() || await system.CompanyDatabases.AnyAsync())
            throw new InvalidOperationException("Testy pozostawiły rekordy w katalogu *_Test.");
        if ((await system.Database.GetAppliedMigrationsAsync()).Count() != 1)
            throw new InvalidOperationException("Testy zmieniły historię bazy systemowej.");
        foreach (var database in TestDatabases.Skip(1))
        {
            await using var company = CompanyContext(database, null);
            if (await company.CompanyProfiles.IgnoreQueryFilters().AnyAsync() ||
                await company.Contractors.IgnoreQueryFilters().AnyAsync() ||
                (await company.Database.GetAppliedMigrationsAsync()).Count() != 1)
                throw new InvalidOperationException($"Testy nie wycofały danych lub historii w {database}.");
        }
        Console.WriteLine("Potwierdzono rollback: wszystkie bazy *_Test puste, historie migracji zachowane.");
    }

    private static async Task ShowSchema(DbContext context, string database, string[] expectedTables, string migrationSuffix)
    {
        await CheckOwnership(database);
        var applied = (await context.Database.GetAppliedMigrationsAsync()).ToArray();
        if (applied.Length != 1 || !applied[0].EndsWith(migrationSuffix, StringComparison.Ordinal) ||
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
        Console.WriteLine($"{database}: {string.Join(", ", tables)}; migracja {applied[0]}.");
    }

    public static async Task Verify()
    {
        await using var system = SystemContext(SystemDatabase);
        await ShowSchema(system, SystemDatabase, ["core.Companies", "core.CompanyDatabases", "dbo.__EFMigrationsHistory"], "_InitialSystem");
        if (await system.Companies.CountAsync() != 2 || await system.CompanyDatabases.CountAsync() != 2)
            throw new InvalidOperationException("Katalog demonstratora powinien zawierać dokładnie dwie firmy i dwa wskazania baz.");
        foreach (var (id, _, database) in DemoCompanies)
        {
            var registration = await system.CompanyDatabases.SingleAsync(x => x.CompanyId == id);
            if (registration.ServerName != Server || registration.DatabaseName != database)
                throw new InvalidOperationException("Błędna lokalizacja firmy.");
            await using var company = CompanyContext(database, id);
            await ShowSchema(company, database, ["core.CompanyProfile", "core.Contractors", "dbo.__EFMigrationsHistory"], "_InitialCompany");
            var profile = await company.CompanyProfiles.IgnoreQueryFilters().SingleAsync();
            if (profile.CompanyId != id || await company.Contractors.IgnoreQueryFilters().AnyAsync(x => x.CompanyId != id))
                throw new InvalidOperationException("Niezgodna tożsamość danych firmy.");
            Console.WriteLine($"  CompanyId={id}; profil=1; kontrahenci={await company.Contractors.CountAsync()}; NIP/REGON puste, dane fikcyjne.");
        }
    }
}
