namespace Vatowiec.Core.Infrastructure.Identity;

public sealed class UserPasswordHistory
{
    private UserPasswordHistory() { }
    public UserPasswordHistory(Guid userId, string hash, DateTimeOffset at)
    { UserId = userId; PasswordHash = hash; ReplacedAtUtc = at; }
    public long Id { get; private set; }
    public Guid UserId { get; private set; }
    public string PasswordHash { get; private set; } = "";
    public DateTimeOffset ReplacedAtUtc { get; private set; }
    public override string ToString() => "UserPasswordHistory [redacted]";
}
