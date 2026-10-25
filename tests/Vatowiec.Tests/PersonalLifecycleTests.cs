using System.Diagnostics;
using System.IO.Pipes;
using System.Net;
using System.Net.Http.Json;
using System.Runtime.InteropServices;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;
using Microsoft.Data.SqlClient;
using Vatowiec.Core.Infrastructure.Persistence;
using Vatowiec.Desktop.Services;
using Vatowiec.Desktop.ViewModels;
using Vatowiec.SharedContracts;

namespace Vatowiec.Tests;

[Collection("LocalSql")]
public sealed class PersonalLifecycleTests
{
    [Fact]
    public void PersonalIdentityIsStableDistinctAndCannotBecomeANetworkAddress()
    {
        var id = Guid.NewGuid(); var identity = PersonalApiIdentity.For(id);
        Assert.True(PersonalApiIdentity.TryParse(identity, out var parsed)); Assert.Equal(id, parsed);
        var user = Guid.NewGuid(); var target = new LoginTarget(identity, "fixture", user);
        Assert.NotEqual(target.CredentialName, new LoginTarget("https://localhost:7243", "fixture", user).CredentialName);
        Assert.NotEqual(target.CredentialName, new LoginTarget(PersonalApiIdentity.For(Guid.NewGuid()), "fixture", user).CredentialName);
        Assert.Throws<ArgumentException>(() => VatowiecApiClient.Connect(identity));
        Assert.Throws<ArgumentException>(() => VatowiecApiClient.ValidateAddress("http://localhost:7243"));
        Assert.False(PersonalApiIdentity.TryParse(identity + "wrong", out _));
    }
    [Fact]
    public async Task PersonalConnectionErrorExplainsLocalFailure()
    {
        using var model = new MainViewModel(_ => throw new HttpRequestException(), apiAddressOverride: PersonalApiIdentity.For(Guid.NewGuid())) { UserName = "fixture" };
        await model.Login("fictional-memory-only");
        Assert.Contains("lokalnego API Personal", model.Message); Assert.DoesNotContain("LAN", model.Message); Assert.DoesNotContain("certyfikat", model.Message);
    }

    [Fact]
    public void LegacyPersonalPreferencesKeepUserBindingAndDefaultWithoutCopyingCredentials()
    {
        var inner = new MemoryLoginPreferencesStore(); var user = Guid.NewGuid(); var company = Guid.NewGuid(); var id = Guid.NewGuid();
        var old = new LoginProfilePreferences("https://localhost:18443", "fixture", true, true, company, user);
        inner.Save(new LoginPreferences().WithProfile(old, makeLast: true));
        var store = new PersonalLoginPreferencesStore(inner, id); var migrated = store.Load();
        var target = new LoginTarget(PersonalApiIdentity.For(id), "fixture", user);
        Assert.Equal(user, migrated.Find(target)!.UserId); Assert.Equal(company, migrated.Find(target)!.DefaultCompanyId);
        Assert.True(migrated.Find(target)!.AutomaticLogin); Assert.Equal(target.Key, migrated.LastTargetKey);
        Assert.NotEqual(old.Target.CredentialName, target.CredentialName); // No automatic adoption of an unscoped saved password.
        Assert.Equal(2, store.Load().Profiles!.Length); // Idempotent migration; the legacy profile/credential is retained.
    }

    [Fact]
    public void SingleInstanceLockIsReleasedWithoutDeletingTheLockFile()
    {
        using (var first = PersonalSingleInstance.TryAcquire())
        { Assert.NotNull(first); Assert.Null(PersonalSingleInstance.TryAcquire()); }
        using var again = PersonalSingleInstance.TryAcquire(); Assert.NotNull(again);
    }

    [PersonalLocalDbFact]
    public async Task InstalledLocalDbIsFoundWithoutPathAndRetryKeepsTheSameInstallation()
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        var root = PersonalSetupTests.NewRoot(); var originalPath = Environment.GetEnvironmentVariable("PATH");
        try
        {
            Environment.SetEnvironmentVariable("PATH", Path.Combine(root, "unavailable-localdb-tool"));
            using var backend = new PersonalBackend(root);
            var status = await backend.Send(new()); Assert.True(status.Success, status.Message); Assert.Contains("17.", status.Message);
            Assert.False(status.Complete);
        }
        finally { Environment.SetEnvironmentVariable("PATH", originalPath); }
        var before = System.Text.Json.JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(root, "installation.json")));
        var id = before.RootElement.GetProperty("InstallationId").GetGuid();
        using var retry = new PersonalBackend(root); var ready = await retry.Send(new()); Assert.True(ready.Success, ready.Message); Assert.False(ready.Complete);
        var after = System.Text.Json.JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(root, "installation.json")));
        Assert.Equal(id, after.RootElement.GetProperty("InstallationId").GetGuid());
        Assert.False(after.RootElement.GetProperty("InstanceReserved").GetBoolean()); // No SQL instance or database was created by either check.
    }

    [PersonalLocalDbFact]
    public async Task OwnedPipe_LoginCsrfCrudPreferencesRestartAccessDenialAndCrash()
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        var root = PersonalSetupTests.NewRoot(); var credentials = new WindowsLoginCredentialStore();
        var preferences = new FileLoginPreferencesStore(Path.Combine(root, "login-preferences.json"));
        PersonalApiConnection firstConnection; Guid userId, company; int contractor;
        var first = new PersonalBackend(root);
        try
        {
            Assert.True((await first.Send(new())).Success);
            var initialized = await first.Send(PersonalSetupTests.Input()); Assert.True(initialized.Success, initialized.Message);
            var ready = await first.Send(new() { Operation = "serve" }); Assert.True(ready.Success, ready.Message);
            firstConnection = first.Connection!;
            using (var raw = first.CreateHttpClient())
            {
                using var denied = await raw.GetAsync("api/v1/companies"); Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
                using var noCsrf = await raw.PostAsJsonAsync("api/v1/auth/login", new { userName = "wizard.fixture", password = "Fictional-Setup-42!" });
                Assert.Equal(HttpStatusCode.BadRequest, noCsrf.StatusCode);
            }
            using (var model = new MainViewModel(first.Connect, preferencesStore: preferences, credentialStore: credentials, apiAddressOverride: firstConnection.Identity)
                { UserName = "wizard.fixture", RememberCredentials = true, AutomaticLogin = true })
            {
                await model.Login("Fictional-Setup-42!"); Assert.False(model.IsLogin, model.Message);
                model.SelectedCompany = Assert.Single(model.Companies); company = model.SelectedCompany.CompanyId;
                await model.SetDefaultCompany(); await model.OpenCompany(); Assert.True(model.IsWorkspace, model.Message);
            }
            using (var api = first.Connect(firstConnection.Identity))
            {
                userId = (await api.Login("wizard.fixture", "Fictional-Setup-42!")).UserId;
                var row = await api.SaveContractor(company, new ContractorWriteRequest(0, "PERSONAL-LIFE", "Fikcyjny kontrahent cyklu życia", "PL", true, OperationId: Guid.NewGuid()), true);
                contractor = row.Id; Assert.Equal(row.Name, (await api.Contractor(company, contractor)).Name);
                await api.Logout();
            }
            var target = new LoginTarget(firstConnection.Identity, "wizard.fixture", userId);
            Assert.True(credentials.Contains(target));
            Assert.Equal(company, preferences.Load().Find(target)!.DefaultCompanyId);
            // A restricted Windows token has the same base user but no allowed restricted SID: the kernel must deny the pipe ACL.
            RestrictedPipeAccessMustFail(firstConnection.PipeName);
            // A fake pipe in this test process cannot impersonate the Process object started by the controller.
            var fakeEndpoint = firstConnection with { PipeName = "Vatowiec.Personal." + Guid.NewGuid().ToString("N") };
            using var fake = new NamedPipeServerStream(fakeEndpoint.PipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            using var actualProcess = Process.GetProcessById(first.ProcessId);
            using var impostorClient = PersonalPipeClient.CreateHttpClient(fakeEndpoint, actualProcess);
            var accept = fake.WaitForConnectionAsync();
            await Assert.ThrowsAsync<HttpRequestException>(() => impostorClient.GetAsync("health")); await accept;
            using (var writer = first.Connect(firstConnection.Identity))
            {
                await writer.Login("wizard.fixture", "Fictional-Setup-42!");
                var targetDatabase = new PersonalDatabaseTarget(firstConnection.InstallationId);
                Task<ContractorResponse> pending;
                await using (var blocker = new SqlConnection(targetDatabase.Connection(targetDatabase.Company)))
                {
                    await blocker.OpenAsync(); await using var transaction = await blocker.BeginTransactionAsync();
                    await using var command = blocker.CreateCommand(); command.Transaction = (SqlTransaction)transaction;
                    command.CommandText = "SELECT COUNT(*) FROM core.Contractors WITH (TABLOCKX,HOLDLOCK)"; await command.ExecuteScalarAsync();
                    pending = writer.SaveContractor(company, new ContractorWriteRequest(0, "PERSONAL-DRAIN", "Fikcyjny zapis podczas zamykania", "PL", true, OperationId: Guid.NewGuid()), true);
                    command.CommandText = "SELECT COUNT(*) FROM sys.dm_exec_requests WHERE blocking_session_id=@@SPID";
                    var deadline = DateTime.UtcNow.AddSeconds(5);
                    while (Equals(await command.ExecuteScalarAsync(), 0) && DateTime.UtcNow < deadline) await Task.Delay(30);
                    Assert.True(Convert.ToInt32(await command.ExecuteScalarAsync()) > 0, "The real API write must be in flight before shutdown.");
                    Assert.False(pending.IsCompleted);
                    var stopping = first.ShutdownAsync(); await Task.Delay(300); Assert.False(stopping.IsCompleted);
                    await transaction.CommitAsync();
                    Assert.Equal("PERSONAL-DRAIN", (await pending.WaitAsync(TimeSpan.FromSeconds(10))).Code);
                }
            }
        }
        finally { await first.ShutdownAsync(); }
        Assert.True(first.GracefulExit);
        var restarted = new PersonalBackend(root);
        try
        {
            Assert.True((await restarted.Send(new())).Complete);
            Assert.True((await restarted.Send(new() { Operation = "serve" })).Success);
            Assert.Equal(firstConnection.Identity, restarted.Connection!.Identity); Assert.NotEqual(firstConnection.PipeName, restarted.Connection.PipeName);
            using var model = new MainViewModel(restarted.Connect, preferencesStore: preferences, credentialStore: credentials, apiAddressOverride: restarted.Connection.Identity);
            await model.InitializeLogin(); Assert.True(model.IsWorkspace, model.Message); Assert.Equal(company, model.SelectedCompany!.CompanyId);
            using var api = restarted.Connect(restarted.Connection.Identity); await api.Login("wizard.fixture", "Fictional-Setup-42!");
            Assert.Equal(contractor, (await api.Contractor(company, contractor)).Id);
            var failed = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            restarted.Failed += message => failed.TrySetResult(message);
            using var process = Process.GetProcessById(restarted.ProcessId); process.Kill(); await process.WaitForExitAsync();
            var error = await failed.Task.WaitAsync(TimeSpan.FromSeconds(5)); model.NotifyPersonalFailure(error);
            Assert.True(model.IsPersonalUnavailable); Assert.True(model.IsLogin); Assert.Empty(model.Contractors); Assert.False(model.CanUseForm);
        }
        finally
        {
            await restarted.ShutdownAsync();
            credentials.Delete(new(firstConnection.Identity, "wizard.fixture", userId)); // Only the credential created above, never existing users' entries.
        }
        using var recovered = new PersonalBackend(root);
        Assert.True((await recovered.Send(new())).Complete);
        Assert.True((await recovered.Send(new() { Operation = "serve" })).Success);
        Assert.True(await recovered.ShutdownAsync());
    }
    private static void RestrictedPipeAccessMustFail(string pipeName)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        using var identity = WindowsIdentity.GetCurrent(TokenAccessLevels.Duplicate | TokenAccessLevels.Query | TokenAccessLevels.Impersonate);
        var sid = new SecurityIdentifier(WellKnownSidType.BuiltinGuestsSid, null); var bytes = new byte[sid.BinaryLength]; sid.GetBinaryForm(bytes, 0);
        var memory = Marshal.AllocHGlobal(bytes.Length);
        try
        {
            Marshal.Copy(bytes, 0, memory, bytes.Length);
            var restricted = new SidAndAttributes { Sid = memory };
            if (!CreateRestrictedToken(identity.AccessToken, 1, 0, IntPtr.Zero, 0, IntPtr.Zero, 1, ref restricted, out var token))
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            using (token)
                WindowsIdentity.RunImpersonated(token, () =>
                {
                    using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut);
                    Assert.Throws<UnauthorizedAccessException>(() => pipe.Connect(1000));
                });
        }
        finally { Marshal.FreeHGlobal(memory); }
    }
    [StructLayout(LayoutKind.Sequential)] private struct SidAndAttributes { public IntPtr Sid; public uint Attributes; }
    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateRestrictedToken(SafeAccessTokenHandle token, uint flags, uint disableCount, IntPtr disable,
        uint privilegeCount, IntPtr privileges, uint restrictedCount, ref SidAndAttributes restricted, out SafeAccessTokenHandle result);
}
