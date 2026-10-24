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
        string? systemConnectionString,
        bool isDevelopment = false)
    {
        DatabaseConnectionPolicy.ValidateSystem(systemConnectionString, isDevelopment);
        return RegisterInfrastructure(services, _ => systemConnectionString, isDevelopment);
    }

    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration,
        bool isDevelopment) =>
        RegisterInfrastructure(services, _ => configuration.GetConnectionString("VatowiecSystem"), isDevelopment,
            configuration["Vatowiec:Edition"] == PersonalPrototypeDatabase.Edition,
            configuration["Vatowiec:Edition"] == PersonalDatabaseTarget.Edition
                ? new PersonalDatabaseTarget(Guid.Parse(configuration["Personal:InstallationId"]!)) : null);

    private static IServiceCollection RegisterInfrastructure(
        IServiceCollection services,
        Func<IServiceProvider, string?> getConnectionString,
        bool isDevelopment, bool isPersonalPrototype = false, PersonalDatabaseTarget? personal = null)
    {
        services.TryAddScoped<ICompanyContext>(_ => new CompanyContext(null));
        services.AddScoped<IExchangeRateReader, ExchangeRateReader>();
        services.AddSingleton<IBankAccountValidator, IbanNetBankAccountValidator>();
        services.AddScoped<IBankDirectory, Registries.EwibBankDirectory>();
        services.AddHttpClient("ewib", client =>
        {
            client.Timeout = TimeSpan.FromSeconds(12);
            client.MaxResponseContentBufferSize = 1024 * 1024;
        }).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false });
        services.AddDbContext<SystemDbContext>((provider, options) =>
        {
            // Odczyt po złożeniu konfiguracji hosta, w tym nadpisań testowych.
            var connectionString = getConnectionString(provider);
            DatabaseConnectionPolicy.ValidateSystem(connectionString, isDevelopment, isPersonalPrototype, personal);
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

        services.AddDbContext<CompanyDbContext>((provider, options) =>
        {
            var company = provider.GetRequiredService<ICompanyContext>();
            var selection = provider.GetService<CompanyDatabaseSelection>();
            if (company.CompanyId is null || company.CompanyId == Guid.Empty ||
                selection is null || selection.CompanyId != company.CompanyId)
            {
                throw new InvalidOperationException("Baza firmy wymaga jawnego wyboru zgodnego z autoryzowanym kontekstem.");
            }

            DatabaseConnectionPolicy.ValidateCompany(selection.ConnectionString, isDevelopment, isPersonalPrototype, personal);
            options.UseSqlServer(selection.ConnectionString);
        });

        return services;
    }
}
