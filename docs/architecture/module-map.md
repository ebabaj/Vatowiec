# Mapa modułów VATowca

## Stan obecny repozytorium

To mapa logicznych obszarów istniejącego kodu, nie opis gotowych, osobnych modułów. Kod biznesowy jest obecnie skupiony w warstwach wspólnego `Core`; projekty `Currencies`, `Contractors` ani pozostałych obszarów nie istnieją.

Istniejące zależności projektów (potwierdzone referencjami):

- `Core.Domain` nie ma referencji do innych projektów.
- `Core.Application` → `Core.Domain`.
- `Core.Infrastructure` → `Core.Application`, `SharedContracts`.
- `Api` → `Core.Application`, `Core.Infrastructure`, `SharedContracts`.
- `Desktop` → wyłącznie `SharedContracts`.
- `Tests` → `Desktop`, `Api`, `Core.Infrastructure`.

Wspólne katalogi warstw to `src/Modules/Core/{Domain,Application,Infrastructure}`, `src/Server/Api`, `src/Desktop`, `src/SharedContracts` i `tests/Vatowiec.Tests`. Poniższe grupy są przypisaniem funkcjonalnym plików w tych warstwach, nie osobnymi granicami kompilacji.

## Obszary funkcjonalne — istniejące pliki i powiązania

### Platform

- **Domain:** `Company.cs`, `CompanyAccess.cs`, `CompanyDatabaseRegistration.cs`, `CompanyProfile.cs`, `InstallationIdentity.cs`, `PasswordPolicy.cs`, `UserAdministrationChange.cs`, `GusConfiguration.cs`, `RegistryConfiguration.cs`.
- **Application:** `CompanyContext.cs`, `CompanyPermissionCatalog.cs`, `UserAdministrationSafety.cs`.
- **Infrastructure:** `Identity/` (użytkownicy, sesje, polityki haseł i administracja); `Persistence/SystemDbContext.cs`, `CompanyDbContext.cs`, fabryki, `CompanyDatabaseResolver.cs`, `CompanyDatabaseSelection.cs`, `CompanyWriteLock.cs`, `DatabaseConnectionPolicy.cs`, `SystemConfigurations/` i `CompanyConfigurations/`.
- **API / kontrakty / UI:** `Security/AuthenticationEndpoints.cs`, `CompanyEndpoints.cs`, `InstallationAdministration.cs`, `UserAdministrationEndpoints.cs`; `CompanyResponses.cs`, `PasswordPolicyContracts.cs`, `UserAdministrationContracts.cs`; widoki i modele logowania, firm, użytkowników oraz ustawień instalacji w `src/Desktop`.
- **Zależności:** API wybiera kontekst firmy na serwerze po uwierzytelnieniu i autoryzacji; katalog i konfiguracja instalacji korzystają z bazy systemowej. Konfiguracja rejestrów jest własnością Platform, a adaptery rejestrów z niej korzystają.
- **Wskazane testy:** `AuthenticationIntegrationTests`, `IdentityLifecycleTests`, `PasswordPolicyTests`, `UserAdministrationTests`, `DatabaseConnectionPolicyTests`, `CompanyModelTests`, `InstallationSecurityUiTests`.

### Contractors

- **Domain:** `Contractor.cs`, `ContractorChange.cs`, `ContractorCreateOperation.cs`, `ContractorDetails.cs`, `ContractorFinance.cs`; `TaxIdentifierScheme.cs` obsługuje wersjonowane reguły identyfikatorów kontrahenta.
- **Application:** `BankAccountPreparation.cs`, `IBankDirectory.cs`, `StandardIdentifiers.cs`, `TaxIdentifiers.cs`.
- **Infrastructure:** `Persistence/Contractor*.cs`, `Persistence/CompanyConfigurations/Contractor*.cs`, `ContractorBankAccountDefaults.cs`, `ContractorTableQuery.cs`, `IbanNetBankAccountValidator.cs`; `EwibBankDirectory.cs` zapewnia osobną, jawnie wywoływaną usługę informacji o banku.
- **API / kontrakty / UI:** `Security/Contractor*Endpoints.cs`, `ContractorDetailMapping.cs`, `IdentifierChecks.cs`; `Contractor*.cs` w `SharedContracts`; `MainViewModel.Contractors.cs`, `MainViewModel.Details.cs`, `MainViewModel.Finance.cs` i powiązane widoki.
- **Zależności:** dane kontrahenta i rachunków należą do bazy konkretnej firmy. Finanse wskazują walutę kodem z istniejącego słownika Currencies. Walidacja rachunku korzysta ze wspólnego interfejsu; weryfikacja zewnętrzna nie zapisuje automatycznie danych kontrahenta.
- **Wskazane testy:** `ContractorDesktopTests`, `ContractorDetailDesktopTests`, `ContractorIntegrationTests`, `ContractorDetailsIntegrationTests`, `ContractorFinanceTests`, `ContractorFinanceSqlTests`, `ContractorModelV2IntegrationTests`, `BankAccountValidatorTests`, `BankAccountIntegrationTests`, `StandardIdentifierTests`, `StandardIdentifierSqlTests`.

### Currencies

- **Domain / Application:** `Currency.cs`; `ExchangeRates.cs` i interfejs odczytu kursu.
- **Infrastructure:** `Persistence/CurrencyConfigurations.cs`, `CurrencySeed.cs`, `ExchangeRateService.cs` oraz istniejące migracje bazy firmy; `Registries/NbpClient.cs`.
- **API / kontrakty / UI:** `Security/CurrencyEndpoints.cs`, `CurrencyContracts.cs`, `MainViewModel.Currencies.cs` i `FinanceCurrencyLabelConverter.cs`.
- **Zależności:** kursy i słownik są danymi firmowymi; endpointy korzystają z uprawnień firmy, a klient tylko z API/kontraktów. Dane NBP dostarcza adapter HTTP wywoływany jawnie. `TableViews` udostępnia wspólną konfigurację widoku dla walut i kursów. ContractorFinance używa kodu waluty, nie własnego słownika.
- **Wskazane testy:** `ActiveCurrencyChoiceTests`, `CurrencyDesktopTests`, `CurrencyIntegrationTests`, `CurrencyActivitySqlTests`, `CurrencyActivitySqlTests`, `NbpTests`.

### Integrations

- **Domain / Application:** `ExternalVerification.cs`, `ForeignVatChecksums.cs`; konfiguracja trybów/sekretów jest w modelach Platform, a reguły identyfikatorów kontrahenta są na granicy z Contractors.
- **Infrastructure:** `Registries/RegistryAdapters.cs`, `DataPortClient.cs`, `DataPortKeyProtector.cs`, `GusKeyProtection.cs`; `NbpClient.cs` i `EwibBankDirectory.cs` są współdzielonymi adapterami przypisanymi funkcjonalnie odpowiednio do Currencies i Contractors.
- **API / kontrakty:** `VerificationEndpoints.cs`, `DataPortEndpoints.cs`, `GusConfigurationEndpoints.cs`, `RegistryConfigurationEndpoints.cs`; `VerificationContracts.cs`, `DataPortContracts.cs`, `GusContracts.cs`, `RegistryConfigurationContracts.cs`.
- **Zależności:** API kontroluje uprawnienia, tryb, konfigurację instalacji/firmy i zakres żądania. Konfiguracja rejestrów należy do Platform; historia weryfikacji do bazy firmy. Testowe żądania HTTP korzystają z atrap.
- **Wskazane testy:** `RegistryAdapterTests`, `ProductionRegistryTests`, `IdentifierRegistryIntegrationTests`, `GusConfigurationSqlTests`, `RegistrySettingsTests`, `RegistrySettingsSqlTests`, `EwibTests`, `EwibIntegrationTests`, `OfficialVatRulesTests`, `OfficialVatRulesSqlTests`.

### TableViews

- **Domain / Infrastructure:** `TableView.cs`, `Persistence/TableViewLayoutJson.cs`; mapowanie `TableView`/audytu jest obecnie bezpośrednio w `Persistence/SystemDbContext.cs`, z migracjami systemowymi.
- **API / kontrakty / UI:** `Security/TableViewEndpoints.cs`, `TableLayout.cs`, `TableViews.cs`; `ConfigurableTable.cs`, `TableCells.cs`, `TableDisplayNames.cs`, `TableHeaderGestures.cs`, `TableStyles.axaml`, `MainViewModel.TableViews.cs`, `MainViewModel.TableLayoutPersistence.cs`.
- **Zależności:** wspólny mechanizm jest używany przez Contractors i Currencies. API i kontrakty określają dostępne kolumny oraz zapis; konfiguracja użytkownika/instalacji jest przechowywana w zakresie bazy systemowej. Nie jest właścicielem danych tabel biznesowych.
- **Wskazane testy:** `TableViewTests`, `TableViewSqlTests`, `TableLayoutTests`, `ColumnLayoutUiTests`, `TableAppearanceUiTests`, `WorkspaceLayoutUiTests`.

### Shared

- **Publiczne kontrakty:** osobny projekt `src/SharedContracts` z DTO używanymi po obu stronach HTTPS (m.in. kontrakty kontrahentów, walut, rejestrów i widoków tabel). Nie zawiera encji EF ani `DbContext`.
- **Wspólna infrastruktura w obecnych warstwach:** `DependencyInjection.cs`, `Persistence/TrackedChanges.cs`, `DatabaseConnectionPolicy.cs`; `CompanyWriteLock.cs` i `IBankDirectory`/walidator są wspólnymi usługami używanymi przez wskazane obszary, ale nie są osobnym projektem Shared.
- **Zależności:** Desktop nie referuje API ani Infrastructure; API mapuje/wystawia kontrakty. Domenowe reguły nie powinny zależeć od Avalonia, HTTP, EF Core ani T-SQL.

## Proponowany podział docelowy — nie jest stanem repozytorium

Zachować wdrożenie jako modularny monolit, ale stopniowo ustanowić jawne granice `Platform`, `Contractors`, `Currencies`, `Integrations` i wspólnego `TableViews`, z warstwami Domain/Application/Infrastructure tam, gdzie uzasadnia to istniejąca logika. API pozostaje granicą transportową, Desktop korzysta z publicznych kontraktów, a Shared zawiera wyłącznie rzeczywiście wspólne kontrakty/usługi. Moduły nie powinny odwoływać się bezpośrednio do cudzych tabel ani `DbContext`; zależności przechodzą przez jawne kontrakty. Nie utworzono pustych projektów ani katalogów dla tego modelu. Jest to kierunek zgodny z intencją ADR-013, a nie deklaracja, że granice są już wdrożone.

## AGENTS.md — rozmiar, struktura i kandydaci do późniejszego uporządkowania

`AGENTS.md` ma 431 linii i 38 641 B. Zawiera najpierw bieżące zasady środowiska oraz historyczne, szczegółowe etapy (w tym użytkowników, kontrahentów, integracje i waluty), a następnie aktualniejszy opis architektury, nienaruszalnych reguł danych, podatków/integracji, sposobu pracy i starszego szkieletu repozytorium. W tym zadaniu plik pozostaje bez zmian.

W przyszłości można przenieść lub zastąpić odnośnikami modułowe szczegóły procedur i historii funkcji: waluty/kursy do `docs/currencies-and-nbp.md`, TableViews do `docs/table-views.md`, kontrahentów/identyfikatorów do dokumentacji kartoteki, ustawienia i adaptery do `docs/production-registries.md` / `docs/identifiers-and-registries.md`, a administrację użytkownikami do `docs/users-and-permissions.md`. Modułowe instrukcje mogą zawierać krótkie zasady pracy i link do dokumentacji, nie kopię specyfikacji.

Nie przenosić ani nie osłabiać globalnych wymagań bezpieczeństwa: izolacji baz firm i autoryzowanego wyboru firmy, Identity/uprawnień, audytu, kontroli współbieżności/idempotencji, ochrony sekretów i danych, atomowości zapisów ani zakazu nieautoryzowanego usuwania. `AGENTS.md` pozostaje nadrzędnym źródłem tych zasad.

## Kandydaci do późniejszego wydzielania — maksymalnie trzy niezależne etapy

1. **Currencies (rekomendowany pierwszy):** wydzielić jego logikę Domain/Application i usługę kursów/NBP w małym zakresie; na początku pozostawić istniejące trasy API, DTO, uprawnienia, schemat bazy, migracje, seed i zachowanie Avalonia bez zmian. Weryfikować testami `NbpTests`, `CurrencyIntegrationTests`, `CurrencyActivitySqlTests` i `CurrencyDesktopTests`.
2. **TableViews:** wyodrębnić wspólny przypadek użycia i komponent konfiguratora, zachowując identyfikatory tabel, kontrakty API, uprawnienia oraz istniejący zapis System DB. Weryfikować oddzielnie testami `TableViewTests`, `TableViewSqlTests`, `TableLayoutTests` i `ColumnLayoutUiTests`.
3. **Integrations — pojedynczy adapter:** zacząć od jednego rejestru (np. GUS), bez przenoszenia odpowiedzialności Platform za konfigurację instalacji, uprawnienia i sekrety; zachować istniejące endpointy oraz format historii. Weryfikować testami adaptera i konfiguracji z atrapami HTTP.

Każdy z tych etapów wymaga osobnego zakresu i akceptacji przed implementacją. Powyższy plan nie oznacza, że refaktoryzację rozpoczęto.
