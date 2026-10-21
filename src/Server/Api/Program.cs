using Vatowiec.Core.Infrastructure;
using Vatowiec.SharedContracts;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();
builder.Services.AddHealthChecks();
builder.Services.AddInfrastructure(builder.Configuration,
    builder.Environment.IsDevelopment());

var app = builder.Build();

app.UseExceptionHandler();
app.UseHttpsRedirection();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapHealthChecks("/health");
app.MapGet("/api/v1/status", () => new ApiStatusResponse("Vatowiec", "M0-skeleton"))
    .WithName("GetApiStatus");

app.Run();

// Punkt wejścia udostępniony hostowi testowemu bez uruchamiania zewnętrznego procesu.
public partial class Program;
