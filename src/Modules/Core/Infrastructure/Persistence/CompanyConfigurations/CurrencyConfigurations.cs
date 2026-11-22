using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Vatowiec.Core.Domain;

namespace Vatowiec.Core.Infrastructure.Persistence.CompanyConfigurations;

// Słownik walut bazy firmy: cel FK kartoteki i firmowa aktywność. Kursy NBP są w System (ADR-048).
internal sealed class CurrencyConfiguration : IEntityTypeConfiguration<Currency>
{
    public void Configure(EntityTypeBuilder<Currency> b)
    {
        b.ToTable("Currencies", "ref", t =>
        {
            t.UseSqlOutputClause(false);
            t.HasCheckConstraint("CK_Currencies_PLN", "[Code] <> 'PLN' OR [IsActive]=1");
            t.HasCheckConstraint("CK_Currencies_Code", "[Code] COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^A-Z]%' AND LEN([Code])=3");
        });
        b.HasKey(x => x.Code);
        b.Property(x => x.Code).HasColumnType("char(3)").UseCollation("Latin1_General_100_BIN2").ValueGeneratedNever().IsRequired();
        b.Property(x => x.Name).HasMaxLength(120).IsRequired(); b.Property(x => x.RowVersion).IsRowVersion();
        b.HasData(CurrencySeed.Rows().Select(x => new { x.Code, x.Name, x.IsActive }));
    }
}
internal sealed class CurrencyOperationConfiguration : IEntityTypeConfiguration<CurrencyOperation>
{
    public void Configure(EntityTypeBuilder<CurrencyOperation> b)
    {
        b.ToTable("CurrencyOperations", "audit", t => t.UseSqlOutputClause(false));
        b.HasKey(x => x.Id); b.Property(x => x.Id).UseIdentityColumn();
        b.Property(x => x.Kind).HasMaxLength(32).IsRequired(); b.Property(x => x.ActorName).HasMaxLength(256).IsRequired();
        b.Property(x => x.RequestHash).HasMaxLength(64).IsRequired(); b.Property(x => x.DetailsJson).HasColumnType("nvarchar(max)").IsRequired();
        b.HasIndex(x => new { x.ActorId, x.ActorIssuerId, x.OperationId }).IsUnique();
    }
}
