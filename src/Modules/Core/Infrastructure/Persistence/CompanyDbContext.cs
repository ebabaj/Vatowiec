using Microsoft.EntityFrameworkCore;
using Vatowiec.Core.Application;
using Vatowiec.Core.Domain;
using Vatowiec.Core.Infrastructure.Persistence.CompanyConfigurations;

namespace Vatowiec.Core.Infrastructure.Persistence;

public sealed class CompanyDbContext(
    DbContextOptions<CompanyDbContext> options,
    ICompanyContext companyContext) : DbContext(options)
{
    public Guid? CurrentCompanyId { get; } = companyContext.CompanyId;
    public DbSet<CompanyProfile> CompanyProfiles => Set<CompanyProfile>();
    public DbSet<Contractor> Contractors => Set<Contractor>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new CompanyProfileConfiguration());
        modelBuilder.ApplyConfiguration(new ContractorConfiguration());
        modelBuilder.Entity<CompanyProfile>().HasQueryFilter(x =>
            CurrentCompanyId != null && x.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<Contractor>().HasQueryFilter(x =>
            CurrentCompanyId != null && x.CompanyId == CurrentCompanyId);
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        PrepareChanges();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default)
    {
        PrepareChanges();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void PrepareChanges()
    {
        ChangeTracker.DetectChanges();
        foreach (var entry in ChangeTracker.Entries<ICompanyEntity>()
                     .Where(x => x.State is EntityState.Added or EntityState.Modified or EntityState.Deleted))
        {
            if (CurrentCompanyId is null || CurrentCompanyId == Guid.Empty ||
                entry.Entity.CompanyId != CurrentCompanyId ||
                (entry.State != EntityState.Added &&
                 entry.Property(nameof(ICompanyEntity.CompanyId)).OriginalValue is Guid original &&
                 original != CurrentCompanyId))
            {
                throw new InvalidOperationException("Zapis wymaga zgodnego kontekstu firmy.");
            }
        }

        TrackedChanges.Prepare(this);
    }
}
