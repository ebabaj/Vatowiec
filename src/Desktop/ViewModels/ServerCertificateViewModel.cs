using System.ComponentModel;
using System.Runtime.CompilerServices;
using Vatowiec.Desktop.Services;
using Vatowiec.SharedContracts.ServerTls;

namespace Vatowiec.Desktop.ViewModels;

// ADR-047 (ekran z własnym modelem widoku, ADR-046): Ustawienia instalacji → Certyfikat serwera.
// Wyłącznie administrator instalacji; serwer sprawdza rolę, CSRF i wersję przy każdym zapisie.
public sealed class ServerCertificateViewModel(IVatowiecApiClient api) : INotifyPropertyChanged
{
    private ServerTlsResponse? response;
    private bool ownCertificate;
    private string dnsNames = "", ipAddresses = "", issuerThumbprint = "";

    public event PropertyChangedEventHandler? PropertyChanged;

    public bool IsLoaded => response != null;
    public bool IsInstallationCa { get => !ownCertificate; set { if (value) SetMode(false); } }
    public bool IsOwnCertificate { get => ownCertificate; set { if (value) SetMode(true); } }
    public string DnsNames { get => dnsNames; set { dnsNames = value ?? ""; ChangedAll(); } }
    public string IpAddresses { get => ipAddresses; set { ipAddresses = value ?? ""; ChangedAll(); } }
    public string IssuerThumbprint { get => issuerThumbprint; set { issuerThumbprint = value ?? ""; ChangedAll(); } }

    public bool IsDirty => response != null && (ownCertificate != (response.Mode == ServerTlsModeNames.OwnCertificate) ||
        !Lines(dnsNames).SequenceEqual(response.DnsNames, StringComparer.OrdinalIgnoreCase) ||
        !Lines(ipAddresses).SequenceEqual(response.IpAddresses, StringComparer.OrdinalIgnoreCase) ||
        !string.Equals(issuerThumbprint.Trim(), response.IssuerThumbprint ?? "", StringComparison.OrdinalIgnoreCase));

    public string ConnectionCode => response?.InstallationCa.ConnectionCode ?? "— CA instalacji jeszcze nie istnieje —";
    public bool HasConnectionCode => response?.InstallationCa.ConnectionCode != null;
    public bool CanRequestCaReplacement => response is { InstallationCa: { Exists: true, ReplacementPending: false } };
    public string InstallationCaSummary => response?.InstallationCa is not { } ca ? "" : !ca.Exists ? "CA instalacji nie zostało jeszcze utworzone przez usługę VATowiec Operations."
        : $"CA instalacji ważne do {ca.CaNotAfter:yyyy-MM-dd}. Certyfikat serwera ważny do {ca.ServerCertificateNotAfter:yyyy-MM-dd} " +
          $"(odnawiany automatycznie 30 dni przed końcem). Nazwy: {string.Join(", ", ca.ServerCertificateNames)}." +
          (ca.ReplacementPending ? " Wymiana CA została zlecona i czeka na usługę Operations." : "");
    public string ServedSummary => response?.Served is not { } served ? "" : !served.Managed
        ? "Ten proces API nie podaje certyfikatu zarządzanego przez VATowiec (brak ServerTls:Endpoints w konfiguracji serwera)."
        : served.Thumbprint == null ? "API nie ma jeszcze certyfikatu do podania na endpointach: " + string.Join(", ", served.Endpoints) + "."
        : $"API podaje na endpointach {string.Join(", ", served.Endpoints)} certyfikat {(served.Source == ServerTlsModeNames.OwnCertificate ? "własny" : "CA instalacji")} " +
          $"{served.Thumbprint}, ważny do {served.NotAfter:yyyy-MM-dd}.";
    public string[] Warnings => response?.Warnings ?? [];
    public bool HasWarnings => Warnings.Length > 0;
    public string[] History => response?.History.Select(x => $"{x.AtUtc.ToLocalTime():yyyy-MM-dd HH:mm} · {KindName(x.Kind)} · {x.ActorName}").ToArray() ?? [];

    public async Task Load() { Apply(await api.ServerCertificate()); }

    public async Task<string> Save()
    {
        if (response == null || !IsDirty) return "";
        Apply(await api.SaveServerCertificate(new(ownCertificate ? ServerTlsModeNames.OwnCertificate : ServerTlsModeNames.InstallationCa,
            Lines(dnsNames), Lines(ipAddresses), string.IsNullOrWhiteSpace(issuerThumbprint) ? null : issuerThumbprint.Trim(), response.RowVersion)));
        return ownCertificate
            ? "Zapisano tryb certyfikatu własnego. Stacje sprawdzają certyfikat standardowo (zaufanie Windows)."
            : "Zapisano ustawienia CA instalacji. Usługa VATowiec Operations wystawi certyfikat z nowymi nazwami w ciągu kilku sekund.";
    }

    public async Task<string> RequestCaReplacement()
    {
        if (response == null) return "";
        Apply(await api.RequestCaReplacement(new(response.RowVersion, true)));
        return "Zlecono wymianę CA instalacji. Po jej wykonaniu każda stacja potrzebuje nowego kodu albo pliku połączenia.";
    }

    public async Task<string> ConnectionFileText() => SharedContracts.ServerTls.ConnectionFile.Serialize(await api.ServerConnectionFile());

    private void Apply(ServerTlsResponse value)
    {
        response = value;
        ownCertificate = value.Mode == ServerTlsModeNames.OwnCertificate;
        dnsNames = string.Join(Environment.NewLine, value.DnsNames);
        ipAddresses = string.Join(Environment.NewLine, value.IpAddresses);
        issuerThumbprint = value.IssuerThumbprint ?? "";
        ChangedAll();
    }

    private void SetMode(bool own) { if (ownCertificate == own) return; ownCertificate = own; ChangedAll(); }

    private static string[] Lines(string text) => text.Split(['\r', '\n', ',', ';', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static string KindName(string kind) => kind switch
    {
        "ConfigurationChanged" => "zmiana ustawień",
        "CaReplacementRequested" => "zlecenie wymiany CA",
        "CaCreated" => "utworzenie CA instalacji",
        "ServerCertificateIssued" => "wystawienie certyfikatu serwera",
        "LocalRecovery" => "lokalne przywrócenie trybu CA instalacji",
        _ => kind
    };

    private void ChangedAll() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
}
