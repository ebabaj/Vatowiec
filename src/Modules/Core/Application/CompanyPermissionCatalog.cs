using Vatowiec.Core.Domain;

namespace Vatowiec.Core.Application;

public static class CompanyPermissionCatalog
{
    public static CompanyPermission All => Enum.GetValues<CompanyPermission>().Aggregate(CompanyPermission.None, (a, b) => a | b);
    public static CompanyPermission Required(CompanyPermission permission) => permission switch
    {
        CompanyPermission.EditContractors or CompanyPermission.AcceptWarnings or CompanyPermission.VerifyRegistries or
            CompanyPermission.ManageContractorPesel => CompanyPermission.ViewProfile | CompanyPermission.ViewContractors,
        CompanyPermission.ManageCurrencies or CompanyPermission.ImportExchangeRates => CompanyPermission.ViewProfile | CompanyPermission.ViewExchangeRates,
        CompanyPermission.ViewProfile => CompanyPermission.None,
        _ => CompanyPermission.ViewProfile
    };
    public static void Validate(CompanyPermission permissions)
    {
        if ((permissions & ~All) != 0) throw new ArgumentException("Nieznane uprawnienie. Odśwież aplikację.");
        foreach (var permission in Enum.GetValues<CompanyPermission>().Where(x => x != CompanyPermission.None && (permissions & x) == x))
            if ((permissions & Required(permission)) != Required(permission))
                throw new ArgumentException($"Uprawnienie {Describe(permission).Name} wymaga: {string.Join(", ", Enum.GetValues<CompanyPermission>().Where(x => x != CompanyPermission.None && (Required(permission) & x) == x).Select(x => Describe(x).Name))}.");
    }
    public static (string Name, string Description) Describe(CompanyPermission permission) => permission switch
    {
        CompanyPermission.ViewProfile => ("Odczyt profilu firmy", "Otwieranie firmy; wymagane przez pozostałe prawa."),
        CompanyPermission.ViewContractors => ("Odczyt kontrahentów", "Przeglądanie kartoteki i warunków finansowych."),
        CompanyPermission.EditContractors => ("Edycja kontrahentów", "Zapis danych i warunków; wymaga odczytu kontrahentów."),
        CompanyPermission.AcceptWarnings => ("Akceptacja ostrzeżeń", "Świadomy zapis mimo ostrzeżenia; nie zastępuje prawa edycji."),
        CompanyPermission.VerifyRegistries => ("Sprawdzanie rejestrów", "Ręczne sprawdzenia MF, GUS, VIES i DataPort."),
        CompanyPermission.ManageCompanyIntegrations => ("Konfiguracja integracji firmy", "Zarządzanie kluczem DataPort; bez administracji instalacją."),
        CompanyPermission.ViewExchangeRates => ("Odczyt walut i kursów", "Przeglądanie słownika, kursów i historii."),
        CompanyPermission.ManageCurrencies => ("Zarządzanie walutami", "Zmiana aktywności i ustawień walut; wymaga odczytu kursów."),
        CompanyPermission.ImportExchangeRates => ("Import kursów NBP", "Ręczne pobieranie i import; wymaga odczytu kursów."),
        CompanyPermission.ManageContractorPesel => ("Odczyt i edycja PESEL", "Dane chronione; zapis wymaga także edycji kontrahentów."),
        _ => (permission.ToString(), "Uprawnienie firmy z aktualnej wersji serwera.")
    };
}
