using Vatowiec.Core.Infrastructure;
using Vatowiec.Core.Infrastructure.Persistence;
using Vatowiec.Api.Security;
using Microsoft.Data.SqlClient;
using System.Text.Json.Serialization;
using Vatowiec.SharedContracts;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();
builder.Services.AddHealthChecks();
builder.Services.ConfigureHttpJsonOptions(options => options.SerializerOptions.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow);
builder.Services.AddApiSecurity(builder.Configuration);
builder.Services.AddInfrastructure(builder.Configuration,
    builder.Environment.IsDevelopment());
builder.Services.AddSingleton<Vatowiec.Core.Application.IIdentifierChecksum, Vatowiec.Core.Application.PolishNipChecksum>();
builder.Services.AddSingleton<Vatowiec.Core.Application.IIdentifierChecksum, Vatowiec.Core.Application.FinnishVatChecksum>();
builder.Services.AddSingleton<Vatowiec.Core.Application.IIdentifierChecksum, Vatowiec.Core.Application.CroatianVatChecksum>();
builder.Services.AddScoped<Vatowiec.Core.Application.TaxIdentifierValidator>();
builder.Services.AddScoped<IdentifierChecks>();
Vatowiec.Core.Infrastructure.Registries.RegistryRegistration.AddRegistryAdapters(builder.Services);

var app = builder.Build();

app.UseExceptionHandler();
app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/api/v1"))
    {
        context.Response.Headers.CacheControl = "no-cache, no-store";
        context.Response.Headers.Pragma = "no-cache";
        if (!context.Request.IsHttps)
        {
            context.Response.StatusCode = 400;
            await context.Response.WriteAsJsonAsync(new { code = "https_required" });
            return;
        }
    }
    try { await next(context); }
    catch (BadHttpRequestException)
    {
        context.Response.StatusCode = 400;
        await context.Response.WriteAsJsonAsync(new { code = "invalid_request" });
    }
    catch (CompanyResolutionException error)
    {
        context.Response.StatusCode = error.Forbidden ? 403 : 503;
        await context.Response.WriteAsJsonAsync(new { code = error.Code });
    }
    catch (SqlException error)
    {
        // Log diagnostic metadata, never SQL text, parameters or connection strings.
        app.Logger.LogError("Database operation failed: SQL {SqlNumber}, route {Route}, correlation {CorrelationId}",
            error.Number, context.Request.Path.Value, context.TraceIdentifier);
        var passwordPolicy = context.Request.Path.StartsWithSegments("/api/v1/installation/security/password-policy");
        context.Response.StatusCode = 503;
        await context.Response.WriteAsJsonAsync(new
        {
            code = passwordPolicy ? error.Number == 208 ? "password_policy_schema_missing" : "password_policy_unavailable" : "system_unavailable",
            message = passwordPolicy ? error.Number == 208
                ? "Brak schematu polityki haseł w bazie systemowej instalacji. Administrator musi wykonać przygotowaną migrację System po sprawdzeniu backupu."
                : "Nie można odczytać lub zapisać polityki haseł w bazie systemowej instalacji. Nie potwierdzono operacji." : null
        });
    }
});
app.UseHttpsRedirection();
app.UseAuthentication();
app.Use(async (context, next) =>
{
    if (context.Items.ContainsKey("Vatowiec:MustChangePassword") && context.Request.Path.Value is not
        ("/api/v1/auth/csrf" or "/api/v1/auth/me" or "/api/v1/auth/logout" or "/api/v1/auth/password"))
    {
        context.Response.StatusCode = 403;
        await context.Response.WriteAsJsonAsync(new { code = "user_password_change_required", message = "Przed dalszą pracą ustaw własne hasło." });
        return;
    }
    await next(context);
});
app.UseAuthorization();
app.UseRateLimiter();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi().AllowAnonymous();
}

app.MapHealthChecks("/health").AllowAnonymous();
app.MapGet("/api/v1/status", () => new ApiStatusResponse("Vatowiec", "M0-skeleton"))
    .WithName("GetApiStatus").AllowAnonymous();

app.MapAuthentication();
app.MapUserAdministration();
app.MapGusConfiguration();
app.MapRegistryConfiguration();
app.MapCompanies();

app.Run();

// Punkt wejścia udostępniony hostowi testowemu bez uruchamiania zewnętrznego procesu.
public partial class Program;
