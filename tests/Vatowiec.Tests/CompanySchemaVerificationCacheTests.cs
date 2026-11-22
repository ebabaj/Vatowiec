using Vatowiec.Core.Infrastructure.Persistence;

namespace Vatowiec.Tests;

public sealed class CompanySchemaVerificationCacheTests
{
    private const string Server = "localhost";
    private static readonly Guid Company = Guid.Parse("8f5b9a63-6f1d-4c5e-9d0b-6f0f8b8a1e01");

    [Fact]
    public void Verification_expires_after_lifetime()
    {
        var clock = new ManualClock();
        var cache = new CompanySchemaVerificationCache(clock);
        Assert.False(cache.IsVerified(Server, "Vatowiec_A", Company));

        cache.MarkVerified(Server, "Vatowiec_A", Company);
        Assert.True(cache.IsVerified("LOCALHOST", "vatowiec_a", Company));

        clock.Advance(CompanySchemaVerificationCache.Lifetime);
        Assert.False(cache.IsVerified(Server, "Vatowiec_A", Company));
    }

    [Fact]
    public void Verification_is_bound_to_server_database_and_company()
    {
        var cache = new CompanySchemaVerificationCache(new ManualClock());
        cache.MarkVerified(Server, "Vatowiec_A", Company);

        Assert.False(cache.IsVerified("other", "Vatowiec_A", Company));
        Assert.False(cache.IsVerified(Server, "Vatowiec_A_przed_20261122", Company));
        Assert.False(cache.IsVerified(Server, "Vatowiec_A", Guid.NewGuid()));

        cache.Forget(Server, "Vatowiec_A", Company);
        Assert.False(cache.IsVerified(Server, "Vatowiec_A", Company));
    }

    private sealed class ManualClock : TimeProvider
    {
        private DateTimeOffset now = new(2026, 11, 22, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => now;
        public void Advance(TimeSpan by) => now += by;
    }
}
