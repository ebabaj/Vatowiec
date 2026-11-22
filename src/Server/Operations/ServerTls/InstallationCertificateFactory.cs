using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Vatowiec.SharedContracts.ServerTls;

namespace Vatowiec.Operations.ServerTls;

// ADR-047 tryb B. CA instalacji: ECDSA P-256, 10 lat, CA=true pathLen=0, keyCertSign+cRLSign.
// Certyfikat CA podpisuje jednorazowy klucz startowy, niszczony od razu po podpisie. Dzięki temu CA nie jest
// certyfikatem samopodpisanym i stos TLS serwera wysyła go stacji jako certyfikat pośredni (korzeń łańcucha
// jest zawsze obcinany). Zaufanie stacji wynika wyłącznie z przypiętego skrótu klucza CA, nie z podpisu startowego.
internal static class InstallationCertificateFactory
{
    public static readonly TimeSpan CaLifetime = TimeSpan.FromDays(3652);
    public static readonly TimeSpan ServerLifetime = TimeSpan.FromDays(365);
    public static readonly TimeSpan RenewBefore = TimeSpan.FromDays(30);
    private static readonly TimeSpan Backdate = TimeSpan.FromHours(1);

    public static X509Certificate2 CreateCa(string installationName, DateTimeOffset now)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var subject = new X500DistinguishedNameBuilder();
        subject.AddOrganizationName("VATowiec");
        subject.AddCommonName(Trim("VATowiec CA instalacji " + installationName));
        var request = new CertificateRequest(subject.Build(), key, HashAlgorithmName.SHA256);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, true, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, true));
        request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));
        var bootstrapName = new X500DistinguishedNameBuilder();
        bootstrapName.AddOrganizationName("VATowiec");
        bootstrapName.AddCommonName("VATowiec jednorazowy klucz startowy " + Convert.ToHexString(RandomNumberGenerator.GetBytes(8)));
        X509Certificate2 certificate;
        using (var bootstrap = ECDsa.Create(ECCurve.NamedCurves.nistP256))
        {
            certificate = request.Create(bootstrapName.Build(), X509SignatureGenerator.CreateForECDsa(bootstrap),
                now - Backdate, now + CaLifetime, SerialNumber());
        }
        return certificate.CopyWithPrivateKey(key);
    }

    public static X509Certificate2 CreateServer(X509Certificate2 caWithKey, IReadOnlyList<string> dnsNames, IReadOnlyList<IPAddress> addresses, DateTimeOffset now)
    {
        if (!caWithKey.HasPrivateKey) throw new InvalidOperationException("Brak klucza CA instalacji.");
        if (dnsNames.Count == 0) throw new ArgumentException("Certyfikat serwera wymaga co najmniej jednej nazwy DNS.", nameof(dnsNames));
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var subject = new X500DistinguishedNameBuilder();
        subject.AddOrganizationName("VATowiec");
        subject.AddCommonName(Trim(dnsNames[0]));
        var request = new CertificateRequest(subject.Build(), key, HashAlgorithmName.SHA256);
        var san = new SubjectAlternativeNameBuilder();
        foreach (var name in dnsNames) san.AddDnsName(name);
        foreach (var address in addresses) san.AddIpAddress(address);
        request.CertificateExtensions.Add(san.Build());
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension([new Oid(InstallationCertificateValidation.ServerAuthenticationOid)], false));
        request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));
        request.CertificateExtensions.Add(X509AuthorityKeyIdentifierExtension.CreateFromCertificate(caWithKey, true, false));
        var notBefore = Max(now - Backdate, caWithKey.NotBefore.ToUniversalTime());
        var notAfter = Min(now + ServerLifetime, caWithKey.NotAfter.ToUniversalTime());
        using var issued = request.Create(caWithKey, notBefore, notAfter, SerialNumber());
        return issued.CopyWithPrivateKey(key);
    }

    private static byte[] SerialNumber()
    {
        var serial = RandomNumberGenerator.GetBytes(16);
        serial[0] &= 0x7F; // dodatni numer seryjny
        return serial;
    }

    private static string Trim(string value) => value.Length <= 64 ? value : value[..64];
    private static DateTimeOffset Max(DateTimeOffset a, DateTimeOffset b) => a > b ? a : b;
    private static DateTimeOffset Min(DateTimeOffset a, DateTimeOffset b) => a < b ? a : b;
}
