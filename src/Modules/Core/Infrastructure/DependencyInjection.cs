using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Vatowiec.Core.Application;
using Vatowiec.Core.Infrastructure.Persistence;

namespace Vatowiec.Core.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        string? connectionString,
        bool isDevelopment = false)
    {
        DevelopmentDatabase.ValidateRuntime(connectionString, isDevelopment);
        return RegisterInfrastructure(services, _ => connectionString, isDevelopment);
    }

    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration,
        bool isDevelopment) =>
        RegisterInfrastructure(services, _ => configuration.GetConnectionString("Vatowiec"), isDevelopment);

    private static IServiceCollection RegisterInfrastructure(
        IServiceCollection services,
        Func<IServiceProvider, string?> getConnectionString,
        bool isDevelopment)
    {
        services.TryAddScoped<ICompanyContext>(_ => new CompanyContext(null));
        services.AddDbContext<CoreDbContext>((provider, options) =>
        {
            // Odczyt po złożeniu konfiguracji hosta, w tym nadpisań testowych.
            var connectionString = getConnectionString(provider);
            DevelopmentDatabase.ValidateRuntime(connectionString, isDevelopment);
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                // Konfiguracja providera bez adresu serwera i bez otwierania połączenia.
                options.UseSqlServer();
            }
            else
            {
                options.UseSqlServer(connectionString);
            }
        });

        return services;
    }
}
