using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Vatowiec.Core.Infrastructure.Identity;

namespace Vatowiec.Core.Infrastructure.Persistence.SystemConfigurations;

internal sealed class UserSessionConfiguration : IEntityTypeConfiguration<UserSession>
{
    public void Configure(EntityTypeBuilder<UserSession> builder)
    {
        builder.ToTable("UserSessions", "security", table =>
            table.HasCheckConstraint("CK_UserSessions_Expiry", "[ExpiresAtUtc] > [CreatedAtUtc]"));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.RowVersion).IsRowVersion();
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(x => new { x.UserId, x.ExpiresAtUtc });
    }
}
