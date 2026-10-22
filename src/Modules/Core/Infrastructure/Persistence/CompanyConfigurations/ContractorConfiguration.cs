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
            table.UseSqlOutputClause(false); // Tabela ma trigger chroniący hierarchię.
            table.HasCheckConstraint("CK_Contractors_Parent", "[ParentId] IS NULL OR [ParentId] <> [Id]");
            table.HasCheckConstraint("CK_Contractors_Name", "LEN(LTRIM(RTRIM([Name]))) > 0");
            table.HasCheckConstraint("CK_Contractors_Code", "LEN(LTRIM(RTRIM([Code]))) > 0");
            table.HasCheckConstraint("CK_Contractors_CountryCode",
                "LEN([CountryCode]) = 2 AND [CountryCode] COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^A-Z]%'");
            table.HasCheckConstraint("CK_Contractors_Dates", "[UpdatedAtUtc] >= [CreatedAtUtc]");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).UseIdentityColumn();
        builder.Property(x => x.Code).HasMaxLength(64).IsRequired();
        builder.Property(x => x.Name).HasMaxLength(256).IsRequired();
        builder.Property(x => x.CountryCode).HasMaxLength(2).IsRequired();
        builder.Property(x => x.TaxId).HasMaxLength(100);
        builder.Property(x => x.VatEuId).HasMaxLength(100);
        builder.Property(x => x.KsefIdWew).HasMaxLength(100);
        builder.Property(x => x.RowVersion).IsRowVersion();
        builder.HasOne<Contractor>().WithMany().HasForeignKey(x => x.ParentId).OnDelete(DeleteBehavior.NoAction);
        builder.HasIndex(x => x.Code).IsUnique();
        builder.HasIndex(x => new { x.IsActive, x.Code });
    }
}
