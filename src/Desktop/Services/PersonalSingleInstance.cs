namespace Vatowiec.Desktop.Services;

public sealed class PersonalSingleInstance : IDisposable
{
    private readonly FileStream gate;
    private PersonalSingleInstance(FileStream gate) => this.gate = gate;
    public static PersonalSingleInstance? TryAcquire()
    {
        var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Vatowiec");
        Directory.CreateDirectory(directory);
        try { return new(new FileStream(Path.Combine(directory, "personal-client.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None)); }
        catch (IOException) { return null; } // Windows releases the handle after a crash; the file is not a stale lock.
    }
    public void Dispose() => gate.Dispose();
}
