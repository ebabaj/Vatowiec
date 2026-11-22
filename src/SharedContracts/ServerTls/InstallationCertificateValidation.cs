using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Vatowiec.SharedContracts.ServerTls;

// Kotwica zaufania jednej instalacji, przypięta przez klienta dla dokładnie jednego adresu API.
// CaSpkiSha256: 16 bajtów (z kodu połączenia) albo 32 bajty (z pliku połączenia).
public sealed record InstallationTrustAnchor
{
    public InstallationTrustAnchor(byte[] caSpkiSha256, byte[]? caCertificate = null)
    {
        if (caSpkiSha256 is not { Length: ServerTls.ConnectionCode.PinLength or 32 })
            throw new ArgumentException("Skrót CA instalacji ma niepoprawną długość.", nameof(caSpkiSha256));
        CaSpkiSha256 = caSpkiSha256.ToArray();
        CaCertificate = caCertificate?.ToArray();
    }
    public byte[] CaSpkiSha256 { get; }
    public byte[]? CaCertificate { get; }
    public string Code => ServerTls.ConnectionCode.Format(CaSpkiSha256.Length == 32 ? CaSpkiSha256 : Pad(CaSpkiSha256));
    private static byte[] Pad(byte[] pin) => [.. pin, .. new byte[16]];
}

public enum InstallationTrustResult
{
    TrustedBySystem,
    TrustedByInstallationCa,
    Rejected
}

// ADR-047: walidacja certyfikatu serwera dla adresu z przypiętą kotwicą CA instalacji.
// Nigdy nie pomija sprawdzenia: nazwa hosta, terminy, podpisy i EKU są zawsze wymagane,
// a jedynym rozluźnieniem jest kotwica (przypięty skrót klucza CA zamiast magazynu systemu).
public static class InstallationCertificateValidation
{
    public const string ServerAuthenticationOid = "1.3.6.1.5.5.7.3.1";

    public static byte[] SpkiSha256(X509Certificate2 certificate) =>
        SHA256.HashData(certificate.PublicKey.ExportSubjectPublicKeyInfo());

    public static bool MatchesPin(ReadOnlySpan<byte> pin, ReadOnlySpan<byte> fullHash) =>
        pin.Length is ConnectionCode.PinLength or 32 && fullHash.Length == 32 &&
        CryptographicOperations.FixedTimeEquals(fullHash[..pin.Length], pin);

    public static bool IsCertificateAuthority(X509Certificate2 certificate) =>
        certificate.Extensions.OfType<X509BasicConstraintsExtension>().FirstOrDefault() is { CertificateAuthority: true };

    public static bool HasServerAuthentication(X509Certificate2 certificate) =>
        certificate.Extensions.OfType<X509EnhancedKeyUsageExtension>()
            .Any(x => x.EnhancedKeyUsages.Cast<System.Security.Cryptography.Oid>().Any(o => o.Value == ServerAuthenticationOid));

    // presented: certyfikaty pośrednie otrzymane w uzgadnianiu TLS (ExtraStore łańcucha z wywołania zwrotnego).
    public static InstallationTrustResult Validate(X509Certificate2? leaf, IEnumerable<X509Certificate2> presented, string host,
        SslPolicyErrors errors, InstallationTrustAnchor anchor, DateTime? verificationTimeUtc = null)
    {
        if (leaf == null) return InstallationTrustResult.Rejected;
        if (errors == SslPolicyErrors.None) return InstallationTrustResult.TrustedBySystem;
        // Brak certyfikatu albo niezgodna nazwa zawsze oznacza odmowę, także z kotwicą.
        if ((errors & ~SslPolicyErrors.RemoteCertificateChainErrors) != 0) return InstallationTrustResult.Rejected;
        if (string.IsNullOrWhiteSpace(host) || !leaf.MatchesHostname(host, allowWildcards: false, allowCommonName: false))
            return InstallationTrustResult.Rejected;
        if (!HasServerAuthentication(leaf) || IsCertificateAuthority(leaf)) return InstallationTrustResult.Rejected;

        using var chain = new X509Chain();
        var policy = chain.ChainPolicy;
        // Pusty własny magazyn: systemowe urzędy nie wpływają na budowę ścieżki. Nieznany urząd (CA instalacji
        // podpisane jednorazowym kluczem startowym) jest dopuszczony wyłącznie dlatego, że ostatni element
        // łańcucha jest dalej porównywany z przypiętym skrótem klucza.
        policy.TrustMode = X509ChainTrustMode.CustomRootTrust;
        policy.VerificationFlags = X509VerificationFlags.AllowUnknownCertificateAuthority;
        policy.RevocationMode = X509RevocationMode.NoCheck;
        policy.DisableCertificateDownloads = true;
        policy.ApplicationPolicy.Add(new System.Security.Cryptography.Oid(ServerAuthenticationOid));
        if (verificationTimeUtc is { } time) { policy.VerificationTimeIgnored = false; policy.VerificationTime = time; }
        foreach (var certificate in presented) policy.ExtraStore.Add(certificate);
        if (anchor.CaCertificate is { } stored) policy.ExtraStore.Add(X509CertificateLoader.LoadCertificate(stored));

        if (!chain.Build(leaf) || chain.ChainElements.Count != 2) return InstallationTrustResult.Rejected;
        var ca = chain.ChainElements[1].Certificate;
        if (!IsCertificateAuthority(ca) || !MatchesPin(anchor.CaSpkiSha256, SpkiSha256(ca))) return InstallationTrustResult.Rejected;
        return InstallationTrustResult.TrustedByInstallationCa;
    }
}
