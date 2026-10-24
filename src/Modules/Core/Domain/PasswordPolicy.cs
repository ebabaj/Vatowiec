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
    public bool PreventPasswordReuse { get; private set; }
    public int PreviousPasswordCount { get; private set; } = 5;
    public bool EnforcePasswordExpiration { get; private set; }
    public int PasswordValidityDays { get; private set; } = 90;
    public DateTimeOffset? PasswordExpirationEnabledAtUtc { get; private set; }
    public void UpdateLifetime(bool history, int count, bool expiration, int days, DateTimeOffset now)
    {
        if (count is < 1 or > 20) throw new ArgumentException("Liczba poprzednich haseł musi wynosić od 1 do 20.");
        if (days is < 1 or > 3650) throw new ArgumentException("Ważność hasła musi wynosić od 1 do 3650 dni.");
        if (expiration && (!EnforcePasswordExpiration || PasswordExpirationEnabledAtUtc == null)) PasswordExpirationEnabledAtUtc = now.ToUniversalTime();
        PreventPasswordReuse = history; PreviousPasswordCount = count; EnforcePasswordExpiration = expiration; PasswordValidityDays = days;
    }
    public bool IsExpired(DateTimeOffset? changedAt, DateTimeOffset now)
    {
        if (!EnforcePasswordExpiration || PasswordExpirationEnabledAtUtc == null) return false;
        var baseline = changedAt.HasValue && changedAt.Value > PasswordExpirationEnabledAtUtc.Value ? changedAt.Value : PasswordExpirationEnabledAtUtc.Value;
        return now >= baseline.AddDays(PasswordValidityDays);
    }
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
