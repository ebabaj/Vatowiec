# ADR-017: Edycja kontrahentów, konflikt wersji i atomowy audyt

Data: 2026-10-22  
Status: przyjęty w zakresie zleconej kartoteki.  
Wymagania: ORG-003/004/005/008, E04, SPECYFIKACJA §8.1, §10, §11.2.  
Uzupełnia ADR-014/015/016. Architektura database-per-company pozostaje bez zmian.

## Kontrakt i kontrola dostępu

GET /api/v1/companies/{companyId}/contractors zachowuje odpowiedź tablicową,
rozszerzoną o TaxIdentifier, IsActive i RowVersion (base64). Dodaje parametry q
(kod/nazwa/identyfikator, najwyżej 128 znaków), country, active, offset i limit.
Domyślnie zwraca do 50 pozycji, maksymalnie 100, uporządkowanych po kodzie i Id.
Filtrowanie, sortowanie i stronicowanie wykonuje SQL przez parametryzowane LINQ.
Contains szuka dosłownego tekstu, także dla znaków procentu; nie jest filtrem SQL
wpisywanym przez użytkownika. Zmienna kartoteka może przesuwać granice stron
offsetowych — to widok bieżący, bez deklaracji spójnego eksportu całej kartoteki.
Indeks CompanyId/IsActive/Code wspiera filtrowanie; wyszukiwanie fragmentu tekstu
nie jest pełnotekstowym indeksem i może skanować rekordy. Wyniki są ograniczone.

Dodajemy GET /{id}, POST kolekcji i PUT /{id}; nie dodajemy DELETE.
Odczyt wymaga ViewContractors; zapis wymaga ViewContractors i EditContractors.
Rola globalna nie zastępuje tych praw. Login i logout pozostają bez zmian;
nowe operacje POST/PUT również wymagają tokenu CSRF pobranego dla aktualnej sesji.
CompanyId pochodzi wyłącznie ze ścieżki. Nie przyjmujemy aktora, lokalizacji SQL
ani CompanyId w JSON; nieznane pola są odrzucane. Resolver nadal sprawdza fizyczny
profil i historię migracji przed każdą operacją.

Id tworzonej pozycji nadaje klient raz na formularz i zachowuje przy ponowieniu.
POST z tym samym Id i tym samym znormalizowanym stanem zwraca istniejący rekord,
bez kolejnego audytu. Inny stan istniejącego Id daje 409. Unikalny kod firmy
jest nadal chroniony indeksem SQL; kolizja kodu daje 409 bez surowych wyjątków.
PUT wymaga poprawnego rowversion długości 8 bajtów. Brak/niepoprawny token: 400;
stary token: 409 contractor_conflict z bieżącym rekordem. Również wyścig między
odczytem a SaveChanges chroni token concurrency EF. Nie ma trybu force ani retry
aktualizującego token bez decyzji użytkownika. Zapis bez zmiany wartości nie
tworzy sztucznego zdarzenia historii. Ponowienie starego PUT nie dodaje historii.

Walidacja obejmuje wymagane pola, długości istniejącego modelu i dwie litery
ASCII kraju. Kod i kraj normalizujemy do wielkich liter, tekst przycinamy.
Identyfikator podatkowy jest opcjonalny, do 32 znaków. Nie dodajemy reguł prawa
podatkowego, kontroli sumy NIP ani weryfikacji w rejestrach — ORG-007 pozostaje poza zakresem.

## Audyt i identyfikacja

Każda rzeczywista zmiana przez API dodaje audit.ContractorChanges w tej samej
bazie firmy. Zdarzenie zawiera Id, CompanyId, ContractorId, ActorId,
ActorIssuerId, historyczną ActorName, OccurredAtUtc z zegara serwera,
CorrelationId żądania, operację Create/Update, SnapshotVersion=1 oraz BeforeJson
i AfterJson pięciu edytowanych pól. Dla utworzenia BeforeJson jest null.
Nie tworzymy fikcyjnej historii dla dawnych danych; pierwsza edycja zapisuje ich
zastany stan. Daty techniczne/rowversion zdarzenia nie zastępują daty operacji.

ActorIssuerId pochodzi z nowej pojedynczej pozycji security.Installation
w SystemDbContext. Migracja generuje NEWID osobno dla każdej nowej instalacji;
kolejne aktualizacje zachowują wartość. To metadane instalacji, bez dokumentów
i kontrahentów. Brak pozycji blokuje zapis zamiast stosować domyślny identyfikator.
Restore/kopiowanie instalacji zachowuje przestrzeń istniejących aktorów;
nowa niezależna instalacja otrzymuje nowy identyfikator przez własną migrację.
Nie należy klonować bazy systemowej jako nowej instalacji bez osobnej procedury.

Audyt nie ma FK do centralnego użytkownika ani do zmiennej kartoteki kontrahentów.
Przeniesienie bazy firmy i usunięcie konta nie odbierają identyfikacji autora.
Jedyną relacją jest lokalny profil firmy, bez kasowania kaskadowego.
EF odmawia aktualizacji/usunięcia śledzonych wpisów historii; API nie udostępnia
operacji zmieniających audyt. Nie jest to kryptograficzny dowód przeciw DBA.
Docelowa tożsamość wykonawcza SQL powinna mieć SELECT/INSERT audytu, bez UPDATE/DELETE/DDL;
lokalny demonstrator nadal używa uprawnień Windows programisty.

Encja i audyt są dodane do jednego CompanyDbContext i zapisywane przez jedno
SaveChanges. Transakcja EF obejmuje oba zapisy. Konflikt, unikalny kod lub błąd
ograniczenia audytu wycofuje całość; nie ma rozproszonej transakcji z katalogiem.
Test wymusza błąd audytu i potwierdza brak częściowego create/update.

## Klient i konflikty

Avalonia pokazuje formularz pięciu pól, nie wykonuje SQL i nie utrwala formularzy
na dysku. Wyszukiwanie wykonuje Enter lub przycisk; klient pobiera 51 rekordów,
pokazuje 50, a nadmiar informuje o kolejnej stronie. Zmiana kryteriów zaczyna od
pierwszej strony. Formularz otwierany do edycji pobiera aktualny rekord z API.

409 zachowuje lokalne pola i pokazuje porównanie wszystkich pól z serwerem.
Zapis jest zablokowany do decyzji: wczytać wersję serwera i odrzucić własne pola
albo zachować własne pola, zaakceptować pokazaną wersję jako bazę i ponownie
świadomie nacisnąć Zapisz. Kolejna cudza zmiana ponownie daje konflikt.
Nie wystarcza samo zamknięcie komunikatu. Podczas porównania pola są zablokowane.
Nie można zmienić firmy ani wylogować przy otwartym formularzu; najpierw trzeba
zapisać lub anulować. Wygaśnięcie/odmowa dostępu usuwa także formularz.
Skróty: Ctrl+N, F2, Ctrl+F, Enter wyszukiwania, Ctrl+S, Esc anulowania.

## Wdrożenie i ryzyko

System: 20261022055958_InstallationIdentity. Company:
20261022060000_ContractorAudit (własna historia każdej firmy, dodatkowy indeks
i tabela audytu; istniejące kolumny kontrahenta nie zmieniają się).
API nie migruje przy starcie. Narzędzie migrate-contractors sprawdza lokalną
instancję, dokładne nazwy/znaczniki trzech baz, profile oraz znane historie,
tworzy COPY_ONLY/CHECKSUM i wykonuje VERIFYONLY wszystkich kopii przed migracją.
Migruje bazę systemową, następnie oddzielnie A i B. Każda migracja ma transakcję;
w razie przerwania polecenie można ponowić, zachowując wcześniejsze kopie.
Resolver odmawia starego/niekompletnego schematu, nie wybiera innej bazy.

Down usuwa audyt lub identyfikator instalacji i nie jest bezpiecznym wycofaniem
używanego systemu. Rollback wymaga kopii i planu przywrócenia zgodnej wersji API.
Nie wolno przywracać starej wersji aplikacji pozwalającej zapisywać bez audytu.
Odtworzenie i przeniesienie danych firmy w aplikacji nadal są kolejnym zadaniem.

Instrukcja użytkownika i wyniki: [kartoteka](../contractors.md).
Źródła implementacji: [konflikty EF Core](https://learn.microsoft.com/en-us/ef/core/saving/concurrency),
[transakcje SaveChanges](https://learn.microsoft.com/en-us/ef/core/saving/transactions).
