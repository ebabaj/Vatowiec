using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Principal;
using Microsoft.AspNetCore.DataProtection;
using Vatowiec.Core.Infrastructure.ServerTls;

namespace Vatowiec.Operations.ServerTls;

internal sealed class ServerTlsFailure(string code) : Exception(code)
{
    public string Code { get; } = code;
}

// ADR-047: pliki CA i certyfikatu serwera. Klucz CA: PKCS#8 chroniony Data Protection + DPAPI bieżącego konta
// (konto usługi Operations), osobny purpose związany z identyfikatorem instalacji. Katalogi mają chroniony ACL:
// konto usługi, SYSTEM i Administratorzy; server\ dodatkowo odczyt dla konta API (ServerTls:ApiAccount),
// connection\ odczyt dla Użytkowników (dane publiczne).
[SupportedOSPlatform("windows")]
internal sealed class ServerTlsStore(ServerTlsPaths paths, IConfiguration configuration)
{
    private const string Purpose = "VATowiec.ServerTls.InstallationCaKey.v1";
    private bool directoriesPrepared;
    public ServerTlsPaths Paths => paths;

    public void EnsureDirectories()
    {
        if (directoriesPrepared) return;
        var apiAccount = configuration["ServerTls:ApiAccount"] is { Length: > 0 } name ? Translate(name) : null;
        Prepare(paths.Root, []);
        Prepare(paths.CaDirectory, []);
        Prepare(paths.ServerDirectory, apiAccount == null ? [] : [(apiAccount, FileSystemRights.ReadAndExecute)]);
        Prepare(paths.ConnectionDirectory, [(new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null), FileSystemRights.ReadAndExecute)]);
        directoriesPrepared = true;
    }

    public X509Certificate2? LoadCaWithKey(Guid installation)
    {
        if (!File.Exists(paths.CaCertificate) || !File.Exists(paths.CaProtectedKey)) return null;
        var certificate = X509CertificateLoader.LoadCertificateFromFile(paths.CaCertificate);
        byte[] pkcs8;
        try { pkcs8 = Protector(installation).Unprotect(File.ReadAllBytes(paths.CaProtectedKey)); }
        catch (CryptographicException) { throw new ServerTlsFailure("tls_ca_key_unreadable"); }
        try
        {
            using var key = ECDsa.Create();
            key.ImportPkcs8PrivateKey(pkcs8, out _);
            return certificate.CopyWithPrivateKey(key);
        }
        finally { CryptographicOperations.ZeroMemory(pkcs8); }
    }

    public void SaveCa(X509Certificate2 caWithKey, Guid installation, DateTimeOffset at)
    {
        using var key = caWithKey.GetECDsaPrivateKey() ?? throw new InvalidOperationException("Brak klucza CA instalacji.");
        var pkcs8 = key.ExportPkcs8PrivateKey();
        try
        {
            var protectedKey = Protector(installation).Protect(pkcs8);
            if (File.Exists(paths.CaCertificate) || File.Exists(paths.CaProtectedKey))
            {
                var retired = Path.Combine(paths.CaDirectory, "retired", at.UtcDateTime.ToString("yyyyMMddHHmmss"));
                Directory.CreateDirectory(retired);
                foreach (var file in new[] { paths.CaCertificate, paths.CaProtectedKey })
                    if (File.Exists(file)) File.Move(file, Path.Combine(retired, Path.GetFileName(file)), overwrite: true);
            }
            WriteAtomically(paths.CaProtectedKey, protectedKey);
            WriteAtomically(paths.CaCertificate, caWithKey.Export(X509ContentType.Cert));
        }
        finally { CryptographicOperations.ZeroMemory(pkcs8); }
    }

    public (X509Certificate2 Server, X509Certificate2 Ca)? LoadServer()
    {
        if (!File.Exists(paths.ServerPfx)) return null;
        try
        {
            var collection = X509CertificateLoader.LoadPkcs12CollectionFromFile(paths.ServerPfx, null, X509KeyStorageFlags.EphemeralKeySet);
            var server = collection.FirstOrDefault(x => x.HasPrivateKey);
            var ca = collection.FirstOrDefault(x => !x.HasPrivateKey);
            return server == null || ca == null ? null : (server, ca);
        }
        catch (CryptographicException) { return null; }
    }

    public void SaveServer(X509Certificate2 serverWithKey, X509Certificate2 ca)
    {
        var collection = new X509Certificate2Collection { serverWithKey, X509CertificateLoader.LoadCertificate(ca.RawData) };
        var pfx = collection.Export(X509ContentType.Pkcs12) ?? throw new InvalidOperationException("Nie wyeksportowano certyfikatu serwera.");
        try { WriteAtomically(paths.ServerPfx, pfx); }
        finally { CryptographicOperations.ZeroMemory(pfx); }
    }

    public bool WriteConnection(string connectionJson, string code)
    {
        var codeText = "Kod połączenia VATowiec (ADR-047). Przekaż stacjom osobiście lub zaufanym kanałem." + Environment.NewLine + code + Environment.NewLine;
        if (File.Exists(paths.ConnectionFile) && File.ReadAllText(paths.ConnectionFile) == connectionJson &&
            File.Exists(paths.ConnectionCodeFile) && File.ReadAllText(paths.ConnectionCodeFile) == codeText) return false;
        WriteAtomically(paths.ConnectionFile, System.Text.Encoding.UTF8.GetBytes(connectionJson));
        WriteAtomically(paths.ConnectionCodeFile, System.Text.Encoding.UTF8.GetBytes(codeText));
        return true;
    }

    private IDataProtector Protector(Guid installation)
    {
        if (installation == Guid.Empty) throw new InvalidOperationException("Brak tożsamości instalacji.");
        var provider = DataProtectionProvider.Create(new DirectoryInfo(paths.KeyRing), builder =>
        {
            builder.SetApplicationName("VATowiec.ServerTls");
            builder.ProtectKeysWithDpapi(protectToLocalMachine: false);
        });
        return provider.CreateProtector(Purpose, installation.ToString("D"));
    }

    private static void WriteAtomically(string path, byte[] content)
    {
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllBytes(temporary, content);
            File.Move(temporary, path, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static IdentityReference Translate(string account)
    {
        try { return new NTAccount(account).Translate(typeof(SecurityIdentifier)); }
        catch (IdentityNotMappedException) { throw new ServerTlsFailure("tls_api_account_invalid"); }
    }

    private static void Prepare(string directory, (IdentityReference Identity, FileSystemRights Rights)[] extra)
    {
        Directory.CreateDirectory(directory);
        var acl = new DirectorySecurity();
        acl.SetAccessRuleProtection(true, false);
        const InheritanceFlags inherit = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;
        foreach (var sid in new IdentityReference[] { WindowsIdentity.GetCurrent().User!, new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
            new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null) })
            acl.AddAccessRule(new FileSystemAccessRule(sid, FileSystemRights.FullControl, inherit, PropagationFlags.None, AccessControlType.Allow));
        foreach (var (identity, rights) in extra)
            acl.AddAccessRule(new FileSystemAccessRule(identity, rights, inherit, PropagationFlags.None, AccessControlType.Allow));
        new DirectoryInfo(directory).SetAccessControl(acl);
    }
}
