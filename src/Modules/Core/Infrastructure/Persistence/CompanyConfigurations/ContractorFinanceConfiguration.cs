using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Vatowiec.Core.Domain;

namespace Vatowiec.Core.Infrastructure.Persistence.CompanyConfigurations;

internal sealed class PaymentMethodConfiguration : IEntityTypeConfiguration<PaymentMethod>
{
    public void Configure(EntityTypeBuilder<PaymentMethod> b)
    {
        b.ToTable("PaymentMethods", "ref", t =>
        {
            t.HasCheckConstraint("CK_PaymentMethods_Name", "LEN(LTRIM(RTRIM([Name])))>0");
            t.HasCheckConstraint("CK_PaymentMethods_Code", "[Code] COLLATE Latin1_General_100_BIN2 LIKE '[A-Z]%' AND [Code] COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^A-Z0-9_-]%'");
        });
        b.HasKey(x => x.Id); b.Property(x => x.Id).UseIdentityColumn(); b.Property(x => x.Code).HasColumnType("varchar(32)");
        b.HasAlternateKey(x => x.Code); b.Property(x => x.Name).HasMaxLength(100); b.Property(x => x.RowVersion).IsRowVersion();
        b.HasData(new { Id = 1, Code = "TRANSFER", Name = "Przelew", IsActive = true, CreatedAtUtc = DateTimeOffset.UnixEpoch, UpdatedAtUtc = DateTimeOffset.UnixEpoch },
            new { Id = 2, Code = "CASH", Name = "Gotówka", IsActive = true, CreatedAtUtc = DateTimeOffset.UnixEpoch, UpdatedAtUtc = DateTimeOffset.UnixEpoch },
            new { Id = 3, Code = "CARD", Name = "Karta", IsActive = true, CreatedAtUtc = DateTimeOffset.UnixEpoch, UpdatedAtUtc = DateTimeOffset.UnixEpoch },
            new { Id = 4, Code = "OFFSET", Name = "Kompensata", IsActive = true, CreatedAtUtc = DateTimeOffset.UnixEpoch, UpdatedAtUtc = DateTimeOffset.UnixEpoch },
            new { Id = 5, Code = "OTHER", Name = "Inny", IsActive = true, CreatedAtUtc = DateTimeOffset.UnixEpoch, UpdatedAtUtc = DateTimeOffset.UnixEpoch });
    }
}
internal sealed class TradeTermsConfiguration : IEntityTypeConfiguration<ContractorTradeTerms>
{
    public void Configure(EntityTypeBuilder<ContractorTradeTerms> b)
    {
        b.ToTable("ContractorTradeTerms", "core", t =>
        {
            t.HasCheckConstraint("CK_TradeTerms_Direction", "[Direction] COLLATE Latin1_General_100_BIN2 IN ('Sale','Purchase')");
            t.HasCheckConstraint("CK_TradeTerms_Days", "[PaymentDays] IS NULL OR [PaymentDays] BETWEEN 0 AND 3650");
            t.HasCheckConstraint("CK_TradeTerms_Discount", "[DiscountPercent] IS NULL OR [DiscountPercent] BETWEEN 0 AND 100");
            t.HasCheckConstraint("CK_TradeTerms_Currency", "[CurrencyCode] IS NULL OR (LEN([CurrencyCode])=3 AND [CurrencyCode] COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^A-Z]%')");
        });
        b.HasKey(x => x.Id); b.Property(x => x.Id).UseIdentityColumn(); b.Property(x => x.Direction).HasColumnType("varchar(8)");
        b.Property(x => x.PaymentMethodCode).HasColumnType("varchar(32)"); b.Property(x => x.CurrencyCode).HasColumnType("char(3)").UseCollation("Latin1_General_100_BIN2");
        b.Property(x => x.DiscountPercent).HasPrecision(7, 4); b.Property(x => x.Notes).HasMaxLength(2000); b.Property(x => x.RowVersion).IsRowVersion();
        b.HasIndex(x => new { x.ContractorId, x.Direction }).IsUnique();
        b.HasOne<Contractor>().WithMany().HasForeignKey(x => x.ContractorId).OnDelete(DeleteBehavior.NoAction);
        b.HasOne<Currency>().WithMany().HasForeignKey(x => x.CurrencyCode).OnDelete(DeleteBehavior.NoAction);
        b.HasOne<PaymentMethod>().WithMany().HasForeignKey(x => x.PaymentMethodCode).HasPrincipalKey(x => x.Code).OnDelete(DeleteBehavior.NoAction);
    }
}
internal sealed class CreditLimitConfiguration : IEntityTypeConfiguration<ContractorCreditLimit>
{
    public void Configure(EntityTypeBuilder<ContractorCreditLimit> b)
    {
        b.ToTable("ContractorCreditLimits", "core", t =>
        {
            t.HasCheckConstraint("CK_CreditLimit_Direction", "[Direction] COLLATE Latin1_General_100_BIN2 IN ('Sale','Purchase')");
            t.HasCheckConstraint("CK_CreditLimit_Amount", "[Amount]>=0");
            t.HasCheckConstraint("CK_CreditLimit_Currency", "LEN([CurrencyCode])=3 AND [CurrencyCode] COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^A-Z]%'");
        });
        b.HasKey(x => x.Id); b.Property(x => x.Id).UseIdentityColumn(); b.Property(x => x.Direction).HasColumnType("varchar(8)");
        b.Property(x => x.CurrencyCode).HasColumnType("char(3)").UseCollation("Latin1_General_100_BIN2"); b.Property(x => x.Amount).HasPrecision(24, 8); b.Property(x => x.RowVersion).IsRowVersion();
        b.HasIndex(x => new { x.ContractorId, x.Direction, x.CurrencyCode }).IsUnique().HasFilter("[IsActive] = 1");
        b.HasOne<Contractor>().WithMany().HasForeignKey(x => x.ContractorId).OnDelete(DeleteBehavior.NoAction);
        b.HasOne<Currency>().WithMany().HasForeignKey(x => x.CurrencyCode).OnDelete(DeleteBehavior.NoAction);
    }
}
