# AGENTS.md — instrukcje dla Codexa i współautorów

Projekt: **Otwarty następca VATowca** (nazwa robocza).  
Główne źródło wymagań: [`SPECYFIKACJA.md`](./SPECYFIKACJA.md).  
Status: REGON, KRS, BDO i chroniony PESEL kontrahenta, 2026-10-23; ADR-030.

**Aktualny etap: standardowe identyfikatory kontrahenta (ADR-030).**
Dodajemy Regon/Krs/Bdo/Pesel jako opcjonalne teksty nvarchar(100) w bazie firmy,
z addytywnymi migracjami i zachowaniem istniejących danych oraz ParentId.
ManageContractorPesel=512 jest osobnym prawem firmy do odczytu i edycji PESEL,
bez automatycznego nadania istniejącym kontom. Bez niego API pomija PESEL
w listach/szczególe/konflikcie/replay i odrzuca jawny zapis tego pola; brak pola
przy PUT zachowuje chronioną wartość. AcceptWarnings nie zastępuje tego prawa.
Nie loguj PESEL, ciał formularzy/wyszukiwań ani wartości parametrów SQL.
Wyszukiwanie klienta używa POST search + CSRF, bez numerów w URL; PESEL wyłącznie
z odpowiednim prawem. Audyt przed/po jest chronioną częścią bazy firmy;
przyszłe udostępnienie historii musi również filtrować PESEL. SnapshotVersion=3
dla nowych zmian danych głównych, bez przepisywania dawnych zdarzeń.
REGON 9/14 i PESEL mają kontrolę sumy; KRS/BDO tylko formatu. Nie potwierdzamy
nadania numeru. Normalizacja jest jawna, błędne dane wymagają AcceptWarnings
i aktualnego WarningHash. Integracje nie nadpisują nowych pól automatycznie.
Nie uruchamiaj destrukcyjnego runnera SQL; nowe StandardIdentifierSqlTests
używają rzeczywistych transakcji wycofywanych po testach, bez czyszczenia baz.
W tym zadaniu zachowujemy wszystkie istniejące dane, konta i tabele System:
nie uruchamiaj resetującego runnera `DatabaseTools test` ani jego fixture.
Używaj niedestrukcyjnych OfficialVatRulesSqlTests/GusConfigurationSqlTests/RegistrySettingsSqlTests/StandardIdentifierSqlTests po jawnej migracji
i backupie. Jest to ograniczenie bieżące, nadrzędne wobec historycznych zgód
na reset demonstratora poniżej. Rozszerzamy istniejącą kartotekę i formularz Avalonia;
nie wykonujemy automatycznych zapytań produkcyjnych ani konfiguracji klucza za użytkownika.
Użytkownik zakończył etap dokumentacyjny. Można odtwarzać wyłącznie lokalne
Vatowiec_System, Vatowiec_Company_A_Dev i Vatowiec_Company_B_Dev po kontroli
instancji, dokładnych nazw, znaczników i fikcyjnego przeznaczenia danych.
Nie migruj dawnych GUID ani danych demonstracyjnych. Nie modyfikuj innych baz.
[ADR-019](docs/adr/ADR-019-implementacja-modelu-kontrahentow.md) opisuje rozwiązania
techniczne; [ADR-018](docs/adr/ADR-018-docelowy-model-kontrahentow.md) pozostaje
źródłem decyzji biznesowych. ADR-020 obejmuje cztery zakładki i pełne szczegóły.
ADR-021 dodaje edycję identyfikatorów, wersjonowane reguły SQL i zakładkę Weryfikacje.
MF/GUS/VIES domyślnie Test. ADR-029 zastępuje publiczne nastawy user-secrets
centralnym security.RegistryConfigurations i konfiguracją Avalonia. Production
wymaga jawnego potwierdzenia administratora instalacji, nie lokalnej flagi.
Ustawienia i trwały audit.RegistryConfigurationChanges zapisuj atomowo.
Nowe żądanie czyta świeży, scoped snapshot z DB; pojedyncza operacja zachowuje
spójny tryb, URL i środowisko historii bez restartu API. Nie kasuj zastanych
sekretów ani plików konfiguracji; stare publiczne Registries:* nie sterują
normalnym serwerem. Automatyczne testy HTTP wyłącznie na atrapach,
bez zapytań produkcyjnych. Ręczne próby wykona użytkownik. KSeF pozostaje poza zakresem
poza lokalną kontrolą formatu IDWew. ADR-022 dodaje osobny rzeczywisty DataPort,
z kluczem każdej firmy szyfrowanym DPAPI/Data Protection poza repozytorium/bazą.
Nie zmienia trybu oficjalnych adapterów. DataPort odpytuj tylko ręcznie po wpisaniu
klucza przez użytkownika; automatyczne testy wyłącznie na atrapach HTTP.
ManageCompanyIntegrations=32 chroni zapis konfiguracji razem z CSRF i rowversion;
API zwraca tylko stan, nigdy klucz/szyfrogram. X-API-Key tylko na jednym żądaniu,
stały HTTPS dataport.pl, bez logowania treści/nagłówka i ponowień. Dane pośrednika
mogą pochodzić z cache, nie są bezpośrednim potwierdzeniem urzędu. Używamy obecnego
audit.ExternalVerifications i selektywnego importu do szkicu. Szczegóły: docs/dataport.md.
ADR-023 dodaje lokalne ref.Currencies (149 pozycji offline i ustawienia A/B/C),
core.ExchangeRates i niezmienne audit.CurrencyOperations, bez CompanyId i GUID PK.
ADR-024 zastępuje numeryczny klucz WALUTY: wyłącznie Code char(3) PRIMARY KEY,
wielkie litery, niezmienny kod, bez Id/int/GUID. Kursy mają CurrencyCode char(3)
FK do Code i zachowują własne bigint IDENTITY. API/DTO/Avalonia używają kodów.
Odtwarzamy tylko trzy demonstracyjne tabele modułu walutowego; nie migrujemy
danych ani CurrencyId. Sama migracja nie dotyka innych kartotek ani DataPort.
Użytkownik następnie jawnie odwołał ograniczenie czyszczenia danych demonstratora
i zatwierdził pełny reset fikcyjnych danych na potrzeby wszystkich testów.
Używamy istniejącego `test` na trzech dozwolonych bazach; nie tworzymy *_Test.
Runner nadal zachowuje zastany szyfrogram DataPort po zweryfikowanych kopiach.
Nie rozszerza to zgody na inne aplikacje, bazy SQL systemowe ani zdalne serwery.
Pozostałe zasady ADR-023 obowiązują.
Domyślnie EUR/USD/GBP/CHF z A; PLN bez pobierania. ViewExchangeRates=64,
ManageCurrencies=128, ImportExchangeRates=256; B tylko odczyt. NBP to publiczne
HTTPS/JSON bez klucza; pobieranie wyłącznie ręczne, zakresy po 93 dni włącznie.
Nie generuj dat/kursów ani nie nadpisuj konfliktu po cichu. Mid/Bid/Ask decimal(24,12),
opcjonalne TradingDate C; świadome rozstrzygnięcie wymaga zgodnej wersji i audytu.
Nie dodawaj harmonogramu ani reguł doboru kursu dla faktury. Automatyczne testy
NBP używają atrap. Pełny runner chroni zastany szyfrogram DataPort zweryfikowanymi
kopiami i przywraca go do zgodnego profilu po testach, bez odczytu sekretu.
Szczegóły: docs/currencies-and-nbp.md. Nie wykonuj Commit ani Push.

## 1. Co budujemy

Wielostanowiskową aplikację księgowo-podatkową dla polskich mikrofirm: **KPiR, ryczałt, VAT, JPK, deklaracje, KSeF**. Aplikacja ma być **otwarta na rozszerzenia** bez przebudowy rdzenia. Pełna księgowość, magazyn i kadry/płace nie należą do początkowego zakresu.

## 2. Stos i architektura

- C# / .NET 10; **Avalonia UI** (klient); **ASP.NET Core** (API/serwer); **SQL Server** (domyślnie Express); **xUnit**.
- **Modularny monolit**: `Domain`, `Application`, `Infrastructure`, `Api`, `Desktop` i wyraźne moduły biznesowe.
- **Klient nie komunikuje się bezpośrednio z SQL Server**. Dane, autoryzacja i księgowanie należą do API.
- **Database-per-company jest obowiązkowe**: `Vatowiec_System` przechowuje katalog firm, użytkowników, role, uprawnienia i lokalizacje baz; każda firma ma fizycznie oddzielną bazę SQL Server, także na innej instancji.
- `SystemDbContext` nie może mapować kontrahentów, dokumentów ani ewidencji księgowych. `CompanyDbContext` mapuje dane dokładnie jednej firmy; oba konteksty mają osobne migracje i snapshoty.
- Wybór bazy następuje wyłącznie na serwerze po uwierzytelnieniu i autoryzacji firmy. Brak wyboru lub niezgodność `CompanyId` oznacza odmowę; nie ma domyślnej bazy firmowej. Kontekst i wybór połączenia nie mogą być współdzielone między żądaniami.
- Uwierzytelnianie i sesje API określa ADR-015: Identity w bazie systemowej, cookie HTTPS/HttpOnly, CSRF dla login/logout, absolutna ważność i serwerowe unieważnienie sesji. Rola globalna nie zastępuje CompanyAccess. Jedynym źródłem firmy jest ścieżka API; resolver weryfikuje rzeczywisty profil i historię migracji przed odczytem danych.
- Hasła kont demonstracyjnych pochodzą wyłącznie z lokalnego user-secrets lub środowiska. Nie loguj haseł, tokenów, cookie ani connection stringów; nie dodawaj publicznego endpointu obchodzącego uwierzytelnianie na potrzeby demonstratora.
- Klient Avalonia stosuje ADR-016: HTTPS ze standardową walidacją certyfikatów, cookie i CSRF tylko w pamięci, nowy token CSRF przed logout. Nie utrwalaj hasła ani sesji; po zmianie firmy, odmowie lub wygaśnięciu usuń poprzednie dane. Ukrycie przycisku nie zastępuje serwerowej kontroli ViewContractors. Adres API konfiguruje VATOWIEC_API_URL lub ekran logowania.
- Kartoteka stosuje ADR-017: POST/PUT wymagają CSRF, ViewContractors i EditContractors; PUT wymaga rowversion. Nie dodawaj fizycznego usuwania, force-update ani automatycznego rozstrzygania konfliktu. Zapis kontrahenta i audit.ContractorChanges jest atomowy w bazie firmy. Audyt zachowuje ActorId, historyczną nazwę i ActorIssuerId z trwałego security.Installation, bez FK do centralnego konta. Listę filtrujemy i stronicujemy po stronie SQL, nie pobieramy całej kartoteki do klienta.
- ADR-017 opisuje wcześniejszy demonstrator; wdrożenie ADR-018 opisuje ADR-019. Brak zbędnego CompanyId w danych jednej bazy firmy, klucze kartoteki int IDENTITY, trwały wynik POST w core.ContractorCreateOperations z OperationKind dla wszystkich kartotek i dodawania rodzaju adresu. Audyt zawiera RecordType/RecordId oraz opcjonalne WarningJson; preferencje adresów i rachunków zmieniaj atomowo z audytem obu rekordów. CompanyId w katalogu, uprawnieniach i wyborze firmy pozostaje. ParentId w Contractors służy grupowaniu podmiotów, grup i jednostek, nie określa zależności podatkowej. Żadne dane nie są dziedziczone: każdy rekord ma własne identyfikatory, adresy, rachunki i kontakty. Wybranie grupy bez identyfikatora przy przyszłej fakturze ma wyświetlać wybór powiązanego podmiotu; nie implementuj faktur teraz.
- API/UI edytują TaxId/VatEuId/KsefIdWew zgodnie z ADR-021: normalizacja i podgląd, ponowna kontrola przed zapisem, ostrzeżenia związane z wartościami/rowversion/regułami/duplikatami i osobne AcceptWarnings. Nieznane pola zapisu nadal są odrzucane. TaxId i VatEuId są odrębnymi opcjonalnymi tekstami do 100 znaków; VatEuId zawiera prefiks. Duplikaty są dozwolone po świadomym potwierdzeniu. Nie twórz ContractorTaxIdentifiers. IDWew ma format NIP–5 cyfr; nie generuj go, nie potwierdzaj nadania ani zgodności z własnym TaxId jednostki. Nie wdrażaj integracji KSeF.
- Zatwierdzone nazwy: core.ContractorAddresses, otwarty ref.AddressTypes (Adres rejestrowy, Adres korespondencyjny i rodzaje użytkownika), core.ContractorContacts, core.ContractorBankAccounts. Pełne kolumny, typy SQL i wymagalność adresów oraz rachunków określa ADR-018 §3.7 i §3.9; nie dodawaj pól ani tabel bez uzasadnionej potrzeby i akceptacji użytkownika. Wiele adresów tego samego rodzaju jest dozwolone, ale najwyżej jeden preferowany na ContractorId i AddressTypeId; oddziały nie dziedziczą adresów. Kontakt ma obowiązkowe Name, pozostałe dane opcjonalne; może łączyć telefon, e-mail i własny adres bez FK do ContractorAddresses. Nie dziel kontaktów na osoby/działy/miejsca i nie buduj CRM.
- ContractorBankAccounts ma CurrencyCode char(3) NULL i IsDefault bit. Najwyżej jeden domyślny rachunek na ContractorId i grupę waluty, także oddzielną grupę NULL; brak domyślnego jest dozwolony. Zmiana domyślnego atomowo odznacza poprzedni w tej samej grupie, z ochroną unikalności w bazie. AcceptWarnings=8 jest osobnym prawem firmy do potwierdzania dokładnie pokazanego ostrzeżenia rachunku (bez uzasadnienia); ADR-025 zastępuje bank-text-v1 adapterem IBankAccountValidator/IbanNet 6.2.0 (Infrastructure, wersja w centralnym props). NRB sprawdzaj na kopii PL+26 cyfr, zapisuj bez dopisywania PL; normalizuj białe znaki i wielkość liter. IBAN sprawdzaj według reguł kraju i sumy, a format spoza IBAN oznaczaj jako niezweryfikowany. Nie potwierdzaj istnienia rachunku, właściciela, banku ani BIC. WarningHash musi wiązać binaria/reguły, wynik, dane, firmę, aktora, operację i rowversion; zmiana reguł wymaga ponownego potwierdzenia niezapisanego żądania. Zachowuj atomowy audyt i idempotentny wynik skutecznego POST. Dependabot tylko IbanNet, bez automatycznego scalania; aktualizacje przy wydaniu i po testach. Dystrybucja wymaga licenses/IbanNet/LICENSE.md i THIRD-PARTY-NOTICES.md (Directory.Build.targets). Nie wymuszaj unikalności AccountNumber; numery waliduje Application/API z możliwością świadomej, audytowanej akceptacji ostrzeżenia przez uprawnionego użytkownika. Nie twórz ContractorDefaultBankAccounts ani osobnej kartoteki banków na tym etapie.
- Walidacja jest wersjonowana przez ref.TaxIdentifierSchemes w SystemDbContext, bez danych kontrahentów. Application/API sprawdza formaty VAT UE 27 państw i XI oraz NIP mod 11 dla PL. ADR-027 dodaje wyłącznie dla VatEuId sumy FI i HR oraz format BE zaczynający się od 0/1. Pozostałe 25 prefiksów pozostaje format-only-v1 / Unknown. Zagraniczne krajowe TaxId pozostają Unknown; nie stosuj do nich automatycznie algorytmów VAT UE. Poprawny format/suma nie potwierdza nadania lub rejestracji. Zachowuj poprzednie wersje, źródła i okresy; nie usuwaj historii przy downgrade. Źródła i wzorce: docs/tax-rules/vat-eu-2026-10-23.md, ADR-021/027. Bez podatkowych CHECK. „Użytkownik ma zawsze rację”: AcceptWarnings umożliwia świadomy zapis bez uzasadnienia, z atomowym audytem, ale nie obchodzi praw, integralności, rowversion i kontraktów usług zewnętrznych.
- Tworzenie kontrahenta ma niewidoczną dla użytkownika idempotencję operacji, niezależną od int Id: trwały wynik powiązany z kluczem żądania, chroniony przed równoległym wykonaniem i zapisany atomowo z kartoteką oraz audytem. Utrata odpowiedzi i ponowienie nie mogą utworzyć drugiego rekordu. Przebieg opisuje ADR-018; nie wymaga GUID jako klucza głównego, map starych GUID ani migracji fikcyjnych danych.
- Zachowaj rozdzielenie ContractorChanges i wdrożonego audit.ExternalVerifications w bazie firmy. Druga historia jest niezmienna (kontekst i trigger), zawiera snapshot faktycznego wejścia, środowisko Test/Production/Mock, status/wynik, referencję, wersje i trwałego aktora bez centralnego FK. Nie zapisuj całych odpowiedzi, SID ani sekretów. Nowe sprawdzenie nie edytuje kontrahenta. Niedostępność nie jest negatywnym wynikiem. TEST/MOCK nie są urzędowym potwierdzeniem produkcyjnym; nie przepisuj ich środowiska. GUS przenosi tylko wybrane wartości do szkicu, nigdy automatycznie nie zapisuje danych.
- Rejestry wymagają osobnego firmowego VerifyRegistries (16), ViewContractors i CSRF. Endpointy są ograniczone do oficjalnej allowlisty, bez przekierowań/fallbacku i dowolnego URL klienta. Production wymaga świadomego potwierdzenia w Ustawieniach instalacji przez InstallationAdministrator (ADR-029); nowe rekordy security.RegistryConfigurations domyślnie Test. Tryby i URL obu środowisk należą wyłącznie do System, z osobnymi adresami MF WL2. API waliduje także nieaktywne URL, wymaga CSRF i rowversion oraz atomowego, niezmiennego audytu; nie używaj user-secrets/klucza firmowego do normalnej konfiguracji publicznych nastaw. Nie zmieniaj trybu w trakcie operacji ani nie stosuj cache procesu. GUS używa publicznego klucza wyłącznie w Test. Produkcyjny klucz jest JEDEN dla całej instalacji: security.GusConfiguration w System, szyfrowany Data Protection + DPAPI z osobnym purpose/ringiem i Installation.Id. Nie kopiuj go do baz firm, nie używaj Registries:GUS:ApiKey, nie zmieniaj ochrony DataPort. Ustawienia instalacji wymagają centralnej roli InstallationAdministrator sprawdzanej w DB przy każdym żądaniu, CSRF/rowversion i potwierdzenia usunięcia. Zmiana klucza pozostawia trwały audyt wyłącznie metadanych obecności/rodzaju operacji; bez klucza, szyfrogramu ani skrótu. ManageCompanyIntegrations nie nadaje tego prawa, rola nie zastępuje CompanyAccess; nadanie pierwszej roli wyłącznie jawne lokalne grant-installation-admin <login>. Brak/utrata sekretu oznacza NotConfigured bez fallbacku i bez blokowania MF/VIES/DataPort. Timeout 12 s, bez ponowień, wspólna bramka GUS obejmuje całą sesję i wszystkie firmy, z 2 s przerwy po zakończeniu. Jeden aktywny proces API; wiele replik wymaga wspólnego budżetu. Automatyczne HTTP wyłącznie na atrapach; produkcyjne próby wykonuje ręcznie użytkownik. Instrukcja: docs/production-registries.md.
- Logika domenowa nie może zależeć od Avalonia, HTTP, EF Core ani T-SQL.
- Nie dodawaj mikrousług, event sourcingu ani nowego frameworka tylko ze względu na modę. Uzasadnij zmiany architektoniczne w ADR.

### Wspólna walidacja rachunków bankowych

We wszystkich modułach VATowca obowiązuje jeden wspólny mechanizm walidacji
rachunków bankowych, oparty na bibliotece IbanNet i istniejącym interfejsie
`IBankAccountValidator`.

Dotyczy to w szczególności:

- kartoteki kontrahentów,
- importu wyciągów bankowych,
- generowania przelewów ISO 20022,
- wszystkich przyszłych funkcjonalności obsługujących numery rachunków.

Nie wolno tworzyć niezależnych implementacji walidacji IBAN/NRB ani powielać
algorytmów sum kontrolnych w poszczególnych modułach.

Nowe funkcjonalności muszą korzystać z istniejącego wspólnego walidatora.
Jeżeli potrzebna będzie nowa reguła, należy rozszerzyć wspólny mechanizm i jego
testy, zachowując rozdzielenie walidacji numeru od potwierdzania właściciela
rachunku.

ADR-026 rozszerza wspólną normalizację o jawną listę myślników wizualnych
(U+002D, U+2010–2015, U+2212, U+FE63, U+FF0D). Usuwaj je z zapisów liczbowych
lub rozpoczynających się jak IBAN (dwie litery i dwie cyfry); w innych formatach,
np. LOCAL-123, mogą być częścią identyfikatora. Nie usuwaj nieznanych znaków.
Polityka bank-iban-v3 wiąże nowe reguły z WarningHash. Wspólny wynik walidatora
udostępnia numer rozliczeniowy wyłącznie dla poprawnego NRB/PL IBAN.

Nazwa banku i BIC pochodzą z osobnego serwerowego adaptera IBankDirectory/EWIB,
nie z IbanNet. Tylko jawne Sprawdź numer odpytuje HTTPS ewib.nbp.pl dla ośmiu cyfr
numeru rozliczeniowego; bez pełnego rachunku, zapytań przy zapisie/odczycie lub w tle.
Nie dobieraj BIC innych numerów/jednostek, nie wybieraj pierwszego z wielu.
Avalonia normalizuje numer w polu i przenosi propozycje tylko do szkicu: automatycznie
jednoznaczne puste pola, zastąpienie niepustych wyłącznie po jawnej decyzji.
Brak BIC/wyniku lub awaria nie blokują ręcznego zapisu. EWIB nie potwierdza
istnienia rachunku ani jego właściciela. Automatyczne testy używają atrap HTTP;
próba publicznego EWIB jest osobna i nie zmienia trybów MF/GUS/VIES TEST/Mock.

## 3. Nienaruszalne zasady danych

1. Każda operacja dotyczy uprawnionej firmy (`CompanyId`). **Sprawdzenie uprawnienia następuje na serwerze**.
2. Zatwierdzony dokument jest audytowalny. Nie wolno ukradkiem edytować lub fizycznie usuwać księgowych zapisów historycznych.
3. Zatwierdzenie dokumentu i jego skutków ewidencyjnych jest **atomowe** — jedna transakcja.
4. Równoczesne zapisy muszą wykrywać konflikty (`rowversion`/ETag) i zapobiegać podwójnej numeracji.
5. Żądania zewnętrzne, importy i zadania kolejkowane muszą być idempotentne.
6. Kwoty pieniężne i stawki liczymy na `decimal`, **nie** `float`/`double`.
7. Reguły podatkowe i schematy JPK mają wersje oraz okres obowiązywania. Nie zastępuj reguł historycznych bieżącymi.
8. Nie umieszczaj sekretów ani autentycznych danych podatników w repozytorium, testach lub logach.
9. Historia księgowa zawiera trwały identyfikator aktora (z przestrzenią identyfikatorów źródłowej instalacji) i historyczny opis użytkownika w bazie firmy. Usunięcie konta centralnego nie może usuwać ani anonimizować tej identyfikacji; nie stosuj FK ani kaskad do centralnej kartoteki użytkowników. Identyfikatorów aktorów nie używa się ponownie.
10. Dokument, skutki ewidencyjne, audyt księgowy i Outbox zapisuje się atomowo w bazie firmy. Nie uzależniaj księgowania od transakcji obejmującej bazę systemową lub inną instancję SQL.
11. Każda firma ma niezależny backup/restore bazy, załączników i wymaganych kluczy. Odtworzenie firmy nie odtwarza centralnych kont ani uprawnień.
12. Docelowe utworzenie firmy w aplikacji automatycznie tworzy bazę, wykonuje migracje i inicjalizuje profil. Osobna tożsamość techniczna obsługuje provisioning; zwykłe API nie otrzymuje uprawnień CREATE DATABASE/DDL. Proces ma trwały klucz idempotencji, checkpointy i bezpieczne wznowienie, bez usuwania zastanej bazy.

## 4. Podatki i integracje

- **Nie zgaduj** stawek, dat, pól XML, interpretacji przepisów ani formatu API MF.
- Każda reguła podatkowa wymaga wskazania okresu, źródła, przykładów wzorcowych i testów.
- Zewnętrzne XSD/OpenAPI są wersjonowane. Testuj XML względem odpowiedniej oficjalnej wersji.
- Oddziel wyliczenie JPK/deklaracji od podpisu, wysyłki i pobierania UPO.
- Rozróżniaj status dokumentu księgowego, faktury w KSeF i operacji wysyłki.
- Nie uznawaj „XSD valid” za wystarczający dowód poprawności podatkowej.

## 5. Rozszerzenia

- Raporty, XML/JSON/CSV, formularze i mapowania powinny działać przez **deklaratywny, wersjonowany kontrakt**.
- Wtyczki nie mogą otrzymać dostępu do całego SQL Server ani do sekretów innych dodatków.
- Publiczne komendy i API posiadają autoryzację, walidację, audyt i wersjonowanie.
- Kod dodatków uruchamiaj w izolowanym procesie lub podobnie skutecznej izolacji; `AssemblyLoadContext` sam w sobie nie jest sandboxem bezpieczeństwa.
- Minimalny demonstrator: eksport XML dla formularza użytkownika **bez zmiany kodu rdzenia**.

## 6. Zasady każdej zmiany

Przed kodowaniem:

1. Przeczytaj odpowiadające zadaniu fragmenty `SPECYFIKACJA.md`.
2. Wskaż ID wymagania (np. `DOC-001`) i sprawdź, czy nie ma kwestii otwartej `ADR-*`.
3. Napisz krótką propozycję rozwiązania i kryteria akceptacji, gdy zmiana wpływa na księgowanie, prawo podatkowe lub model danych.

Podczas kodowania:

4. Twórz możliwie małe, kompilowalne zmiany.
5. Pisz testy jednostkowe, integracyjne i testy konfliktów/uprawnień odpowiednio do zmiany.
6. Wprowadzając migrację SQL, opisz sposób wdrożenia i ryzyko niezgodności danych.
   Generowanie migracji i SQL offline jest odrębne od ich wykonania. Nie uruchamiaj migracji wszystkich baz przy starcie API. Provisioning wykonuje je jawnie dla nowo tworzonej firmy; aktualizacje istniejących firm są kontrolowane osobno.
7. W API zwracaj konkretne błędy biznesowe zamiast surowych wyjątków SQL.

Po kodowaniu:

8. Uruchom build i odpowiednie testy; opisz dokładnie co zostało sprawdzone oraz co nie było możliwe do sprawdzenia.
9. Uaktualnij specyfikację, jeśli powstało nowe ustalenie, oraz dopisz ADR dla decyzji architektonicznej.
10. Nie ogłaszaj gotowości produkcyjnej bez zgodności z definicją wersji z `SPECYFIKACJA.md`.

## 7. Proponowany szkielet repozytorium (do zatwierdzenia)

```text
/
  SPECYFIKACJA.md
  AGENTS.md
  README.md
  LICENSE
  docs/
    adr/
    tax-rules/
    integrations/
    user-guide/
  src/
    Server/
    Desktop/
    Modules/
      Core/
      Documents/
      Vat/
      IncomeTax/
      Reporting/
      Ksef/
      Payments/
      Extensions/
    SharedContracts/
  tests/
    Unit/
    Integration/
    Contract/
    EndToEnd/
  examples/
    extensions/
  tools/
```

Nie twórz wszystkich katalogów pustymi szablonami bez potrzeby; szkielet jest mapą docelową.

## 8. Pierwsze zadanie dla Codexa

Poniżej zapis wcześniejszych etapów i zgody na lokalne operacje testowe.
Aktualny zakres implementacyjny i lista trzech dozwolonych baz z początku pliku mają pierwszeństwo.
Po zatwierdzeniu szczegółów nowego modelu przyszła implementacja może odtworzyć
wyłącznie demonstracyjne bazy VATowca: fikcyjne dane nie wymagają zachowania,
nie projektujemy ich migracji, map GUID ani zachowania przykładowych kontrahentów.
Nie rozszerza to zgody na bazy systemowe SQL ani bazy innych aplikacji.

> Przygotuj propozycję i minimalny szkielet repozytorium według M0 w `SPECYFIKACJA.md`: solution .NET 10, ASP.NET Core Web API, Avalonia Desktop, projekt Domain/Application/Infrastructure, integracja z SQL Server, testy xUnit i instrukcja uruchomienia. Zacznij od dokumentu ADR opisującego podział projektów i granice modułów. Nie implementuj podatków, KSeF ani schematów JPK bez osobnych wymagań i testów. Zapewnij możliwość zalogowania dwóch użytkowników oraz podstawowy model dwóch firm z izolacją danych.

Aktualny etap po zaakceptowaniu ADR-014: wdrożenie lokalne i testy SQL. Użytkownik potwierdził, że wszystkie bazy projektu są testowe, i zezwolił na ich tworzenie, modyfikowanie i usuwanie na `localhost\SQLEXPRESS`, w tym usunięcie starej `Vatowiec_Dev`, bez ponawiania pytań. Przed usunięciem należy potwierdzić dokładną nazwę i przeznaczenie bazy; nie wystarcza sam prefiks nazwy. Nie wolno zmieniać baz systemowych SQL ani baz innych aplikacji. Narzędzie administracyjne korzysta z jawnej listy nazw i znacznika własności (dla starej Vatowiec_Dev: schemat i znana migracja). Aktualny runner SQL sprawdza trzy dozwolone bazy demonstracyjne i czyści ich fikcyjne fixture, wykonuje testy i ponownie inicjalizuje konta oraz dane fikcyjne; nie tworzy baz *_Test. Aktualny runner czyści tylko fikcyjne fixture DML w istniejących bazach; nie wykonuje DROP/CREATE. Przed testami zatrzymaj API i desktop. Pominiętych testów nie raportujemy jako zaliczonych. Zgoda nie obejmuje baz produkcyjnych ani zdalnych instancji i nie oznacza migracji przy starcie API.

## 9. Licencje i cudze materiały

Oprogramowanie źródłowe planujemy publikować na licencji **MPL-2.0**, decyzja pozostaje do zatwierdzenia. Dopuszcza się opcjonalne bezpłatne komponenty zamknięte tylko wtedy, gdy ich licencje zezwalają na przyjęty sposób użycia/dystrybucji. **Nie kopiuj** kodu, ikon, tekstów ani innych chronionych zasobów oryginalnego VATowca. Historyczna instrukcja jest wyłącznie materiałem do opisu funkcji i potrzeb użytkownika.
