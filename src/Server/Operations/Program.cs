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
        if (ServerTls.ServerTlsCommand.Matches(args))
        {
            // ADR-047: lokalna komenda administracyjna zamiast uruchomienia usługi.
            // Konfiguracja jak usługi: appsettings z katalogu programu, niezależnie od bieżącego katalogu konsoli.
            var commandHost = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
            {
                Args = [], ContentRootPath = AppContext.BaseDirectory, EnvironmentName = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")
            });
            Environment.ExitCode = OperatingSystem.IsWindows() ? await ServerTls.ServerTlsCommand.Run(args, commandHost.Configuration) : 5;
            return;
        }
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
        if (OperatingSystem.IsWindows())
        {
            builder.Services.AddSingleton(Vatowiec.Core.Infrastructure.ServerTls.ServerTlsPaths.From(builder.Configuration));
            builder.Services.AddSingleton<ServerTls.ServerTlsStore>();
            builder.Services.AddScoped<ServerTls.ServerTlsReconciler>();
        }
        builder.Services.AddHostedService<OperationsWorker>();

        await builder.Build().RunAsync();
    }
}
