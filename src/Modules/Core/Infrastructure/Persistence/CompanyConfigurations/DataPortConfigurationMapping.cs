using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Vatowiec.Core.Domain;

namespace Vatowiec.Core.Infrastructure.Persistence.CompanyConfigurations;

internal sealed class DataPortConfigurationMapping : IEntityTypeConfiguration<DataPortConfiguration>
{
    public void Configure(EntityTypeBuilder<DataPortConfiguration> builder)
    {
        builder.ToTable("DataPortConfiguration", "security", t => t.HasCheckConstraint("CK_DataPort_Singleton", "[SingletonKey] = 1"));
        builder.HasKey(x => x.SingletonKey);
        builder.Property(x => x.SingletonKey).ValueGeneratedNever();
        builder.Property(x => x.ProtectedApiKey).HasColumnType("nvarchar(max)");
        builder.Property(x => x.RowVersion).IsRowVersion();
    }
}
