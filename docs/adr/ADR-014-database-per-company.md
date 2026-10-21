# ADR-014: Database-per-company i rozdzielenie persystencji

Data: 2026-10-21  
Status: architektura zaakceptowana przez użytkownika; realizacja kroków 1–2,
bez tworzenia baz i wykonywania migracji.  
Zastępuje: [ADR-005](ADR-005-izolacja-firm-i-pierwszy-model.md).  
Wymagania: ORG-001, ORG-003–005, ORG-008–011, E02–E04; SPECYFIKACJA §3, §8,
§11–12, M0. Scenariusze T-012, T-014, T-016–019.

## Kontekst

Dotychczasowy CoreDbContext mapował firmy i kontrahentów w jednej bazie,
z filtrami CompanyId i kontrolą zapisu. Użytkownik wymaga osobnej fizycznej
bazy SQL Server dla każdej firmy, niezależnego backupu/restore oraz możliwości
umieszczania baz na różnych instancjach. Sam filtr lub osobny schemat SQL
nie spełnia tego wymagania. Jedna aplikacja ASP.NET Core pozostaje modularnym
monolitem; Desktop nadal korzysta wyłącznie z HTTPS i publicznych DTO.

## Decyzja

| Obszar | Baza i kontekst |
|---|---|
| Katalog firm i lokalizacje baz | Vatowiec_System / SystemDbContext |
| Użytkownicy, role, CompanyAccess, audyt dostępu i operacje administracyjne | Docelowo Vatowiec_System / SystemDbContext |
| Profil firmy, kontrahenci, dokumenty i pozostała księgowość | Jedna baza firmy / CompanyDbContext |
| Audyt księgowy i Outbox związany z księgowaniem | Ta sama baza firmy i transakcja co dokument |

SystemDbContext nie zawiera kontrahentów, dokumentów, ewidencji VAT/KPiR/ryczałtu
ani ich centralnych kopii. CompanyDbContext nie zawiera centralnych kont i ról.
W tej iteracji modele obejmują tylko istniejące kartoteki i metadane wyboru bazy;
nie dodajemy pustego modułu tożsamości ani pozornej implementacji logowania.

Pozostają istniejące projekty Domain/Application/Infrastructure. Rozdzielenie
kontekstów nie wymaga nowego frameworka, mikrousług ani dodatkowych procesów
biznesowych. Każdy kontekst jawnie aplikuje tylko własne konfiguracje EF.
Domain nie zna EF, HTTP, SQL ani sekretów.

## Model i los dotychczasowych tabel

- `core.Companies` w nowej bazie systemowej: stabilny Id firmy, etykieta
  katalogowa Name, IsActive, daty i rowversion. Nie przechowuje profilu podatnika.
- `core.CompanyDatabases` w bazie systemowej: PK/FK CompanyId, ServerName,
  DatabaseName i ConnectionProfileKey, daty, rowversion; unikalna para serwer/baza.
  Klucz profilu wskazuje konfigurację serwera i referencje do sekretów, nie hasło.
  Rejestracja nie jest dowodem gotowości bazy ani nadaniem dostępu.
- `core.CompanyProfile` w bazie firmy: CompanyId, nazwa prawna, NIP/REGON,
  daty i rowversion. PK SingletonKey i CHECK `= 1` dopuszczają najwyżej jeden
  profil; po inicjalizacji musi istnieć dokładnie jeden. CompanyId jest
  kluczem alternatywnym i trwałą tożsamością bazy po restore/przeniesieniu.
- `core.Contractors` w każdej bazie firmy: zachowane CompanyId i Id, PK
  (CompanyId, Id), unikalny (CompanyId, Code), rowversion oraz lokalny FK
  do CompanyProfile.CompanyId bez kaskady. SQL wymusza spójność z jedyną firmą.
- Nie ma FK, widoków ani zapytań wymagających połączeń między bazami.

Dotychczasowe `Vatowiec_Dev.core.Companies` i `core.Contractors` pozostają
niezmienione. Późniejsze kopiowanie rozdzieli katalog i profile firm, a kontrahentów
przypisze według CompanyId. Zachowa Id i daty źródłowe; rowversion po kopiowaniu
będzie nowy. Katalogowa etykieta jest niezależna od lokalnej nazwy prawnej;
nie tworzymy obowiązku atomowej synchronizacji tych dwóch pól między bazami.

Trzy oryginalne pliki migracji/snapshotu przeniesiono bez zmiany treści do
`Persistence/LegacyMigrations`; są wyłączone z kompilacji i nie są wykrywane
przez nowe konteksty. Stanowią archiwum historii Vatowiec_Dev. Nie zmieniamy
jej tabeli __EFMigrationsHistory i nie przepisujemy historii na nowy model.

## Wybór połączenia i autoryzacja

Docelowo: uwierzytelnienie → CompanyAccess i uprawnienie czynności w bazie
systemowej → sprawdzenie stanu Ready → lokalizacja bazy i sekret po stronie
serwera → CompanyDatabaseSelection → CompanyDbContext → sprawdzenie rzeczywistego
CompanyProfile.CompanyId i obsługiwanej wersji schematu → operacja.

W tej iteracji przygotowano niezmienny, wyłącznie serwerowy CompanyDatabaseSelection.
DI wymaga zgodnego ICompanyContext i jawnego wyboru połączenia. Domyślny kontekst
nie zawiera firmy, wybór nie jest rejestrowany automatycznie, a próba uzyskania
CompanyDbContext bez obu elementów jest odrzucana. Nie ma fallbacku do bazy
systemowej, pierwszej firmy ani Vatowiec_Dev. Filtry i kontrola zapisu pozostają
dodatkowym zabezpieczeniem.

Resolver, uwierzytelnienie, autoryzacja i sprawdzanie fizycznej tożsamości bazy
nie są jeszcze zaimplementowane. Samo skonstruowanie obiektów DI przez zaufany
kod testowy nie dowodzi autoryzacji. Nie wolno bindować wyboru połączenia z HTTP.
Przyszły resolver wykona pracę asynchroniczną przed utworzeniem DbContext;
rejestracja DI nie wykonuje zapytań do katalogu. Zmiana firmy oznacza nowy scope,
nie zmianę connection stringa używanego kontekstu. Te same zasady obejmą zadania.

Każda jednostka pracy ma własny kontekst; równolegli operatorzy tej samej firmy
pracują na tej samej bazie przez niezależne konteksty. Rowversion wykrywa konflikty.
Dokument, skutki, audyt i Outbox pozostają w jednej transakcji bazy firmy;
nie wymagamy transakcji rozproszonych z katalogiem systemowym.

Nowy klucz konfiguracji API: `ConnectionStrings:VatowiecSystem`.
Stary `ConnectionStrings:Vatowiec` jest ignorowany. Oba appsettings pozostawiają
połączenie puste. Polityka połączeń odrzuca Vatowiec_Dev, bazy techniczne SQL
oraz bazę systemową jako cel firmy. Wymaga jawnego katalogu i szyfrowania;
zaufanie certyfikatowi jest ograniczone do jawnych lokalnych baz Development.
Różne instancje są dozwolone z poprawną walidacją TLS.

## Migracje

- `Persistence/Migrations/System`: osobny InitialSystem i SystemDbContextModelSnapshot.
- `Persistence/Migrations/Company`: osobny InitialCompany i CompanyDbContextModelSnapshot.
- Każda fizyczna baza ma własną `dbo.__EFMigrationsHistory`. Ten sam zestaw
  Company jest stosowany niezależnie do kolejnych baz; wersje mogą chwilowo
  różnić się podczas kontrolowanej aktualizacji. API musi odmawiać obsługi
  nieobsługiwanej wersji. Katalog nie zastępuje fizycznej historii EF.
- Fabryki projektowe nie odczytują connection stringów ani środowiska i nie
  otwierają SQL. Generowanie migracji, snapshotów i skryptów jest offline.
- API nie wywołuje Migrate/EnsureCreated. Aktualizacja istniejącej firmy jest
  oddzielną czynnością, z backupem, przeglądem SQL i kontrolą zgodności.
- Docelowy provisioning wywoła migracje jawnie dla tworzonej firmy przez
  uprawnioną tożsamość techniczną. To nie migracja wszystkich baz przy starcie API.

## Automatyczne tworzenie firmy — następny etap implementacji

Użytkownik tworzy firmę w aplikacji bez SSMS. Serwer utrwala operację zawierającą
OperationId, klucz idempotencji z zakresem nadawcy, hash żądania, CompanyId,
zarezerwowaną lokalizację, etap, ostatni ukończony checkpoint, wersję/lease i błąd
bez sekretów. Nazwę bazy wyznacza serwer z tożsamości operacji, nie surowy tekst
użytkownika. Unikalna rezerwacja oraz lease chronią przed równoległymi wykonawcami.

Etapy: Requested → DatabaseCreated → SchemaApplied → ProfileInitialized → Ready.
Tożsamość z CREATE DATABASE/DDL jest oddzielona od konta zwykłego CRUD. Tworzenie
zasobów i aktualizacja katalogu nie są jedną transakcją: każdy etap sprawdza
rzeczywisty stan, jest idempotentny i pozwala wznowić pracę po przerwaniu.
To samo żądanie nie tworzy kolejnej firmy/bazy. Odmienna treść dla tego samego
klucza jest konfliktem. Przed Ready sprawdzamy profil, migracje i uprawnienia.

Zastanej bazy nie wolno usuwać, migrować ani przejmować bez potwierdzenia jej
tożsamości i powiązania z operacją. Awaria między CREATE DATABASE a zapisaniem
znacznika własności jest stanem do kontrolowanego uzgodnienia, nie powodem
do ślepego ponownego CREATE lub DROP. Interfejs ma pokazać błąd i umożliwić
bezpieczne wznowienie/naprawę. Testy awarii muszą objąć każdą granicę etapów.
W tej iteracji nie ma jeszcze tabel ani wykonawcy tego procesu.

## Backup i samodzielne odtworzenie

Każda firma ma własny backup, harmonogram, retencję i historię operacji. Uprawniony
użytkownik uruchomi backup/restore z aplikacji; zlecenie wykona techniczny proces
serwera. Pakiet obejmuje SQL, załączniki, manifest CompanyId/wersji/integralności
oraz wymagane klucze. Baza systemowa ma oddzielną strategię ochrony.

Restore następuje najpierw do nowej bazy roboczej. Po weryfikacji tożsamości,
integralności, schematu i załączników, kopii dotychczasowej bazy oraz zatrzymaniu
zapisów firmy następuje kontrolowane przełączenie katalogu i unieważnienie
starych wyborów połączenia. Inne firmy pozostają dostępne. Przeniesienie na inny
SQL Server przygotowuje też tożsamości techniczne i sekrety docelowej instancji.
Nie odtwarza centralnych kont ani nie nadaje uprawnień na podstawie backupu firmy.
Klon jako nowa firma jest inną operacją. Szczegóły RPO/RTO i retencji: ADR-011.
Backup i restore nie zostały zaimplementowane w tej iteracji.

## Trwała identyfikacja aktorów

Audyt księgowy w bazie firmy będzie przechowywał stabilny ActorId, ActorIssuerId
(przestrzeń źródłowej instalacji) i historyczny opis wykonawcy. Nie jest to
wyłącznie FK do bieżącego centralnego użytkownika. Identyfikatory nie mogą być
ponownie nadawane; usunięcie konta lub zmiana loginu nie modyfikuje historii.
Brak FK/kaskady do bazy systemowej. Odtworzona/przeniesiona firma nadal rozpoznaje
dawnych aktorów, także bez ich kont w nowej instalacji. Bieżący dostęp nadaje
się osobno. Audyt będzie zapisywany atomowo z dokumentem. Ten etap ustala
kontrakt; nie implementuje jeszcze audytu księgowego ani centralnych kont.

## Weryfikacja i ograniczenia etapu

Testy offline sprawdzają rozłączność modeli, lokalne FK, singleton profilu,
zestawy migracji i zgodność snapshotów, treść generowanego SQL, odmowę bez wyboru
bazy, izolację scope/połączeń, politykę konfiguracji, filtr i odmowę zapisu.
Nie dowodzą działania nowych baz ani uprawnień użytkowników.

Testy SQL zostały dostosowane do dedykowanych Vatowiec_System_Test i dwóch baz
Vatowiec_Company_A_Test/B_Test po migracjach. Domyślnie są pomijane; stara flaga
VATOWIEC_SQL_INTEGRATION nie włącza ich. Nie tworzą/usuwają baz i nie migrują.
Wynik SQL, provisioning, restore, rzeczywista równoczesność użytkowników i
trwałość aktorów muszą zostać zweryfikowane w kolejnych etapach. Nie deklarujemy
ukończenia całego M0 ani gotowości produkcyjnej.

Plan uruchomienia: [database-per-company](../database-per-company.md).

## Źródła techniczne

- [EF Core: multi-tenancy](https://learn.microsoft.com/ef/core/miscellaneous/multitenancy)
- [EF Core: cykl życia DbContext](https://learn.microsoft.com/ef/core/dbcontext-configuration/)
- [EF Core: wdrażanie migracji](https://learn.microsoft.com/ef/core/managing-schemas/migrations/applying)
