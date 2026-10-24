using System.IO.Pipes;
using System.Runtime.Versioning;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Vatowiec.Core.Infrastructure.Personal;
using Vatowiec.Core.Infrastructure.Persistence;
using Vatowiec.SharedContracts;

namespace Vatowiec.Api.Hosting;

// There is intentionally no HTTP bootstrap route. Only the spawning desktop inherits both handles.
public sealed class PersonalWorker : IDisposable
{
    private readonly AnonymousPipeClientStream input;
    private readonly AnonymousPipeClientStream output;
    private readonly StreamReader reader;
    private readonly StreamWriter writer;
    private PersonalInstallation? installation;
    private PersonalWorker(string readHandle, string writeHandle)
    {
        input = new(PipeDirection.In, readHandle); output = new(PipeDirection.Out, writeHandle);
        // Do not let SqlLocalDB (or any later child) inherit the desktop lifetime channel.
        if (!SetHandleInformation(input.SafePipeHandle.DangerousGetHandle(), 1, 0) ||
            !SetHandleInformation(output.SafePipeHandle.DangerousGetHandle(), 1, 0)) throw new IOException("Nie przygotowano prywatnego kanału Personal.");
        reader = new(input); writer = new(output) { AutoFlush = true };
    }
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetHandleInformation(IntPtr handle, uint mask, uint flags);
    [SupportedOSPlatform("windows")]
    public static async Task<PersonalWorker?> Prepare(string[] args)
    {
        var worker = new PersonalWorker(args[1], args[2]);
        try
        {
            var root = JsonSerializer.Deserialize<string>(await worker.reader.ReadLineAsync() ?? throw new IOException());
            worker.installation = new PersonalInstallation(root ?? throw new IOException());
            while (await worker.reader.ReadLineAsync() is { } line)
            {
                var request = JsonSerializer.Deserialize<PersonalSetupCommand>(line) ?? throw new IOException(); line = "";
                try
                {
                    if (request.Operation == "status") await worker.Write(await worker.installation.Status());
                    else if (request.Operation == "initialize")
                    {
                        await worker.installation.Initialize(request, worker.Write);
                        await worker.Write(await worker.installation.Status());
                    }
                    else if (request.Operation == "serve") { await worker.installation.VerifyComplete(); return worker; }
                    else await worker.Write(new(false, "Nieznana operacja kreatora.", "error"));
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    await worker.Write(new(false, SafeError(ex), "error"));
                }
                finally { request.Password = null; }
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        { try { await worker.Write(new(false, SafeError(ex), "error")); } catch (IOException) { } }
        worker.Dispose(); return null;
    }
    private static string SafeError(Exception error) => error switch
    {
        PersonalSetupException => error.Message,
        Microsoft.Data.SqlClient.SqlException => "Nie udało się przygotować bazy Personal. Zachowano dotychczasowe dane. Uruchom kreator ponownie, aby wznowić; jeśli błąd powraca, skontaktuj się z administratorem.",
        UnauthorizedAccessException => "Brak dostępu do katalogu danych Personal. Sprawdź uprawnienia konta Windows.",
        _ => "Nie ukończono konfiguracji Personal. Zachowano zapisane etapy. Uruchom kreator ponownie, aby wznowić."
    };
    [SupportedOSPlatform("windows")]
    public void Configure(WebApplicationBuilder builder)
    {
        // Ignore Network Kestrel/connection overrides, including inherited environment variables.
        builder.Configuration.Sources.Clear();
        builder.Configuration.AddInMemoryCollection(installation!.Configuration());
        builder.Services.AddDataProtection().SetApplicationName("VATowiec.Personal." + installation.State.InstallationId)
            .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(installation.Root, "Keys", "Sessions"))).ProtectKeysWithDpapi(false);
        builder.Services.PostConfigure<Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationOptions>(
            Microsoft.AspNetCore.Identity.IdentityConstants.ApplicationScheme, o => o.Cookie.Name = "__Host-Vatowiec.Personal.Session");
        builder.Services.PostConfigure<Microsoft.AspNetCore.Antiforgery.AntiforgeryOptions>(o => o.Cookie.Name = "__Host-Vatowiec.Personal.Csrf");
    }
    public void Attach(WebApplication app)
    {
        app.Lifetime.ApplicationStarted.Register(() => _ = Started(app));
    }
    private async Task Started(WebApplication app)
    {
        if (!OperatingSystem.IsWindows()) return;
        try
        {
            await Write(new(true, "Lokalne API Personal jest gotowe.", "ready", Complete: true, ApiAddress: PersonalInstallation.Address));
            // A close command or EOF from a closed/crashed desktop stops only this host, gracefully.
            await reader.ReadLineAsync();
        }
        catch (IOException) { }
        finally { app.Lifetime.StopApplication(); }
    }
    private Task Write(PersonalSetupStatus status) => writer.WriteLineAsync(JsonSerializer.Serialize(status));
    public void Dispose()
    {
        writer.Dispose(); reader.Dispose(); output.Dispose(); input.Dispose();
        if (OperatingSystem.IsWindows()) installation?.Dispose();
    }
}
