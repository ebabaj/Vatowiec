# GUS, MF i VIES — ustawienia instalacji (ADR-028/029)

Tryby i publiczne adresy rejestrów konfiguruje administrator **w Avalonia**.
Nowa instalacja ma wszystkie trzy usługi w **TEST**. Nie potrzeba poleceń
`dotnet user-secrets`, flagi `AllowProduction` ani restartu API po zapisaniu.
W tym zadaniu nie wykonano żadnych produkcyjnych zapytań do rejestrów.

## Konfiguracja w programie

1. Zaloguj konto z istniejącą rolą **InstallationAdministrator**. Otwórz
   **Ustawienia instalacji → Integracje zewnętrzne**, przed wyborem firmy
   albo z okna kartoteki. Uprawnienia administratora firmy nie zastępują tej roli.
2. Wybierz **GUS**, **MF** albo **VIES**. Każda usługa ma własne ustawienia.
3. Sprawdź adresy **TEST** i **PRODUKCJA**. Dla MF są cztery pola:
   API wykazu VAT oraz WL2, każde dla obu środowisk. Domyślne oficjalne adresy
   są już wpisane; nie trzeba ich przepisywać.
4. Wybierz środowisko. Przy **PRODUKCJA** zaznacz świadome potwierdzenie:
   kolejne sprawdzenia będą kierowane do rzeczywistego rejestru. Potwierdzenie
   jest wymagane przy każdym zapisie ustawień w tym trybie, także przez API.
5. Wybierz **Zapisz ustawienia usługi**. Zmiana obowiązuje dla nowych żądań
   bez restartu serwera. Sam zapis nie odpytuje żadnego rejestru.
6. Powtórz dla pozostałych usług, jeśli mają korzystać z produkcji.
   Powrót do TEST polega na zmianie trybu i zapisaniu tej konkretnej usługi.

Pola URL są edytowalne, ale serwer dopuszcza wyłącznie oficjalny host i dokładną
ścieżkę przypisaną do usługi oraz środowiska. HTTPS jest obowiązkowy. Nie można
podać własnego serwera, loginu, parametrów, fragmentu ani innej ścieżki.
Końcowy ukośnik jest opcjonalny; zapis ma postać kanoniczną. Błąd wskazuje
konkretne pole i dozwolony adres. Nie ma przekierowań ani fallbacku do innej usługi.

Gdy inny administrator zapisze ustawienia wcześniej, pojawi się konflikt wersji.
Wybierz **Odśwież ustawienia**, porównaj wartości i ponów świadomy zapis.
Trwająca operacja zachowuje pierwotny tryb i adresy przez cały czas, także
w sesji GUS login–wyszukanie–logout. Historia otrzymuje środowisko faktycznie
użyte przez tę operację. Dawnych wyników TEST/MOCK nie przekształcamy w produkcyjne.

## Produkcyjny klucz GUS

W tej samej konfiguracji wybierz GUS i wpisz własny klucz w maskowane pole,
a następnie **Zapisz / zastąp klucz**. Nie wpisuj go w terminalu ani plikach
konfiguracji. Jest to jeden sekret całej instalacji, wspólny dla uprawnionych
użytkowników jej firm. MF i podstawowe VIES checkVat nie wymagają kluczy.
Publiczny klucz GUS `abcde12345abcde12345` działa wyłącznie w TEST i nie może
zostać zapisany jako produkcyjny. Własny klucz uzyskuje administrator według
[instrukcji GUS](https://api.stat.gov.pl/Home/RegonApi).

Program pokazuje tylko **Skonfigurowano / Nie skonfigurowano**, wersję i tryb.
Nie odczytuje sekretu do Avalonia. „Skonfigurowano” oznacza obecność szyfrogramu,
nie potwierdzenie klucza przez GUS. Zastąpienie i usunięcie wymagają aktualnej
RowVersion; usunięcie dodatkowo jawnego potwierdzenia. Pole jest czyszczone
po wysłaniu, opuszczeniu ustawień i przejściu do innej usługi.

Ochrona pozostaje bez zmian: `Vatowiec_System.security.GusConfiguration`
(SingletonKey int PK CHECK=1, ProtectedApiKey nvarchar(max) NULL, RowVersion).
Data Protection ma purpose `VATowiec.GUS.BIR1.2`, application name `VATowiec.GUS`
i powiązanie z `security.Installation.Id`. Osobny ring domyślnie leży w
`%LOCALAPPDATA%\VATowiec\ServerKeys\GUS`, chroniony DPAPI konta serwera.
Ustawienie administracyjne `GUS:KeyRingDirectory` pozostaje dostępne dla hostingu.
Nie zmieniono ringu, purpose ani ochrony DataPort. Po utracie ochrony lub
przeniesieniu instalacji administrator wpisuje klucz ponownie; sam backup SQL
nie wystarczy do jego odszyfrowania.

Brak klucza/ochrony blokuje tylko produkcyjny GUS, z wynikiem NotConfigured/Unknown.
Nie przełącza go na TEST lub DataPort i nie blokuje MF/VIES. DataPort zachowuje
osobną konfigurację oraz istniejące sekrety każdej firmy.

## Uprawnienia, zapis i audyt

Publiczne ustawienia są w `Vatowiec_System.security.RegistryConfigurations`:

| Kolumna | SQL | Znaczenie |
|---|---|---|
| Source | varchar(4) PK | GUS, MF albo VIES |
| Environment | varchar(10) | Test albo Production |
| TestUrl / ProductionUrl | nvarchar(1024) | publiczne adresy obu środowisk |
| TestWl2Url / ProductionWl2Url | nvarchar(1024) NULL | wymagane tylko dla MF |
| RowVersion | rowversion | kontrola współbieżności |

`audit.RegistryConfigurationChanges` zapisuje Source, BeforeJson, AfterJson,
OccurredAtUtc, ActorId, ActorIssuerId i historyczną ActorName oraz własny bigint
IDENTITY. Nie ma FK do konta użytkownika. Zapis ustawień i audytu jest atomowy.
Historia jest niezmienna, chroniona przez kontekst i trigger SQL.
Zmiana klucza GUS również zostawia trwały audyt, ale tylko rodzaj operacji
i stan obecności sekretu — bez klucza, szyfrogramu i jego skrótu.

GET `/api/v1/installation/integrations/registries` i PUT `.../{GUS|MF|VIES}`
wymagają roli InstallationAdministrator sprawdzanej w DB przy każdym żądaniu.
PUT wymaga CSRF, RowVersion i potwierdzenia Production. Istniejące endpointy
GET/PUT/DELETE `.../gus` nadal obsługują wyłącznie stan i ochronę sekretu.
Centralna rola nie nadaje CompanyAccess. Sprawdzenia kontrahenta nadal wymagają
firmowych ViewContractors + VerifyRegistries, CSRF i aktualnej wersji rekordu.

Istniejących lokalnych user-secrets, zmiennych środowiskowych i plików nie
usunięto ani nie zmodyfikowano. Ich dawne publiczne nastawy `Registries:*`
nie sterują już normalnie działającym serwerem; źródłem jest baza systemowa.
Dotyczy to także dawnego `AllowProduction`. Nie importujemy automatycznie
starego Production: pierwsza migracja dodaje trzy rekordy TEST. Jawne ustawienia
Mock/Test pozostają narzędziem testów i samodzielnego testowego probe.

Pierwszą rolę administratora instalacji nadaje administrator serwera przez
istniejące narzędzie `grant-installation-admin <login>`; jest to osobna operacja
zarządzania rolami, nie konfiguracja adresów. Nie nadajemy roli automatycznie.

## Ręczne sprawdzenie

Najpierw sprawdź samo okno ustawień, bez wykonywania zapytań: zmień usługę,
zobacz osobne pola MF/WL2, spróbuj niedozwolonego URL i zapisu PRODUKCJA bez
potwierdzenia. Program powinien wyjaśnić odmowę. Zapisz wybrany tryb i ponownie
otwórz ustawienia bez restartu API. Konto bez centralnej roli nie może ich zapisać,
także przez bezpośrednie żądanie API.

Rzeczywiste sprawdzenia produkcyjne wykonuje świadomie użytkownik po konfiguracji:

1. Wybierz firmę i zapisanego kontrahenta, którego masz podstawę sprawdzić.
   Sprawdź etykietę środowiska konkretnej usługi w formularzu.
2. **Pobierz dane z GUS** pokazuje podgląd; przeniesienie wybranych danych
   do szkicu wymaga decyzji użytkownika i nie zapisuje kartoteki automatycznie.
3. **Sprawdź podatnika VAT**, **Status VAT (WL2)** oraz sprawdzenie rachunku
   dotyczą wskazanego dnia. W historii porównaj źródło, środowisko, dane użyte
   w zapytaniu, dzień stanu, wynik i referencję MF.
4. **Sprawdź VAT UE** używa pełnego numeru z prefiksem, również EL/XI.
   Niedostępność/timeout oznacza Unknown, nie „nieważny”. Zwykły SOAP checkVat
   nie dostarcza requestIdentifier; nie tworzymy sztucznej referencji.

Nie prezentuj TEST/MOCK jako aktualnego urzędowego potwierdzenia. Limity,
timeout 12 s, brak ponowień i istniejące parsery adapterów pozostają bez zmian.
Wspólna bramka GUS obejmuje całą sesję i wszystkie firmy, z przerwą 2 s po sesji.
Zakładamy jeden aktywny proces API; wiele replik wymaga wspólnego budżetu dostawcy.

## Wdrożenie i weryfikacja

Jawnie wykonano `20261023120832_InstallationRegistrySettings` wyłącznie w System,
po kontroli lokalnej instancji SQL Server 2025 Express, Windows Authentication,
znaczników trzech baz oraz backupie COPY_ONLY/CHECKSUM i RESTORE VERIFYONLY.
Migracja dodaje tylko dwie tabele, indeks, trigger i trzy domyślne rekordy TEST.
System ma 10 migracji, każda firma nadal 7. Nie resetowano baz ani kartotek.
Rollback migracji odmawia kasowania audytu; ewentualny powrót aplikacji wymaga
osobnej decyzji operacyjnej i zachowania historii.

Backup przed zmianą:
`C:\Program Files\Microsoft SQL Server\MSSQL17.SQLEXPRESS\MSSQL\Backup\Vatowiec_System_before_contractors_1c3908d8b2254f45b78cea6d6276c0d8.bak`.
Kontrola liczności i SHA256 wykazała zgodność 42 wcześniejszych tabel po migracji,
w tym szyfrogramów GUS i DataPort. Po testach 41 nadal identycznych; stary test
logowania dopisał zwykłe, następnie unieważnione sesje. Nowe testy SQL/API
wycofują własne konta, konfigurację i audyt zewnętrzną transakcją SQL.

**335/335 testów:** 330 bez SQL i 5 niedestrukcyjnych SQL/API; 0 pominiętych.
Sprawdzono rolę/CSRF/rowversion, potwierdzenie, URL, routing obu środowisk na
atrapach HTTP, nowy snapshot po zmianie, stały snapshot w trwającej sesji GUS,
atomowy i niezmienny audyt oraz istniejącą ochronę klucza. Kompilacja: 0 błędów
i ostrzeżeń. Nie uruchomiono 103 starszych testów SQL/API z resetującymi fixture;
nie są zaliczone w tym przebiegu. Nie używaj destrukcyjnego runnera DatabaseTools
test/prepare-tests/initialize do sprawdzania tej zmiany.

Próba w rzeczywistym oknie Avalonia przez HTTPS: logowanie operator.m0 z jego
istniejącą rolą, osobne pola trzech usług i MF/WL2, odrzucenie URL HTTP,
odrzucenie PRODUKCJI bez potwierdzenia, zapis MF PRODUKCJA, odświeżenie bez
restartu, powrót MF do TEST i wylogowanie. Nie wykonano zapytania do rejestru.
W audycie pozostały dwie rzeczywiste zmiany ustawień MF operatora, Test→Production
i Production→Test. Wszystkie trzy usługi po próbie są w TEST. Nie wpisywano
ani nie zastępowano klucza; szyfrogramy GUS/DataPort i lokalny plik user-secrets
pozostały identyczne (SHA256). API i Avalonia uruchomiono ponownie po kompilacji.

```powershell
dotnet build Vatowiec.slnx --no-restore
$env:VATOWIEC_PRESERVED_VAT_SQL = '1'
dotnet test Vatowiec.slnx --no-build --filter '(Category!=SqlIntegration)|FullyQualifiedName~OfficialVatRulesSqlTests|FullyQualifiedName~GusConfigurationSqlTests|FullyQualifiedName~RegistrySettingsSqlTests' --logger 'trx;LogFileName=registry-settings-preserved.trx' --results-directory TestResults
Remove-Item Env:VATOWIEC_PRESERVED_VAT_SQL
```

Raporty lokalne: TestResults/registry-settings-preserved.trx,
registry-settings-build.log, registry-settings-migration.sql,
registry-settings-before-data.json, registry-settings-after-data.json,
registry-settings-final-data.json. Decyzja i rozwiązania:
Przebieg okna: TestResults/registry-settings-ui.log.
[ADR-029](adr/ADR-029-ustawienia-rejestrow-w-instalacji.md).
Wcześniejszy [ADR-028](adr/ADR-028-centralny-klucz-gus-i-produkcja.md) pozostaje
historycznym opisem wdrożenia centralnego klucza i adapterów.

## Źródła kontraktów adapterów

Adresy i kontrakty zachowano z poprzedniego etapu:
[GUS BIR1.2](https://api.stat.gov.pl/Home/RegonApi),
[MF — specyfikacja 1.6.1 i nowe adresy](https://www.gov.pl/web/kas/api-wykazu-podatnikow-vat),
[oficjalny WSDL VIES](https://ec.europa.eu/taxation_customs/vies/checkVatService.wsdl).
Ta zmiana dotyczy sposobu konfiguracji, nie nowych kontraktów rejestrów.
