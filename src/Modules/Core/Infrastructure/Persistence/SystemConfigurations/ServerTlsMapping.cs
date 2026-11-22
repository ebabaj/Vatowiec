using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Vatowiec.Core.Domain;

namespace Vatowiec.Core.Infrastructure.Persistence.SystemConfigurations;

// ADR-047: konfiguracja i stan certyfikatu serwera API oraz niezmienny audyt. Bez kluczy prywatnych.
internal sealed class ServerTlsConfigurationMapping : IEntityTypeConfiguration<ServerTlsConfiguration>
{
    public void Configure(EntityTypeBuilder<ServerTlsConfiguration> b)
    {
        b.ToTable("ServerTlsConfiguration", "security", t =>
        {
            t.HasCheckConstraint("CK_ServerTlsConfiguration_Singleton", "[SingletonKey] = 1");
            t.HasCheckConstraint("CK_ServerTlsConfiguration_Mode", "[Mode] IN (N'InstallationCa', N'OwnCertificate')");
            t.HasCheckConstraint("CK_ServerTlsConfiguration_Generation", "[CaGeneration] >= 1");
        });
        b.HasKey(x => x.SingletonKey);
        b.Property(x => x.SingletonKey).ValueGeneratedNever();
        b.Property(x => x.Mode).HasMaxLength(16).IsRequired();
        b.Property(x => x.DnsNames).HasMaxLength(6000).IsRequired();
        b.Property(x => x.IpAddresses).HasMaxLength(1200).IsRequired();
        b.Property(x => x.IssuerThumbprint).HasMaxLength(64).IsUnicode(false);
        b.Property(x => x.RowVersion).IsRowVersion();
        b.Ignore(x => x.DnsNameList);
        b.Ignore(x => x.IpAddressList);
        b.HasData(new { SingletonKey = (byte)1, Mode = ServerTlsModes.InstallationCa, DnsNames = "", IpAddresses = "", CaGeneration = 1 });
    }
}

internal sealed class ServerTlsStateMapping : IEntityTypeConfiguration<ServerTlsState>
{
    public void Configure(EntityTypeBuilder<ServerTlsState> b)
    {
        b.ToTable("ServerTlsState", "security", t => t.HasCheckConstraint("CK_ServerTlsState_Singleton", "[SingletonKey] = 1"));
        b.HasKey(x => x.SingletonKey);
        b.Property(x => x.SingletonKey).ValueGeneratedNever();
        b.Property(x => x.CaSpkiSha256).HasMaxLength(64).IsUnicode(false).IsFixedLength();
        b.Property(x => x.CaThumbprint).HasMaxLength(64).IsUnicode(false);
        b.Property(x => x.CaCertificate).HasMaxLength(8192);
        b.Property(x => x.ServerCertificateThumbprint).HasMaxLength(64).IsUnicode(false);
        b.Property(x => x.ServerCertificateNames).HasMaxLength(8000).IsRequired();
        b.Property(x => x.LastErrorCode).HasMaxLength(64);
        b.Property(x => x.RowVersion).IsRowVersion();
        b.Ignore(x => x.ServerCertificateNameList);
    }
}

internal sealed class ServerTlsChangeMapping : IEntityTypeConfiguration<ServerTlsChange>
{
    public void Configure(EntityTypeBuilder<ServerTlsChange> b)
    {
        b.ToTable("ServerTlsChanges", "audit", t => { t.HasTrigger("TR_ServerTlsChanges_Immutable"); t.UseSqlOutputClause(false); });
        b.HasKey(x => x.Id);
        b.Property(x => x.Kind).HasMaxLength(32).IsRequired();
        b.Property(x => x.ActorName).HasMaxLength(256).IsRequired();
        b.Property(x => x.BeforeJson).HasMaxLength(2048);
        b.Property(x => x.AfterJson).HasMaxLength(2048).IsRequired();
        b.HasIndex(x => x.AtUtc);
    }
}
