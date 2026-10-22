using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Vatowiec.Core.Domain;

namespace Vatowiec.Core.Infrastructure.Persistence.CompanyConfigurations;

internal sealed class ExternalVerificationConfiguration : IEntityTypeConfiguration<ExternalVerification>
{
    public void Configure(EntityTypeBuilder<ExternalVerification> b)
    {
        b.ToTable("ExternalVerifications", "audit", t =>
        {
            t.UseSqlOutputClause(false);
            t.HasCheckConstraint("CK_ExternalVerifications_Environment", "[Environment] IN ('Test','Production','Mock')");
            t.HasCheckConstraint("CK_ExternalVerifications_Json", "ISJSON([DataJson])=1");
            t.HasCheckConstraint("CK_ExternalVerifications_Actor", "[ActorId]<>'00000000-0000-0000-0000-000000000000' AND [ActorIssuerId]<>'00000000-0000-0000-0000-000000000000'");
        });
        b.HasKey(x => x.Id); b.Property(x => x.Id).ValueGeneratedNever();
        b.HasOne<Contractor>().WithMany().HasForeignKey(x => x.ContractorId).OnDelete(DeleteBehavior.NoAction);
        b.HasOne<ContractorBankAccount>().WithMany().HasForeignKey(x => x.BankAccountId).OnDelete(DeleteBehavior.NoAction);
        b.HasIndex(x => new { x.ContractorId, x.StartedAtUtc });
        b.HasIndex(x => new { x.ActorIssuerId, x.ActorId, x.OperationId }).IsUnique();
        b.Property(x => x.Source).HasMaxLength(16); b.Property(x => x.Operation).HasMaxLength(32);
        b.Property(x => x.Environment).HasMaxLength(16); b.Property(x => x.TaxId).HasMaxLength(100);
        b.Property(x => x.VatEuId).HasMaxLength(100); b.Property(x => x.AccountNumber).HasMaxLength(100);
        b.Property(x => x.Outcome).HasMaxLength(32); b.Property(x => x.ServiceStatus).HasMaxLength(32);
        b.Property(x => x.ErrorCode).HasMaxLength(64); b.Property(x => x.Message).HasMaxLength(1000);
        b.Property(x => x.OfficeReference).HasMaxLength(256); b.Property(x => x.ActorName).HasMaxLength(256);
        b.Property(x => x.RequestHash).HasMaxLength(64); b.Property(x => x.AdapterVersion).HasMaxLength(64);
        b.Property(x => x.RuleVersion).HasMaxLength(256);
    }
}
