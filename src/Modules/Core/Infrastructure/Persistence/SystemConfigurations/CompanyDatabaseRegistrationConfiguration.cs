using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Vatowiec.Core.Domain;

namespace Vatowiec.Core.Infrastructure.Persistence.SystemConfigurations;

internal sealed class CompanyDatabaseRegistrationConfiguration : IEntityTypeConfiguration<CompanyDatabaseRegistration>
{
    public void Configure(EntityTypeBuilder<CompanyDatabaseRegistration> builder)
    {
        builder.ToTable("CompanyDatabases", "core", table =>
        {
            table.HasCheckConstraint("CK_CompanyDatabases_Location",
                "LEN(LTRIM(RTRIM([ServerName]))) > 0 AND LEN(LTRIM(RTRIM([DatabaseName]))) > 0 AND LEN(LTRIM(RTRIM([ConnectionProfileKey]))) > 0");
            table.HasCheckConstraint("CK_CompanyDatabases_Dates", "[UpdatedAtUtc] >= [CreatedAtUtc]");
        });
        builder.HasKey(x => x.CompanyId);
        builder.Property(x => x.CompanyId).ValueGeneratedNever();
        builder.Property(x => x.ServerName).HasMaxLength(128).IsRequired();
        builder.Property(x => x.DatabaseName).HasMaxLength(128).IsRequired();
        builder.Property(x => x.ConnectionProfileKey).HasMaxLength(128).IsRequired();
        builder.Property(x => x.RowVersion).IsRowVersion();
        builder.HasIndex(x => new { x.ServerName, x.DatabaseName }).IsUnique();
        builder.HasOne<Company>().WithOne().HasForeignKey<CompanyDatabaseRegistration>(x => x.CompanyId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}
