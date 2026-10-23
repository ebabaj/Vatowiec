# Lokalne bazy database-per-company — ADR-018–024

Wdrożenie używa localhost\SQLEXPRESS, SQL Server 2025 Express i Windows
Authentication. Wszystkie dane demonstracyjne są fikcyjne i odtwarzalne.
Decyzje: [ADR-014](adr/ADR-014-database-per-company.md),
[ADR-018](adr/ADR-018-docelowy-model-kontrahentow.md),
[ADR-019](adr/ADR-019-implementacja-modelu-kontrahentow.md).

## Bazy i tabele

Vatowiec_System zawiera 14 tabel:

- core.Companies, core.CompanyDatabases;
- security.Users, security.Roles, security.UserRoles, security.UserClaims,
  security.UserLogins, security.UserTokens, security.RoleClaims;
- security.CompanyAccess, security.UserSessions, security.Installation;
- ref.TaxIdentifierSchemes (wersjonowane reguły, bez danych kontrahentów);
- dbo.__EFMigrationsHistory.

Każda z Vatowiec_Company_A_Dev i Vatowiec_Company_B_Dev zawiera 14 tabel:

- core.CompanyProfile;
- core.Contractors;
- core.ContractorAddresses;
- core.ContractorBankAccounts;
- core.ContractorContacts;
- core.ContractorCreateOperations;
- ref.AddressTypes;
- audit.ContractorChanges;
- audit.ExternalVerifications (niezmienne wejście/wynik, środowisko i trwały aktor);
- security.DataPortConfiguration (trzy kolumny: singleton, zaszyfrowany klucz, rowversion);
- ref.Currencies (149 pozycji, lokalne ustawienia pobierania A/B/C);
- core.ExchangeRates (kursy decimal, publikacja, numer tabeli i pochodzenie);
- audit.CurrencyOperations (niezmienna historia ustawień, importów i decyzji);
- dbo.__EFMigrationsHistory.

Contractors.Id i klucze szczegółów są int IDENTITY, lokalne dla fizycznej bazy.
Nie ma CompanyId w kartotece i audycie. CompanyProfile zachowuje CompanyId
jako tożsamość bazy, a centralny katalog, dostęp i ścieżki API zachowują GUID.
Resolver sprawdza uprawnienia, rzeczywisty profil i historię migracji przed
udostępnieniem danych; brak zgodności oznacza odmowę, bez domyślnej bazy.
AddressTypes jest otwartym lokalnym słownikiem dwóch początkowych rodzajów.
ADR-021 wdraża TaxIdentifierSchemes w System oraz ExternalVerifications w firmach.
Słownik centralny nie zawiera kartotek ani wyników sprawdzeń.

## Migracje i odtworzenie demonstratora

Historia SystemDbContext pozostaje zestawem:

- 20261021102909_InitialSystem;
- 20261021114324_IdentityAndCompanyAccess;
- 20261022055958_InstallationIdentity;
- 20261022161054_WarningPermission;
- 20261022175431_TaxIdentifierRules;
- 20261022194948_CompanyIntegrationPermission (firmowe prawo 32, maska do 63);
- 20261022205924_CurrencyPermissions (prawa 64/128/256, maska do 511).

CompanyDbContext ma migrację inicjalną 20261022143310_CompanyModelV2 i techniczną 20261022161050_ContractorDetailAudit. Ta druga rozszerza istniejący audyt i operacje tworzenia; nie dodaje tabel biznesowych.
20261022175433_ExternalVerifications dodaje historię z triggerem blokującym UPDATE/DELETE.
20261022194946_DataPortConfiguration dodaje konfigurację DataPort bez zmiany kartoteki
i historii. Sekret jest chroniony niezależnym trwałym key ring poza SQL i repozytorium,
DPAPI konta API. Po przeniesieniu bez dostępnej ochrony wpisz klucz ponownie.
[Wdrożenie, migracje, kopie, ograniczenie runnera i konfiguracja](dataport.md).
20261022205922_CurrenciesAndNbpRates dodaje trzy lokalne tabele, słownik i
trigger audytu. 20261022211121_OptionalNbpTradingDate doprecyzowuje opcjonalność
daty notowania C w CHECK. [Struktura i obsługa](currencies-and-nbp.md).
Wszystkie trzy bazy zaktualizowano jawnie po zweryfikowanych backupach, bez ich
odtwarzania. Down usuwa historię/reguły i nie jest bezpiecznym wycofaniem z danymi.
Poprzednie migracje firmy i snapshot (.cs.archive) są w LegacyCompanyMigrations, wyłączone
z kompilacji. Archiwum Vatowiec_Dev pozostaje w LegacyMigrations.
Nie przenosimy danych, nie mapujemy GUID na int i nie uruchamiamy nowej migracji
inicjalnej na starym schemacie. API nie migruje baz przy starcie.

Historyczny etap ADR-019 odtworzył trzy wskazane bazy. Przed jego DROP sprawdzono
maszynę i nazwę instancji, wersję Express 17, Windows Authentication,
dokładną allowlistę nazw, znacznik Vatowiec.LocalTestPurpose o wartości
„VATowiec M0 fictional company demonstration v1”, znane tabele/historie oraz
cechy danych fikcyjnych. Niepotwierdzony cel blokuje zmianę. Ta kontrola
korzysta z deklaracji użytkownika o demonstracyjnym charakterze danych;
nie jest narzędziem automatycznego rozpoznawania autentycznych podatników.
Nie wolno oznaczać w ten sposób baz zawierających dane rzeczywiste.

W etapie ADR-020 ponownie zweryfikowano cele i wykonano tylko jawne migracje
techniczne, po COPY_ONLY/CHECKSUM i VERIFYONLY; nie odtwarzano baz ani tabel
biznesowych.

Operacje administracyjne są jawne:

```powershell
dotnet build Vatowiec.slnx --no-restore
dotnet run --project tools/database/Vatowiec.DatabaseTools --no-build -- inspect
dotnet run --project tools/database/Vatowiec.DatabaseTools --no-build -- verify
```

initialize inicjalizuje aktualny pusty demonstrator i brakujące fikcyjne rekordy;
seed-users odczytuje hasła z user-secrets API lub środowiska. Nie wpisuj haseł
w polecenia przechowywane w repozytorium. Szczegóły: [konta](authentication.md).
Polecenie migrate-contractors aktualizuje model ADR-019 o ADR-020–023: audyt
szczegółów, prawa, reguły, weryfikacje, konfigurację DataPort oraz waluty i kursy.
Nie konwertuje dawnego schematu GUID na int:
stara historia jest odrzucana. Czyszczenie danych testowych opisane niżej jest przeznaczone
wyłącznie dla jawnie dozwolonych baz demonstracyjnych, nigdy produkcyjnych.

## Testy

Zatrzymaj API i desktop. Polecenie test **usuwa bieżące fikcyjne dane i sesje**
w trzech wskazanych istniejących bazach, uruchamia pełny zestaw,
a następnie inicjalizuje ponownie demonstrator i konta:

```powershell
dotnet run --project tools/database/Vatowiec.DatabaseTools --no-build -- test
```

Nie wykonuje DROP/CREATE ani migracji przy starcie API. Wymaga wcześniej jawnie wykonanych migracji migrate-contractors. Nie tworzy dodatkowych *_Test i nie modyfikuje baz innych aplikacji. Kontrola
wszystkich celów poprzedza wyczyszczenie fikcyjnych danych fixture przez DML. Niepowodzenie kontroli przerywa operację.
Testy persystencji używają rollbacku, testy API sprzątają własne fixture.
Runner sprawdza sprzątanie i niezależne historie. Jeżeli test nie posprząta
fixture, nie należy ręcznie usuwać niezweryfikowanych danych; trzeba zbadać błąd.

Runner ustawia VATOWIEC_DATABASE_PER_COMPANY_SQL=1 i dodatkową flagę
VATOWIEC_DEMO_RESET_AUTHORIZED=1 dopiero po przygotowaniu pustych fixture w istniejących bazach. Zmienne
ConnectionStrings__VatowiecSystemTest oraz VatowiecCompanyATest/BTest
wskazują odpowiednio trzy bazy demonstracyjne; nazwy zmiennych zachowano
technicznie, nie oznaczają tworzenia baz o sufiksie _Test. Nie ustawiaj tych
flag na używanym demonstratorze bez jawnego przygotowania pustych fixture.
Bez runnera testy SQL są pomijane, nie zaliczane.

Wynik wcześniejszego etapu ADR-021: **180/180**, bez pominięć (76 SQL/API, 104 bez SQL). Testy sprawdzają
również wszystkie kolumny/typy adresów i rachunków, klucze i zaufane FK,
trigger hierarchii, domyślność PLN/EUR/NULL, niezależne historie migracji,
odmowę obcego profilu, rowversion, atomowy audyt oraz trwałe ponowienia.
Raporty TRX znajdują się w TestResults, poza repozytorium.

## Korekta klucza waluty ADR-024

`20261023041721_CurrencyIsoCodePrimaryKey` odtwarza wyłącznie trzy tabele
modułu walutowego. Currencies ma Code char(3) jako jedyny PK, bez Id;
ExchangeRates.CurrencyCode char(3) jest FK do Code, a własne bigint Id kursu
pozostaje. Indeksy, konfiguracja i kontrakty używają kodów. System ma 7 migracji,
firmy po 7 i nadal po 14 tabel. Pozostałe tabele i sekrety nie są resetowane.
[Liczniki, porównanie kursów i próby](currencies-and-nbp.md#korekta-klucza-waluty-adr-024-2026-10-23).

Po migracji użytkownik odwołał wcześniejsze ograniczenie czyszczenia innych
danych demonstracyjnych i zatwierdził pełny reset fixture. Pełny zestaw wykonuje
zatem dotychczasowy `test`, na tych samych trzech bazach po kontroli i kopiach.
Nie utworzono dodatkowych *_Test. Zastany szyfrogram DataPort nadal zachowujemy
bez odszyfrowania. Reset testowy odtwarza fikcyjne rekordy i konta, nie wykonuje
DROP/CREATE baz. Nie dotyczy innych aplikacji. Samo DDL migracji było ograniczone
do modułu walutowego; szersze czyszczenie DML jest osobną operacją testową.

Wynik ADR-024: **244/244 bez pominięć (94 SQL/API, 150 bez SQL)**, build bez
błędów/ostrzeżeń. Po testach przywrócono demonstracyjne konta i fikcyjne rekordy
oraz zaszyfrowaną konfigurację DataPort. A ma 149 walut i 415 ponownie pobranych
kursów, B 149 walut i 0 kursów. Nie utworzono ani nie usunięto żadnej bazy.

## Konfiguracja, backup i granice

ConnectionStrings:VatowiecSystem wskazuje centralną bazę; CompanyConnections:
LocalWindows zawiera serwerowy profil połączenia. Lokalizacje A/B pochodzą
z katalogu. Dane dostępowe do SQL nie są zwracane klientowi Avalonia.
Poza Development wymagane jest prawidłowe zaufanie TLS; nie rozszerzamy
lokalnego TrustServerCertificate na inne serwery.

Każda baza zachowuje własny backup i historię migracji. Interfejs samodzielnego
backup/restore, provisioning firm i przenoszenie na inną instancję pozostają
kolejnymi zadaniami ADR-014. Nie ma automatycznych migracji przy starcie API.
Czyszczenie fixture usuwa wyłącznie fikcyjne rekordy demonstratora; nie jest planem migracji
ani procedurą odtworzenia produkcji. Historyczne etapy, wyniki i kopie przed
wcześniejszymi migracjami opisują ADR-014–017.

## Weryfikacja ADR-022

210/210 bez pominięć (83 SQL/API, 127 bez SQL). W tamtym etapie System miał sześć migracji
i 14 tabel, A/B po cztery migracje i 11 tabel. Zmiany tego etapu wdrożono jawnie po trzech
backupach COPY_ONLY/CHECKSUM/VERIFYONLY. Właściciel potwierdził demonstracyjne
przeznaczenie zastanych rekordów przed czyszczeniem; odtworzono fikcyjne fixture
i konta, bez DROP/CREATE baz. Nie zapisano rzeczywistego klucza DataPort.
Ówczesny runner odmawiał czyszczenia zastanej konfiguracji z kluczem.
Od ADR-023 pełny `test` wykonuje zweryfikowane kopie trzech baz i zabezpiecza
szyfrogram w pamięci, po czym przywraca go po inicjalizacji do zgodnego profilu.
Nie odczytuje sekretu. Samodzielne `prepare-tests` nadal odmawia czyszczenia
zastanego klucza. [Szczegóły](dataport.md).

## Weryfikacja ADR-023

242/242 bez pominięć: 93 SQL/API i 149 bez SQL. System ma siedem migracji i
14 tabel, firmy po sześć migracji i 14 tabel. Wdrożenie jawne po preflight i
zweryfikowanych kopiach, bez DROP/CREATE. Testy sprawdzają także fizyczne FK,
unikalność i CHECK kursów, niezmienność audytu, transakcje, CAS, ponowienia,
izolację A/B i prawa. Automatyczne wywołania NBP są atrapami HTTP.
Zastany szyfrogram DataPort zachowano; nie ma go w repozytorium ani logach.
