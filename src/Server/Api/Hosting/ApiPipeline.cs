using Microsoft.Data.SqlClient;
using Vatowiec.Core.Application;
using Vatowiec.Core.Infrastructure.Contractors;
using Vatowiec.Core.Infrastructure.Identity;
using Vatowiec.Core.Infrastructure.Operations;
using Vatowiec.Core.Infrastructure.Persistence;

namespace Vatowiec.Api.Hosting;

/// <summary>Rejestracja usług aplikacyjnych i wspólne elementy potoku żądań API.</summary>
internal static class ApiPipeline
{
    private const string PasswordPolicyPath = "/api/v1/installation/security/password-policy";

    /// <summary>Usługi aplikacyjne wywoływane przez endpointy (ADR-046).</summary>
    public static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        services.AddSingleton<IIdentifierChecksum, PolishNipChecksum>();
        services.AddSingleton<IIdentifierChecksum, FinnishVatChecksum>();
        services.AddSingleton<IIdentifierChecksum, CroatianVatChecksum>();
        services.AddScoped<TaxIdentifierValidator>();
        services.AddScoped<IdentifierChecks>();
        services.AddScoped<CompanyOperationRequests>();
        services.AddScoped<CompanyBackupService>();
        services.AddScoped<Core.Infrastructure.ServerTls.ServerTlsService>();
        services.AddSingleton<Core.Infrastructure.ServerTls.IServerCertificateSource, Core.Infrastructure.ServerTls.LocalMachineCertificateSource>();
        services.AddSingleton(sp => Core.Infrastructure.ServerTls.ServerTlsPaths.From(sp.GetRequiredService<IConfiguration>()));
        services.AddSingleton<ServerCertificateProvider>();
        services.AddHostedService(sp => sp.GetRequiredService<ServerCertificateProvider>());
        Core.Infrastructure.Registries.RegistryRegistration.AddRegistryAdapters(services);
        return services;
    }

    /// <summary>
    /// Nagłówki bez cache, wymóg HTTPS dla /api/v1 (poza zaufanym potokiem Personal) oraz zamiana
    /// znanych wyjątków na konkretne kody błędów zamiast surowych wyjątków SQL (AGENTS §6.7).
    /// </summary>
    public static WebApplication UseApiErrorHandling(this WebApplication app, Func<HttpContext, bool> isTrustedPipe)
    {
        app.Use(async (context, next) =>
        {
            if (context.Request.Path.StartsWithSegments("/api/v1"))
            {
                context.Response.Headers.CacheControl = "no-cache, no-store";
                context.Response.Headers.Pragma = "no-cache";
                if (!context.Request.IsHttps && !isTrustedPipe(context))
                {
                    await WriteError(context, 400, new { code = "https_required" });
                    return;
                }
            }

            try
            {
                await next(context);
            }
            catch (BadHttpRequestException)
            {
                await WriteError(context, 400, new { code = "invalid_request" });
            }
            catch (CompanyResolutionException error)
            {
                await WriteError(context, error.Forbidden ? 403 : 503, new { code = error.Code });
            }
            catch (UserAdministrationException error)
            {
                await WriteError(context, error.Status, new { code = error.Code, message = error.Message });
            }
            catch (SqlException error)
            {
                // Tylko metadane diagnostyczne: nigdy treść SQL, parametry ani connection string.
                app.Logger.LogError("Database operation failed: SQL {SqlNumber}, route {Route}, correlation {CorrelationId}",
                    error.Number, context.Request.Path.Value, context.TraceIdentifier);
                await WriteError(context, 503, SqlFailure(context, error));
            }
        });
        return app;
    }

    /// <summary>Sesja z obowiązkową zmianą hasła ma dostęp wyłącznie do endpointów zmiany hasła (ADR-033).</summary>
    public static WebApplication UsePasswordChangeGate(this WebApplication app)
    {
        app.Use(async (context, next) =>
        {
            if (context.Items.ContainsKey("Vatowiec:MustChangePassword") && context.Request.Path.Value is not
                ("/api/v1/auth/csrf" or "/api/v1/auth/me" or "/api/v1/auth/logout" or "/api/v1/auth/password"))
            {
                await WriteError(context, 403, new
                {
                    code = "user_password_change_required",
                    message = "Przed dalszą pracą ustaw własne hasło."
                });
                return;
            }
            await next(context);
        });
        return app;
    }

    private static object SqlFailure(HttpContext context, SqlException error)
    {
        if (!context.Request.Path.StartsWithSegments(PasswordPolicyPath))
            return new { code = "system_unavailable", message = (string?)null };
        return error.Number == 208
            ? new
            {
                code = "password_policy_schema_missing",
                message = (string?)"Brak schematu polityki haseł w bazie systemowej instalacji. Administrator musi wykonać przygotowaną migrację System po sprawdzeniu backupu."
            }
            : new
            {
                code = "password_policy_unavailable",
                message = (string?)"Nie można odczytać lub zapisać polityki haseł w bazie systemowej instalacji. Nie potwierdzono operacji."
            };
    }

    private static Task WriteError(HttpContext context, int status, object body)
    {
        context.Response.StatusCode = status;
        return context.Response.WriteAsJsonAsync(body);
    }
}
