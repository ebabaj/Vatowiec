using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Vatowiec.Core.Domain;

namespace Vatowiec.Core.Infrastructure.Persistence.CompanyConfigurations;

internal sealed class ContractorConfiguration : IEntityTypeConfiguration<Contractor>
{
    public void Configure(EntityTypeBuilder<Contractor> builder)
    {
        builder.ToTable("Contractors", "core", table =>
        {
            table.HasCheckConstraint("CK_Contractors_Id", "[Id] <> '00000000-0000-0000-0000-000000000000'");
            table.HasCheckConstraint("CK_Contractors_Name", "LEN(LTRIM(RTRIM([Name]))) > 0");
            table.HasCheckConstraint("CK_Contractors_Code", "LEN(LTRIM(RTRIM([Code]))) > 0");
            table.HasCheckConstraint("CK_Contractors_CountryCode",
                "LEN([CountryCode]) = 2 AND [CountryCode] COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^A-Z]%'");
            table.HasCheckConstraint("CK_Contractors_Dates", "[UpdatedAtUtc] >= [CreatedAtUtc]");
        });
        builder.HasKey(x => new { x.CompanyId, x.Id });
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Code).HasMaxLength(64).IsRequired();
        builder.Property(x => x.Name).HasMaxLength(256).IsRequired();
        builder.Property(x => x.CountryCode).HasMaxLength(2).IsRequired();
        builder.Property(x => x.TaxIdentifier).HasMaxLength(32);
        builder.Property(x => x.RowVersion).IsRowVersion();
        builder.HasOne<CompanyProfile>().WithMany().HasForeignKey(x => x.CompanyId)
            .HasPrincipalKey(x => x.CompanyId).OnDelete(DeleteBehavior.NoAction);
        builder.HasIndex(x => new { x.CompanyId, x.Code }).IsUnique();
        builder.HasIndex(x => new { x.CompanyId, x.IsActive, x.Code });
    }
}
