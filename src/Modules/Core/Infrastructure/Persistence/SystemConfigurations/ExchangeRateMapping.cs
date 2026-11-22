using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Vatowiec.Core.Domain;

namespace Vatowiec.Core.Infrastructure.Persistence.SystemConfigurations;

// ADR-048: publiczne dane referencyjne w Vatowiec_System — słownik ISO, kursy NBP, stan pobierania,
// konflikty i niezmienna historia. Bez CompanyId i bez ustawień poszczególnych firm.
internal sealed class ReferenceCurrencyMapping : IEntityTypeConfiguration<ReferenceCurrency>
{
    public void Configure(EntityTypeBuilder<ReferenceCurrency> b)
    {
        b.ToTable("Currencies", "ref", t =>
        {
            t.UseSqlOutputClause(false);
            t.HasCheckConstraint("CK_Currencies_Code", "[Code] COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^A-Z]%' AND LEN([Code])=3");
        });
        b.HasKey(x => x.Code);
        b.Property(x => x.Code).HasColumnType("char(3)").UseCollation("Latin1_General_100_BIN2").ValueGeneratedNever().IsRequired();
        b.Property(x => x.Name).HasMaxLength(120).IsRequired();
        b.HasData(CurrencySeed.ReferenceRows().Select(x => new { x.Code, x.Name }));
    }
}

internal sealed class ExchangeRateMapping : IEntityTypeConfiguration<ExchangeRate>
{
    public void Configure(EntityTypeBuilder<ExchangeRate> b)
    {
        b.ToTable("ExchangeRates", "ref", t =>
        {
            t.UseSqlOutputClause(false);
            t.HasCheckConstraint("CK_ExchangeRates_Values", "[Source]='NBP' AND (([TableType] IN ('A','B') AND [Mid]>0 AND [Mid] IS NOT NULL AND [Bid] IS NULL AND [Ask] IS NULL AND [TradingDate] IS NULL) OR ([TableType]='C' AND [Mid] IS NULL AND [Bid]>0 AND [Ask]>0 AND [Bid] IS NOT NULL AND [Ask] IS NOT NULL AND [Bid]<=[Ask] AND ([TradingDate] IS NULL OR [TradingDate]<=[EffectiveDate])))");
        });
        b.HasKey(x => x.Id); b.Property(x => x.Id).UseIdentityColumn();
        b.Property(x => x.CurrencyCode).HasColumnType("char(3)").UseCollation("Latin1_General_100_BIN2").IsRequired();
        b.HasOne<ReferenceCurrency>().WithMany().HasForeignKey(x => x.CurrencyCode).OnDelete(DeleteBehavior.NoAction);
        b.Property(x => x.Source).HasMaxLength(8).IsUnicode(false).IsRequired();
        b.Property(x => x.TableType).HasColumnType("char(1)").IsRequired(); b.Property(x => x.TableNumber).HasMaxLength(32).IsRequired();
        b.Property(x => x.Mid).HasPrecision(24, 12); b.Property(x => x.Bid).HasPrecision(24, 12); b.Property(x => x.Ask).HasPrecision(24, 12);
        b.Property(x => x.RowVersion).IsRowVersion();
        b.HasIndex(x => new { x.CurrencyCode, x.Source, x.TableType, x.EffectiveDate }).IsUnique();
        b.HasIndex(x => new { x.TableType, x.EffectiveDate });
        b.HasIndex(x => x.EffectiveDate);
    }
}

internal sealed class ExchangeRateSyncMapping : IEntityTypeConfiguration<ExchangeRateSync>
{
    public void Configure(EntityTypeBuilder<ExchangeRateSync> b)
    {
        b.ToTable("ExchangeRateSync", "ops", t =>
        {
            t.HasCheckConstraint("CK_ExchangeRateSync_Singleton", "[SingletonKey] = 1");
            t.HasCheckConstraint("CK_ExchangeRateSync_HistoryStart", "[HistoryStartDate] IS NULL OR [HistoryStartDate] >= '2002-01-02'");
        });
        b.HasKey(x => x.SingletonKey);
        b.Property(x => x.SingletonKey).ValueGeneratedNever();
        b.Property(x => x.LastErrorCode).HasMaxLength(64).IsUnicode(false);
        b.Property(x => x.RowVersion).IsRowVersion();
        b.HasData(new { SingletonKey = (byte)1 });
    }
}

internal sealed class ExchangeRateConflictMapping : IEntityTypeConfiguration<ExchangeRateConflict>
{
    public void Configure(EntityTypeBuilder<ExchangeRateConflict> b)
    {
        b.ToTable("ExchangeRateConflicts", "ops", t =>
            t.HasCheckConstraint("CK_ExchangeRateConflicts_Decision", "([ResolvedAtUtc] IS NULL AND [Decision] IS NULL) OR ([ResolvedAtUtc] IS NOT NULL AND [Decision] IN (N'AcceptIncoming', N'KeepExisting'))"));
        b.HasKey(x => x.Id); b.Property(x => x.Id).UseIdentityColumn();
        b.HasOne<ExchangeRate>().WithMany().HasForeignKey(x => x.RateId).OnDelete(DeleteBehavior.NoAction);
        b.Property(x => x.IncomingHash).HasColumnType("char(64)").IsUnicode(false).IsRequired();
        b.Property(x => x.IncomingTableNumber).HasMaxLength(32).IsRequired();
        b.Property(x => x.IncomingMid).HasPrecision(24, 12); b.Property(x => x.IncomingBid).HasPrecision(24, 12); b.Property(x => x.IncomingAsk).HasPrecision(24, 12);
        b.Property(x => x.Decision).HasMaxLength(16);
        b.Property(x => x.ResolvedByName).HasMaxLength(256);
        b.Property(x => x.RowVersion).IsRowVersion();
        b.HasIndex(x => new { x.RateId, x.IncomingHash }).IsUnique();
        b.HasIndex(x => x.ResolvedAtUtc);
    }
}

internal sealed class ExchangeRateOperationMapping : IEntityTypeConfiguration<ExchangeRateOperation>
{
    public void Configure(EntityTypeBuilder<ExchangeRateOperation> b)
    {
        b.ToTable("ExchangeRateOperations", "audit", t => { t.HasTrigger("TR_ExchangeRateOperations_Immutable"); t.UseSqlOutputClause(false); });
        b.HasKey(x => x.Id); b.Property(x => x.Id).UseIdentityColumn();
        b.Property(x => x.Kind).HasMaxLength(32).IsRequired();
        b.Property(x => x.ActorName).HasMaxLength(256).IsRequired();
        b.Property(x => x.DetailsJson).HasMaxLength(8000).IsRequired();
        b.HasIndex(x => x.AtUtc);
    }
}
