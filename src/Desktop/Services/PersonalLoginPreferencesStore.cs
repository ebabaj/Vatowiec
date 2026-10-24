using Vatowiec.SharedContracts;

namespace Vatowiec.Desktop.Services;

// Only nonsecret preferences from this installation's own ADR-038 file are rebound.
// Credentials under the old, shared localhost origin are never read/copied automatically.
public sealed class PersonalLoginPreferencesStore(ILoginPreferencesStore inner, Guid installationId) : ILoginPreferencesStore
{
    public LoginPreferences Load()
    {
        var original = inner.Load(); var updated = original;
        foreach (var profile in original.Profiles ?? [])
        {
            if (profile.Target.ApiAddress != "https://localhost:18443/" || profile.UserId == null) continue;
            var migrated = profile with { ApiAddress = PersonalApiIdentity.For(installationId) };
            if (updated.Find(migrated.Target) != null) continue;
            updated = updated.WithProfile(migrated, makeLast: original.LastTargetKey == profile.Target.Key);
        }
        if (!ReferenceEquals(original, updated)) inner.Save(updated);
        return updated;
    }
    public void Save(LoginPreferences preferences) => inner.Save(preferences);
}
