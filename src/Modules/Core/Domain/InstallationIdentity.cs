namespace Vatowiec.Core.Domain;

// Identyfikator źródłowej instalacji: bez danych firmowych i bez sekretów.
public sealed class InstallationIdentity
{
    public byte SingletonKey { get; private set; } = 1;
    public Guid Id { get; private set; }
}
