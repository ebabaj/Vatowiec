# ADR-030 — REGON, KRS, BDO i chroniony PESEL kontrahenta

Data: 2026-10-23. Status: wdrożone i przetestowane na zlecenie użytkownika.
Wymagania ORG-003/007; rozszerzenie ADR-018–021 bez zmiany ParentId.

Dodajemy cztery opcjonalne teksty Regon, Krs, Bdo, Pesel w core.Contractors,
każdy nvarchar(100) NULL. Limit techniczny jak dla TaxId pozwala zachować błędną
wartość po świadomym ostrzeżeniu; oczekiwane długości nie są podatkowym CHECK.
Migracje są addytywne, bez resetów i mapowania danych; stare rekordy mają NULL.

REGON 9/14 cyfr: mod 11 (10→0), wagi 89234567 / 2485097361248; wariant 14
sprawdza także bazowy REGON 9. PESEL: 11 cyfr, wagi 1379137913 i dopełnienie
mod 10. Nie potwierdzamy nadania numeru ani tożsamości; nie dodajemy kontroli
daty urodzenia poza zakresem zlecenia. KRS 10 i BDO 9 cyfr: tylko format.
Jawna normalizacja usuwa białe znaki, kropki i typowe myślniki, zachowuje zera
i nieznane znaki. UI pokazuje normalizację; błędna suma nie jest naprawiana.
Wynik ma wersję reguły wiązaną z WarningHash. AcceptWarnings nie zastępuje
pozostałych praw, CSRF ani rowversion. Audyt i zapis pozostają atomowe.

Nowe firmowe ManageContractorPesel=512 obejmuje odczyt i edycję PESEL; wymaga
także istniejących praw do kartoteki. Brak automatycznego nadania nowego prawa
istniejącym kontom. Bez prawa PESEL nie występuje w odpowiedziach listy,
szczegółu, konfliktu ani ponowienia POST. Jawne wysłanie pola bez prawa daje
403; pominięcie przy PUT zachowuje zapisany numer. Odczyt trwałego wyniku POST
ponownie stosuje aktualne prawa; przechowywany JSON nie jest bezpośrednią odpowiedzią.
W audycie bazy firmy zachowujemy wartości przed/po, również chroniony PESEL;
nie udostępniamy surowego audytu przez publiczny endpoint. Przyszły odczyt audytu
musi stosować to samo prawo. Nie umieszczamy PESEL w ToString, logach,
komunikatach błędów ani ogólnym porównaniu konfliktu. Formularz uprawnionej osoby
może wyświetlać jego wartość. Diagnostyka nie loguje ciał żądań i danych SQL.

Wyszukiwanie pozostaje filtrowane/stronicowane w SQL. Dla klienta dodajemy POST
search z CSRF, aby wpisywane numery nie trafiały do URL i logów dostępowych.
PESEL jest przeszukiwany tylko w tej ścieżce i tylko z nowym prawem. Dotychczasowy
GET pozostaje zgodny dla publicznych identyfikatorów. Integracje nie zapisują
nowych pól; dotychczasowy świadomy import szkicu pozostaje bez zmian.

Kryteria: walidacja i zera, ostrzeżenia/hash/wersje, odmowa praw/CSRF,
brak ujawnienia PESEL (także konflikt i replay po odebraniu prawa), zachowanie
PESEL przy zwykłej edycji, izolacja A/B, atomowy audyt i zachowanie starych danych.
Testy SQL bez resetującego runnera; jawne migracje po backupie zweryfikowanych baz.

## Wykonanie i doprecyzowania techniczne

Zastosowano 20261023173707_ContractorPeselPermission w System i
20261023173705_StandardContractorIdentifiers osobno w A/B po COPY_ONLY/CHECKSUM
i RESTORE VERIFYONLY. System ma 11 migracji, firmy po 8. Dane i przypisania
pozostały: 42 wcześniejsze tabele miały zgodne liczności/SHA256 po migracji;
po testach 41, z wyjątkiem zwykłych, następnie unieważnionych UserSessions.
Nowe kolumny wcześniejszych rekordów mają NULL; liczba kontrahentów A=4, B=1
przed i po. Nie uruchomiono resetującego runnera ani zapytań produkcyjnych.

Nowe zmiany danych głównych mają SnapshotVersion=3; stare i zmiany szczegółów
zachowują 2. RequestHash dla operacji bez czterech nowych pól używa dawnej
postaci kanonicznej, aby zachować replay także po addytywnej aktualizacji,
bez przepisywania operacji. Nowe wartości są częścią hash i audytu.

Mała fabryka ICompanyDatabaseContextFactory oddziela utworzenie DbContext
od niezmienionych kontroli resolvera. Dzięki temu niedestrukcyjne testy używają
prawdziwych kontekstów SQL w zewnętrznych transakcjach z savepoint dla każdego
żądania, bez obejścia profilu, migracji ani praw. Fabryka produkcyjna nadal
tworzy nowy kontekst z autoryzowanego połączenia dla pojedynczej operacji.
Test wymusza awarię drugiego SaveChanges (audyt) i sprawdza wycofanie pierwszego.

Build: 0 błędów i ostrzeżeń. Testy: 359/359 (352 bez SQL, 7 niedestrukcyjnych
SQL/API), bez pominięć. 103 stare testy SQL z resetującymi fixture nieuruchomione.
Rzeczywiste GUI: pola, normalizacja, ostrzeżenie REGON, ukrycie PESEL bez prawa,
anulowanie szkicu i logout; bez zapisu danych i bez zapytań do rejestrów.
[Instrukcja, źródła reguł i dokładny zakres weryfikacji](../standard-identifiers.md).
