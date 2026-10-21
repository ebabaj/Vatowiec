# Przygotowanie i późniejsze uruchomienie database-per-company

Status 2026-10-21: przygotowano kod i migracje offline. Nie utworzono nowych baz,
nie wykonano migracji ani operacji w Vatowiec_Dev. Decyzja: [ADR-014](adr/ADR-014-database-per-company.md).

## Co jest gotowe

SystemDbContext mapuje `core.Companies` i `core.CompanyDatabases`.
CompanyDbContext mapuje `core.CompanyProfile` i `core.Contractors`. Każdy ma
własną migrację początkową i snapshot. Dotychczasowe migracje są archiwalne.

Konfiguracja API używa `ConnectionStrings__VatowiecSystem`; domyślnie jest pusta.
Stary klucz `ConnectionStrings__Vatowiec` jest ignorowany. Nie ma połączeń firm
w appsettings ani w Desktop. CompanyDatabaseSelection jest przygotowaniem do
serwerowego resolvera, nie działającą autoryzacją czy kontrolą fizycznego profilu.

## Generowanie SQL offline

Te polecenia nie łączą się z SQL i nie wykonują wygenerowanego SQL:

```powershell
dotnet build Vatowiec.slnx --no-restore
dotnet ef migrations script 0 --idempotent --context SystemDbContext --project src/Modules/Core/Infrastructure --no-build --output src/Modules/Core/Infrastructure/obj/system.initial.sql
dotnet ef migrations script 0 --idempotent --context CompanyDbContext --project src/Modules/Core/Infrastructure --no-build --output src/Modules/Core/Infrastructure/obj/company.initial.sql
```

Fabryki projektowe nie dziedziczą konfiguracji środowiska i nie mają connection
stringa. Powstałe pliki są artefaktami do przeglądu, nie skryptem tworzenia baz.
System SQL nie może tworzyć tabel kontrahentów/księgowych; Company SQL nie może
tworzyć katalogu firm ani centralnych użytkowników. Nie stosuj tych migracji
do istniejącej Vatowiec_Dev i nie kopiuj jej historii EF do nowych baz.

## Plan bezpiecznego utworzenia trzech baz — jeszcze niewykonany

1. Ustalić docelową instancję, katalogi danych/backupów i konta techniczne.
   Dla demonstratora proponowane nazwy: `Vatowiec_System`,
   `Vatowiec_Company_A_Dev`, `Vatowiec_Company_B_Dev` na localhost\SQLEXPRESS.
   Sprawdzić zajętość dokładnie tych nazw i pojemność. Zastanej bazy nie
   nadpisywać ani nie uznawać automatycznie za własną. Wyłączyć Vatowiec_Dev
   z listy celów; nie wymaga ona odczytu ani modyfikacji do startu pustych firm.
2. Przed wykonaniem przedstawić SQL do przeglądu i uzyskać zgodę na operacje
   SQL. Docelowy skrypt/wykonawca musi sprawdzać nazwę serwera i katalogu przed
   DDL, odmawiać operacji na Vatowiec_Dev i stosować osobne tożsamości
   provisioningu/migracji, zwykłego CRUD i backupu. Sam SQL z EF nie zawiera
   zabezpieczeń wskazujących konkretną docelową instancję i bazę.
3. Utworzyć wyłącznie nową Vatowiec_System i wykonać InitialSystem. Sprawdzić
   historię EF, rozłączność tabel i minimalne uprawnienia konta aplikacji.
   Konta użytkowników i role wymagają uprzedniej implementacji logowania;
   obecna migracja nie tworzy użytkowników ani fikcyjnych poświadczeń.
4. Przydzielić dwa stabilne, sztuczne CompanyId. Dla każdej firmy wykonać
   kontrolowane: utworzenie pustej bazy → InitialCompany → inicjalizację
   dokładnie jednego CompanyProfile → weryfikację CompanyId, CHECK, FK,
   rowversion i historii EF. Nazwy i identyfikatory nie mogą pochodzić z
   autentycznych danych podatników. Inicjalizator profilu jest kolejnym zadaniem;
   sam skrypt migracji nie seeduje profilu.
5. Zapisać katalog i lokalizacje w bazie systemowej oraz serwerowe profile
   połączeń. Po implementacji autoryzacji i resolvera dopuścić firmę do pracy
   dopiero po weryfikacji stanu Ready, rzeczywistego profilu i wersji schematu.
   Sama obecność rekordu CompanyDatabases nie udostępnia bazy klientowi.
6. Wykonać oddzielny backup każdej bazy i wymaganych załączników, próbny restore
   do nowych baz roboczych, sprawdzić dane i tożsamość oraz udokumentować wynik.
   W późniejszym interfejsie uprawniony użytkownik zleci to bez SSMS.
7. Po osobnej zgodzie przygotować dedykowane, puste bazy *_Test opisane niżej
   i wykonać testy SQL. Dalej: test dwóch użytkowników tej samej firmy, IDOR,
   błędnego routingu, awarii provisioningu, niezależnego restore i audytu po
   usunięciu konta. Dopiero wykonanie tych scenariuszy pozwala je zaliczyć.

Docelowo kroki tworzenia firmy wykona wznawialny proces uruchamiany z aplikacji,
z trwałą operacją i checkpointami z ADR-014. Na tym etapie nie ma jeszcze
wykonawcy, UI, tabel stanu provisioningu, backupu ani restore. Pierwsze bazy mogą
powstać kontrolowanym narzędziem wdrożeniowym po przeglądzie; nie oznacza to,
że końcowy użytkownik będzie potrzebował SSMS.

## Późniejsze przeniesienie danych z Vatowiec_Dev

To oddzielne zadanie. Przed kopiowaniem backup i weryfikacja źródła, zatrzymanie
zapisów, mapa CompanyId → nowa baza, kopiowanie katalogu/profili i kontrahentów
z zachowaniem identyfikatorów/dat, sprawdzenie liczności, wartości, unikatów i FK.
Rowversion zostanie wygenerowany ponownie. Import musi być idempotentny.
Przełączenie dopiero po weryfikacji; źródło i jego historia EF pozostają
nienaruszone. Po zapisach w nowym systemie powrót do starego źródła wymaga
uzgodnienia danych, nie prostego przełączenia connection stringa.

## Testy

Domyślne testy bez SQL sprawdzają modele, migracje/skrypty offline, DI,
politykę połączeń i host API. Bezpieczny przebieg bez względu na stare flagi:

```powershell
$env:VATOWIEC_DATABASE_PER_COMPANY_SQL = '0'
dotnet test Vatowiec.slnx --no-build
```

Dziewięć testów SQL pozostaje pominiętych. Nie tworzą baz ani nie migrują.
Do ich przyszłego wykonania wymagane są trzy wcześniej zmigrowane, puste bazy:

| Zmienna środowiskowa | Wymagany katalog |
|---|---|
| ConnectionStrings__VatowiecSystemTest | Vatowiec_System_Test |
| ConnectionStrings__VatowiecCompanyATest | Vatowiec_Company_A_Test |
| ConnectionStrings__VatowiecCompanyBTest | Vatowiec_Company_B_Test |

Połączenia muszą wskazywać localhost\SQLEXPRESS i Windows Authentication,
z Encrypt=True. TrustServerCertificate=True jest dopuszczalne tylko dla tych
lokalnych baz Development. Nie podawaj sekretów w repozytorium ani logach.
Gdy bazy i konfiguracja są gotowe, jawnie ustaw
`VATOWIEC_DATABASE_PER_COMPANY_SQL=1` i uruchom testy. Brak konfiguracji jest
wtedy błędem, nie pominięciem. Stara flaga VATOWIEC_SQL_INTEGRATION jest ignorowana.

Testy zapisują wyłącznie w transakcjach wycofywanych na końcu. Dla company testy
oczekują braku profilu i danych przed startem. Nie należy wskazywać baz Dev.
Test rowversion używa dwóch kontekstów i nie dowodzi jeszcze równoczesności
dwóch zalogowanych operatorów. Provisioning/restore/audyt kont wymagają osobnych
testów po ich implementacji.

## Wynik weryfikacji kodu (2026-10-21)

- `dotnet build Vatowiec.slnx --no-restore`: wszystkie siedem projektów,
  zero błędów i ostrzeżeń.
- `VATOWIEC_DATABASE_PER_COMPANY_SQL=0` i `dotnet test Vatowiec.slnx --no-build`:
  38 zaliczonych testów offline, 9 pominiętych testów SQL, 0 niepowodzeń.
- Wygenerowano idempotentne skrypty obu kontekstów poleceniami opisanymi wyżej;
  żadnego nie wykonano. Test potwierdza zgodność modeli ze snapshotami i rozłączność
  tabel w generowanym SQL.
- `dotnet format Vatowiec.slnx --verify-no-changes --no-restore`: powodzenie.
- Porównanie git hash-object z oryginalnymi plikami HEAD potwierdza zachowanie
  treści wszystkich trzech archiwalnych plików historii Vatowiec_Dev.

Nie łączono się z SQL Serverem, nie utworzono baz i nie wykonano migracji.
Nie zmieniono Vatowiec_Dev; nie wykonano commit ani push. Wyniki nie dowodzą
działania nowej struktury na SQL, autoryzacji, provisioningu, backupu/restore
ani audytu po usunięciu konta. Te funkcje wymagają dalszej implementacji i prób.
