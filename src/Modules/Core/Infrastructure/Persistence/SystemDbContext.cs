using Microsoft.EntityFrameworkCore;
using Vatowiec.Core.Domain;
using Vatowiec.Core.Infrastructure.Persistence.SystemConfigurations;

namespace Vatowiec.Core.Infrastructure.Persistence;

public sealed class SystemDbContext(DbContextOptions<SystemDbContext> options) : DbContext(options)
{
    public DbSet<Company> Companies => Set<Company>();
    public DbSet<CompanyDatabaseRegistration> CompanyDatabases => Set<CompanyDatabaseRegistration>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new CompanyConfiguration());
        modelBuilder.ApplyConfiguration(new CompanyDatabaseRegistrationConfiguration());
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        TrackedChanges.Prepare(this);
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default)
    {
        TrackedChanges.Prepare(this);
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }
}
