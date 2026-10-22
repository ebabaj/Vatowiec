# Lokalne bazy database-per-company — ADR-018/019/020

Wdrożenie używa localhost\SQLEXPRESS, SQL Server 2025 Express i Windows
Authentication. Wszystkie dane demonstracyjne są fikcyjne i odtwarzalne.
Decyzje: [ADR-014](adr/ADR-014-database-per-company.md),
[ADR-018](adr/ADR-018-docelowy-model-kontrahentow.md),
[ADR-019](adr/ADR-019-implementacja-modelu-kontrahentow.md).

## Bazy i tabele

Vatowiec_System zawiera 13 tabel:

- core.Companies, core.CompanyDatabases;
- security.Users, security.Roles, security.UserRoles, security.UserClaims,
  security.UserLogins, security.UserTokens, security.RoleClaims;
- security.CompanyAccess, security.UserSessions, security.Installation;
- dbo.__EFMigrationsHistory.

Każda z Vatowiec_Company_A_Dev i Vatowiec_Company_B_Dev zawiera 9 tabel:

- core.CompanyProfile;
- core.Contractors;
- core.ContractorAddresses;
- core.ContractorBankAccounts;
- core.ContractorContacts;
- core.ContractorCreateOperations;
- ref.AddressTypes;
- audit.ContractorChanges;
- dbo.__EFMigrationsHistory.

Contractors.Id i klucze szczegółów są int IDENTITY, lokalne dla fizycznej bazy.
Nie ma CompanyId w kartotece i audycie. CompanyProfile zachowuje CompanyId
jako tożsamość bazy, a centralny katalog, dostęp i ścieżki API zachowują GUID.
Resolver sprawdza uprawnienia, rzeczywisty profil i historię migracji przed
udostępnieniem danych; brak zgodności oznacza odmowę, bez domyślnej bazy.
AddressTypes jest otwartym lokalnym słownikiem dwóch początkowych rodzajów.
Nie wdrożono TaxIdentifierSchemes ani ExternalVerifications.

## Migracje i odtworzenie demonstratora

Historia SystemDbContext pozostaje zestawem:

- 20261021102909_InitialSystem;
- 20261021114324_IdentityAndCompanyAccess;
- 20261022055958_InstallationIdentity;
- 20261022161054_WarningPermission.

CompanyDbContext ma migrację inicjalną 20261022143310_CompanyModelV2 i techniczną 20261022161050_ContractorDetailAudit. Ta druga rozszerza istniejący audyt i operacje tworzenia; nie dodaje tabel biznesowych.
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
Polecenie migrate-contractors aktualizuje model ADR-019 o audyt szczegółów i uprawnienie ostrzeżeń; nie konwertuje dawnego schematu GUID na int:
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

Pełny wynik: **142/142**, bez pominięć (67 SQL/API, 75 bez SQL). Testy sprawdzają
również wszystkie kolumny/typy adresów i rachunków, klucze i zaufane FK,
trigger hierarchii, domyślność PLN/EUR/NULL, niezależne historie migracji,
odmowę obcego profilu, rowversion, atomowy audyt oraz trwałe ponowienia.
Raporty TRX znajdują się w TestResults, poza repozytorium.

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
