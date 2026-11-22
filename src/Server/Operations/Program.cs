using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Vatowiec.Core.Infrastructure.Persistence;

namespace Vatowiec.Operations;

// ADR-044: VATowiec Operations Worker. Działa jako usługa Windows albo w konsoli (dotnet run).
// Używa własnych połączeń z prawami tworzenia baz i migracji; zwykłe API ich nie otrzymuje.
public static class OperationsProgram
{
    public static async Task Main(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(args);
        builder.Services.AddWindowsService(options => options.ServiceName = "VATowiec Operations");

        var systemConnection = builder.Configuration.GetConnectionString("VatowiecSystem");
        DatabaseConnectionPolicy.ValidateSystem(systemConnection, builder.Environment.IsDevelopment());
        if (string.IsNullOrWhiteSpace(systemConnection))
            throw new InvalidOperationException("Brak ConnectionStrings:VatowiecSystem dla procesu operacji technicznych.");

        builder.Services.TryAddSingleton(TimeProvider.System);
        builder.Services.AddDbContext<SystemDbContext>(options => options.UseSqlServer(systemConnection));
        builder.Services.AddSingleton(new OperationsWorkerIdentity($"{Environment.MachineName}/{Environment.ProcessId}"));
        builder.Services.AddSingleton<OperationsConnections>();
        builder.Services.AddSingleton<BackupStorage>();
        builder.Services.AddScoped<Vatowiec.Core.Infrastructure.Operations.CompanyBackupService>();
        builder.Services.AddScoped<BackupScheduler>();
        builder.Services.AddScoped<BackupImportScanner>();
        builder.Services.AddScoped<CompanyOperationExecutor>();
        builder.Services.AddHostedService<OperationsWorker>();

        await builder.Build().RunAsync();
    }
}
