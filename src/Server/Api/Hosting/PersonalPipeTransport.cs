using System.Security.AccessControl;
using System.Security.Principal;
using System.IO.Pipes;
using System.Runtime.Versioning;
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Vatowiec.SharedContracts;

namespace Vatowiec.Api.Hosting;

// This feature can only be installed by the pipe listener, never by an HTTP header.
public sealed class PersonalPipeConnection;

public sealed class PersonalPipeTransport(PersonalApiConnection endpoint)
{
    private readonly PersonalPipeConnection proof = new();
    public bool IsTrusted(HttpContext context) => ReferenceEquals(context.Features.Get<PersonalPipeConnection>(), proof);

    [SupportedOSPlatform("windows")]
    public void Configure(WebApplicationBuilder builder)
    {
        var security = new PipeSecurity(); security.SetAccessRuleProtection(true, false);
        var user = WindowsIdentity.GetCurrent().User!; security.SetOwner(user);
        security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.NetworkSid, null), PipeAccessRights.FullControl, AccessControlType.Deny));
        security.AddAccessRule(new PipeAccessRule(user, PipeAccessRights.FullControl, AccessControlType.Allow));
        builder.WebHost.UseNamedPipes(options =>
        {
            // A custom ACL replaces CurrentUserOnly; granting only this SID retains that restriction,
            // while the explicit Network SID deny also rejects access over SMB.
            options.CurrentUserOnly = false; options.PipeSecurity = security;
        });
        builder.WebHost.ConfigureKestrel(options => options.ListenNamedPipe(endpoint.PipeName, listen =>
        {
            listen.Protocols = HttpProtocols.Http1;
            listen.Use(next => connection =>
            {
                connection.Features.Set(proof);
                return next(connection);
            });
        }));
        builder.Services.Configure<HostOptions>(o => o.ShutdownTimeout = TimeSpan.FromSeconds(30));
    }
}
