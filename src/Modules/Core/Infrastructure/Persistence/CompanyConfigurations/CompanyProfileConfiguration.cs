using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Vatowiec.Core.Domain;

namespace Vatowiec.Core.Infrastructure.Persistence.CompanyConfigurations;

internal sealed class CompanyProfileConfiguration : IEntityTypeConfiguration<CompanyProfile>
{
    public void Configure(EntityTypeBuilder<CompanyProfile> builder)
    {
        builder.ToTable("CompanyProfile", "core", table =>
        {
            table.HasCheckConstraint("CK_CompanyProfile_Singleton", "[SingletonKey] = 1");
            table.HasCheckConstraint("CK_CompanyProfile_CompanyId", "[CompanyId] <> '00000000-0000-0000-0000-000000000000'");
            table.HasCheckConstraint("CK_CompanyProfile_Name", "LEN(LTRIM(RTRIM([Name]))) > 0");
            table.HasCheckConstraint("CK_CompanyProfile_Dates", "[UpdatedAtUtc] >= [CreatedAtUtc]");
        });
        builder.HasKey(x => x.SingletonKey);
        builder.Property(x => x.SingletonKey).ValueGeneratedNever();
        builder.HasAlternateKey(x => x.CompanyId);
        builder.Property(x => x.CompanyId).ValueGeneratedNever();
        builder.Property(x => x.Name).HasMaxLength(256).IsRequired();
        builder.Property(x => x.Nip).HasMaxLength(32);
        builder.Property(x => x.Regon).HasMaxLength(14);
        builder.Property(x => x.RowVersion).IsRowVersion();
    }
}
