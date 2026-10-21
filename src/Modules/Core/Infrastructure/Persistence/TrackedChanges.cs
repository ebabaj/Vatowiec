using Microsoft.EntityFrameworkCore;
using Vatowiec.Core.Domain;

namespace Vatowiec.Core.Infrastructure.Persistence;

internal static class TrackedChanges
{
    public static void Prepare(DbContext context)
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var entry in context.ChangeTracker.Entries<ITrackedEntity>()
                     .Where(x => x.State is EntityState.Added or EntityState.Modified))
        {
            if (entry.State == EntityState.Added)
            {
                entry.Property(nameof(ITrackedEntity.CreatedAtUtc)).CurrentValue = now;
            }
            else
            {
                entry.Property(nameof(ITrackedEntity.CreatedAtUtc)).IsModified = false;
            }

            entry.Property(nameof(ITrackedEntity.UpdatedAtUtc)).CurrentValue = now;
        }
    }
}
