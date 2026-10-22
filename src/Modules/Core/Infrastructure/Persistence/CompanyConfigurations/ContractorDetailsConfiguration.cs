using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Vatowiec.Core.Domain;

namespace Vatowiec.Core.Infrastructure.Persistence.CompanyConfigurations;

internal sealed class AddressTypeConfiguration : IEntityTypeConfiguration<AddressType>
{
    public void Configure(EntityTypeBuilder<AddressType> b)
    {
        b.ToTable("AddressTypes", "ref", t => t.HasCheckConstraint("CK_AddressTypes_Name", "LEN(LTRIM(RTRIM([Name]))) > 0"));
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).UseIdentityColumn();
        b.Property(x => x.Name).HasMaxLength(100).IsRequired();
        b.HasIndex(x => x.Name).IsUnique();
        b.HasData(new { Id = 1, Name = "Adres rejestrowy" }, new { Id = 2, Name = "Adres korespondencyjny" });
    }
}

internal sealed class ContractorAddressConfiguration : IEntityTypeConfiguration<ContractorAddress>
{
    public void Configure(EntityTypeBuilder<ContractorAddress> b)
    {
        b.ToTable("ContractorAddresses", "core");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).UseIdentityColumn();
        b.Property(x => x.Name).HasMaxLength(100);
        b.Property(x => x.Street).HasMaxLength(200);
        b.Property(x => x.BuildingNo).HasMaxLength(20);
        b.Property(x => x.ApartmentNo).HasMaxLength(20);
        b.Property(x => x.PostalCode).HasMaxLength(20);
        b.Property(x => x.City).HasMaxLength(150);
        b.Property(x => x.Region).HasMaxLength(100);
        b.Property(x => x.CountryCode).HasColumnType("char(2)");
        b.Property(x => x.RowVersion).IsRowVersion();
        b.HasOne<Contractor>().WithMany().HasForeignKey(x => x.ContractorId).OnDelete(DeleteBehavior.NoAction);
        b.HasOne<AddressType>().WithMany().HasForeignKey(x => x.AddressTypeId).OnDelete(DeleteBehavior.NoAction);
        b.HasIndex(x => new { x.ContractorId, x.AddressTypeId }).IsUnique().HasFilter("[IsDefault] = 1");
    }
}

internal sealed class ContractorBankAccountConfiguration : IEntityTypeConfiguration<ContractorBankAccount>
{
    public void Configure(EntityTypeBuilder<ContractorBankAccount> b)
    {
        b.ToTable("ContractorBankAccounts", "core");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).UseIdentityColumn();
        b.Property(x => x.AccountNumber).HasMaxLength(100).IsRequired();
        b.Property(x => x.BankName).HasMaxLength(200);
        b.Property(x => x.Bic).HasColumnType("varchar(11)");
        b.Property(x => x.CurrencyCode).HasColumnType("char(3)");
        b.Property(x => x.AccountHolderName).HasMaxLength(256);
        b.Property(x => x.Name).HasMaxLength(100);
        b.Property(x => x.Notes).HasColumnType("nvarchar(max)");
        b.Property(x => x.RowVersion).IsRowVersion();
        b.HasOne<Contractor>().WithMany().HasForeignKey(x => x.ContractorId).OnDelete(DeleteBehavior.NoAction);
        // Jawny filtr zastępuje domyślny filtr EF "CurrencyCode IS NOT NULL".
        // SQL Server uwzględnia NULL w unikalności: osobna grupa bez waluty.
        b.HasIndex(x => new { x.ContractorId, x.CurrencyCode }).IsUnique().HasFilter("[IsDefault] = 1");
    }
}

internal sealed class ContractorContactConfiguration : IEntityTypeConfiguration<ContractorContact>
{
    public void Configure(EntityTypeBuilder<ContractorContact> b)
    {
        b.ToTable("ContractorContacts", "core", t => t.HasCheckConstraint("CK_ContractorContacts_Name", "LEN(LTRIM(RTRIM([Name]))) > 0"));
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).UseIdentityColumn();
        b.Property(x => x.Name).HasMaxLength(256).IsRequired();
        b.Property(x => x.Position).HasMaxLength(100);
        b.Property(x => x.Email).HasMaxLength(256);
        b.Property(x => x.Phone).HasMaxLength(100);
        b.Property(x => x.Street).HasMaxLength(200);
        b.Property(x => x.BuildingNo).HasMaxLength(20);
        b.Property(x => x.ApartmentNo).HasMaxLength(20);
        b.Property(x => x.PostalCode).HasMaxLength(20);
        b.Property(x => x.City).HasMaxLength(150);
        b.Property(x => x.CountryCode).HasColumnType("char(2)");
        b.Property(x => x.Notes).HasColumnType("nvarchar(max)");
        b.Property(x => x.RowVersion).IsRowVersion();
        b.HasOne<Contractor>().WithMany().HasForeignKey(x => x.ContractorId).OnDelete(DeleteBehavior.NoAction);
    }
}

internal sealed class ContractorCreateOperationConfiguration : IEntityTypeConfiguration<ContractorCreateOperation>
{
    public void Configure(EntityTypeBuilder<ContractorCreateOperation> b)
    {
        b.ToTable("ContractorCreateOperations", "core");
        b.HasKey(x => new { x.ActorIssuerId, x.ActorId, x.OperationKind, x.OperationId });
        b.Property(x => x.OperationKind).HasMaxLength(32).HasDefaultValue("Contractor");
        b.Property(x => x.RequestHash).HasColumnType("varchar(64)").IsRequired();
        b.Property(x => x.ResponseJson).HasColumnType("nvarchar(max)").IsRequired();
        b.HasOne<Contractor>().WithMany().HasForeignKey(x => x.ContractorId).OnDelete(DeleteBehavior.NoAction);
    }
}
