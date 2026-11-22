namespace Vatowiec.SharedContracts.ServerTls;

// ADR-047: ustawienia certyfikatu serwera (wyłącznie administrator instalacji). Bez kluczy i haseł.
public static class ServerTlsModeNames
{
    public const string InstallationCa = "InstallationCa";
    public const string OwnCertificate = "OwnCertificate";
}

public sealed record ServerTlsRequest(string Mode, string[] DnsNames, string[] IpAddresses, string? IssuerThumbprint, string RowVersion);
public sealed record ServerTlsCaReplacementRequest(string RowVersion, bool Confirm);

public sealed record ServerTlsResponse(
    string Mode, string[] DnsNames, string[] IpAddresses, string? IssuerThumbprint, string RowVersion,
    ServerTlsInstallationCaState InstallationCa,
    ServerTlsServedCertificate Served,
    string[] Warnings,
    ServerTlsHistory[] History);

public sealed record ServerTlsInstallationCaState(
    bool Exists, bool ReplacementPending, string? ConnectionCode, string? CaThumbprint, DateTimeOffset? CaNotAfter,
    string? ServerCertificateThumbprint, DateTimeOffset? ServerCertificateNotAfter, string[] ServerCertificateNames,
    string? LastErrorCode, DateTimeOffset? LastErrorAtUtc, DateTimeOffset? UpdatedAtUtc);

// Stan certyfikatu faktycznie podawanego przez ten proces API (odczyt z pamięci procesu).
public sealed record ServerTlsServedCertificate(
    bool Managed, string[] Endpoints, string? Source, string? Thumbprint, string? Subject, DateTimeOffset? NotAfter, string? ErrorCode);

public sealed record ServerTlsHistory(long Id, DateTimeOffset AtUtc, string Kind, string ActorName, string? BeforeJson, string AfterJson);
