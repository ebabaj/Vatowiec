using System.Diagnostics;
using System.IO.Pipes;
using System.Text.Json;
using Vatowiec.SharedContracts;

namespace Vatowiec.Desktop.Services;

public interface IPersonalBackend : IDisposable
{
    string Root { get; }
    Task<PersonalSetupStatus> Send(PersonalSetupCommand command, Action<PersonalSetupStatus>? progress = null);
}

// Desktop never opens SQL. Only its own child server receives inherited anonymous handles.
public sealed class PersonalBackend : IPersonalBackend
{
    public string Root { get; }
    public int ProcessId => process?.Id ?? 0;
    public bool? GracefulExit { get; private set; }
    private Process? process;
    private AnonymousPipeServerStream? input, output;
    private StreamReader? reader;
    private StreamWriter? writer;
    private readonly SemaphoreSlim serial = new(1, 1);
    private bool disposed;
    public PersonalBackend(string? root = null) => Root = root ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Vatowiec", "Personal");
    public static string FindApi()
    {
        foreach (var relative in new[] { "Server/Vatowiec.Api.exe", "Vatowiec.Api.exe" })
        { var candidate = Path.Combine(AppContext.BaseDirectory, relative); if (File.Exists(candidate)) return candidate; }
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory != null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "AGENTS.md")))
            {
                var api = Path.Combine(directory.FullName, "src", "Server", "Api", "bin", "Debug", "net10.0", "Vatowiec.Api.exe");
                if (File.Exists(api)) return api;
                break;
            }
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
                var line = await reader!.ReadLineAsync().WaitAsync(TimeSpan.FromMinutes(5));
                if (line == null) throw new IOException("Lokalny serwer Personal zakończył pracę. Sprawdź, czy port Personal jest wolny i certyfikat localhost jest zaufany. Zapisane etapy konfiguracji zostaną zachowane.");
                var status = JsonSerializer.Deserialize<PersonalSetupStatus>(line) ?? throw new IOException("Nieprawidłowa odpowiedź lokalnego serwera.");
                progress?.Invoke(status);
                if (status.Final) return status;
            }
        }
        finally { command.Password = null; serial.Release(); }
    }
    public void Dispose()
    {
        if (disposed) return; disposed = true;
        // Closing the private channel requests graceful shutdown, including when the desktop crashes.
        writer?.Dispose(); output?.Dispose();
        if (process != null)
        {
            if (!process.HasExited && !process.WaitForExit(10000)) { GracefulExit = false; process.Kill(entireProcessTree: true); process.WaitForExit(5000); }
            else GracefulExit = process.ExitCode == 0;
            process.Dispose();
        }
        reader?.Dispose(); input?.Dispose();
    }
}
