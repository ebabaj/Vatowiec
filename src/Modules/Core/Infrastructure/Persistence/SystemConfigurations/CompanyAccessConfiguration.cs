using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Vatowiec.Core.Domain;
using Vatowiec.Core.Infrastructure.Identity;

namespace Vatowiec.Core.Infrastructure.Persistence.SystemConfigurations;

internal sealed class CompanyAccessConfiguration : IEntityTypeConfiguration<CompanyAccess>
{
    public void Configure(EntityTypeBuilder<CompanyAccess> builder)
    {
        builder.ToTable("CompanyAccess", "security", table =>
            table.HasCheckConstraint("CK_CompanyAccess_Permissions", "[Permissions] > 0 AND [Permissions] <= 7"));
        builder.HasKey(x => new { x.UserId, x.CompanyId });
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Company>().WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.NoAction);
        builder.Property(x => x.RowVersion).IsRowVersion();
    }
}
