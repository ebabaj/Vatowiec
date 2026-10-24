namespace Vatowiec.Core.Domain;

public sealed class PasswordPolicy
{
    public int SingletonKey { get; private set; } = 1;
    public bool IsEnabled { get; private set; } = true;
    public int MinimumLength { get; private set; } = 12;
    public bool RequireUppercase { get; private set; } = true;
    public bool RequireLowercase { get; private set; } = true;
    public bool RequireDigit { get; private set; } = true;
    public bool RequireSpecialCharacter { get; private set; } = true;
    public byte[] RowVersion { get; private set; } = [];
    public void Update(bool enabled, int minimum, bool upper, bool lower, bool digit, bool special)
    {
        if (minimum is < 1 or > 1024) throw new ArgumentException("Minimalna długość musi wynosić od 1 do 1024 znaków.");
        IsEnabled = enabled; MinimumLength = minimum; RequireUppercase = upper;
        RequireLowercase = lower; RequireDigit = digit; RequireSpecialCharacter = special;
    }
}

public sealed class PasswordPolicyChange
{
    private PasswordPolicyChange() { }
    public PasswordPolicyChange(Guid actor, Guid issuer, string name, DateTimeOffset at, string before, string after)
    {
        ActorId = EntityText.Id(actor, nameof(actor)); ActorIssuerId = EntityText.Id(issuer, nameof(issuer));
        ActorName = name; OccurredAtUtc = at; BeforeJson = before; AfterJson = after;
    }
    public long Id { get; private set; }
    public Guid ActorId { get; private set; }
    public Guid ActorIssuerId { get; private set; }
    public string ActorName { get; private set; } = "";
    public DateTimeOffset OccurredAtUtc { get; private set; }
    public string BeforeJson { get; private set; } = "";
    public string AfterJson { get; private set; } = "";
}
