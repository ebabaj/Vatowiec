using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Vatowiec.Core.Application;
using Vatowiec.Core.Domain;
using Vatowiec.Core.Infrastructure;
using Vatowiec.Core.Infrastructure.Identity;
using Vatowiec.Core.Infrastructure.Persistence;
using Vatowiec.SharedContracts;
using P = Vatowiec.Core.Infrastructure.Persistence.PersonalPrototypeDatabase;

namespace Vatowiec.DatabaseTools;

[SupportedOSPlatform("windows")]
internal static class PersonalPrototype
{
    private static readonly string Token = Guid.NewGuid().ToString("N");
    private static readonly Guid A = Guid.NewGuid(), B = Guid.NewGuid();
    private static readonly List<string> Checks = [];
    private static readonly List<object> Schemas = [];
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private static string Repo = "", Api = "";
    private static bool Completed;
    private const string Login = "personal.prototype.fictional";
    private const string Address = "https://localhost:17443";

    private static void Check(bool success, string description)
    {
        if (!success) throw new InvalidOperationException("Prototype check failed: " + description);
        Checks.Add(description); Console.WriteLine("PASS: " + description);
    }
    private static string Connection(string database)
    {
        if (database != "master" && !P.Databases.Contains(database, StringComparer.Ordinal)) throw new InvalidOperationException("Unapproved database.");
        return new SqlConnectionStringBuilder { DataSource = P.Server, InitialCatalog = database,
            IntegratedSecurity = true, Encrypt = SqlConnectionEncryptOption.Mandatory, TrustServerCertificate = true,
            ConnectTimeout = 15, Pooling = false }.ConnectionString;
    }
    private static async Task<SqlConnection> Open(string database, bool owned = true)
    {
        var connection = new SqlConnection(Connection(database));
        try
        {
            await connection.OpenAsync();
            await using var verify = connection.CreateCommand();
            verify.CommandText = "SELECT CASE WHEN SERVERPROPERTY('IsLocalDB')=1 AND SERVERPROPERTY('ProductMajorVersion')=N'17' AND DB_NAME()=@database THEN 1 ELSE 0 END";
            verify.Parameters.AddWithValue("@database", database);
            if (!Equals(await verify.ExecuteScalarAsync(), 1)) throw new InvalidOperationException("Wrong SQL instance/database.");
            if (database != "master" && owned)
            {
                verify.Parameters.Clear(); verify.CommandText = "SELECT CONVERT(nvarchar(128),value) FROM sys.extended_properties WHERE class=0 AND name=N'Vatowiec.PersonalPrototype'";
                if (!Equals(await verify.ExecuteScalarAsync(), Token)) throw new InvalidOperationException("Missing prototype ownership marker.");
            }
            return connection;
        }
        catch { await connection.DisposeAsync(); throw; }
    }
    private static async Task<string> LocalDb(params string[] args)
    {
        var start = new ProcessStartInfo("SqlLocalDB.exe") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in args) start.ArgumentList.Add(arg);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("LocalDB utility unavailable.");
        var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(45));
        await error;
        if (process.ExitCode != 0) throw new InvalidOperationException("LocalDB operation failed; existing instances were not changed.");
        return await output;
    }
    private static SystemDbContext SystemDb() => new(new DbContextOptionsBuilder<SystemDbContext>().UseSqlServer(Connection(P.System)).Options);
    private static CompanyDbContext CompanyDb(string name, Guid id) => new(new DbContextOptionsBuilder<CompanyDbContext>().UseSqlServer(Connection(name)).Options, new CompanyContext(id));

    public static async Task Run()
    {
        Repo = FindRepo(); Api = Path.Combine(Repo, "src", "Server", "Api", "bin", "Debug", "net10.0", "Vatowiec.Api.exe");
        var parent=Path.GetDirectoryName(P.Root)!; Directory.CreateDirectory(parent);
        using var gate = new FileStream(Path.Combine(parent,"personal-prototype.provision.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
        try
        {
            Check(File.Exists(Api), "standard Debug API executable exists");
            Check(!Directory.Exists(P.Root), "prototype data directory does not already exist");
            var instances = (await LocalDb("info")).Split(['\r','\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            Check(!instances.Contains(P.Instance, StringComparer.OrdinalIgnoreCase), "prototype instance name is unused; existing LocalDB instances untouched");
            Check((await LocalDb("versions")).Contains("17.0", StringComparison.Ordinal), "SQL Server 2025 LocalDB binaries are installed");
            var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback,17443);
            listener.Start(); listener.Stop(); // Refuse to create anything if our HTTPS port is occupied.
            Directory.CreateDirectory(P.Root);
            var acl = new DirectorySecurity(); acl.SetAccessRuleProtection(true,false);
            foreach (var sid in new[] { WindowsIdentity.GetCurrent().User!, new SecurityIdentifier("S-1-5-18"), new SecurityIdentifier("S-1-5-32-544") })
                acl.AddAccessRule(new FileSystemAccessRule(sid,FileSystemRights.FullControl,InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,PropagationFlags.None,AccessControlType.Allow));
            new DirectoryInfo(P.Root).SetAccessControl(acl);
            Directory.CreateDirectory(Path.Combine(P.Root,"SqlData"));
            await LocalDb("create",P.Instance,"17.0","-s");
            Check(true,"created only the dedicated Personal prototype LocalDB instance");
            await using (var master = await Open("master"))
            {
                await using var check = master.CreateCommand(); check.CommandText = "SELECT COUNT(*) FROM sys.databases WHERE database_id>4";
                Check(Equals(await check.ExecuteScalarAsync(),0),"new instance has no existing user databases");
                foreach (var name in P.Databases)
                {
                    if (!P.Databases.Contains(name,StringComparer.Ordinal)) throw new InvalidOperationException("Invalid target.");
                    check.CommandText = "SELECT DB_ID(@name)"; check.Parameters.Clear(); check.Parameters.AddWithValue("@name",name);
                    Check(await check.ExecuteScalarAsync() is DBNull,"database name is unused: "+name);
                    var data = Path.Combine(P.Root,"SqlData",name+".mdf").Replace("'","''");
                    var log = Path.Combine(P.Root,"SqlData",name+"_log.ldf").Replace("'","''");
                    check.Parameters.Clear(); check.CommandText = $"CREATE DATABASE [{name}] ON PRIMARY (NAME=N'{name}',FILENAME=N'{data}',SIZE=8MB,FILEGROWTH=8MB,MAXSIZE=256MB) LOG ON (NAME=N'{name}_log',FILENAME=N'{log}',SIZE=8MB,FILEGROWTH=8MB,MAXSIZE=128MB)";
                    await check.ExecuteNonQueryAsync();
                    await using var created = await Open(name,owned:false);
                    await using var mark = created.CreateCommand(); mark.CommandText="EXEC sys.sp_addextendedproperty @name=N'Vatowiec.PersonalPrototype',@value=@token"; mark.Parameters.AddWithValue("@token",Token); await mark.ExecuteNonQueryAsync();
                }
            }
            await using (var guard = await Open(P.System))
            await using (var system = SystemDb()) { await system.Database.MigrateAsync(); await Schema(system,P.System); }
            foreach (var (name,id) in new[] { (P.CompanyA,A),(P.CompanyB,B) })
            {
                await using var guard = await Open(name);
                await using var company = CompanyDb(name,id); await company.Database.MigrateAsync();
                company.CompanyProfiles.Add(new CompanyProfile(id,"Personal prototype fictional company "+(id==A ? "A" : "B")));
                await company.SaveChangesAsync(); await Schema(company,name);
            }
            var profile = new Dictionary<string,string?> {
                ["Vatowiec:Edition"]=P.Edition,["ConnectionStrings:VatowiecSystem"]=Connection(P.System),
                ["CompanyConnections:PersonalPrototype"]=Connection(P.CompanyA),
                ["Kestrel:Endpoints:Personal:Url"]=Address,["AllowedHosts"]="localhost",
                ["DataPort:KeyRingDirectory"]=Path.Combine(P.Root,"Keys","DataPort"),
                ["Gus:KeyRingDirectory"]=Path.Combine(P.Root,"Keys","Gus") };
            await File.WriteAllTextAsync(Path.Combine(P.Root,"host.json"),JsonSerializer.Serialize(profile,Json));
            var password=TemporaryPasswordGenerator.Generate(new PasswordOptions {RequiredLength=16,RequireUppercase=true,RequireLowercase=true,RequireDigit=true,RequireNonAlphanumeric=true});
            var config = new ConfigurationBuilder().AddInMemoryCollection(profile).Build();
            var services = new ServiceCollection(); services.AddLogging(); services.AddInfrastructure(config,false); services.AddVatowiecIdentity();
            services.AddDataProtection().SetApplicationName("VATowiec.PersonalPrototype").PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(P.Root,"Keys","Sessions"))).ProtectKeysWithDpapi(false);
            await using (var provider=services.BuildServiceProvider())
            await using (var scope=provider.CreateAsyncScope())
            {
                await using var guard=await Open(P.System);
                var system=scope.ServiceProvider.GetRequiredService<SystemDbContext>();
                foreach(var (id,name) in new[] {(A,P.CompanyA),(B,P.CompanyB)})
                { system.Companies.Add(new Company(id,"Personal fictional "+(id==A?"A":"B"))); system.CompanyDatabases.Add(new CompanyDatabaseRegistration(id,P.Server,name,"PersonalPrototype")); }
                await system.SaveChangesAsync();
                var initializer=new DemoUserInitializer(system,scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>(),scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>());
                Check(await initializer.CreateIfMissing(Login,password,InstallationRoles.Administrator,new Dictionary<Guid,CompanyPermission> {
                    [A]=Enum.GetValues<CompanyPermission>().Aggregate(CompanyPermission.None,(all,x)=>all|x),[B]=CompanyPermission.ViewProfile|CompanyPermission.ViewContractors }),"created a new fictional administrator with explicit company access, using existing Identity");
            }
            await EndToEnd(password); password="";
            foreach(var database in P.Databases) await Backup(database);
            Check(Directory.EnumerateFiles(Path.Combine(P.Root,"Keys","Sessions"),"key-*.xml").Any(),"Personal session key ring persists outside application/repository");
            SqlConnection.ClearAllPools(); await LocalDb("stop",P.Instance);
            Check(true,"stopped only the prototype LocalDB instance after testing; all prototype data retained");
            Completed=true;
        }
        finally
        {
            var results=Path.Combine(Repo,"TestResults"); Directory.CreateDirectory(results);
            var report=Completed ? "personal-localdb.json" : $"personal-localdb-incomplete-{Token}.json";
            await File.WriteAllTextAsync(Path.Combine(results,report),JsonSerializer.Serialize(new {completed=Completed,token=Token,instance=P.Instance,root=P.Root,databases=P.Databases,schemas=Schemas,checks=Checks},Json));
        }
    }
    private static async Task Schema(DbContext db,string name)
    {
        var applied=(await db.Database.GetAppliedMigrationsAsync()).ToArray();
        Check(applied.Order().SequenceEqual(db.Database.GetMigrations().Order()),"all existing EF migrations applied: "+name);
        await using var connection=await Open(name);
        await using var command=connection.CreateCommand();
        command.CommandText="SELECT (SELECT COUNT(*) FROM sys.tables WHERE is_ms_shipped=0),(SELECT COUNT(*) FROM sys.foreign_keys),(SELECT COUNT(*) FROM sys.indexes i JOIN sys.tables t ON t.object_id=i.object_id WHERE t.is_ms_shipped=0 AND i.index_id>0 AND i.is_hypothetical=0),(SELECT COUNT(*) FROM sys.columns WHERE system_type_id=189),(SELECT COUNT(*) FROM sys.triggers WHERE parent_class=1 AND is_disabled=0),(SELECT COUNT(*) FROM sys.foreign_keys WHERE is_disabled=1 OR is_not_trusted=1)+(SELECT COUNT(*) FROM sys.check_constraints WHERE is_disabled=1 OR is_not_trusted=1)";
        await using var reader=await command.ExecuteReaderAsync(); await reader.ReadAsync();
        Check(reader.GetInt32(1)>0 && reader.GetInt32(2)>0 && reader.GetInt32(3)>0 && reader.GetInt32(4)>0 && reader.GetInt32(5)==0,"FK, indexes, rowversion, enabled triggers and trusted constraints: "+name);
        Schemas.Add(new {database=name,migrations=applied.Length,tables=reader.GetInt32(0),foreignKeys=reader.GetInt32(1),indexes=reader.GetInt32(2),rowversions=reader.GetInt32(3),triggers=reader.GetInt32(4)});
    }
    private static string FindRepo()
    {
        var dir=new DirectoryInfo(AppContext.BaseDirectory);
        while(dir!=null && !File.Exists(Path.Combine(dir.FullName,"AGENTS.md"))) dir=dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Run the prototype from this source checkout.");
    }
    private static Process StartApi()
    {
        var start=new ProcessStartInfo(Api) {WorkingDirectory=Path.Combine(Repo,"src","Server","Api"),UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
        start.Environment["ASPNETCORE_ENVIRONMENT"]=P.Edition; start.Environment["DOTNET_ENVIRONMENT"]=P.Edition;
        start.Environment["Logging__LogLevel__Default"]="Warning";
        var process=Process.Start(start) ?? throw new InvalidOperationException("Prototype API did not start.");
        // Drain diagnostics without recording payloads/credentials. The API itself has safe metadata logging.
        process.OutputDataReceived+=(_,_)=>{}; process.ErrorDataReceived+=(_,_)=>{}; process.BeginOutputReadLine(); process.BeginErrorReadLine();
        return process;
    }
    private static void StopApi(Process process)
    {
        if(!process.HasExited) { process.Kill(entireProcessTree:true); process.WaitForExit(10000); }
        process.Dispose(); // Only the process created above, never the Network server.
    }
    private static async Task Ready(HttpClient client,Process api)
    {
        for(var i=0;i<80;i++)
        {
            if(api.HasExited) throw new InvalidOperationException("Personal API exited before readiness.");
            try { using var response=await client.GetAsync("/health"); if(response.StatusCode==HttpStatusCode.OK) return; } catch(HttpRequestException) { }
            await Task.Delay(200);
        }
        throw new InvalidOperationException("Personal HTTPS API not ready; trust the existing localhost developer certificate, never disable TLS.");
    }
    private static async Task Csrf(HttpClient client)
    {
        var value=await client.GetFromJsonAsync<CsrfResponse>("/api/v1/auth/csrf") ?? throw new InvalidOperationException();
        client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN"); client.DefaultRequestHeaders.Add("X-CSRF-TOKEN",value.Token);
    }
    private static async Task EndToEnd(string password)
    {
        using var handler=new HttpClientHandler {CookieContainer=new CookieContainer(),AllowAutoRedirect=false};
        using var client=new HttpClient(handler) {BaseAddress=new Uri(Address),Timeout=TimeSpan.FromSeconds(15)};
        Process? api=StartApi();
        try
        {
            await Ready(client,api); Check(true,"isolated API starts headless on localhost:17443 with trusted HTTPS");
            await Csrf(client);
            using(var login=await client.PostAsJsonAsync("/api/v1/auth/login",new {userName=Login,password}))
            { Check(login.IsSuccessStatusCode,"real Identity login with CSRF and HTTPS succeeds against LocalDB"); var session=await login.Content.ReadFromJsonAsync<SessionResponse>(); Check(session is {UserId:var id,IsInstallationAdministrator:true,MustChangePassword:false} && id!=Guid.Empty,"Identity returns the new immutable UserId and role"); }
            var companies=await client.GetFromJsonAsync<CompanyResponse[]>("/api/v1/companies");
            Check(companies is {Length:2} && companies.Any(x=>x.CompanyId==A) && companies.Any(x=>x.CompanyId==B),"authenticated company selection exposes only two prototype companies");
            var selected=await client.GetFromJsonAsync<CompanyProfileResponse>($"/api/v1/companies/{A}/profile");
            Check(selected?.CompanyId==A,"existing API selects the actual company profile");
            var value=new ContractorWriteRequest(0,"PERSONAL-FICTION","Personal fictional contractor","PL",true,OperationId:Guid.NewGuid());
            using(var noCsrf=new HttpClient(new HttpClientHandler {CookieContainer=handler.CookieContainer,AllowAutoRedirect=false}) {BaseAddress=new Uri(Address)})
            using(var denied=await noCsrf.PostAsJsonAsync($"/api/v1/companies/{A}/contractors",value))
                Check(denied.StatusCode==HttpStatusCode.BadRequest,"contractor creation without CSRF is rejected");
            await Csrf(client);
            ContractorResponse row;
            using(var created=await client.PostAsJsonAsync($"/api/v1/companies/{A}/contractors",value))
            { Check(created.IsSuccessStatusCode,"contractor is created through the existing HTTP API"); row=(await created.Content.ReadFromJsonAsync<ContractorResponse>())!; }
            using(var replay=await client.PostAsJsonAsync($"/api/v1/companies/{A}/contractors",value))
                Check(replay.IsSuccessStatusCode && (await replay.Content.ReadFromJsonAsync<ContractorResponse>())?.Id==row.Id,"idempotent replay returns the original contractor");
            var update=value with {Id=row.Id,Name="Personal fictional contractor updated",OperationId=null,RowVersion=row.RowVersion};
            using(var saved=await client.PutAsJsonAsync($"/api/v1/companies/{A}/contractors/{row.Id}",update))
                Check(saved.IsSuccessStatusCode && (await saved.Content.ReadFromJsonAsync<ContractorResponse>())?.RowVersion!=row.RowVersion,"rowversion changes after a real update");
            using(var stale=await client.PutAsJsonAsync($"/api/v1/companies/{A}/contractors/{row.Id}",update)) Check(stale.StatusCode==HttpStatusCode.Conflict,"stale rowversion is rejected with 409");
            using(var other=await client.GetAsync($"/api/v1/companies/{B}/contractors/{row.Id}")) Check(other.StatusCode==HttpStatusCode.NotFound,"company B cannot read company A contractor");
            using(var denied=await client.PostAsJsonAsync($"/api/v1/companies/{B}/contractors",value with {OperationId=Guid.NewGuid()})) Check(denied.StatusCode==HttpStatusCode.Forbidden,"read-only company B rejects writes");
            await using(var guard=await Open(P.CompanyA))
            await using(var db=CompanyDb(P.CompanyA,A))
                Check(await db.Contractors.CountAsync()==1 && await db.ContractorChanges.CountAsync()==2 && await db.ContractorChanges.AllAsync(x=>x.ActorId!=Guid.Empty && x.ActorIssuerId!=Guid.Empty),"contractor and exactly two audit events persisted atomically, without replay/conflict duplicates");
            await SqlChecks(row.Id);
            StopApi(api); api=null;
            api=StartApi(); await Ready(client,api);
            var after=await client.GetFromJsonAsync<ContractorResponse>($"/api/v1/companies/{A}/contractors/{row.Id}");
            Check(after?.Name==update.Name,"data and the existing Personal session survive a real API process restart");
            await Csrf(client); using(var logout=await client.PostAsync("/api/v1/auth/logout",null)) Check(logout.IsSuccessStatusCode,"normal logout revokes only the fictional Personal session");
            using(var denied=await client.GetAsync("/api/v1/companies")) Check(denied.StatusCode==HttpStatusCode.Unauthorized,"revoked session is rejected after logout");
        }
        finally { if(api!=null) StopApi(api); }
    }
    private static async Task SqlChecks(int contractor)
    {
        await using var connection=await Open(P.CompanyA);
        foreach(var (sql,number,label) in new[] {
            ($"UPDATE core.Contractors SET ParentId=2147483647 WHERE Id={contractor}",547,"real SQL foreign key rejects an orphan"),
            ($"UPDATE core.Contractors SET ParentId=Id WHERE Id={contractor}",0,"hierarchy trigger/check rejects self-parent"),
            ("UPDATE ref.Currencies SET Code='ZZZ' WHERE Code='EUR'",0,"currency immutability trigger rejects key changes")})
        {
            await using var tx=await connection.BeginTransactionAsync(); await using var cmd=connection.CreateCommand(); cmd.Transaction=(SqlTransaction)tx; cmd.CommandText=sql;
            var rejected=false; try { await cmd.ExecuteNonQueryAsync(); } catch(SqlException e) when(number==0 || e.Number==number) { rejected=true; }
            try { await tx.RollbackAsync(); } catch(InvalidOperationException) { }
            Check(rejected,label+" (rolled back)");
        }
        await using(var guard=await Open(P.System))
        {
            await using var tx=await guard.BeginTransactionAsync(); await using var cmd=guard.CreateCommand(); cmd.Transaction=(SqlTransaction)tx;
            cmd.CommandText="INSERT audit.UserAdministrationChanges (ActorId,ActorIssuerId,ActorName,TargetId,TargetName,OccurredAtUtc,Operation) SELECT u.Id,i.Id,N'Personal fictional',u.Id,N'Personal fictional',SYSUTCDATETIME(),N'PrototypeProbe' FROM security.Users u CROSS JOIN security.Installation i; UPDATE audit.UserAdministrationChanges SET ActorName=N'forbidden'";
            var rejected=false; try {await cmd.ExecuteNonQueryAsync();} catch(SqlException e) when(e.Number>=50000) {rejected=true;}
            try {await tx.RollbackAsync();} catch(InvalidOperationException) {}
            Check(rejected,"immutable system audit trigger rejects UPDATE (transaction rolled back)");
        }
    }
    private static async Task Backup(string database)
    {
        await using var guard=await Open(database); await using var master=await Open("master");
        var directory=Path.Combine(P.Root,"Backups"); Directory.CreateDirectory(directory);
        var path=Path.Combine(directory,database+".bak"); if(File.Exists(path)) throw new InvalidOperationException("Backup already exists.");
        await using var command=master.CreateCommand(); command.CommandTimeout=60;
        command.CommandText=$"BACKUP DATABASE [{database}] TO DISK=@path WITH COPY_ONLY,CHECKSUM; RESTORE VERIFYONLY FROM DISK=@path WITH CHECKSUM";
        command.Parameters.AddWithValue("@path",path); await command.ExecuteNonQueryAsync();
        Check(true,"COPY_ONLY/CHECKSUM backup and VERIFYONLY succeed: "+database);
    }
}
