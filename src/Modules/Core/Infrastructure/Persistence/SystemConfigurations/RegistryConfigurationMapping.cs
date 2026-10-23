using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Vatowiec.Core.Domain;
using Vatowiec.Core.Infrastructure.Registries;

namespace Vatowiec.Core.Infrastructure.Persistence.SystemConfigurations;

public sealed class RegistryConfigurationMapping : IEntityTypeConfiguration<RegistryConfiguration>
{
    public void Configure(EntityTypeBuilder<RegistryConfiguration> b)
    {
        b.ToTable("RegistryConfigurations", "security", t =>
        {
            t.HasCheckConstraint("CK_RegistryConfigurations_Source", "[Source] IN ('GUS','MF','VIES')");
            t.HasCheckConstraint("CK_RegistryConfigurations_Environment", "[Environment] IN ('Test','Production')");
            t.HasCheckConstraint("CK_RegistryConfigurations_Wl2", "([Source]='MF' AND [TestWl2Url] IS NOT NULL AND [ProductionWl2Url] IS NOT NULL) OR ([Source]<>'MF' AND [TestWl2Url] IS NULL AND [ProductionWl2Url] IS NULL)");
        });
        b.HasKey(x => x.Source); b.Property(x => x.Source).HasColumnType("varchar(4)").ValueGeneratedNever();
        b.Property(x => x.Environment).HasColumnType("varchar(10)");
        b.Property(x => x.TestUrl).HasMaxLength(1024); b.Property(x => x.ProductionUrl).HasMaxLength(1024);
        b.Property(x => x.TestWl2Url).HasMaxLength(1024); b.Property(x => x.ProductionWl2Url).HasMaxLength(1024);
        b.Property(x => x.RowVersion).IsRowVersion();
        b.HasData(new RegistryConfiguration("GUS", RegistrySettings.GusTest, RegistrySettings.GusProduction),
            new RegistryConfiguration("MF", RegistrySettings.MfTest, RegistrySettings.MfProduction,
                "https://wykazpodatnikowvat-test.mf.gov.pl/api2/", "https://wykazpodatnikowvat.mf.gov.pl/api2/"),
            new RegistryConfiguration("VIES", RegistrySettings.ViesTest, RegistrySettings.ViesProduction));
    }
}

public sealed class RegistryConfigurationChangeMapping : IEntityTypeConfiguration<RegistryConfigurationChange>
{
    public void Configure(EntityTypeBuilder<RegistryConfigurationChange> b)
    {
        b.ToTable("RegistryConfigurationChanges", "audit", t =>
        { t.HasTrigger("TR_RegistryConfigurationChanges_Immutable"); t.UseSqlOutputClause(false); });
        b.HasKey(x => x.Id); b.Property(x => x.Id).UseIdentityColumn();
        b.Property(x => x.Source).HasColumnType("varchar(4)"); b.Property(x => x.ActorName).HasMaxLength(256);
        b.HasIndex(x => new { x.Source, x.OccurredAtUtc });
    }
}
