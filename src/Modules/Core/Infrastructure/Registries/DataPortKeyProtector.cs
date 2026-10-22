using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Vatowiec.Core.Infrastructure.Registries;

public interface IDataPortKeyProtector
{
    string Protect(Guid company, string key);
    string Unprotect(Guid company, string encrypted);
}

public sealed class DataPortKeyProtector(IDataProtectionProvider provider) : IDataPortKeyProtector
{
    private IDataProtector For(Guid company) => provider.CreateProtector("VATowiec.DataPort", company.ToString("D"));
    public string Protect(Guid company, string key) => For(company).Protect(key);
    public string Unprotect(Guid company, string encrypted) => For(company).Unprotect(encrypted);

    public static IDataPortKeyProtector Create(IConfiguration config, IHostEnvironment host)
    {
        if (!OperatingSystem.IsWindows()) throw new InvalidOperationException("Ochrona kluczy DataPort wymaga Windows DPAPI.");
        var path = Path.GetFullPath(config["DataPort:KeyRingDirectory"] ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VATowiec", "ServerKeys", "DataPort"));
        var root = new DirectoryInfo(host.ContentRootPath);
        var candidate = root;
        while (candidate != null && !File.Exists(Path.Combine(candidate.FullName, "AGENTS.md"))) candidate = candidate.Parent;
        root = candidate ?? root;
        if (path.Equals(root.FullName, StringComparison.OrdinalIgnoreCase) || path.StartsWith(root.FullName + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Key ring DataPort musi znajdować się poza repozytorium.");
        return new DataPortKeyProtector(DataProtectionProvider.Create(new DirectoryInfo(path), builder =>
        {
            builder.SetApplicationName("VATowiec.DataPort");
            if (OperatingSystem.IsWindows()) builder.ProtectKeysWithDpapi(protectToLocalMachine: false);
            else throw new InvalidOperationException("Wymagana ochrona Windows DPAPI.");
        }));
    }
}

// Creating a key ring is deferred until a DataPort key is actually written/read.
// Unrelated official adapters and read-only configuration status do not depend on DPAPI availability.
public sealed class PersistentDataPortKeyProtector(IConfiguration config, IHostEnvironment host) : IDataPortKeyProtector
{
    private readonly Lazy<IDataPortKeyProtector> inner = new(() => DataPortKeyProtector.Create(config, host));
    public string Protect(Guid company, string key) => inner.Value.Protect(company, key);
    public string Unprotect(Guid company, string encrypted) => inner.Value.Unprotect(company, encrypted);
}
