using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Vatowiec.SharedContracts.ServerTls;

namespace Vatowiec.Core.Infrastructure.ServerTls;

// ADR-047 tryb A: certyfikat własny wskazany regułą (wymagane nazwy SAN, opcjonalnie odcisk wystawcy), nie odciskiem.
// Wybór wykonuje proces API (konto, które musi mieć dostęp do klucza), dlatego automatyczne odnowienie
// przez AD CS lub narzędzie ACME nie wymaga zmiany konfiguracji.
public interface IServerCertificateSource
{
    IReadOnlyList<X509Certificate2> Candidates();
}

public sealed class LocalMachineCertificateSource : IServerCertificateSource
{
    public IReadOnlyList<X509Certificate2> Candidates()
    {
        if (!OperatingSystem.IsWindows()) return [];
        using var store = new X509Store(StoreName.My, StoreLocation.LocalMachine);
        store.Open(OpenFlags.ReadOnly | OpenFlags.OpenExistingOnly);
        return [.. store.Certificates.Cast<X509Certificate2>()];
    }
}

public sealed record OwnCertificateSelectionResult(X509Certificate2? Certificate, string? ErrorCode, string Message, string[] Warnings);

public static class OwnCertificateSelection
{
    public static OwnCertificateSelectionResult Select(IEnumerable<X509Certificate2> candidates, IReadOnlyCollection<string> requiredNames,
        string? issuerThumbprint, DateTimeOffset now)
    {
        if (requiredNames.Count == 0)
            return new(null, "tls_own_names_missing", "Podaj nazwy DNS lub adresy IP, które musi zawierać certyfikat.", []);
        var matching = new List<X509Certificate2>();
        var withoutKey = 0; var withoutEku = 0; var namesMissing = 0; var expired = 0; var otherIssuer = 0;
        foreach (var certificate in candidates)
        {
            if (requiredNames.Any(name => !certificate.MatchesHostname(name, allowWildcards: true, allowCommonName: false))) { namesMissing++; continue; }
            if (now < certificate.NotBefore.ToUniversalTime() || now > certificate.NotAfter.ToUniversalTime()) { expired++; continue; }
            if (!InstallationCertificateValidation.HasServerAuthentication(certificate) || InstallationCertificateValidation.IsCertificateAuthority(certificate)) { withoutEku++; continue; }
            if (issuerThumbprint != null && !IssuedBy(certificate, issuerThumbprint)) { otherIssuer++; continue; }
            if (!PrivateKeyAccessible(certificate)) { withoutKey++; continue; }
            matching.Add(certificate);
        }
        var selected = matching.OrderByDescending(x => x.NotBefore).ThenByDescending(x => x.NotAfter).FirstOrDefault();
        if (selected == null)
        {
            var (code, message) = withoutKey > 0 ? ("tls_own_key_inaccessible", "Pasujący certyfikat istnieje, ale konto procesu API nie ma dostępu do jego klucza prywatnego.")
                : withoutEku > 0 ? ("tls_own_eku_missing", "Pasujący certyfikat nie ma przeznaczenia „Uwierzytelnianie serwera” (EKU).")
                : otherIssuer > 0 ? ("tls_own_issuer_mismatch", "Pasujący certyfikat został wystawiony przez innego wystawcę niż wskazany odciskiem.")
                : expired > 0 ? ("tls_own_expired", "Pasujący certyfikat jest wygasły albo jeszcze nieważny.")
                : ("tls_own_not_found", "W magazynie certyfikatów komputera (Lokalny komputer → Osobisty) nie ma certyfikatu zawierającego wszystkie podane nazwy.");
            return new(null, code, message, []);
        }
        var warnings = new List<string>();
        if (selected.NotAfter.ToUniversalTime() - now < TimeSpan.FromDays(30))
            warnings.Add($"Certyfikat serwera wygasa {selected.NotAfter:yyyy-MM-dd}. Odnów go w źródle (np. firmowe CA).");
        if (!ChainTrustedBySystem(selected))
            warnings.Add("Łańcuch certyfikatu nie jest zaufany na samym serwerze. O zaufaniu decydują stacje; upewnij się, że ufają wystawcy.");
        return new(selected, null, "Wybrano certyfikat " + selected.Thumbprint + ".", [.. warnings]);
    }

    public static bool PrivateKeyAccessible(X509Certificate2 certificate)
    {
        if (!certificate.HasPrivateKey) return false;
        try
        {
            using var rsa = certificate.GetRSAPrivateKey();
            if (rsa != null) return true;
            using var ecdsa = certificate.GetECDsaPrivateKey();
            return ecdsa != null;
        }
        catch (CryptographicException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }

    private static bool IssuedBy(X509Certificate2 certificate, string issuerThumbprint)
    {
        using var chain = new X509Chain();
        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
        chain.ChainPolicy.VerificationFlags = X509VerificationFlags.AllowUnknownCertificateAuthority;
        chain.ChainPolicy.DisableCertificateDownloads = true;
        chain.Build(certificate);
        if (chain.ChainElements.Count < 2) return false;
        var issuer = chain.ChainElements[1].Certificate;
        if (!issuer.SubjectName.RawData.AsSpan().SequenceEqual(certificate.IssuerName.RawData)) return false;
        return issuerThumbprint.Length == 40
            ? string.Equals(issuer.Thumbprint, issuerThumbprint, StringComparison.OrdinalIgnoreCase)
            : string.Equals(issuer.GetCertHashString(HashAlgorithmName.SHA256), issuerThumbprint, StringComparison.OrdinalIgnoreCase);
    }

    private static bool ChainTrustedBySystem(X509Certificate2 certificate)
    {
        using var chain = new X509Chain();
        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
        chain.ChainPolicy.DisableCertificateDownloads = true;
        return chain.Build(certificate);
    }
}
