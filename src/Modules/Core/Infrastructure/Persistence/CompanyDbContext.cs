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
    public DbSet<ContractorChange> ContractorChanges => Set<ContractorChange>();
    public DbSet<AddressType> AddressTypes => Set<AddressType>();
    public DbSet<ContractorAddress> ContractorAddresses => Set<ContractorAddress>();
    public DbSet<ContractorBankAccount> ContractorBankAccounts => Set<ContractorBankAccount>();
    public DbSet<ContractorContact> ContractorContacts => Set<ContractorContact>();
    public DbSet<ContractorCreateOperation> ContractorCreateOperations => Set<ContractorCreateOperation>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new CompanyProfileConfiguration());
        modelBuilder.ApplyConfiguration(new ContractorConfiguration());
        modelBuilder.ApplyConfiguration(new ContractorChangeConfiguration());
        modelBuilder.ApplyConfiguration(new AddressTypeConfiguration());
        modelBuilder.ApplyConfiguration(new ContractorAddressConfiguration());
        modelBuilder.ApplyConfiguration(new ContractorBankAccountConfiguration());
        modelBuilder.ApplyConfiguration(new ContractorContactConfiguration());
        modelBuilder.ApplyConfiguration(new ContractorCreateOperationConfiguration());
        modelBuilder.Entity<ContractorChange>().HasQueryFilter(x =>
            CurrentCompanyId != null && CompanyProfiles.Any(p => p.CompanyId == CurrentCompanyId));
        modelBuilder.Entity<CompanyProfile>().HasQueryFilter(x =>
            CurrentCompanyId != null && x.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<Contractor>().HasQueryFilter(x =>
            CurrentCompanyId != null && CompanyProfiles.Any(p => p.CompanyId == CurrentCompanyId));
        modelBuilder.Entity<AddressType>().HasQueryFilter(x =>
            CurrentCompanyId != null && CompanyProfiles.Any(p => p.CompanyId == CurrentCompanyId));
        modelBuilder.Entity<ContractorAddress>().HasQueryFilter(x =>
            CurrentCompanyId != null && CompanyProfiles.Any(p => p.CompanyId == CurrentCompanyId));
        modelBuilder.Entity<ContractorBankAccount>().HasQueryFilter(x =>
            CurrentCompanyId != null && CompanyProfiles.Any(p => p.CompanyId == CurrentCompanyId));
        modelBuilder.Entity<ContractorContact>().HasQueryFilter(x =>
            CurrentCompanyId != null && CompanyProfiles.Any(p => p.CompanyId == CurrentCompanyId));
        modelBuilder.Entity<ContractorCreateOperation>().HasQueryFilter(x =>
            CurrentCompanyId != null && CompanyProfiles.Any(p => p.CompanyId == CurrentCompanyId));
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        PrepareChanges();
        EnsureProfile(CompanyProfiles.IgnoreQueryFilters().AsNoTracking().Select(x => x.CompanyId).ToArray());
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default)
    {
        PrepareChanges();
        EnsureProfile(await CompanyProfiles.IgnoreQueryFilters().AsNoTracking().Select(x => x.CompanyId).ToArrayAsync(cancellationToken));
        return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void PrepareChanges()
    {
        ChangeTracker.DetectChanges();
        if (ChangeTracker.Entries<ContractorChange>().Any(x => x.State is EntityState.Modified or EntityState.Deleted))
            throw new InvalidOperationException("Historia kontrahenta jest niezmienna.");
        if (ChangeTracker.Entries<ContractorCreateOperation>().Any(x => x.State is EntityState.Modified or EntityState.Deleted))
            throw new InvalidOperationException("Wynik operacji tworzenia jest niezmienny.");
        if (CurrentCompanyId is null || CurrentCompanyId == Guid.Empty)
            throw new InvalidOperationException("Zapis wymaga zgodnego kontekstu firmy.");
        if (ChangeTracker.Entries<Contractor>().Any(x => x.State == EntityState.Deleted))
            throw new InvalidOperationException("Kontrahenta oznaczamy jako nieaktywnego, bez usuwania.");
        if (ChangeTracker.Entries<CompanyProfile>().Any(x => x.State == EntityState.Deleted))
            throw new InvalidOperationException("Nie można usunąć tożsamości bazy firmy.");
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

    private void EnsureProfile(Guid[] stored)
    {
        var added = ChangeTracker.Entries<CompanyProfile>().Where(x => x.State == EntityState.Added).Select(x => x.Entity.CompanyId).ToArray();
        if (stored.Concat(added).Count() != 1 || stored.Concat(added).Single() != CurrentCompanyId)
            throw new InvalidOperationException("Tożsamość fizycznej bazy nie pasuje do firmy.");
    }
}
