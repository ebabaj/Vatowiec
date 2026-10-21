using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Vatowiec.Core.Application;

namespace Vatowiec.Core.Infrastructure.Persistence;

public sealed class CoreDbContextFactory : IDesignTimeDbContextFactory<CoreDbContext>
{
    public CoreDbContext CreateDbContext(string[] args)
    {
        // Celowo bez domyślnej bazy i bez zależności od profilu uruchomienia API.
        if (Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") != "Development")
        {
            throw new InvalidOperationException("Narzędzie migracji wymaga DOTNET_ENVIRONMENT=Development.");
        }

        var connection = DevelopmentDatabase.Validate(
            Environment.GetEnvironmentVariable("ConnectionStrings__Vatowiec"));
        var options = new DbContextOptionsBuilder<CoreDbContext>().UseSqlServer(connection).Options;
        return new CoreDbContext(options, new CompanyContext(null));
    }
}
