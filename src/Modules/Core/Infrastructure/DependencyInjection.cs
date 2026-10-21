using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Vatowiec.Core.Infrastructure.Persistence;

namespace Vatowiec.Core.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        string? connectionString)
    {
        services.AddDbContext<CoreDbContext>(options =>
        {
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
