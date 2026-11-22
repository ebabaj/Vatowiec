using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Vatowiec.SharedContracts.ServerTls;

// ADR-047: plik połączenia (*.vatowiec-polaczenie). Zawiera wyłącznie dane publiczne: adresy API,
// skrót klucza CA instalacji i certyfikat CA bez klucza. Ma tę samą moc co kod połączenia,
// więc musi dotrzeć do stacji autentycznym kanałem.
public sealed record ConnectionFileContent(string Format, int Version, string InstallationName, string[] ApiAddresses,
    string CaSpkiSha256, string CaCertificate);

public static class ConnectionFile
{
    public const string FormatName = "vatowiec-polaczenie";
    public const string Extension = ".vatowiec-polaczenie";
    public const int MaxBytes = 65536;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, WriteIndented = true };

    public static ConnectionFileContent Create(string installationName, IEnumerable<string> apiAddresses, X509Certificate2 ca)
    {
        var addresses = apiAddresses.Select(NormalizeOrigin).Distinct(StringComparer.Ordinal).ToArray();
        return new(FormatName, 1, installationName.Trim(), addresses,
            Convert.ToHexString(InstallationCertificateValidation.SpkiSha256(ca)), ca.ExportCertificatePem());
    }

    public static string Serialize(ConnectionFileContent content) => JsonSerializer.Serialize(content, Json);

    public static bool TryRead(string text, out ConnectionFileContent? content, out InstallationTrustAnchor? anchor, out string error)
    {
        content = null; anchor = null;
        error = "Plik połączenia jest uszkodzony albo nie pochodzi z VATowca.";
        if (string.IsNullOrWhiteSpace(text) || Encoding.UTF8.GetByteCount(text) > MaxBytes) return false;
        try
        {
            var value = JsonSerializer.Deserialize<ConnectionFileContent>(text, Json);
            if (value is not { Format: FormatName, Version: 1 } || value.ApiAddresses is not { Length: > 0 and <= 32 } ||
                string.IsNullOrWhiteSpace(value.CaCertificate) || value.CaSpkiSha256 is not { Length: 64 } ||
                (value.InstallationName ?? "").Length > 256) return false;
            var addresses = value.ApiAddresses.Select(NormalizeOrigin).ToArray();
            var ca = X509Certificate2.CreateFromPem(value.CaCertificate);
            var hash = Convert.FromHexString(value.CaSpkiSha256);
            if (!InstallationCertificateValidation.IsCertificateAuthority(ca) ||
                !InstallationCertificateValidation.MatchesPin(hash, InstallationCertificateValidation.SpkiSha256(ca)))
            {
                error = "Plik połączenia jest niespójny: certyfikat CA nie odpowiada zapisanemu skrótowi.";
                return false;
            }
            content = value with { ApiAddresses = addresses, InstallationName = value.InstallationName ?? "" };
            anchor = new InstallationTrustAnchor(hash, ca.RawData);
            error = "";
            return true;
        }
        catch (Exception e) when (e is JsonException or FormatException or ArgumentException or
            System.Security.Cryptography.CryptographicException or NotSupportedException)
        {
            return false;
        }
    }

    // Ten sam kształt adresu co w kliencie (ADR-016): wyłącznie origin HTTPS bez ścieżki, zapytania i userinfo.
    public static string NormalizeOrigin(string address)
    {
        if (!Uri.TryCreate(address?.Trim(), UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps ||
            string.IsNullOrEmpty(uri.Host) || uri.UserInfo.Length != 0 || uri.Query.Length != 0 ||
            uri.Fragment.Length != 0 || uri.AbsolutePath != "/")
            throw new ArgumentException("Niepoprawny adres HTTPS API.");
        return uri.GetLeftPart(UriPartial.Authority).ToLowerInvariant() + "/";
    }
}
