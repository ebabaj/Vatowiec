using System.Text.Json.Serialization;
using Vatowiec.Api.Endpoints;
using Vatowiec.Api.Hosting;
using Vatowiec.Api.Security;
using Vatowiec.Core.Infrastructure;
using Vatowiec.Core.Infrastructure.Persistence;
using Vatowiec.SharedContracts;

PersonalWorker? personalWorker = null;
if (args is ["--personal-worker", _, _])
{
    if (!OperatingSystem.IsWindows()) return;
    personalWorker = await PersonalWorker.Prepare(args);
    if (personalWorker == null) return;
}
await using var personalLifetime = personalWorker;
var builder = personalWorker == null ? WebApplication.CreateBuilder(args) :
    WebApplication.CreateBuilder(new WebApplicationOptions { Args = [], EnvironmentName = PersonalDatabaseTarget.Edition });
if (personalWorker != null)
{
    if (!OperatingSystem.IsWindows()) return;
    personalWorker.Configure(builder);
}
else if (builder.Environment.IsEnvironment(PersonalDatabaseTarget.Edition))
    throw new InvalidOperationException("Personal wymaga uruchomienia przez lokalny klient VATowiec.");
if (builder.Environment.IsEnvironment(PersonalPrototypeDatabase.Edition))
    PersonalPrototypeConfiguration.Configure(builder);

if (personalWorker == null) ServerTlsHosting.ConfigureManagedEndpoints(builder);
builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();
builder.Services.AddHealthChecks();
builder.Services.ConfigureHttpJsonOptions(options => options.SerializerOptions.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow);
builder.Services.AddApiSecurity(builder.Configuration);
builder.Services.AddInfrastructure(builder.Configuration,
    builder.Environment.IsDevelopment());
builder.Services.AddApplicationServices();

var app = builder.Build();

app.UseExceptionHandler();
app.UseApiErrorHandling(context => personalWorker?.IsTrustedPipe(context) == true);
if (personalWorker == null) app.UseHttpsRedirection();
app.UseAuthentication();
app.UsePasswordChangeGate();
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
app.MapCompanyOperations();
app.MapBackupPolicy();
app.MapTableViews();
app.MapExchangeRateAdministration();
if (personalWorker == null) app.MapServerTls();

if (personalWorker != null && OperatingSystem.IsWindows()) personalWorker.Attach(app);
await app.RunAsync();

// Punkt wejścia udostępniony hostowi testowemu bez uruchamiania zewnętrznego procesu.
public partial class Program;
