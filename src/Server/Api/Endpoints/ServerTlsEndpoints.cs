using Microsoft.AspNetCore.Antiforgery;
using Vatowiec.Api.Hosting;
using Vatowiec.Api.Security;
using Vatowiec.Core.Infrastructure.Operations;
using Vatowiec.Core.Infrastructure.ServerTls;
using Vatowiec.SharedContracts.ServerTls;

namespace Vatowiec.Api.Endpoints;

// ADR-047: ustawienia certyfikatu serwera. Wyłącznie administrator instalacji (świeża kontrola roli w DB),
// zapisy z CSRF i kontrolą wersji. Odpowiedzi nie zawierają kluczy ani haseł.
public static class ServerTlsEndpoints
{
    public static void MapServerTls(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/installation/server-certificate").RequireAuthorization(InstallationAdministration.Policy);
        group.MapGet("", (ServerTlsService service, ServerCertificateProvider provider, HttpContext http) =>
            Handle(() => service.Read(provider.Status, http.RequestAborted)));
        group.MapPut("", async (ServerTlsRequest value, HttpContext http, IAntiforgery csrf, ServerTlsService service,
            ServerCertificateProvider provider, IServerCertificateSource source, TimeProvider clock) =>
            await ValidCsrf(csrf, http) ? await Handle(async () =>
            {
                var response = await service.Save(Requester(http), value,
                    (names, issuer) => OwnCertificateSelection.Select(source.Candidates(), names, issuer, clock.GetUtcNow()),
                    provider.Status, http.RequestAborted);
                // Natychmiastowe przełączenie zamiast czekania na odświeżenie co 30 s.
                if (provider.Managed) { await provider.Refresh(http.RequestAborted); response = response with { Served = provider.Status }; }
                return response;
            }) : Csrf());
        group.MapPost("/ca-replacement", async (ServerTlsCaReplacementRequest value, HttpContext http, IAntiforgery csrf,
            ServerTlsService service, ServerCertificateProvider provider) =>
            await ValidCsrf(csrf, http) ? await Handle(() => service.RequestCaReplacement(Requester(http), value, provider.Status, http.RequestAborted)) : Csrf());
        group.MapGet("/connection-file", (ServerTlsService service, HttpContext http) =>
            Handle(() => service.ConnectionFile(new Uri(http.Request.Scheme + "://" + http.Request.Host.Value + "/"), Environment.MachineName, http.RequestAborted)));
    }

    private static async Task<IResult> Handle<T>(Func<Task<T>> action)
    {
        try { return Results.Ok(await action()); }
        catch (ServerTlsException error) { return Results.Json(new { code = error.Code, message = error.Message }, statusCode: error.Status); }
    }

    private static async Task<bool> ValidCsrf(IAntiforgery csrf, HttpContext http)
    {
        try { await csrf.ValidateRequestAsync(http); return true; }
        catch (AntiforgeryValidationException) { return false; }
    }

    private static OperationRequester Requester(HttpContext http) => new(http.UserId(), http.User.Identity?.Name ?? "");
    private static IResult Csrf() => Results.Json(new { code = "csrf_invalid", message = "Nie udało się potwierdzić żądania." }, statusCode: 400);
}
