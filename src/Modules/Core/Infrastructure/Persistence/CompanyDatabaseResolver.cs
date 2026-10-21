using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Vatowiec.Core.Application;
using Vatowiec.Core.Domain;

namespace Vatowiec.Core.Infrastructure.Persistence;

public sealed class CompanyResolutionException(string code, bool forbidden = false) : Exception(code)
{
    public string Code { get; } = code;
    public bool Forbidden { get; } = forbidden;
}

// Wynik należy do jednej operacji; nie jest współdzielony między żądaniami/użytkownikami.
public sealed class AuthorizedCompanyDatabase(CompanyDbContext context, CompanyPermission permissions) : IAsyncDisposable
{
    public CompanyDbContext Context { get; } = context;
    public CompanyPermission Permissions { get; } = permissions;
    public ValueTask DisposeAsync() => Context.DisposeAsync();
}

public sealed class CompanyDatabaseResolver(SystemDbContext system, IConfiguration configuration, ILogger<CompanyDatabaseResolver> logger)
{
    public async Task<AuthorizedCompanyDatabase> Resolve(Guid userId, Guid companyId,
        CompanyPermission required, bool isDevelopment, CancellationToken cancellationToken = default)
    {
        if (userId == Guid.Empty || companyId == Guid.Empty || required == CompanyPermission.None)
            throw new CompanyResolutionException("company_access_denied", true);

        CompanyDbContext? context = null;
        try
        {
            var access = await system.CompanyAccesses.AsNoTracking().SingleOrDefaultAsync(
                x => x.UserId == userId && x.CompanyId == companyId, cancellationToken);
            if (access is null || (access.Permissions & required) != required ||
                !await system.Companies.AnyAsync(x => x.Id == companyId && x.IsActive, cancellationToken))
                throw new CompanyResolutionException("company_access_denied", true);
            var registration = await system.CompanyDatabases.AsNoTracking().SingleOrDefaultAsync(
                x => x.CompanyId == companyId, cancellationToken);
            if (registration is null) throw new CompanyResolutionException("company_unavailable");
            var profile = configuration[$"CompanyConnections:{registration.ConnectionProfileKey}"];
            if (string.IsNullOrWhiteSpace(profile)) throw new CompanyResolutionException("company_unavailable");
            var connection = new SqlConnectionStringBuilder(profile);
            if (!string.Equals(connection.DataSource, registration.ServerName, StringComparison.OrdinalIgnoreCase))
                throw new CompanyResolutionException("company_unavailable");
            connection.InitialCatalog = registration.DatabaseName;
            DatabaseConnectionPolicy.ValidateCompany(connection.ConnectionString, isDevelopment);
            context = new CompanyDbContext(new DbContextOptionsBuilder<CompanyDbContext>()
                .UseSqlServer(connection.ConnectionString).Options, new CompanyContext(companyId));
            var identity = await context.CompanyProfiles.IgnoreQueryFilters().AsNoTracking()
                .SingleOrDefaultAsync(cancellationToken);
            if (identity is null || identity.CompanyId != companyId)
                throw new CompanyResolutionException("company_identity_mismatch");
            var applied = (await context.Database.GetAppliedMigrationsAsync(cancellationToken)).Order().ToArray();
            if (!applied.SequenceEqual(context.Database.GetMigrations().Order()))
                throw new CompanyResolutionException("company_schema_incompatible");
            return new AuthorizedCompanyDatabase(context, access.Permissions);
        }
        catch (Exception error) when (error is SqlException or InvalidOperationException or ArgumentException or CompanyResolutionException)
        {
            logger.LogWarning("Company operation denied: user {UserId}, company {CompanyId}, reason {Reason}",
                userId, companyId, error is CompanyResolutionException reason ? reason.Code : "company_unavailable");
            if (context is not null) await context.DisposeAsync();
            if (error is CompanyResolutionException) throw;
            throw new CompanyResolutionException("company_unavailable");
        }
        catch
        {
            if (context is not null) await context.DisposeAsync();
            throw;
        }
    }
}
