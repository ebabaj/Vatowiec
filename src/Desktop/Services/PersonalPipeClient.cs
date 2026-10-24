using System.IO.Pipes;
using System.Net;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Diagnostics;
using Vatowiec.SharedContracts;

namespace Vatowiec.Desktop.Services;

public static class PersonalPipeClient
{
    public static HttpClient CreateHttpClient(PersonalApiConnection endpoint, Process owner)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        if (endpoint.InstallationId == Guid.Empty || endpoint.ProcessId != owner.Id || owner.HasExited ||
            !endpoint.PipeName.StartsWith("Vatowiec.Personal.", StringComparison.Ordinal) || endpoint.PipeName.Length != "Vatowiec.Personal.".Length + 32)
            throw new ArgumentException("Nie potwierdzono tożsamości procesu API Personal.");
        var origin = new Uri(endpoint.HttpOrigin);
        var handler = new SocketsHttpHandler
        {
            CookieContainer = new CookieContainer(), UseCookies = true, UseProxy = false, AllowAutoRedirect = false,
            ConnectTimeout = TimeSpan.FromSeconds(5),
            ConnectCallback = async (context, ct) =>
            {
                if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
                if (context.DnsEndPoint.Host != origin.Host || context.DnsEndPoint.Port != 80 || owner.HasExited)
                    throw new HttpRequestException("Nie potwierdzono lokalnego celu połączenia Personal.");
                var pipe = new NamedPipeClientStream(".", endpoint.PipeName, PipeDirection.InOut,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly, TokenImpersonationLevel.Identification);
                try
                {
                    await pipe.ConnectAsync(5000, ct);
                    if (!GetNamedPipeServerProcessId(pipe.SafePipeHandle.DangerousGetHandle(), out var pid) || pid != endpoint.ProcessId || owner.HasExited)
                        throw new HttpRequestException("Kanał nie należy do uruchomionego procesu VATowiec Personal.");
                    return pipe;
                }
                catch { pipe.Dispose(); throw; }
            }
        };
        // Plain HTTP framing exists only inside a SID-restricted Windows pipe. No sockets, DNS or TLS callback.
        return new HttpClient(handler) { BaseAddress = origin, Timeout = TimeSpan.FromSeconds(20), DefaultRequestVersion = HttpVersion.Version11 };
    }
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetNamedPipeServerProcessId(IntPtr pipe, out uint processId);
}
