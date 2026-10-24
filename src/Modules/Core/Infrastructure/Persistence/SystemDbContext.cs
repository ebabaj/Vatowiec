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
    public DbSet<UserPasswordHistory> UserPasswordHistories => Set<UserPasswordHistory>();
    public DbSet<PasswordPolicy> PasswordPolicies => Set<PasswordPolicy>();
    public DbSet<PasswordPolicyChange> PasswordPolicyChanges => Set<PasswordPolicyChange>();
    public DbSet<UserAdministrationChange> UserAdministrationChanges => Set<UserAdministrationChange>();
    public DbSet<InstallationIdentity> InstallationIdentities => Set<InstallationIdentity>();
    public DbSet<TaxIdentifierScheme> TaxIdentifierSchemes => Set<TaxIdentifierScheme>();
    public DbSet<GusConfiguration> GusConfigurations => Set<GusConfiguration>();
    public DbSet<RegistryConfiguration> RegistryConfigurations => Set<RegistryConfiguration>();
    public DbSet<RegistryConfigurationChange> RegistryConfigurationChanges => Set<RegistryConfigurationChange>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<PasswordPolicy>(b =>
        {
            b.ToTable("PasswordPolicy", "security", t => t.HasCheckConstraint("CK_PasswordPolicy_Values", "[SingletonKey]=1 AND [MinimumLength] BETWEEN 1 AND 1024"));
            b.HasKey(x => x.SingletonKey); b.Property(x => x.SingletonKey).ValueGeneratedNever(); b.Property(x => x.RowVersion).IsRowVersion();
            b.HasData(new { SingletonKey = 1, IsEnabled = true, MinimumLength = 12, RequireUppercase = true,
                RequireLowercase = true, RequireDigit = true, RequireSpecialCharacter = true, PreventPasswordReuse = false,
                PreviousPasswordCount = 5, EnforcePasswordExpiration = false, PasswordValidityDays = 90 });
            b.ToTable("PasswordPolicy", "security", t => t.HasCheckConstraint("CK_PasswordPolicy_Lifetime", "[PreviousPasswordCount] BETWEEN 1 AND 20 AND [PasswordValidityDays] BETWEEN 1 AND 3650"));
        });
        modelBuilder.Entity<UserPasswordHistory>(b =>
        {
            b.ToTable("UserPasswordHistory", "security"); b.HasKey(x => x.Id);
            b.HasIndex(x => new { x.UserId, x.Id }); b.Property(x => x.PasswordHash).HasMaxLength(2048);
            b.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<PasswordPolicyChange>(b =>
        {
            b.ToTable("PasswordPolicyChanges", "audit", t => t.HasTrigger("TR_PasswordPolicyChanges_Immutable"));
            b.HasKey(x => x.Id); b.Property(x => x.ActorName).HasMaxLength(256);
            b.Property(x => x.BeforeJson).HasMaxLength(2048); b.Property(x => x.AfterJson).HasMaxLength(2048);
        });
        modelBuilder.Entity<UserAdministrationChange>(b =>
        {
            b.ToTable("UserAdministrationChanges", "audit", t => t.HasTrigger("TR_UserAdministrationChanges_Immutable"));
            b.HasKey(x => x.Id);
            b.Property(x => x.ActorName).HasMaxLength(256); b.Property(x => x.TargetName).HasMaxLength(256);
            b.Property(x => x.Operation).HasMaxLength(48);
            b.HasIndex(x => new { x.TargetId, x.Id });
        });
        modelBuilder.ApplyConfiguration(new RegistryConfigurationMapping());
        modelBuilder.ApplyConfiguration(new RegistryConfigurationChangeMapping());
        modelBuilder.Entity<GusConfiguration>(entity =>
        {
            entity.ToTable("GusConfiguration", "security", table =>
                table.HasCheckConstraint("CK_GusConfiguration_Singleton", "[SingletonKey] = 1"));
            entity.HasKey(x => x.SingletonKey);
            entity.Property(x => x.SingletonKey).ValueGeneratedNever();
            entity.Property(x => x.ProtectedApiKey).HasMaxLength(8192);
            entity.Property(x => x.RowVersion).IsRowVersion();
        });
        modelBuilder.Entity<TaxIdentifierScheme>(b =>
        {
            b.ToTable("TaxIdentifierSchemes", "ref", t => t.HasCheckConstraint("CK_TaxIdentifierSchemes_Dates", "[ValidTo] IS NULL OR [ValidTo]>=[ValidFrom]"));
            b.HasKey(x => x.Id);
            b.Property(x => x.CountryCode).HasColumnType("char(2)");
            b.Property(x => x.IdentifierType).HasMaxLength(16);
            b.Property(x => x.VatPrefix).HasMaxLength(2);
            b.Property(x => x.FormatPattern).HasMaxLength(512);
            b.Property(x => x.AlgorithmKey).HasMaxLength(64);
            b.Property(x => x.Version).HasMaxLength(32);
            b.Property(x => x.SourceUrl).HasMaxLength(1024);
            b.HasIndex(x => new { x.CountryCode, x.IdentifierType, x.Version }).IsUnique();
            b.HasData(Vatowiec.Core.Application.TaxSchemeSeed.Rules());
        });
        modelBuilder.Entity<InstallationIdentity>(entity =>
        {
            entity.ToTable("Installation", "security", table =>
                table.HasCheckConstraint("CK_Installation_Singleton", "[SingletonKey] = 1 AND [Id] <> '00000000-0000-0000-0000-000000000000'"));
            entity.HasKey(x => x.SingletonKey);
            entity.Property(x => x.SingletonKey).ValueGeneratedNever();
            entity.HasIndex(x => x.Id).IsUnique();
        });
        modelBuilder.Entity<ApplicationUser>().ToTable("Users", "security");
        modelBuilder.Entity<ApplicationUser>().Property(x => x.HistoricalUserName).HasMaxLength(256);
        modelBuilder.Entity<ApplicationUser>().Property(x => x.HistoricalNormalizedUserName).HasMaxLength(256);
        modelBuilder.Entity<ApplicationUser>().Property(x => x.ArchivedByName).HasMaxLength(256);
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
        ProtectRegistryAudit();
        TrackedChanges.Prepare(this);
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default)
    {
        ProtectRegistryAudit();
        TrackedChanges.Prepare(this);
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }
    private void ProtectRegistryAudit()
    {
        if (ChangeTracker.Entries<PasswordPolicyChange>().Any(x => x.State is EntityState.Modified or EntityState.Deleted))
            throw new InvalidOperationException("Historia polityki haseł jest niezmienna.");
        if (ChangeTracker.Entries<UserAdministrationChange>().Any(x => x.State is EntityState.Modified or EntityState.Deleted))
            throw new InvalidOperationException("Historia administracji użytkownikami jest niezmienna.");
        if (ChangeTracker.Entries<RegistryConfigurationChange>().Any(x => x.State is EntityState.Modified or EntityState.Deleted))
            throw new InvalidOperationException("Audyt konfiguracji rejestrów jest niezmienny.");
    }
}
