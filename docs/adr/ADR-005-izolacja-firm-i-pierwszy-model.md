# ADR-005: Wspólna baza i pierwszy model firmowy

Data: 2026-10-21  
Status: przyjęty dla etapu bazy M0 w zakresie zleconym przez użytkownika.  
Wymagania: ORG-001, ORG-004, podstawowa część ORG-005, E02–E04; §3.2, §8, §11.

## Decyzja i zakres

Jedna baza SQL Server, z obowiązkowym CompanyId dla danych należących do firmy.
Company.Id jest identyfikatorem firmy. Contractor ma klucz złożony (CompanyId, Id),
FK do Company bez kaskadowego usuwania i unikalny (CompanyId, Code).
Klucze złożone będą podstawą relacji wewnątrz firmy w kolejnych modułach.

Company: Id, Name, Nip, Regon, IsActive, CreatedAtUtc, UpdatedAtUtc, RowVersion.
Contractor: CompanyId, Id, Code, Name, TaxIdentifier, CountryCode, IsActive,
CreatedAtUtc, UpdatedAtUtc, RowVersion. Identyfikatory podatkowe są opcjonalnym
tekstem, bez deklarowania walidacji prawnej, sprawdzania rejestrów czy reguł VAT.
Nazwa jest wymagana (maks. 256 znaków), kod kontrahenta 64 znaki, identyfikator
podatkowy 32 znaki, REGON 14 znaków. To limity techniczne pierwszego modelu.
CountryCode to dwie wielkie litery ASCII, bez założenia domyślnego kraju.
Wiele adresów, rachunki, historia danych kartotekowych, profile podatkowe,
użytkownicy i audyt biznesowy zostaną dodane osobno. Daty zmian nie zastępują audytu.

Niezmienny kontekst firmy jest przekazywany do DbContext przez warstwę serwerową.
Filtry zapytań dotyczą obu encji, a SaveChanges sprawdza firmę każdego zapisu.
Brak kontekstu daje pusty odczyt i odmowę zapisu. Złożony klucz kontrahenta
uniemożliwia aktualizację rekordu innej firmy przez podstawienie samego Id.
Obie encje mają SQL rowversion; zmiana CompanyId jest niedozwolona.
IgnoreQueryFilters, surowy SQL i operacje zbiorcze omijające SaveChanges mogą być
używane wyłącznie przez zaufany kod infrastruktury, nigdy przez publiczne API.

Nie powstają endpointy danych firmowych ani mechanizm wyboru firmy z żądania.
Przyszłe logowanie musi zastąpić pusty kontekst kontekstem autoryzowanym na serwerze.
Filtry i kontrola zapisów nie zastępują autoryzacji użytkownika ani RLS w SQL.

## Migracja i lokalne środowisko

Wyłącznie localhost\SQLEXPRESS / Vatowiec_Dev / Windows Authentication.
Połączenie pochodzi ze zmiennej środowiskowej lub lokalnych ustawień Development.
TrustServerCertificate=True dopuszczamy tylko w Development, wyłącznie dla tej
lokalnej bazy, przy zachowanym Encrypt=True. Poza Development taka konfiguracja
jest odrzucana. Nie zmieniamy magazynów certyfikatów ani konfiguracji SQL Server.

Pierwsza migracja tworzy dwie tabele i historię EF; bez seedowania danych.
Migracje wykonuje się jawnie narzędziem dotnet-ef, nigdy przy starcie API.
Fabryka projektowa wymaga środowiska Development i sprawdza dokładny serwer,
bazę, Windows Authentication i szyfrowanie. Utworzenie nieistniejącej bazy
wymaga technicznego połączenia EF z master; nie modyfikuje innych baz użytkownika.
Przed wykonaniem sprawdzamy istnienie i zawartość wyłącznie Vatowiec_Dev oraz SQL
migracji. Gdy baza zawiera dane, należy wykonać backup i ocenić zgodność.
Down usuwa tabele i dane, więc nie jest procedurą wycofania dla używanej bazy.

## Kryteria odbioru

- Build i dotychczasowe testy przechodzą.
- Migracja została jawnie wykonana w Vatowiec_Dev; katalog SQL potwierdza PK, FK,
  unikalny indeks, ograniczenia CHECK i rowversion.
- Testy na prawdziwym SQL sprawdzają odczyt/zapis, izolację, brak kontekstu,
  niedozwolony zapis do innej firmy, FK, unikalność, konflikty i atomowość zapisu.
- Testy SQL są jawnie włączane, używają wyłącznie Vatowiec_Dev i wycofywanych
  transakcji z losowymi identyfikatorami; nie tworzą/usuwają bazy ani nie migrują.
- README podaje konfigurację, migrację, sprawdzenie schematu i uruchamianie testów.

Źródła techniczne: [filtry EF](https://learn.microsoft.com/ef/core/querying/filters),
[migracje](https://learn.microsoft.com/ef/core/managing-schemas/migrations/applying),
[fabryka projektowa](https://learn.microsoft.com/ef/core/cli/dbcontext-creation),
[TLS SqlClient](https://learn.microsoft.com/sql/connect/ado-net/encryption-and-certificate-validation).
