# ADR-019: Implementacja modelu ADR-018 i odtworzenie demonstratora

Data: 2026-10-22
Status: przyjęty dla implementowanego etapu M0.
Wymagania: ORG-003/004/005/008, E04, SPECYFIKACJA §8, §10–11.
Uzupełnia ADR-018, ADR-014–017; nie zmienia zatwierdzonych decyzji biznesowych.

## Zakres i kryteria akceptacji

Kończymy etap dokumentacyjny. Wdrażamy Contractors z int IDENTITY, ParentId
bez dziedziczenia, osobne adresy, kontakty, rachunki i lokalny AddressTypes.
Logowanie, HTTPS, CSRF, autoryzacja firmy, rowversion, filtrowanie SQL oraz
podstawowy klient pozostają działające. Każdy zapis API ma atomowy audyt.
Pełny przebieg obejmuje build, testy SQL/API i próbę rzeczywistego klienta.
Nie wdrażamy podatkowych walidatorów, VIES/białej listy, KSeF, faktur ani
nowych ekranów szczegółów. TaxIdentifierSchemes i ExternalVerifications
pozostają projektem; nie tworzymy ich fizycznych tabel.

## Klucze, granice i typy

Contractors.Id, adresy, kontakty i rachunki mają int IDENTITY. Usunięto
CompanyId z lokalnej kartoteki i ContractorChanges. Centralny katalog,
CompanyAccess, ścieżka API i CompanyProfile zachowują GUID firmy. GUID
identyfikuje nadal aktora, instalację, zdarzenie audytu i operację tworzenia,
a nie rekord kontrahenta. Nie ma relacji między bazami ani map dawnych GUID.

Resolver nadal sprawdza fizyczny profil i dokładną historię migracji.
Dodatkowe filtry lokalnych encji sprawdzają istnienie zgodnego CompanyProfile,
a SaveChanges wymaga zgodności profilu fizycznej bazy z kontekstem. Inicjalizacja
może dodać profil i dane w tej samej transakcji. Nie wolno usuwać tożsamości
bazy ani fizycznie usuwać kontrahenta przez śledzone operacje kontekstu.
CompanyId w odpowiedzi DTO pochodzi z autoryzowanego kontekstu żądania.

Kolumny adresów i rachunków odpowiadają dokładnie ADR-018 §3.7/3.9.
AddressTypes jest lokalnym, otwartym słownikiem: Id int IDENTITY, Name
nvarchar(100), wymagane i unikalne. Migracja inicjalizuje dwa zatwierdzone
rodzaje. Nie dodajemy ekranu słownika w tym etapie.

KsefIdWew przechowujemy jako nvarchar(100) NULL: cały tekst, bez utraty zer,
bez deklarowania poprawności semantycznej. Nie jest to walidator KSeF.
Dla kontaktów przyjęto techniczne długości: Name 256, Position 100, Email 256,
Phone 100, Street 200, BuildingNo/ApartmentNo/PostalCode po 20, City 150,
CountryCode char(2), Notes nvarchar(max). Nazwy i adresy odpowiadają istniejącym
limitom; telefon pozostaje swobodnym tekstem. Wszystkie opisowe pola poza
Name są opcjonalne. Jedyna dodatkowa kolumna techniczna to RowVersion.

## Hierarchia i domyślność

ParentId ma lokalny FK bez kaskady i CHECK zabraniający wskazania siebie.
Trigger TR_Contractors_Hierarchy sprawdza wszystkich przodków zmienionych
rekordów, także przy zbiorczych zmianach SQL. Wykrycie cyklu wycofuje zapis.
API pobiera transakcyjną blokadę SQL sp_getapplock ContractorHierarchy przed
odczytem/zapisem; trigger używa tej samej blokady. Blokada jest lokalna dla
bazy i działa między procesami API. Bezpośrednie konkurencyjne zapisy SQL
mogą dostać deadlock/odmowę zamiast zatwierdzić cykl. Limit rekurencji SQL
powoduje odmowę zbyt głębokiej hierarchii, nie pominięcie kontroli.
W M0 serializujemy zapisy kartoteki jednej firmy; odczyty i inne firmy
pozostają niezależne. Nie dziedziczymy ani nie kopiujemy danych nadrzędnego.

Unikalny indeks adresów (ContractorId, AddressTypeId) filtruje tylko IsDefault=1.
Indeks rachunków (ContractorId, CurrencyCode) również filtruje tylko IsDefault=1,
bez wykluczenia CurrencyCode IS NULL. SQL Server obejmuje grupę NULL unikalnością.
AccountNumber nie ma unikalnego indeksu. Flagi nie zależą od IsActive:
nieaktywny rekord też zajmuje oznaczenie, dopóki nie zostanie ono zmienione.

Wewnętrzna operacja ContractorBankAccountDefaults.SetDefault sprawdza wersję,
pobiera blokadę SQL i w jednej transakcji odznacza poprzedni rachunek, oznacza
nowy i zapisuje audyt wartości całej zmienianej grupy. Dwa SaveChanges dla
znaczników pozwalają zwolnić unikat przed ustawieniem nowego. Błąd audytu lub
konflikt wycofuje całość. Nie udostępniamy jeszcze tej operacji przez API/UI.

## Trwała idempotencja i audyt

Dodano techniczną core.ContractorCreateOperations: złożony PK
(ActorIssuerId, ActorId, OperationId), RequestHash varchar(64), ContractorId int
oraz ResponseJson nvarchar(max). Rodzaj operacji określa ta wyspecjalizowana tabela;
zakres firmy wynika z fizycznej bazy. Jest potrzebna do trwałego rozpoznania
ponowienia niezależnie od IDENTITY. Nie przechowuje dawnych identyfikatorów.

OperationId to GUID nadawany raz na formularz tworzenia. POST z Id=0
weryfikuje uprawnienia i CSRF przy każdej próbie. W transakcji, po blokadzie SQL,
porównuje SHA-256 znormalizowanego stanu żądania. Utworzenie rekordu, audyt
SnapshotVersion=2 i wynik operacji są zatwierdzane razem. Pierwszy SaveChanges
nadaje int/rowversion; drugi zapisuje audyt i wynik, następnie COMMIT.
Błąd drugiego zapisu wycofuje pierwszy. Luki w IDENTITY po rollbacku są poprawne.

Ponowienie zwraca pierwotny DTO i ten sam int (200), nawet gdy kontrahenta później
zmieniono. Nie dopisuje audytu. Zmieniona treść tego samego żądania daje 409.
Po takim błędzie użytkownik anuluje formularz, wyszukuje pozycję i otwiera edycję.
Wyniki są niezmienne i zachowywane bez automatycznej retencji; ich usunięcie
złamałoby gwarancję ponowienia. Późniejszą retencję trzeba zaprojektować osobno.

ContractorChanges zachowuje ActorId, ActorIssuerId, historyczną ActorName,
czas i korelację, BeforeJson/AfterJson oraz kontrolę niezmienności przez EF.
SnapshotVersion=2 opisuje podstawowe pola, ParentId i trzy identyfikatory;
operacja domyślności rachunków zapisuje grupę rachunków. Lokalny FK do
Contractors ma NO ACTION; nie ma FK do użytkownika ani kaskady usuwania historii.
Nie jest to zabezpieczenie kryptograficzne przed administratorem SQL.

## API i klient

Dotychczasowe ścieżki pozostają, ale {contractorId} to int. POST/PUT używają
ContractorWriteRequest: Id, Code, Name, CountryCode, IsActive, RowVersion,
OperationId (tylko POST), ParentId. POST wymaga Id=0 i niepustego OperationId;
PUT zgodnego dodatniego Id i 8-bajtowego RowVersion, bez OperationId.
Odpowiedź ma TaxId zamiast TaxIdentifier, dodatkowo ParentId, VatEuId i KsefIdWew.
To świadomie niezgodna zmiana kontraktu demonstratora: API i desktop wdrażamy razem.

Identyfikatory podatkowe/KSeF nie są polami zapisu tego API. Nieznane pola
JSON są odrzucane; istniejące numery są zachowywane przy edycji pozostałych
pól. W Avalonia TaxId jest tylko do odczytu, z informacją o braku walidacji.
Nie implementujemy obejścia ostrzeżeń ani nie uznajemy numerów za poprawne.
Wyszukiwanie SQL obejmuje Code, Name, TaxId i VatEuId. Pozostałe limity,
uprawnienia i świadome rozstrzyganie rowversion pozostają jak w ADR-017.

## Odtworzenie, ryzyko i weryfikacja

Odtwarzamy wyłącznie lokalne Vatowiec_System i Vatowiec_Company_A_Dev/B_Dev.
Przed pierwszym DROP narzędzie sprawdza wszystkie cele: maszynę/instancję,
Express 17/Windows Authentication, dokładne nazwy, znacznik przeznaczenia,
znane tabele i historie oraz cechy danych fikcyjnych. Niepotwierdzony cel
blokuje operację. Znacznik i oznaczenia są kontrolą demonstratora w połączeniu
z deklaracją użytkownika o fikcyjności, nie ogólnym detektorem prawdziwych danych.

Stare migracje firmowe zachowano w LegacyCompanyMigrations, poza kompilacją.
Nowa historia zaczyna się od 20261022143310_CompanyModelV2; system zachowuje
zestaw trzech migracji, wykonany na świeżej bazie. Nie tworzymy migracji danych.
Nie uruchamiamy migracji przy starcie API. Stary schemat nie jest aktualizowany
nową migracją inicjalną; wymaga zatwierdzonego odtworzenia demonstratora.
Operacja niszczy stare fikcyjne rekordy, audyt i sesje; hasła kont ponownie
pobieramy wyłącznie z lokalnej konfiguracji. Nie jest to procedura dla produkcji.

Runner test wykonuje testy na tych samych trzech jawnie dozwolonych bazach,
a po przebiegu inicjalizuje demonstrator i konta. Nie tworzy dodatkowych *_Test.
API należy wcześniej zatrzymać. Flagi testów ustawia runner po przygotowaniu
baz. Testy fizycznego SQL sprawdzają wszystkie kolumny/typy adresów i rachunków,
relacje, cykle, domyślność PLN/EUR/NULL, idempotencję, audyt i izolację.
Wyniki i instrukcja: [kartoteka](../contractors.md), [bazy](../database-per-company.md).
