namespace Vatowiec.Core.Domain;

// One secret for the installation, never a company-scoped entity.
public sealed class GusConfiguration
{
    public int SingletonKey { get; private set; } = 1;
    public string? ProtectedApiKey { get; private set; }
    public byte[] RowVersion { get; private set; } = [];
    public void SetKey(string? protectedKey) => ProtectedApiKey = protectedKey;
}
