using System.Net;
using System.Net.Sockets;

namespace Vatowiec.Core.Domain;

// ADR-047: certyfikat serwera API w sieci prywatnej.
// Konfiguracja (jeden wiersz) należy do administratora instalacji; stan wykonania (jeden wiersz) zapisuje
// wyłącznie usługa VATowiec Operations. Klucze prywatne nigdy nie trafiają do bazy.
public static class ServerTlsModes
{
    public const string InstallationCa = "InstallationCa";
    public const string OwnCertificate = "OwnCertificate";
    public static bool IsKnown(string? mode) => mode is InstallationCa or OwnCertificate;
}

public static class ServerTlsChangeKinds
{
    public const string ConfigurationChanged = "ConfigurationChanged";
    public const string CaReplacementRequested = "CaReplacementRequested";
    public const string CaCreated = "CaCreated";
    public const string ServerCertificateIssued = "ServerCertificateIssued";
    public const string LocalRecovery = "LocalRecovery";
}

public sealed class ServerTlsConfiguration
{
    public const int MaxNames = 20;
    private ServerTlsConfiguration() { }
    public byte SingletonKey { get; private set; } = 1;
    public string Mode { get; private set; } = ServerTlsModes.InstallationCa;
    // Nazwy i adresy rozdzielone znakiem nowej linii; puste DnsNames w trybie CA instalacji = nazwa komputera i FQDN.
    public string DnsNames { get; private set; } = "";
    public string IpAddresses { get; private set; } = "";
    public string? IssuerThumbprint { get; private set; }
    // Zwiększenie oznacza zlecenie awaryjnej wymiany CA instalacji (wykonuje usługa Operations).
    public int CaGeneration { get; private set; } = 1;
    public byte[] RowVersion { get; private set; } = [];

    public string[] DnsNameList => Split(DnsNames);
    public string[] IpAddressList => Split(IpAddresses);

    public void Change(string mode, IEnumerable<string> dnsNames, IEnumerable<string> ipAddresses, string? issuerThumbprint)
    {
        if (!ServerTlsModes.IsKnown(mode)) throw new ArgumentException("Nieznany tryb certyfikatu serwera.", nameof(mode));
        var names = NormalizeDnsNames(dnsNames);
        var addresses = NormalizeIpAddresses(ipAddresses);
        var issuer = NormalizeThumbprint(issuerThumbprint);
        if (mode == ServerTlsModes.OwnCertificate && names.Length + addresses.Length == 0)
            throw new ArgumentException("Tryb certyfikatu własnego wymaga co najmniej jednej nazwy DNS albo adresu IP, które certyfikat musi zawierać.", nameof(dnsNames));
        Mode = mode;
        DnsNames = string.Join('\n', names);
        IpAddresses = string.Join('\n', addresses);
        IssuerThumbprint = issuer;
    }

    public void RequestCaReplacement() => CaGeneration++;

    public static string[] NormalizeDnsNames(IEnumerable<string> values)
    {
        var result = new List<string>();
        foreach (var raw in values ?? [])
        {
            var name = (raw ?? "").Trim().TrimEnd('.').ToLowerInvariant();
            if (name.Length == 0) continue;
            if (name.Length > 253 || Uri.CheckHostName(name) != UriHostNameType.Dns || name.Contains('*') ||
                name.Split('.').Any(label => label.Length is 0 or > 63 || label.StartsWith('-') || label.EndsWith('-') ||
                    !label.All(c => c is >= 'a' and <= 'z' or >= '0' and <= '9' or '-')))
                throw new ArgumentException($"Niepoprawna nazwa DNS: „{raw}”. Dozwolone są litery a–z, cyfry, myślniki i kropki, bez symboli wieloznacznych.", nameof(values));
            if (!result.Contains(name)) result.Add(name);
        }
        if (result.Count > MaxNames) throw new ArgumentException($"Można podać najwyżej {MaxNames} nazw DNS.", nameof(values));
        return [.. result];
    }

    public static string[] NormalizeIpAddresses(IEnumerable<string> values)
    {
        var result = new List<string>();
        foreach (var raw in values ?? [])
        {
            var text = (raw ?? "").Trim();
            if (text.Length == 0) continue;
            if (!IPAddress.TryParse(text, out var address) || address.AddressFamily is not (AddressFamily.InterNetwork or AddressFamily.InterNetworkV6) ||
                address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any) || text.Contains('%'))
                throw new ArgumentException($"Niepoprawny adres IP: „{raw}”.", nameof(values));
            var normalized = address.ToString();
            if (!result.Contains(normalized)) result.Add(normalized);
        }
        if (result.Count > MaxNames) throw new ArgumentException($"Można podać najwyżej {MaxNames} adresów IP.", nameof(values));
        return [.. result];
    }

    public static string? NormalizeThumbprint(string? value)
    {
        var text = new string((value ?? "").Where(c => !char.IsWhiteSpace(c) && c != ':').ToArray()).ToUpperInvariant();
        if (text.Length == 0) return null;
        if (text.Length is not (40 or 64) || !text.All(Uri.IsHexDigit))
            throw new ArgumentException("Odcisk wystawcy musi mieć 40 (SHA-1) albo 64 (SHA-256) znaki szesnastkowe.", nameof(value));
        return text;
    }

    private static string[] Split(string value) => value.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}

public sealed class ServerTlsState
{
    private ServerTlsState() { }
    public static ServerTlsState Create() => new();
    public byte SingletonKey { get; private set; } = 1;
    public int CaGeneration { get; private set; }
    public string? CaSpkiSha256 { get; private set; }
    public string? CaThumbprint { get; private set; }
    public DateTimeOffset? CaNotAfter { get; private set; }
    // Certyfikat CA jest publiczny (bez klucza); API buduje z niego plik połączenia.
    public byte[]? CaCertificate { get; private set; }
    public string? ServerCertificateThumbprint { get; private set; }
    public DateTimeOffset? ServerCertificateNotAfter { get; private set; }
    public string ServerCertificateNames { get; private set; } = "";
    public string? LastErrorCode { get; private set; }
    public DateTimeOffset? LastErrorAtUtc { get; private set; }
    public DateTimeOffset? UpdatedAtUtc { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    public string[] ServerCertificateNameList => ServerCertificateNames.Split('\n', StringSplitOptions.RemoveEmptyEntries);

    public void CaCreated(int generation, string spkiSha256, string thumbprint, DateTimeOffset notAfter, byte[] certificate, DateTimeOffset at)
    {
        CaGeneration = generation;
        CaSpkiSha256 = spkiSha256.Length == 64 && spkiSha256.All(Uri.IsHexDigit) ? spkiSha256.ToUpperInvariant()
            : throw new ArgumentException("Niepoprawny skrót klucza CA.", nameof(spkiSha256));
        CaThumbprint = thumbprint;
        CaNotAfter = notAfter;
        CaCertificate = certificate.ToArray();
        ServerCertificateThumbprint = null;
        ServerCertificateNotAfter = null;
        ServerCertificateNames = "";
        Succeeded(at);
    }

    public void ServerCertificateIssued(string thumbprint, DateTimeOffset notAfter, IEnumerable<string> names, DateTimeOffset at)
    {
        ServerCertificateThumbprint = thumbprint;
        ServerCertificateNotAfter = notAfter;
        ServerCertificateNames = string.Join('\n', names);
        Succeeded(at);
    }

    public void Succeeded(DateTimeOffset at) { LastErrorCode = null; LastErrorAtUtc = null; UpdatedAtUtc = at; }

    public void Failed(string code, DateTimeOffset at)
    {
        LastErrorCode = EntityText.Required(code, 64, nameof(code));
        LastErrorAtUtc = at;
        UpdatedAtUtc = at;
    }
}

// Niezmienny audyt (trigger w bazie): aktor, rodzaj, metadane przed/po. Bez kluczy, haseł i ich skrótów.
public sealed class ServerTlsChange
{
    private ServerTlsChange() { }
    public ServerTlsChange(string kind, Guid? actorId, string actorName, DateTimeOffset atUtc, string? beforeJson, string afterJson)
    {
        Kind = EntityText.Required(kind, 32, nameof(kind));
        ActorId = actorId is { } id ? EntityText.Id(id, nameof(actorId)) : null;
        ActorName = EntityText.Required(actorName, 256, nameof(actorName));
        AtUtc = atUtc;
        BeforeJson = beforeJson is { Length: > 2048 } ? throw new ArgumentException("Zbyt długi opis.", nameof(beforeJson)) : beforeJson;
        AfterJson = EntityText.Required(afterJson, 2048, nameof(afterJson));
    }
    public long Id { get; private set; }
    public string Kind { get; private set; } = null!;
    public Guid? ActorId { get; private set; }
    public string ActorName { get; private set; } = null!;
    public DateTimeOffset AtUtc { get; private set; }
    public string? BeforeJson { get; private set; }
    public string AfterJson { get; private set; } = null!;
}
