using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Runtime.Versioning;
using System.Diagnostics;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Vatowiec.Core.Infrastructure.Personal;
using Vatowiec.Core.Infrastructure.Persistence;
using Vatowiec.Desktop.Services;
using Vatowiec.SharedContracts;

namespace Vatowiec.Tests;

[Collection("LocalSql")]
[SupportedOSPlatform("windows")]
public sealed class PersonalSetupTests
{
    internal static string NewRoot() => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Vatowiec", "Personal.WizardTest." + Guid.NewGuid().ToString("N"));
    internal static PersonalSetupCommand Input(string password = "Fictional-Setup-42!", string company = "Firma fikcyjna kreatora") =>
        new() { Operation = "initialize", UserName = "wizard.fixture", Password = password, CompanyName = company };

    [Theory]
    [InlineData("Microsoft SQL Server 2025 (17.0.4025.3)", "17.0.4025.3")]
    [InlineData("17.0", "17.0")]
    [InlineData("Microsoft SQL Server 2022 (16.0.1000.6)", null)]
    public void DetectsActualLocalDbUtilityVersionFormat(string output, string? expected) => Assert.Equal(expected, PersonalInstallation.DetectVersion(output));

    [Theory]
    [InlineData("", "Firma", null)]
    [InlineData("Fictional-Setup-42!", "", null)]
    [InlineData("Fictional-Setup-42!", "Firma", "1234567890")]
    public void InvalidInputNeverReachesProvisioning(string password, string company, string? nip)
    {
        if (!OperatingSystem.IsWindows()) return;
        var input = Input(password, company); input.Nip = nip;
        Assert.Throws<PersonalSetupException>(() => PersonalInstallation.ValidateInput(input));
    }
    [Fact]
    public void TargetIsBoundToExactInstallationAndNetworkPolicyRemainsClosed()
    {
        var target = new PersonalDatabaseTarget(Guid.NewGuid());
        Assert.Throws<InvalidOperationException>(() => DatabaseConnectionPolicy.ValidateSystem(target.Connection(target.System), true));
        Assert.Throws<InvalidOperationException>(() => DatabaseConnectionPolicy.ValidateSystem(target.Connection(target.Company), false, personal: target));
        Assert.Throws<InvalidOperationException>(() => DatabaseConnectionPolicy.ValidateCompany(target.Connection(target.System), false, personal: target));
        Assert.Throws<InvalidOperationException>(() => DatabaseConnectionPolicy.ValidateCompany(target.Connection(target.Company), false, personal: new(Guid.NewGuid())));
        var network = new SqlConnectionStringBuilder(target.Connection(target.System)) { DataSource = @"localhost\SQLEXPRESS", InitialCatalog = "Vatowiec_System" };
        Assert.Throws<InvalidOperationException>(() => DatabaseConnectionPolicy.ValidateSystem(network.ConnectionString, false, personal: target));
        if (OperatingSystem.IsWindows())
        {
            DatabaseConnectionPolicy.ValidateSystem(target.Connection(target.System), false, personal: target);
            DatabaseConnectionPolicy.ValidateCompany(target.Connection(target.Company), false, personal: target);
            Assert.Throws<PersonalSetupException>(() => PersonalInstallation.ValidateRoot(Path.GetTempPath()));
        }
    }

    [PersonalLocalDbFact]
    public async Task RealLocalDb_InterruptedInitializationResumes_LoginRestartAndPermanentSeal()
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        var root = NewRoot(); PersonalDatabaseTarget target; Guid company;
        using (var setup = new PersonalInstallation(root))
        {
            target = setup.Target; company = setup.State.CompanyId;
            Assert.True((await setup.Status()).Success);
            Assert.Throws<PersonalSetupException>(() => new PersonalInstallation(root));
            await Assert.ThrowsAsync<SimulatedInterruption>(() => setup.Initialize(Input(), status =>
            { if (status.Stage == "administrator") throw new SimulatedInterruption(); return Task.CompletedTask; }));
            await using var system = System(target);
            Assert.Equal(0, await system.Users.CountAsync());
            Assert.Empty(await system.UserAdministrationChanges.ToArrayAsync());
            Assert.Equal(system.Database.GetMigrations().Order(), (await system.Database.GetAppliedMigrationsAsync()).Order());
        }
        var checks = new List<string>(); Guid userId;
        using (var backend = new PersonalBackend(root))
        {
            var status = await backend.Send(new()); Assert.True(status.Success, status.Message); Assert.False(status.Complete);
            Assert.Equal("Firma fikcyjna kreatora", status.CompanyName);
            var wrong = Input(""); var failed = await backend.Send(wrong); Assert.False(failed.Success); Assert.Contains("puste", failed.Message); Assert.Null(wrong.Password);
            wrong = Input(); wrong.Nip = "1234567890"; failed = await backend.Send(wrong); Assert.False(failed.Success); Assert.Contains("NIP", failed.Message);
            status = await backend.Send(Input(), x => checks.Add(x.Stage)); Assert.True(status.Success, status.Message); Assert.True(status.Complete);
            Assert.Contains("committed", checks);
            var denied = await backend.Send(Input()); Assert.False(denied.Success); Assert.Contains("zabroniona", denied.Message);
            using (var second = new PersonalBackend(root))
            { var locked = await second.Send(new()); Assert.False(locked.Success); Assert.Contains("już uruchomiona", locked.Message); }
            var ready = await backend.Send(new() { Operation = "serve" }); Assert.True(ready.Success, ready.Message); Assert.Equal("ready", ready.Stage);
            using var api = backend.Connect(ready.ApiAddress!);
            var session = await api.Login("wizard.fixture", "Fictional-Setup-42!"); userId = session.UserId;
            Assert.True(session.IsInstallationAdministrator); Assert.False(session.MustChangePassword);
            var companies = await api.Companies(); Assert.Single(companies); Assert.Equal(company, companies[0].CompanyId);
            Assert.Equal("Firma fikcyjna kreatora", (await api.Profile(company)).Name);
            using var raw = backend.CreateHttpClient();
            using var response = await raw.PostAsJsonAsync("/api/v1/personal/initialize", Input());
            Assert.False(response.IsSuccessStatusCode); // No public HTTP bootstrap route, even during setup.
            await api.Logout();
        }
        using (var restarted = new PersonalBackend(root))
        {
            var status = await restarted.Send(new()); Assert.True(status.Success, status.Message); Assert.True(status.Complete);
            var denied = await restarted.Send(Input()); Assert.False(denied.Success);
            var ready = await restarted.Send(new() { Operation = "serve" }); Assert.True(ready.Success, ready.Message);
            using var api = restarted.Connect(ready.ApiAddress!);
            Assert.Equal(userId, (await api.Login("wizard.fixture", "Fictional-Setup-42!")).UserId);
            Assert.Equal(company, Assert.Single(await api.Companies()).CompanyId);
            await api.Logout();
        }
        await using (var system = System(target))
        {
            Assert.Equal(1, await system.Users.CountAsync()); Assert.Equal(1, await system.Companies.CountAsync());
            Assert.Equal(1, await system.UserRoles.CountAsync()); Assert.Equal(1, await system.CompanyAccesses.CountAsync());
            Assert.Equal(4, await system.UserAdministrationChanges.CountAsync());
            Assert.All(await system.UserAdministrationChanges.ToArrayAsync(), x => Assert.Equal(userId, x.ActorId));
        }
        Assert.DoesNotContain("Fictional-Setup-42!", await File.ReadAllTextAsync(Path.Combine(root, "installation.json")));
        await PersonalInstallation.LocalDb("stop", target.Instance);
        Report(root, target, "interruption-before-administrator, atomic audit, validation, pipe isolation, real HTTPS login, restart, permanent seal");
    }

    [PersonalLocalDbFact]
    public async Task RealLocalDb_KilledWorkerResumesSameInstallationWithoutReset()
    {
        var root = NewRoot(); var interrupted = false;
        using (var backend = new PersonalBackend(root))
        {
            Assert.True((await backend.Send(new())).Success);
            await Assert.ThrowsAsync<IOException>(() => backend.Send(Input(), status =>
            {
                if (status.Stage != "company") return;
                interrupted = true;
                using var worker = Process.GetProcessById(backend.ProcessId);
                worker.Kill(); worker.WaitForExit(10000); // Only the new worker created by this test; never Network or SQL Server.
            }));
        }
        Assert.True(interrupted);
        var before = JsonSerializer.Deserialize<PersonalInstallationState>(await File.ReadAllTextAsync(Path.Combine(root, "installation.json")))!;
        using (var backend = new PersonalBackend(root))
        {
            var status = await backend.Send(new()); Assert.True(status.Success, status.Message);
            Assert.False(status.Complete);
            status = await backend.Send(Input()); Assert.True(status.Success, status.Message); Assert.True(status.Complete);
            var ready = await backend.Send(new() { Operation = "serve" }); Assert.True(ready.Success, ready.Message);
            using var api = backend.Connect(ready.ApiAddress!);
            Assert.True((await api.Login("wizard.fixture", "Fictional-Setup-42!")).IsInstallationAdministrator);
            Assert.Equal(before.CompanyId, Assert.Single(await api.Companies()).CompanyId);
            await api.Logout();
        }
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        using (var setup = new PersonalInstallation(root))
        {
            Assert.Equal(before.InstallationId, setup.State.InstallationId); Assert.Equal(before.CompanyId, setup.State.CompanyId);
            await using var db = System(setup.Target); Assert.Equal(1, await db.Users.CountAsync()); Assert.Equal(1, await db.Companies.CountAsync());
            Report(root, setup.Target, "actual worker process terminated during company provisioning; restart and same-identity recovery, no reset");
            await PersonalInstallation.LocalDb("stop", setup.Target.Instance);
        }
    }

    [PersonalLocalDbFact]
    public async Task RealLocalDb_CrashAfterCommitDoesNotRecreateAdministratorOrCompany()
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        var root = NewRoot(); PersonalDatabaseTarget target;
        using (var setup = new PersonalInstallation(root))
        {
            target = setup.Target;
            await Assert.ThrowsAsync<SimulatedInterruption>(() => setup.Initialize(Input(), status =>
            { if (status.Stage == "committed") throw new SimulatedInterruption(); return Task.CompletedTask; }));
            Assert.False(setup.State.Complete); // Simulate loss before publishing the final file checkpoint.
        }
        using (var resumed = new PersonalInstallation(root))
        {
            Assert.True((await resumed.Status()).Complete);
            await Assert.ThrowsAsync<PersonalSetupException>(() => resumed.Initialize(Input(), _ => Task.CompletedTask));
            await using var db = System(target); Assert.Equal(1, await db.Users.CountAsync()); Assert.Equal(1, await db.Companies.CountAsync());
        }
        await PersonalInstallation.LocalDb("stop", target.Instance);
        Report(root, target, "interruption-after-commit, authoritative SQL seal recovery, no duplicate accounts or company");
    }
    private static SystemDbContext System(PersonalDatabaseTarget target) => new(new DbContextOptionsBuilder<SystemDbContext>().UseSqlServer(target.Connection(target.System)).Options);
    private sealed class SimulatedInterruption : Exception;
    private static void Report(string root, PersonalDatabaseTarget target, string checks)
    {
        Directory.CreateDirectory(PersonalLocalPaths.VerificationDirectory);
        File.WriteAllText(Path.Combine(PersonalLocalPaths.VerificationDirectory, "personal-wizard-" + target.InstallationId.ToString("N") + ".json"),
            JsonSerializer.Serialize(new { completed = true, root, instance = target.Instance, databases = new[] { target.System, target.Company }, checks }));
    }
}

public sealed class PersonalLocalDbFactAttribute : FactAttribute
{
    public PersonalLocalDbFactAttribute()
    {
        if (!OperatingSystem.IsWindows() || Environment.GetEnvironmentVariable("VATOWIEC_PERSONAL_SETUP_TESTS") != "1")
            Skip = "Requires Windows LocalDB 17 and explicit VATOWIEC_PERSONAL_SETUP_TESTS=1; creates new isolated databases only.";
    }
}
