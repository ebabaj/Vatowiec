using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Vatowiec.Core.Infrastructure.Persistence;

namespace Vatowiec.Core.Infrastructure.Registries;

public interface IGusKeyProtector
{
    string Protect(Guid installation, string key);
    string Unprotect(Guid installation, string encrypted);
}

public sealed class GusKeyProtector(IDataProtectionProvider provider) : IGusKeyProtector
{
    private IDataProtector For(Guid installation)
    {
        if (installation == Guid.Empty) throw new InvalidOperationException("Brak tożsamości instalacji.");
        return provider.CreateProtector("VATowiec.GUS.BIR1.2", installation.ToString("D"));
    }
    public string Protect(Guid installation, string key) => For(installation).Protect(key);
    public string Unprotect(Guid installation, string encrypted) => For(installation).Unprotect(encrypted);

    public static IGusKeyProtector Create(IConfiguration config, IHostEnvironment host)
    {
        if (!OperatingSystem.IsWindows()) throw new InvalidOperationException("Ochrona klucza GUS wymaga Windows DPAPI.");
        var path = Path.GetFullPath(config["GUS:KeyRingDirectory"] ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VATowiec", "ServerKeys", "GUS"));
        var root = new DirectoryInfo(host.ContentRootPath);
        var candidate = root;
        while (candidate != null && !File.Exists(Path.Combine(candidate.FullName, "AGENTS.md"))) candidate = candidate.Parent;
        foreach (var forbidden in new[] { (candidate ?? root).FullName, AppContext.BaseDirectory })
        {
            var boundary = Path.TrimEndingDirectorySeparator(Path.GetFullPath(forbidden));
            if (path.Equals(boundary, StringComparison.OrdinalIgnoreCase) || path.StartsWith(boundary + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Key ring GUS musi znajdować się poza repozytorium i katalogiem aplikacji.");
        }
        return new GusKeyProtector(DataProtectionProvider.Create(new DirectoryInfo(path), builder =>
        {
            builder.SetApplicationName("VATowiec.GUS");
            if (OperatingSystem.IsWindows()) builder.ProtectKeysWithDpapi(protectToLocalMachine: false);
        }));
    }
}

// Lazy and independent of DataPort; read-only status and other services do not need DPAPI.
public sealed class PersistentGusKeyProtector(IConfiguration config, IHostEnvironment host) : IGusKeyProtector
{
    private readonly Lazy<IGusKeyProtector> inner = new(() => GusKeyProtector.Create(config, host));
    public string Protect(Guid installation, string key) => inner.Value.Protect(installation, key);
    public string Unprotect(Guid installation, string encrypted) => inner.Value.Unprotect(installation, encrypted);
}

public interface IGusApiKeyProvider
{
    Task<string?> Read(CancellationToken cancellationToken);
}

public sealed class SystemGusApiKeyProvider(IServiceProvider services) : IGusApiKeyProvider
{
    public async Task<string?> Read(CancellationToken cancellationToken)
    {
        var system = services.GetRequiredService<SystemDbContext>();
        var encrypted = await system.GusConfigurations.AsNoTracking().Select(x => x.ProtectedApiKey).SingleOrDefaultAsync(cancellationToken);
        if (encrypted == null) return null;
        var installation = await system.InstallationIdentities.AsNoTracking().Select(x => x.Id).SingleAsync(cancellationToken);
        return services.GetRequiredService<IGusKeyProtector>().Unprotect(installation, encrypted);
    }
}
