namespace Vatowiec.Core.Infrastructure.Identity;

public sealed class UserSession
{
    private UserSession() { }

    public UserSession(Guid userId, DateTimeOffset now, TimeSpan lifetime)
    {
        Id = Guid.NewGuid();
        UserId = userId;
        CreatedAtUtc = now;
        ExpiresAtUtc = now.Add(lifetime);
    }

    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset ExpiresAtUtc { get; private set; }
    public DateTimeOffset? RevokedAtUtc { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    public void Revoke(DateTimeOffset now) => RevokedAtUtc ??= now;
}
