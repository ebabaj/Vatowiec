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
    catch (SqlException)
    {
        context.Response.StatusCode = 503;
        await context.Response.WriteAsJsonAsync(new { code = "system_unavailable" });
    }
});
app.UseHttpsRedirection();
app.UseAuthentication();
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
app.MapCompanies();

app.Run();

// Punkt wejścia udostępniony hostowi testowemu bez uruchamiania zewnętrznego procesu.
public partial class Program;
