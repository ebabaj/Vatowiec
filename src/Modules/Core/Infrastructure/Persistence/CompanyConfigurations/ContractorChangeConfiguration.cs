using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Vatowiec.Core.Domain;

namespace Vatowiec.Core.Infrastructure.Persistence.CompanyConfigurations;

internal sealed class ContractorChangeConfiguration : IEntityTypeConfiguration<ContractorChange>
{
    public void Configure(EntityTypeBuilder<ContractorChange> builder)
    {
        builder.ToTable("ContractorChanges", "audit", table =>
        {
            table.HasCheckConstraint("CK_ContractorChanges_Json", "ISJSON([AfterJson]) = 1 AND ([BeforeJson] IS NULL OR ISJSON([BeforeJson]) = 1)");
            table.HasCheckConstraint("CK_ContractorChanges_Actor", "[ActorId] <> '00000000-0000-0000-0000-000000000000' AND [ActorIssuerId] <> '00000000-0000-0000-0000-000000000000'");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.ActorName).HasMaxLength(256).IsRequired();
        builder.Property(x => x.CorrelationId).HasMaxLength(128).IsRequired();
        builder.Property(x => x.Operation).HasMaxLength(16).IsRequired();
        builder.Property(x => x.RowVersion).IsRowVersion();
        builder.HasOne<Contractor>().WithMany().HasForeignKey(x => x.ContractorId).OnDelete(DeleteBehavior.NoAction);
        builder.HasIndex(x => new { x.ContractorId, x.OccurredAtUtc });
    }
}
