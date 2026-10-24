using System.Diagnostics;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Vatowiec.Core.Application;
using Vatowiec.Core.Domain;
using Vatowiec.Core.Infrastructure.Identity;
using Vatowiec.Core.Infrastructure.Persistence;
using Vatowiec.SharedContracts;

namespace Vatowiec.Core.Infrastructure.Personal;

public sealed class PersonalSetupException(string message) : Exception(message);

public sealed class PersonalInstallationState
{
    public Guid InstallationId { get; set; } = Guid.NewGuid();
    public Guid CompanyId { get; set; } = Guid.NewGuid();
    public bool InstanceReserved { get; set; }
    public List<string> DatabaseIntents { get; set; } = [];
    public string? CompanyName { get; set; }
    public string? Nip { get; set; }
    public bool Complete { get; set; }
}

// Provisioning only. Normal API requests cannot resolve this service or invoke initialization.
[SupportedOSPlatform("windows")]
public sealed class PersonalInstallation : IDisposable
{
    public const string Address = "https://localhost:18443";
    public static string DefaultRoot => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Vatowiec", "Personal");
    public string Root { get; }
    public PersonalInstallationState State { get; }
    public PersonalDatabaseTarget Target { get; }
    private readonly FileStream gate;
    private string StateFile => Path.Combine(Root, "installation.json");

    public PersonalInstallation(string root)
    {
        Root = ValidateRoot(root);
        Directory.CreateDirectory(Root);
        var acl = new DirectorySecurity(); acl.SetAccessRuleProtection(true, false);
        foreach (var sid in new[] { WindowsIdentity.GetCurrent().User!, new SecurityIdentifier("S-1-5-18"), new SecurityIdentifier("S-1-5-32-544") })
            acl.AddAccessRule(new FileSystemAccessRule(sid, FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        new DirectoryInfo(Root).SetAccessControl(acl);
        try { gate = new FileStream(Path.Combine(Root, "installation.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
        catch (IOException) { throw new PersonalSetupException("Ta instalacja Personal jest już uruchomiona. Wróć do otwartego okna VATowca."); }
        try
        {
            if (!File.Exists(StateFile))
            {
                // Do not silently start a second installation when a previously used journal disappeared.
                var pending = StateFile + ".pending";
                if (File.Exists(pending))
                {
                    var recovered = JsonSerializer.Deserialize<PersonalInstallationState>(File.ReadAllText(pending));
                    if (recovered == null || recovered.InstallationId == Guid.Empty) throw new PersonalSetupException("Niekompletny zapis konfiguracji Personal. Dane wymagają sprawdzenia.");
                    File.Move(pending, StateFile);
                }
                else if (Directory.EnumerateFileSystemEntries(Root).Any(x => Path.GetFileName(x) != "installation.lock"))
                    throw new PersonalSetupException("Katalog Personal zawiera dane bez konfiguracji. Nie utworzono nowej instalacji ani zastępczych baz.");
            }
            State = File.Exists(StateFile) ? JsonSerializer.Deserialize<PersonalInstallationState>(File.ReadAllText(StateFile))
                ?? throw new PersonalSetupException("Nie można odczytać stanu instalacji. Nie utworzono zastępczych baz.") : new();
            Target = new(State.InstallationId);
            if (State.CompanyId == Guid.Empty || State.DatabaseIntents.Any(x => x != Target.System && x != Target.Company))
                throw new PersonalSetupException("Niespójny stan instalacji. Dane nie zostały zmienione.");
            Save();
        }
        catch { gate.Dispose(); throw; }
    }
    public static string ValidateRoot(string root)
    {
        var full = Path.GetFullPath(root);
        var parent = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Vatowiec");
        var name = Path.GetFileName(full);
        if (!string.Equals(Path.GetDirectoryName(full), parent, StringComparison.OrdinalIgnoreCase) ||
            !(name == "Personal" || (name.StartsWith("Personal.WizardTest.", StringComparison.Ordinal) && Guid.TryParseExact(name[20..], "N", out _))))
            throw new PersonalSetupException("Niedozwolony katalog instalacji Personal.");
        for (var dir = new DirectoryInfo(full); dir != null && dir.FullName.Length >= parent.Length; dir = dir.Parent)
            if (dir.Exists && dir.Attributes.HasFlag(FileAttributes.ReparsePoint)) throw new PersonalSetupException("Katalog Personal nie może być dowiązaniem.");
        return full;
    }
    private void Save()
    {
        var temp = StateFile + ".pending";
        using (var file = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
        { JsonSerializer.Serialize(file, State); file.Flush(true); }
        File.Move(temp, StateFile, overwrite: true);
    }
    public static async Task<string> LocalDb(params string[] args)
    {
        var start = new ProcessStartInfo("SqlLocalDB.exe") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in args) start.ArgumentList.Add(arg);
        try
        {
            using var process = Process.Start(start) ?? throw new PersonalSetupException("Nie znaleziono SQL Server 2025 LocalDB. Zainstaluj LocalDB według instrukcji Personal, a następnie uruchom program ponownie.");
            var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(45)); await error;
            if (process.ExitCode != 0) throw new PersonalSetupException("Nie udało się uruchomić prywatnej instancji LocalDB. Inne instancje nie zostały zmienione.");
            return await output;
        }
        catch (System.ComponentModel.Win32Exception) { throw new PersonalSetupException("Nie znaleziono SQL Server 2025 LocalDB. Zainstaluj LocalDB według instrukcji Personal. Program niczego nie instaluje automatycznie."); }
    }
    public async Task<PersonalSetupStatus> Status()
    {
        var version = DetectVersion(await LocalDb("versions"));
        if (version == null)
            throw new PersonalSetupException("Wymagany jest SQL Server 2025 LocalDB (17.x). Istniejące wersje i instancje pozostają bez zmian.");
        var instances = (await LocalDb("info")).Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (State.InstanceReserved && instances.Contains(Target.Instance, StringComparer.OrdinalIgnoreCase) && await DatabaseExists(Target.System))
        {
            await using var guard = await Open(Target.System, false);
            await using var cmd = guard.CreateCommand();
            cmd.CommandText = "SELECT CONVERT(nvarchar(128),value) FROM sys.extended_properties WHERE class=0 AND name=N'Vatowiec.Personal.Owner'";
            var owner = await cmd.ExecuteScalarAsync();
            if (owner != null && !Equals(owner, State.InstallationId.ToString("N"))) throw new PersonalSetupException("Baza należy do innej instalacji.");
            if (owner != null)
            {
                cmd.CommandText = "SELECT COUNT(*) FROM sys.extended_properties WHERE class=0 AND name=N'Vatowiec.Personal.Initialized'";
                if (Equals(await cmd.ExecuteScalarAsync(), 1)) { State.Complete = true; Save(); }
            }
        }
        if (State.Complete) await VerifyComplete();
        return new(true, State.Complete ? "Instalacja jest gotowa. Przejdź do logowania." : $"SQL Server 2025 LocalDB {version} jest dostępny. Możesz skonfigurować nową instalację.",
            "localdb", State.Complete, ApiAddress: Address, CompanyName: State.CompanyName, Nip: State.Nip);
    }
    public static string? DetectVersion(string output)
    {
        var match = Regex.Match(output, @"(?<![\d.])17\.0(?:\.\d+){0,2}(?![\d.])", RegexOptions.CultureInvariant);
        return match.Success ? match.Value : null;
    }
    private SystemDbContext SystemDb() => new(new DbContextOptionsBuilder<SystemDbContext>().UseSqlServer(Target.Connection(Target.System)).Options);
    private CompanyDbContext CompanyDb() => new(new DbContextOptionsBuilder<CompanyDbContext>().UseSqlServer(Target.Connection(Target.Company)).Options, new CompanyContext(State.CompanyId));
    private async Task<SqlConnection> Open(string database, bool owned = true)
    {
        var connection = new SqlConnection(Target.Connection(database));
        try
        {
            await connection.OpenAsync();
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT CASE WHEN SERVERPROPERTY('IsLocalDB')=1 AND SERVERPROPERTY('ProductMajorVersion')=N'17' AND DB_NAME()=@database THEN 1 ELSE 0 END";
            cmd.Parameters.AddWithValue("@database", database);
            if (!Equals(await cmd.ExecuteScalarAsync(), 1)) throw new PersonalSetupException("Niezgodna instancja lub baza Personal. Operacja została zatrzymana.");
            if (owned && database != "master")
            {
                cmd.Parameters.Clear(); cmd.CommandText = "SELECT CONVERT(nvarchar(128),value) FROM sys.extended_properties WHERE class=0 AND name=N'Vatowiec.Personal.Owner'";
                if (!Equals(await cmd.ExecuteScalarAsync(), State.InstallationId.ToString("N"))) throw new PersonalSetupException("Baza nie ma znacznika tej instalacji. Nie zostanie przejęta ani nadpisana.");
            }
            return connection;
        }
        catch { await connection.DisposeAsync(); throw; }
    }
    private async Task<bool> DatabaseExists(string database)
    {
        await using var master = await Open("master"); await using var cmd = master.CreateCommand();
        cmd.CommandText = "SELECT DB_ID(@name)"; cmd.Parameters.AddWithValue("@name", database);
        return await cmd.ExecuteScalarAsync() is not DBNull;
    }
    private async Task EnsureDatabase(string database)
    {
        if (database != Target.System && database != Target.Company) throw new PersonalSetupException("Niedozwolona baza.");
        Directory.CreateDirectory(Path.Combine(Root, "SqlData"));
        var data = Path.Combine(Root, "SqlData", database + ".mdf"); var log = Path.Combine(Root, "SqlData", database + "_log.ldf");
        var exists = await DatabaseExists(database);
        if (!State.DatabaseIntents.Contains(database))
        {
            if (exists || File.Exists(data) || File.Exists(log)) throw new PersonalSetupException("Nazwa bazy lub plik jest już zajęty. Nie nadpisano danych.");
            State.DatabaseIntents.Add(database); Save();
        }
        if (!exists)
        {
            await using var master = await Open("master"); await using var create = master.CreateCommand();
            static string Quote(string s) => s.Replace("'", "''");
            create.CommandText = $"CREATE DATABASE [{database}] ON PRIMARY (NAME=N'{database}',FILENAME=N'{Quote(data)}',SIZE=8MB,FILEGROWTH=8MB) LOG ON (NAME=N'{database}_log',FILENAME=N'{Quote(log)}',SIZE=8MB,FILEGROWTH=8MB)";
            await create.ExecuteNonQueryAsync();
        }
        await using var db = await Open(database, false); await using var cmd = db.CreateCommand();
        cmd.CommandText = "SELECT CONVERT(nvarchar(128),value) FROM sys.extended_properties WHERE class=0 AND name=N'Vatowiec.Personal.Owner'";
        var marker = await cmd.ExecuteScalarAsync();
        if (marker == null)
        {
            // CREATE DATABASE cannot join a transaction. Resume only our recorded intent, exact files and an empty schema.
            cmd.CommandText = "SELECT COUNT(*) FROM sys.tables WHERE is_ms_shipped=0";
            if (!Equals(await cmd.ExecuteScalarAsync(), 0)) throw new PersonalSetupException("Istniejąca baza nie należy do kreatora.");
            cmd.CommandText = "SELECT physical_name FROM sys.database_files ORDER BY file_id";
            var files = new List<string>(); await using (var reader = await cmd.ExecuteReaderAsync()) while (await reader.ReadAsync()) files.Add(reader.GetString(0));
            if (files.Count != 2 || !string.Equals(files[0], data, StringComparison.OrdinalIgnoreCase) || !string.Equals(files[1], log, StringComparison.OrdinalIgnoreCase))
                throw new PersonalSetupException("Niezgodne położenie plików bazy. Operacja zatrzymana.");
            cmd.CommandText = "EXEC sys.sp_addextendedproperty @name=N'Vatowiec.Personal.Owner',@value=@owner"; cmd.Parameters.AddWithValue("@owner", State.InstallationId.ToString("N"));
            await cmd.ExecuteNonQueryAsync();
        }
        else if (!Equals(marker, State.InstallationId.ToString("N"))) throw new PersonalSetupException("Baza należy do innej instalacji.");
    }
    public static (string UserName, string Name, string? Nip) ValidateInput(PersonalSetupCommand input)
    {
        var login = input.UserName?.Trim() ?? ""; var name = input.CompanyName?.Trim() ?? ""; var nip = input.Nip?.Trim();
        if (login.Length is < 1 or > 128 || login.Any(c => !"abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-._@+".Contains(c)))
            throw new PersonalSetupException("Login musi mieć 1–128 znaków: litery, cyfry lub znaki - . _ @ +.");
        if (name.Length is < 1 or > 256 || name.Any(char.IsControl)) throw new PersonalSetupException("Podaj nazwę firmy (1–256 znaków).");
        if (string.IsNullOrEmpty(nip)) nip = null;
        else if (!new PolishNipChecksum().IsValid(nip)) throw new PersonalSetupException("NIP powinien zawierać 10 cyfr i prawidłową sumę kontrolną. Możesz pozostawić go pustego.");
        var policy = InstallationPasswordValidator.Validate(new PasswordPolicy(), input.Password);
        if (!policy.Succeeded) throw new PersonalSetupException(string.Join(" ", policy.Errors.Select(x => x.Description)));
        return (login, name, nip);
    }
    public async Task Initialize(PersonalSetupCommand input, Func<PersonalSetupStatus, Task> progress)
    {
        try
        {
            var status = await Status();
            if (status.Complete) throw new PersonalSetupException("Instalacja ma już administratora. Ponowna inicjalizacja jest zabroniona. Przejdź do logowania.");
            var (login, name, nip) = ValidateInput(input);
            if (State.CompanyName != null && (State.CompanyName != name || State.Nip != nip)) throw new PersonalSetupException("Wznawiana instalacja ma już dane firmy. Zachowaj wyświetloną nazwę i NIP.");
            async Task Stage(string key, string text) => await progress(new(true, text, key, Final: false));
            await Stage("instance", "2/5 Przygotowanie prywatnej instancji LocalDB…");
            var instances = (await LocalDb("info")).Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (!State.InstanceReserved)
            {
                if (instances.Contains(Target.Instance, StringComparer.OrdinalIgnoreCase)) throw new PersonalSetupException("Nazwa instancji jest zajęta. Nie zmieniono istniejącej instancji.");
                State.InstanceReserved = true; Save();
            }
            if (!instances.Contains(Target.Instance, StringComparer.OrdinalIgnoreCase)) await LocalDb("create", Target.Instance, "17.0", "-s");
            else await LocalDb("start", Target.Instance);
            await using (var master = await Open("master"))
            await using (var known = master.CreateCommand())
            {
                known.CommandText = "SELECT name FROM sys.databases WHERE database_id>4";
                await using var names = await known.ExecuteReaderAsync();
                while (await names.ReadAsync())
                    if (!State.DatabaseIntents.Contains(names.GetString(0))) throw new PersonalSetupException("Instancja zawiera bazę spoza zapisanych etapów kreatora. Nie zmieniono danych.");
            }
            await EnsureDatabase(Target.System);
            await Stage("system", "3/5 Przygotowanie bazy systemowej i polityki bezpieczeństwa…");
            await using (var guard = await Open(Target.System))
            await using (var db = SystemDb())
            {
                await db.Database.MigrateAsync();
                if (await db.Users.AnyAsync() || await db.UserAdministrationChanges.AnyAsync(x => x.Operation == "PersonalInitialized"))
                    throw new PersonalSetupException("W instalacji istnieją już konta. Kreator nie może ich zastąpić.");
            }
            await Stage("company", "4/5 Przygotowanie osobnej bazy pierwszej firmy…");
            State.CompanyName = name; State.Nip = nip; Save();
            await EnsureDatabase(Target.Company);
            await using (var guard = await Open(Target.Company))
            await using (var db = CompanyDb())
            {
                await db.Database.MigrateAsync();
                var profile = await db.CompanyProfiles.IgnoreQueryFilters().SingleOrDefaultAsync();
                if (profile == null) { db.CompanyProfiles.Add(new(State.CompanyId, name, nip)); await db.SaveChangesAsync(); }
                else if (profile.CompanyId != State.CompanyId || profile.Name != name || profile.Nip != nip) throw new PersonalSetupException("Profil firmy jest niezgodny. Dane nie zostały zastąpione.");
            }
            await Stage("administrator", "5/5 Zapis administratora, uprawnień i audytu…");
            var services = new ServiceCollection(); services.AddLogging();
            services.AddDbContext<SystemDbContext>(o => o.UseSqlServer(Target.Connection(Target.System))); services.AddVatowiecIdentity();
            await using var provider = services.BuildServiceProvider(); await using var scope = provider.CreateAsyncScope();
            var system = scope.ServiceProvider.GetRequiredService<SystemDbContext>();
            await using var tx = await system.Database.BeginTransactionAsync(); await UserAdministrationService.Lock(system, default);
            if (await system.Users.AnyAsync()) throw new PersonalSetupException("Administrator został już utworzony. Inicjalizacja jest zamknięta.");
            var manager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
            var user = new ApplicationUser { Id = Guid.NewGuid(), UserName = login, LockoutEnabled = true };
            static void Require(IdentityResult result)
            { if (!result.Succeeded) throw new PersonalSetupException(string.Join(" ", result.Errors.Select(x => x.Description))); }
            Require(await manager.CreateAsync(user, input.Password!));
            if (!await roles.RoleExistsAsync(InstallationRoles.Administrator)) Require(await roles.CreateAsync(new IdentityRole<Guid>(InstallationRoles.Administrator)));
            Require(await manager.AddToRoleAsync(user, InstallationRoles.Administrator));
            system.Companies.Add(new(State.CompanyId, name));
            system.CompanyDatabases.Add(new(State.CompanyId, Target.Server, Target.Company, "Personal"));
            var permissions = Enum.GetValues<CompanyPermission>().Aggregate(CompanyPermission.None, (a, p) => a | p);
            system.CompanyAccesses.Add(new(user.Id, State.CompanyId, permissions));
            var issuer = (await system.InstallationIdentities.SingleAsync()).Id; var now = DateTimeOffset.UtcNow;
            foreach (var operation in new[] { "PersonalInitialized", "Created", "AdministratorGranted", "CompanyAccessGranted" })
                system.UserAdministrationChanges.Add(new(user.Id, issuer, login, user.Id, login, now, operation,
                    operation == "CompanyAccessGranted" ? State.CompanyId : null, after: operation == "CompanyAccessGranted" ? permissions : null));
            await system.SaveChangesAsync();
            await system.Database.ExecuteSqlRawAsync("EXEC sys.sp_addextendedproperty @name=N'Vatowiec.Personal.Initialized',@value=N'1'");
            await tx.CommitAsync();
            await Stage("committed", "Administrator i firma zapisane. Kończenie konfiguracji…");
            State.Complete = true; Save();
        }
        finally { input.Password = null; }
    }
    public async Task VerifyComplete()
    {
        await using var guard = await Open(Target.System);
        await using var cmd = guard.CreateCommand(); cmd.CommandText = "SELECT COUNT(*) FROM sys.extended_properties WHERE class=0 AND name=N'Vatowiec.Personal.Initialized'";
        if (!Equals(await cmd.ExecuteScalarAsync(), 1)) throw new PersonalSetupException("Nie potwierdzono zakończenia instalacji. Kreator nie zastąpi danych.");
        await using var db = SystemDb();
        if (!await db.Users.AnyAsync() || !await db.UserAdministrationChanges.AnyAsync(x => x.Operation == "PersonalInitialized") ||
            !await db.CompanyDatabases.AnyAsync(x => x.CompanyId == State.CompanyId && x.DatabaseName == Target.Company && x.ServerName == Target.Server))
            throw new PersonalSetupException("Niekompletna konfiguracja instalacji. Dane wymagają sprawdzenia przez administratora.");
        await using var companyGuard = await Open(Target.Company);
    }
    public Dictionary<string, string?> Configuration() => new()
    {
        ["Vatowiec:Edition"] = PersonalDatabaseTarget.Edition, ["Personal:InstallationId"] = State.InstallationId.ToString(),
        ["ConnectionStrings:VatowiecSystem"] = Target.Connection(Target.System), ["CompanyConnections:Personal"] = Target.Connection(Target.Company),
        ["Kestrel:Endpoints:Personal:Url"] = Address, ["AllowedHosts"] = "localhost",
        ["DataPort:KeyRingDirectory"] = Path.Combine(Root, "Keys", "DataPort"), ["Gus:KeyRingDirectory"] = Path.Combine(Root, "Keys", "Gus")
    };
    public void Dispose() => gate.Dispose();
}
