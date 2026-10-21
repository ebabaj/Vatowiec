using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Vatowiec.Core.Domain;
using Vatowiec.Core.Infrastructure.Identity;
using Vatowiec.Core.Infrastructure.Persistence.SystemConfigurations;

namespace Vatowiec.Core.Infrastructure.Persistence;

public sealed class SystemDbContext(DbContextOptions<SystemDbContext> options)
    : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>(options)
{
    public DbSet<Company> Companies => Set<Company>();
    public DbSet<CompanyDatabaseRegistration> CompanyDatabases => Set<CompanyDatabaseRegistration>();
    public DbSet<CompanyAccess> CompanyAccesses => Set<CompanyAccess>();
    public DbSet<UserSession> UserSessions => Set<UserSession>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<ApplicationUser>().ToTable("Users", "security");
        modelBuilder.Entity<IdentityRole<Guid>>().ToTable("Roles", "security");
        modelBuilder.Entity<IdentityUserRole<Guid>>().ToTable("UserRoles", "security");
        modelBuilder.Entity<IdentityUserClaim<Guid>>().ToTable("UserClaims", "security");
        modelBuilder.Entity<IdentityUserLogin<Guid>>().ToTable("UserLogins", "security");
        modelBuilder.Entity<IdentityUserToken<Guid>>().ToTable("UserTokens", "security");
        modelBuilder.Entity<IdentityRoleClaim<Guid>>().ToTable("RoleClaims", "security");
        modelBuilder.Entity<IdentityUserLogin<Guid>>().Property(x => x.LoginProvider).HasMaxLength(128);
        modelBuilder.Entity<IdentityUserLogin<Guid>>().Property(x => x.ProviderKey).HasMaxLength(128);
        modelBuilder.Entity<IdentityUserToken<Guid>>().Property(x => x.LoginProvider).HasMaxLength(128);
        modelBuilder.Entity<IdentityUserToken<Guid>>().Property(x => x.Name).HasMaxLength(128);
        modelBuilder.ApplyConfiguration(new CompanyConfiguration());
        modelBuilder.ApplyConfiguration(new CompanyDatabaseRegistrationConfiguration());
        modelBuilder.ApplyConfiguration(new CompanyAccessConfiguration());
        modelBuilder.ApplyConfiguration(new UserSessionConfiguration());
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
