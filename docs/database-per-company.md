# Lokalne wdrożenie database-per-company

**Bieżące ustalenie projektowe — ADR-018, 2026-10-22:** database-per-company
pozostaje bez zmian. Docelowo nie powielamy CompanyId w tabelach danych jednej
firmy (zwłaszcza Contractors); zachowujemy tożsamość bazy i CompanyId katalogu
oraz autoryzacji. Preferowany klucz kontrahenta to int IDENTITY. Szczegóły:
[docelowy model](adr/ADR-018-docelowy-model-kontrahentow.md).

Wszystkie dotychczasowe dane projektu są fikcyjną demonstracją i nie wymagają
zachowania. Po zatwierdzeniu szczegółów nowego modelu można odtworzyć od podstaw
wyłącznie demonstracyjne bazy VATowca, po potwierdzeniu nazwy i przeznaczenia.
Nie przygotowujemy migracji danych, map dawnych GUID ani zgodności ze starym
schematem demonstracyjnym. Nie dotyczy to innych aplikacji ani baz systemowych SQL.
Obecny etap jest tylko dokumentacyjny: nie wykonujemy odtworzenia, migracji
ani jakichkolwiek zmian w bazach. Poniżej pozostaje opis wdrożonego demonstratora
i wcześniejszych czynności; nie jest to plan przebudowy jego danych.

Aktualizacja 2026-10-22: dodano security.Installation w bazie systemowej oraz
audit.ContractorChanges i indeks listy w każdej bazie firmy. Jawne migracje wykonano
po trzech kopiach COPY_ONLY/CHECKSUM i VERIFYONLY. Instrukcja polecenia
`migrate-contractors`, schemat i ryzyko rollbacku: [kartoteka](contractors.md),
[ADR-017](adr/ADR-017-kartoteka-kontrahentow-i-audyt.md).
Pełny zestaw obejmuje obecnie 117 testów, w tym 46 SQL/API. Poniższy opis zachowuje
również historię wcześniejszych etapów.

Status historyczny 2026-10-21: wdrożono trzy bazy demonstracyjne, wykonano migracje EF Core
i zainicjalizowano dwie fikcyjne firmy. Następnie dodano Identity i autoryzowany
resolver; pełny zestaw obejmuje 70 testów (32 SQL/API).
Decyzje: [ADR-014](adr/ADR-014-database-per-company.md),
[ADR-015](adr/ADR-015-identity-i-autoryzowany-wybor-bazy.md).
Logowanie dwóch użytkowników: [instrukcja](authentication.md).

## Zgoda i zakres

Użytkownik potwierdził, że wszystkie bazy projektu są obecnie testowe, i zezwolił
na ich usuwanie, ponowne tworzenie i modyfikowanie na localhost\SQLEXPRESS bez
ponawiania pytań. Przed usunięciem sprawdzamy dokładną nazwę i przeznaczenie.
Zgoda nie obejmuje baz innych aplikacji, baz systemowych SQL ani zdalnych instancji.

Dotychczasowa Vatowiec_Dev zawierała wyłącznie core.Companies, core.Contractors,
historię 20261021092204_InitialCompaniesAndContractors i zero rekordów domenowych.
Po sprawdzeniu została usunięta. Oryginalne pliki migracji pozostają w
Persistence/LegacyMigrations, poza kompilacją. Nie kopiowano danych do nowych baz.

## Pozostawione bazy i dane

Instancja: localhost\SQLEXPRESS, SQL Server 2025 Express 17.0.1000.7.
Połączenie: Windows Authentication (NTLM), Encrypt=True.
TrustServerCertificate=True jest wyjątkiem wyłącznie dla lokalnego Development.
Użyto istniejącego konta Windows operatora; nie tworzono loginów SQL ani haseł.

| Baza | Tabele | Migracja |
|---|---|---|
| Vatowiec_System | core.Companies, core.CompanyDatabases, dbo.__EFMigrationsHistory oraz dziewięć tabel security opisanych poniżej | InitialSystem i IdentityAndCompanyAccess |
| Vatowiec_Company_A_Dev | core.CompanyProfile, core.Contractors, dbo.__EFMigrationsHistory | 20261021102912_InitialCompany |
| Vatowiec_Company_B_Dev | core.CompanyProfile, core.Contractors, dbo.__EFMigrationsHistory | 20261021102912_InitialCompany |

Tabele security: Users, Roles, UserRoles, UserClaims, UserLogins, UserTokens,
RoleClaims, CompanyAccess i UserSessions. Druga migracja systemowa to
20261021114324_IdentityAndCompanyAccess. Nie zmienia schematów firmowych.

Katalog zawiera dokładnie dwie firmy i dwie lokalizacje. Nie zawiera dokumentów
księgowych ani kontrahentów. Każda firma ma własną historię migracji i singleton
profilu z lokalnym FK kontrahentów do CompanyProfile.CompanyId, bez relacji SQL
między bazami.

| Firma fikcyjna | CompanyId | Baza |
|---|---|---|
| Firma demonstracyjna A — dane fikcyjne | aaaaaaaa-0000-4000-8000-000000000001 | Vatowiec_Company_A_Dev |
| Firma demonstracyjna B — dane fikcyjne | bbbbbbbb-0000-4000-8000-000000000002 | Vatowiec_Company_B_Dev |

Każda baza firmowa ma jeden profil i jednego fikcyjnego kontrahenta. NIP/REGON
i identyfikator podatkowy kontrahenta są puste. Kontrahenci celowo mają ten sam
techniczny Id cccccccc-0000-4000-8000-000000000001 i kod DEMO w obu bazach,
ale inne nazwy i CompanyId. Nie są wspólną kartoteką.

## Narzędzie administracyjne

Narzędzie tools/database/Vatowiec.DatabaseTools jest osobnym programem konsolowym
.NET, zależnym od infrastruktury. Nie jest wywoływane przez API. Z głównego katalogu:

```powershell
dotnet restore Vatowiec.slnx
dotnet build Vatowiec.slnx --no-restore
dotnet run --project tools/database/Vatowiec.DatabaseTools --no-build -- inspect
dotnet run --project tools/database/Vatowiec.DatabaseTools --no-build -- initialize
dotnet run --project tools/database/Vatowiec.DatabaseTools --no-build -- verify
```

Polecenia mają jawny zakres:

- inspect: sprawdza lokalny serwer i wyświetla listę baz; jeśli istnieje Vatowiec_Dev,
  sprawdza jej dokładny schemat i znaną migrację.
- initialize: usuwa tylko potwierdzoną starą Vatowiec_Dev, tworzy trzy bazy
  demonstracyjne, jawnie wykonuje EF MigrateAsync i seed przez encje domenowe.
  Ponowienie zachowuje profile, dane i wpisy migracji; nie tworzy duplikatów.
- verify: sprawdza własność, dokładne zestawy tabel, historie EF, brak oczekujących
  migracji, zgodność CompanyId, katalog i dane firm.
- prepare-tests / cleanup-tests: tworzy z migracjami lub usuwa wyłącznie trzy
  oznaczone bazy *_Test.
- test: przygotowuje bazy *_Test, uruchamia wszystkie testy, zapisuje TRX, weryfikuje
  rollbacky i usuwa bazy *_Test w finally, także gdy testy zgłoszą błąd.
- backup-system / migrate-system: kopia COPY_ONLY z CHECKSUM i VERIFYONLY oraz jawna
  migracja wyłącznie bazy systemowej. Nie są wywoływane przy starcie API.
- seed-users / verify-users: konta z lokalnej konfiguracji i próba dwóch sesji
  na działającym HTTPS API; szczegóły w instrukcji logowania.

Narzędzie sprawdza lokalną nazwę komputera, instancję SQLEXPRESS, wersję 17,
edycję Express i Windows Authentication. Nazwy baz pochodzą z zamkniętej listy,
a nie z argumentów użytkownika. Nowe bazy otrzymują rozszerzoną właściwość
Vatowiec.LocalTestPurpose: osobno demonstrator i jednorazowe fixture integracyjne.
Istniejąca baza bez zgodnego znacznika nie jest przejmowana, migrowana ani usuwana.
Wyjątkiem jest wskazana przez użytkownika Vatowiec_Dev, rozpoznawana po dokładnym
schemacie i archiwalnej migracji. Sam prefiks Vatowiec nie upoważnia do usunięcia.

Kod używa Windows Authentication; poświadczenia nie są przekazywane klientowi
Avalonia ani zapisywane w repozytorium. Administrator wykonujący narzędzie musi
mieć istniejące uprawnienia do tworzenia baz i migracji. Nie nadawano nowych praw
zwykłemu kontu aplikacji. Przyszły provisioning użyje osobnej tożsamości technicznej.

## Konfiguracja API i granice obecnego etapu

appsettings.Development.json wskazuje lokalną Vatowiec_System z Windows
Authentication. Można nadpisać ConnectionStrings__VatowiecSystem.
Bazowy appsettings poza Development nie ma domyślnego połączenia produkcyjnego.
Stary klucz ConnectionStrings__Vatowiec jest ignorowany.

CompanyDatabases.ConnectionProfileKey = LocalWindows jest referencją do
serwerowego sposobu połączenia. Resolver sprawdza aktualne CompanyAccess, dopasowuje
serwerowy profil połączenia i weryfikuje fizyczny profil i schemat przed odczytem.
Sam wpis w katalogu nie udostępnia firmy. Bezpośrednio uzyskiwany CompanyDbContext
nadal wymaga jawnego CompanyDatabaseSelection zgodnego z ICompanyContext.
Nie ma domyślnego wyboru firmy i nie ma migracji przy starcie API.

To narzędzie lokalnego wdrożenia, nie pełny provisioning firmy z interfejsu.
Identity, role, sesje i autoryzowany resolver są wdrożone zgodnie z ADR-015.
Logowanie Avalonia, trwała operacja provisioningu z checkpointami, audyt księgowy
aktorów i backup/restore z aplikacji pozostają kolejnymi zadaniami. M0 nie jest ukończone.

## Przegląd SQL offline

Fabryki projektowe nadal nie mają connection stringów i nie odczytują konfiguracji
środowiska. Poniższe polecenia jedynie generują SQL:

```powershell
dotnet ef migrations script 0 --idempotent --context SystemDbContext --project src/Modules/Core/Infrastructure --no-build --output src/Modules/Core/Infrastructure/obj/system.initial.sql
dotnet ef migrations script 0 --idempotent --context CompanyDbContext --project src/Modules/Core/Infrastructure --no-build --output src/Modules/Core/Infrastructure/obj/company.initial.sql
```

Oba skrypty przejrzano przed wdrożeniem. System tworzy tylko katalog i lokalizacje,
Company tylko profil i kontrahentów; każdy dodaje własną historię EF. Nie zawierają
DROP ani kopii księgowości innych firm. Migracje wykonano jawnie przez narzędzie,
osobno dla każdej bazy. Nie stosuje się tych migracji do starego schematu.

## Testy i wyniki

Pełny przebieg z lokalnym SQL:

```powershell
dotnet build Vatowiec.slnx --no-restore
dotnet run --project tools/database/Vatowiec.DatabaseTools --no-build -- test
dotnet format Vatowiec.slnx --verify-no-changes --no-restore
```

Runner przygotowuje puste Vatowiec_System_Test, Vatowiec_Company_A_Test i
Vatowiec_Company_B_Test, wykonuje ich migracje i włącza
VATOWIEC_DATABASE_PER_COMPANY_SQL=1 wyłącznie w procesie testowym.
Wyniki trafiają do ignorowanego TestResults/database-per-company-*.trx.
Runner odrzuca przebieg bez testów lub z pominięciami, nawet przy kodzie wyjścia 0.
Po sprawdzeniu rollbacków usuwa tylko te trzy fixture. Bazy demonstracyjne pozostają.

Przy ręcznym uruchamianiu testów po prepare-tests wymagane są zmienne:

| Zmienna | Wymagany katalog |
|---|---|
| ConnectionStrings__VatowiecSystemTest | Vatowiec_System_Test |
| ConnectionStrings__VatowiecCompanyATest | Vatowiec_Company_A_Test |
| ConnectionStrings__VatowiecCompanyBTest | Vatowiec_Company_B_Test |

Każde połączenie wskazuje localhost\SQLEXPRESS, Windows Authentication,
Encrypt=True; lokalne TrustServerCertificate=True jest dozwolone.
Nie wskazuj baz demonstracyjnych w tych zmiennych.
Stara flaga VATOWIEC_SQL_INTEGRATION nie włącza testów.

Zwykłe dotnet test bez flagi SQL wykonuje 38 testów i pomija 32 testy SQL/API.
Można wymusić ten tryb ustawiając VATOWIEC_DATABASE_PER_COMPANY_SQL=0.
To odrębny wynik od pełnego przebiegu opisanego poniżej.

Weryfikacja wcześniejszego etapu trzech baz przed Identity (2026-10-21):

- Build wszystkich ośmiu projektów: 0 błędów, 0 ostrzeżeń.
- Wszystkie 50 testów zaliczone: 38 bez SQL i 12 na rzeczywistym SQL Server,
  bez pominięć i błędów.
- SQL: fizyczna izolacja dwóch baz, ten sam Id/kod kontrahenta w różnych firmach,
  odmowa odczytu/zapisu przy złym/brakującym kontekście, singleton profilu,
  FK także przy surowym SQL, unikalność kodu i lokalizacji bazy, rowversion,
  atomowość SaveChanges, daty i rollbacky.
- Niezależność migracji: usunięcie wpisu historii w A w wycofywanej transakcji
  nie zmieniło historii B ani systemowej; po testach każda historia wróciła do
  jednego wpisu i wszystkie fixture były puste.
- Ponowiono initialize: nadal dwie firmy, dwa wskazania baz i po jednym profilu
  oraz kontrahencie w firmach, bez powtórzenia migracji.
- Końcowa inspekcja: tylko cztery bazy systemowe SQL i trzy bazy demonstracyjne;
  brak Vatowiec_Dev i tymczasowych *_Test.
- Formatowanie i git diff --check poprawne. Nie wykonano commit ani push.

Test rowversion używa dwóch kontekstów. W etapie Identity osobno sprawdzono
równoczesne żądania HTTP dwóch zalogowanych użytkowników firmy A, odmowy dostępu,
CSRF, wygasanie i unieważnianie sesji oraz rzeczywisty profil bazy. Wyniki etapu
Identity i ręczna próba działającego HTTPS API są w docs/authentication.md.
Backup/restore z aplikacji, provisioning i audyt księgowy pozostają poza tym etapem.
