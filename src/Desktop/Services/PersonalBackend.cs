using System.Diagnostics;
using System.IO.Pipes;
using System.Text.Json;
using Vatowiec.SharedContracts;

namespace Vatowiec.Desktop.Services;

public interface IPersonalBackend : IDisposable
{
    string Root { get; }
    Task<PersonalSetupStatus> Send(PersonalSetupCommand command, Action<PersonalSetupStatus>? progress = null);
    IVatowiecApiClient Connect(string identity);
    event Action<string>? Failed;
    Task<bool> ShutdownAsync();
}

// Desktop never opens SQL. Only its own child server receives inherited anonymous handles.
public sealed class PersonalBackend : IPersonalBackend
{
    public string Root { get; }
    public int ProcessId => process?.Id ?? 0;
    public bool? GracefulExit { get; private set; }
    public PersonalApiConnection? Connection { get; private set; }
    public event Action<string>? Failed;
    private Process? process;
    private AnonymousPipeServerStream? input, output;
    private StreamReader? reader;
    private StreamWriter? writer;
    private readonly SemaphoreSlim serial = new(1, 1);
    private bool disposed;
    private volatile bool closing;
    private Task<bool>? shutdown;
    private readonly object shutdownGate = new();
    public PersonalBackend(string? root = null) => Root = PersonalLocalPaths.ValidateRoot(root ?? PersonalLocalPaths.DefaultRoot);
    public static string FindApi()
    {
        foreach (var relative in new[] { "Server/Vatowiec.Api.exe", "Vatowiec.Api.exe" })
        { var candidate = Path.Combine(AppContext.BaseDirectory, relative); if (File.Exists(candidate)) return candidate; }
#if DEBUG
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory != null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "AGENTS.md")))
            {
                var api = Path.Combine(directory.FullName, "src", "Server", "Api", "bin", "Debug", "net10.0", "Vatowiec.Api.exe");
                if (File.Exists(api)) return api;
                break;
            }
#endif
        throw new InvalidOperationException("Nie znaleziono lokalnego serwera VATowiec Personal. Sprawdź kompletność instalacji.");
    }
    private async Task Start()
    {
        if (!OperatingSystem.IsWindows()) throw new InvalidOperationException("VATowiec Personal wymaga Windows i SQL Server 2025 LocalDB.");
        var api = FindApi();
        input = new(PipeDirection.In, HandleInheritability.Inheritable); output = new(PipeDirection.Out, HandleInheritability.Inheritable);
        var start = new ProcessStartInfo(api) { WorkingDirectory = Path.GetDirectoryName(api)!, UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add("--personal-worker"); start.ArgumentList.Add(output.GetClientHandleAsString()); start.ArgumentList.Add(input.GetClientHandleAsString());
        // Never inherit a Network environment or its user-secrets. The host builds an isolated configuration.
        start.Environment["ASPNETCORE_ENVIRONMENT"] = "Personal"; start.Environment["DOTNET_ENVIRONMENT"] = "Personal";
        process = Process.Start(start) ?? throw new IOException("Nie uruchomiono lokalnego serwera Personal.");
        process.EnableRaisingEvents = true;
        process.Exited += (_, _) => { if (!closing) Failed?.Invoke("Lokalny serwer VATowiec Personal zakończył pracę. Zamknij program i uruchom go ponownie. Zapisane dane pozostają zachowane."); };
        output.DisposeLocalCopyOfClientHandle(); input.DisposeLocalCopyOfClientHandle();
        process.OutputDataReceived += (_, _) => { }; process.ErrorDataReceived += (_, _) => { };
        process.BeginOutputReadLine(); process.BeginErrorReadLine();
        reader = new(input); writer = new(output) { AutoFlush = true };
        await writer.WriteLineAsync(JsonSerializer.Serialize(Root));
    }
    public async Task<PersonalSetupStatus> Send(PersonalSetupCommand command, Action<PersonalSetupStatus>? progress = null)
    {
        await serial.WaitAsync();
        try
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (process?.HasExited == true)
            {
                writer?.Dispose(); reader?.Dispose(); output?.Dispose(); input?.Dispose(); process.Dispose(); process = null;
            }
            if (process == null) await Start();
            await writer!.WriteLineAsync(JsonSerializer.Serialize(command));
            command.Password = null;
            while (true)
            {
                var line = await reader!.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(command.Operation == "initialize" ? 180 : 30));
                if (line == null) throw new IOException("Lokalny serwer Personal zakończył pracę. Zapisane dane i etapy konfiguracji zachowano. Uruchom Personal ponownie.");
                var status = JsonSerializer.Deserialize<PersonalSetupStatus>(line) ?? throw new IOException("Nieprawidłowa odpowiedź lokalnego serwera.");
                if (status.Success && status.Stage == "ready")
                {
                    var endpoint = status.Connection ?? throw new IOException("API nie potwierdziło lokalnego kanału.");
                    if (endpoint.ProcessId != process!.Id || status.ApiAddress != endpoint.Identity) throw new IOException("Niezgodna tożsamość serwera Personal.");
                    Connection = endpoint;
                    try
                    {
                        using var probe = CreateHttpClient();
                        using var health = await probe.GetAsync("health"); health.EnsureSuccessStatusCode();
                    }
                    catch (Exception error) when (error is HttpRequestException or IOException or ArgumentException or TaskCanceledException)
                    { throw new IOException("Nie potwierdzono gotowości bezpiecznego kanału lokalnego API. Zamknij Personal i uruchom ponownie. Zapisane dane zachowano."); }
                }
                progress?.Invoke(status);
                if (status.Final) return status;
            }
        }
        finally { command.Password = null; serial.Release(); }
    }
    public HttpClient CreateHttpClient() => PersonalPipeClient.CreateHttpClient(Connection ?? throw new InvalidOperationException("API Personal nie jest gotowe."),
        process ?? throw new InvalidOperationException("Brak własnego procesu API Personal."));
    public IVatowiecApiClient Connect(string identity)
    {
        if (Connection == null || identity != Connection.Identity || closing) throw new ApiException(503, "Nie potwierdzono połączenia z tą instalacją Personal.");
        return VatowiecApiClient.ForPersonal(CreateHttpClient(), Connection);
    }
    public Task<bool> ShutdownAsync()
    {
        lock (shutdownGate) return shutdown ??= StopCore();
    }
    private async Task<bool> StopCore()
    {
        closing = true; disposed = true;
        // EOF requests Kestrel draining (30 s), followed by a normal stop of our LocalDB instance.
        try { writer?.Dispose(); } catch (IOException) { }
        output?.Dispose();
        if (process != null)
        {
            try
            {
                await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(60)).ConfigureAwait(false);
                GracefulExit = process.ExitCode == 0;
            }
            catch (TimeoutException)
            {
                // This Process object was returned by our own Start and retains its OS handle. Never enumerate/kill by name or stored PID.
                GracefulExit = false; if (!process.HasExited) process.Kill();
                await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            }
            process.Dispose();
        }
        reader?.Dispose(); input?.Dispose();
        return GracefulExit != false;
    }
    public void Dispose() => ShutdownAsync().GetAwaiter().GetResult();
}
