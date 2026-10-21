using Microsoft.EntityFrameworkCore;
using Vatowiec.Core.Application;
using Vatowiec.Core.Domain;

namespace Vatowiec.Core.Infrastructure.Persistence;

public sealed class CoreDbContext(
    DbContextOptions<CoreDbContext> options,
    ICompanyContext companyContext) : DbContext(options)
{
    public Guid? CurrentCompanyId { get; } = companyContext.CompanyId;
    public DbSet<Company> Companies => Set<Company>();
    public DbSet<Contractor> Contractors => Set<Contractor>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(CoreDbContext).Assembly);
        modelBuilder.Entity<Company>().HasQueryFilter(x =>
            CurrentCompanyId != null && x.Id == CurrentCompanyId);
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
        var entries = ChangeTracker.Entries<ICompanyEntity>()
            .Where(x => x.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            .ToArray();

        // Najpierw sprawdzenie całego zestawu; żadnych zmian przed odmową dostępu.
        foreach (var entry in entries)
        {
            if (CurrentCompanyId is null || CurrentCompanyId == Guid.Empty ||
                entry.Entity.CompanyId != CurrentCompanyId ||
                (entry.Entity is Contractor && entry.State != EntityState.Added &&
                 entry.Property(nameof(Contractor.CompanyId)).OriginalValue is Guid original &&
                 original != CurrentCompanyId))
            {
                throw new InvalidOperationException("Zapis wymaga zgodnego kontekstu firmy.");
            }
        }

        var now = DateTimeOffset.UtcNow;
        foreach (var entry in entries.Where(x => x.State != EntityState.Deleted))
        {
            if (entry.State == EntityState.Added)
            {
                entry.Property(nameof(ICompanyEntity.CreatedAtUtc)).CurrentValue = now;
            }
            else
            {
                entry.Property(nameof(ICompanyEntity.CreatedAtUtc)).IsModified = false;
            }

            entry.Property(nameof(ICompanyEntity.UpdatedAtUtc)).CurrentValue = now;
        }
    }
}
