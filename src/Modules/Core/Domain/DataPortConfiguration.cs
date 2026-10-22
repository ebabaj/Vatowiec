namespace Vatowiec.Core.Domain;

public sealed class DataPortConfiguration
{
    public int SingletonKey { get; private set; } = 1;
    public string? ProtectedApiKey { get; private set; }
    public byte[] RowVersion { get; private set; } = [];
    public void SetKey(string? protectedKey) => ProtectedApiKey = protectedKey;
}
