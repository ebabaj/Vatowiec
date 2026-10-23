using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Vatowiec.Core.Domain;

namespace Vatowiec.Core.Infrastructure.Persistence.CompanyConfigurations;

internal sealed class CurrencyConfiguration : IEntityTypeConfiguration<Currency>
{
    public void Configure(EntityTypeBuilder<Currency> b)
    {
        b.ToTable("Currencies", "ref", t =>
        {
            t.UseSqlOutputClause(false);
            t.HasCheckConstraint("CK_Currencies_PLN", "[Code] <> 'PLN' OR ([IsActive]=1 AND [DownloadA]=0 AND [DownloadB]=0 AND [DownloadC]=0)");
            t.HasCheckConstraint("CK_Currencies_Active", "[IsActive]=1 OR ([DownloadA]=0 AND [DownloadB]=0 AND [DownloadC]=0)");
            t.HasCheckConstraint("CK_Currencies_Code", "[Code] COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^A-Z]%' AND LEN([Code])=3");
        });
        b.HasKey(x => x.Code);
        b.Property(x => x.Code).HasColumnType("char(3)").UseCollation("Latin1_General_100_BIN2").ValueGeneratedNever().IsRequired();
        b.Property(x => x.Name).HasMaxLength(120).IsRequired(); b.Property(x => x.RowVersion).IsRowVersion();
        b.HasData(CurrencySeed.Rows().Select(x => new { x.Code, x.Name, x.IsActive, x.DownloadA, x.DownloadB, x.DownloadC }));
    }
}
internal sealed class ExchangeRateConfiguration : IEntityTypeConfiguration<ExchangeRate>
{
    public void Configure(EntityTypeBuilder<ExchangeRate> b)
    {
        b.ToTable("ExchangeRates", "core", t =>
        {
            t.HasCheckConstraint("CK_ExchangeRates_Values", "[Source]='NBP' AND (([TableType] IN ('A','B') AND [Mid]>0 AND [Mid] IS NOT NULL AND [Bid] IS NULL AND [Ask] IS NULL AND [TradingDate] IS NULL) OR ([TableType]='C' AND [Mid] IS NULL AND [Bid]>0 AND [Ask]>0 AND [Bid] IS NOT NULL AND [Ask] IS NOT NULL AND [Bid]<=[Ask] AND ([TradingDate] IS NULL OR [TradingDate]<=[EffectiveDate])))");
        });
        b.HasKey(x => x.Id); b.Property(x => x.Id).UseIdentityColumn();
        b.Property(x => x.CurrencyCode).HasColumnType("char(3)").UseCollation("Latin1_General_100_BIN2").IsRequired();
        b.HasOne<Currency>().WithMany().HasForeignKey(x => x.CurrencyCode).OnDelete(DeleteBehavior.NoAction);
        b.Property(x => x.Source).HasMaxLength(8).IsUnicode(false).IsRequired();
        b.Property(x => x.TableType).HasColumnType("char(1)").IsRequired(); b.Property(x => x.TableNumber).HasMaxLength(32).IsRequired();
        b.Property(x => x.Mid).HasPrecision(24, 12); b.Property(x => x.Bid).HasPrecision(24, 12); b.Property(x => x.Ask).HasPrecision(24, 12);
        b.Property(x => x.RowVersion).IsRowVersion();
        b.HasIndex(x => new { x.CurrencyCode, x.Source, x.TableType, x.EffectiveDate }).IsUnique();
        b.HasIndex(x => x.EffectiveDate);
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
