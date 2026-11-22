using Vatowiec.Desktop.Services;
using Vatowiec.SharedContracts.ServerTls;

namespace Vatowiec.Desktop.ViewModels;

// ADR-047: parowanie stacji z serwerem w sieci prywatnej na ekranie logowania (kod albo plik połączenia).
// Kotwica dotyczy wyłącznie dokładnego adresu API; bez TOFU i bez „akceptuj mimo to”.
public sealed partial class MainViewModel
{
    private readonly IServerTrustStore trustStore;
    private string connectionCodeInput = "";

    public string ConnectionCodeInput
    {
        get => connectionCodeInput;
        set { if (busy || value == connectionCodeInput) return; connectionCodeInput = value; Changed(); }
    }

    public bool HasServerTrust => TryFindTrust(out var anchor) && anchor != null;

    public string ServerTrustStatus
    {
        get
        {
            if (IsPersonal) return "";
            try
            {
                return trustStore.Find(ApiAddress) is { } anchor
                    ? "Zaufanie dla tego adresu: CA instalacji VATowiec (kod " + anchor.Code + ")."
                    : "Zaufanie dla tego adresu: certyfikat zaufany przez Windows (standardowa walidacja).";
            }
            catch (ArgumentException) { return ""; }
            catch (LocalLoginStorageException ex) { return ex.Message; }
        }
    }

    private InstallationTrustAnchor? FindTrust(string address) => trustStore.Find(address);

    private bool TryFindTrust(out InstallationTrustAnchor? anchor)
    {
        anchor = null;
        try { anchor = trustStore.Find(ApiAddress); return true; }
        catch (Exception e) when (e is ArgumentException or LocalLoginStorageException) { return false; }
    }

    public void ApplyConnectionCode()
    {
        if (busy || IsPersonal || !IsLogin) return;
        try
        {
            var origin = ConnectionFile.NormalizeOrigin(ApiAddress);
            if (!ConnectionCode.TryParse(connectionCodeInput, out var pin, out var error)) { message = error; return; }
            trustStore.Save(origin, new InstallationTrustAnchor(pin), "Code");
            connectionCodeInput = "";
            message = "Zapisano kod połączenia dla " + origin + ". Program ufa CA instalacji wyłącznie dla tego adresu; nazwa, ważność i podpis certyfikatu są nadal sprawdzane.";
        }
        catch (ArgumentException) { message = "Najpierw wpisz poprawny adres HTTPS serwera (np. https://serwer:7443), a potem kod połączenia."; }
        catch (LocalLoginStorageException ex) { message = ex.Message; }
        finally { ChangedAll(); }
    }

    public void ImportConnectionFile(string text)
    {
        if (busy || IsPersonal || !IsLogin) return;
        try
        {
            if (!ConnectionFile.TryRead(text, out var content, out var anchor, out var error)) { message = error; return; }
            foreach (var address in content!.ApiAddresses) trustStore.Save(address, anchor!, "File");
            apiAddress = content.ApiAddresses[0];
            ApplyLoginFlags();
            message = $"Wczytano plik połączenia instalacji „{content.InstallationName}”. Adres API: {apiAddress}. " +
                "Zaufanie do CA instalacji dotyczy wyłącznie adresów z pliku: " + string.Join(", ", content.ApiAddresses) + ".";
        }
        catch (LocalLoginStorageException ex) { message = ex.Message; }
        finally { ChangedAll(); }
    }

    public void ForgetServerTrust()
    {
        if (busy || IsPersonal || !IsLogin) return;
        try
        {
            trustStore.Remove(ApiAddress);
            message = "Usunięto zaufanie do CA instalacji dla " + ConnectionFile.NormalizeOrigin(ApiAddress) + ". Obowiązuje standardowa walidacja certyfikatu.";
        }
        catch (ArgumentException) { message = "Wpisz poprawny adres HTTPS serwera."; }
        catch (LocalLoginStorageException ex) { message = ex.Message; }
        finally { ChangedAll(); }
    }

    private string SecureConnectionMessage() => HasServerTrust
        ? "Certyfikat serwera nie pochodzi od urzędu (CA) zaufanego dla tej instalacji albo nie pasuje do adresu lub terminu ważności. " +
          "Poproś administratora o aktualny kod lub plik połączenia. Połączenie zostało odrzucone."
        : "Nie można potwierdzić bezpiecznego połączenia HTTPS z API. Sprawdź certyfikat serwera: zaufanie, ważność i zgodność nazwy z adresem. " +
          "Jeśli serwer używa CA instalacji VATowiec, wpisz kod połączenia albo otwórz plik połączenia w Ustawieniach połączenia.";
}
