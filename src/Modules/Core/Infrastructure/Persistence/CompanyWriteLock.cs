using Microsoft.EntityFrameworkCore;

namespace Vatowiec.Core.Infrastructure.Persistence;

public static class CompanyWriteLock
{
    // Blokada SQL związana z transakcją, także przy wielu procesach API.
    public static Task Acquire(CompanyDbContext db, string resource, CancellationToken ct = default) =>
        db.Database.ExecuteSqlInterpolatedAsync($"""
            DECLARE @result int;
            EXEC @result = sys.sp_getapplock @Resource={resource}, @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=10000;
            IF @result < 0 THROW 51002, 'Nie uzyskano blokady operacji.', 1;
            """, ct);
}
