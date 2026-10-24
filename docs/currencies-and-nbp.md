# Waluty i ręczne kursy NBP

Wdrożenie DOC-006 w zakresie słownika i kursów, zgodnie z ADR-023 i ADR-024.
Bez faktur, automatycznego doboru podatkowego kursu, różnic kursowych i harmonogramu.
[Oficjalny kontrakt NBP](https://api.nbp.pl/) sprawdzono 2026-10-22.

## Obsługa programu

1. Uruchom API i Avalonia według [instrukcji](desktop.md). Zaloguj się jako
   `operator.m0` swoim lokalnie skonfigurowanym hasłem i otwórz firmę A.
2. W głównej nawigacji wybierz **Słowniki → Waluty i kursy NBP**. Lista zapisanych
   kursów jest stronicowana przez SQL; filtry obejmują walutę, A/B/C i daty
   publikacji. Przycisk Filtruj stosuje filtry, a sortowanie można odwrócić.
   Liczby mają polski przecinek i do 12 miejsc, daty dd.MM.yyyy. A/B pokazują
   kurs średni, C kupna/sprzedaży oraz odrębną datę notowania, jeśli była podana.
   Filtr walut domyślnie pokazuje tylko aktywne waluty bez PLN. „Wszystkie kursy
   z bieżącego zakresu” obejmują także historię walut nieaktywnych. Włącz
   **Pokaż nieaktywne (archiwum)**, aby móc wskazać nieaktywną walutę mającą
   zapisane kursy. Przy aktywnych PLN/EUR domyślnie dostępna jest tylko EUR,
   a po włączeniu archiwum także np. USD, jeśli posiada historię.
   Zmiana aktywności nie kasuje wybranego filtra ani pozostałych kryteriów.
   Wybrana pozycja archiwalna pozostaje widoczna do świadomego wyboru innego
   kodu lub wszystkich kursów, także po wyłączeniu opcji archiwum.
3. **Ustawienia pobierania** otwierają ten sam słownik konfiguracyjny co
   **Ustawienia firmy → Waluty i kursy NBP**. Wyszukaj walutę po kodzie/nazwie,
   wybierz ją z listy i ustaw aktywność oraz flagi A/B/C. Zapisz wybraną walutę.
   Ctrl+S zapisuje, Esc wraca; porzucenie zmienionego formularza wymaga decyzji.
4. Początkowo dostępnych jest **149 pozycji**, według list A/B NBP z 2026-10-22
   i PLN. Słownik jest seedowany lokalnie przez migrację: konfiguracja nie wymaga
   wcześniejszego połączenia z NBP. Domyślnie wybrane są EUR, USD, GBP i CHF z A.
   Inne waluty są dostępne do wyboru, bez domyślnego pobierania. PLN zawsze jest
   aktywny i nie można pobierać dla niego kursu obcej waluty. Wyłączenie waluty
   usuwa zaznaczenia pobierania i zachowuje zapisane kursy.
5. **Pobierz kursy z NBP** działa wyłącznie po kliknięciu. Wybierz:
   - Najnowsze kursy — ostatnia dostępna publikacja każdego wybranego typu tabeli;
     pola dat nie są wtedy używane.
   - Za okres publikacji — obie daty włącznie; bez dat przyszłych, od 2002-01-02,
     najwyżej 10 lat na jedną świadomą operację. Dłuższą historię pobierz w częściach.
6. Podsumowanie pokazuje tabele, nowe kursy, pominięte identyczne, konflikty,
   błędy, zakres i otrzymane daty. „Brak nowych kursów” jest normalnym wynikiem.
   Brak publikacji, brak waluty w danym typie tabeli i awaria są rozróżnione.
   Obok listy pokazujemy ostatnie daty **zapisane w firmie**. W podsumowaniu są
   ostatnie daty **otrzymane w tej operacji**; pusty typ nie był pobierany lub
   nie zwrócił danych. Nie wykonujemy dodatkowego HTTP tylko po metadane C/B/A.
7. W **Konfliktach** wybierz pozycję, porównaj kurs zapisany z propozycją NBP,
   zaznacz świadome potwierdzenie i wybierz zachowanie starego lub przyjęcie nowego.
   Serwer kontroluje wersję z chwili wykrycia konfliktu. Jeśli ktoś już zmienił
   kurs, ponownie pobierz i porównaj; nie istnieje force-update.
8. **Historia operacji walutowych** pokazuje wykonawcę, czas i wyniki. Wybranie
   historycznego importu przywraca propozycje konfliktów bez nowego zapytania NBP.
   Propozycje są historyczne i podlegają tej samej kontroli wersji.

Firma B pozostaje tylko do odczytu. Czytelnik ma odczyt kursów w A, bez zmiany
konfiguracji/importu. Ukrycie lub wyłączenie przycisku jest dodatkowe: każde
żądanie API sprawdza uprawnienia i fizyczną bazę firmy. Zmiana firmy, odmowa lub
wygaśnięcie sesji usuwa poprzedni słownik/listę/historię/konflikty z klienta.

### Zbiorowa aktywność walut

Checkboxy przy walutach są niezależne od wyboru waluty do edycji.
**Zaznacz widoczne** dodaje pozycje pasujące do filtra; **Zaznacz wszystkie**
obejmuje cały słownik firmy, także ukryte wyniki. **Wyczyść zaznaczenie** usuwa
wszystkie zaznaczenia. Licznik obejmuje również pozycje ukryte przez filtr.
**Aktywuj/Dezaktywuj zaznaczone** korzystają z tych checkboxów;
**Aktywuj wszystkie/Dezaktywuj wszystkie** działają bez zaznaczania.

Przed zapisem panel pokazuje zakres, liczbę zmian i pominięć oraz wymaga
osobnego potwierdzenia. Dezaktywacja pomija PLN i wyłącza A/B/C, zachowując
historyczne kursy. Ponowna aktywacja nie włącza A/B/C. Obie operacje zachowują
pozostałe ustawienia i nigdy nie pobierają kursów NBP. Najpierw zapisz lub porzuć
niezapisane zmiany edytowanej waluty. Po wykonaniu słownik jest odświeżany,
a filtr i zaznaczenia pozostają. Po konflikcie anuluj panel, odśwież słownik
i ponownie sprawdź zakres; nie ma częściowego zapisu ani wymuszenia wersji.

Całą partię obsługuje jedno **POST /currencies/activity**, z ViewExchangeRates,
ManageCurrencies i CSRF. Żądanie zawiera OperationId, IsActive, All, pary
Code/RowVersion w Currencies oraz Confirm. All wymaga zgodności z pełnym
słownikiem firmy. W jednej transakcji sprawdzane są wersje, zapisywane tylko
rzeczywiste zmiany i jeden niezmienny audyt BulkActivity z wartościami przed/po
oraz trwałym wynikiem. Powtórzenie tego samego żądania zwraca zapisany wynik;
inne dane pod tym samym kluczem są odrzucane. Rekordy o zgodnym stanie i PLN
pominięty przy dezaktywacji zachowują RowVersion. Nie potrzeba migracji.

Weryfikacja usprawnień 2026-10-23:

- `dotnet build Vatowiec.slnx --no-restore`: bez błędów i ostrzeżeń.
- `dotnet test tests/Vatowiec.Tests/Vatowiec.Tests.csproj --no-restore --filter "Category!=SqlIntegration"`:
  **389/389**, bez pominięć; w tym wybór widocznych/wszystkich, niezależność
  zaznaczenia, potwierdzenie, zachowanie filtra, ponowienia, prawa, brak NBP
  oraz dwuklik właściwej firmy, pojedyncze otwarcie i odmowa dostępu.
- Nowe `CurrencyActivitySqlTests`, uruchomione osobno z
  `VATOWIEC_PRESERVED_VAT_SQL=1`: **1/1**, bez pominięć. Rzeczywiste API/SQL,
  aktywacja/dezaktywacja kilku i wszystkich walut, PLN, zachowanie flag przy
  braku zmiany, RowVersion, CSRF, firma B i czytelnik, idempotencja, audyt,
  konflikt wstępny i podczas UPDATE oraz wycofanie po błędzie audytu.
  Wszystkie zapisy w System/A/B wycofane zewnętrznymi transakcjami i savepointami,
  bez resetu, DDL, migracji ani destrukcyjnych fixture. Kursy historyczne
  i słownik B porównane bez zmian; adapter NBP nie został wywołany.
- Uruchomione rzeczywiste okno MainWindow z atrapą API i 149 walutami;
  sprawdzone natywne zdarzenia myszy oraz kontrolki przez Windows UI Automation.
  Checkbox AUD nie zmienił edytowanej EUR; pełny zakres 149 przy filtrze USD;
  anulowanie bez zapisu, potwierdzenie dezaktywacji 148 z pominięciem PLN,
  aktywacja bez A/B/C i NBP, zachowany filtr, blokada zapisu w B. Dwuklik B
  po wybranej A otworzył B raz; pojedyncze kliknięcie nie otworzyło firmy.
  Zrzuty lokalne: `TestResults/currency-ui-confirm.png`,
  `TestResults/currency-ui-deactivated.png`, `TestResults/currency-ui-readonly.png`.
  Próba okna nie używała danych ani sekretów istniejących baz.

Końcowy wspólny przebieg powyższych testów: **390/390**, bez pominięć;
`TestResults/currency-ui-final.trx`. `dotnet format --verify-no-changes`
dla zmienionych plików C# i `git diff --check` zakończone poprawnie.
Pełny destrukcyjny runner pozostał wyłączony. Bez Commit/Push.

### Weryfikacja aktywnych walut w formularzach — 2026-10-24

ORG-012 / DOC-006: nowe listy w Finansowe zawierają tylko aktywne waluty,
z zachowaniem zapisanych nieaktywnych odwołań. Filtr kursów zawiera aktywne
waluty bez PLN i świadomie włączane archiwum. Konfiguracja słownika pozostaje
pełna. DTO, model danych, migracje, reguły importu i uprawnienia bez zmian;
jedynym rozszerzeniem odczytu API jest opcjonalny filtr withRatesOnly.

- `dotnet build Vatowiec.slnx --no-restore -c Release`: 0 błędów i ostrzeżeń.
  Wybrano Release, ponieważ działające API/desktop blokowały pliki Debug;
  bieżących procesów użytkownika nie zatrzymywano ani nie restartowano.
- Testy `Category!=SqlIntegration` w Release: **399/399**, bez pominięć.
  Raport: `TestResults/active-currencies-offline.trx`. Obejmują PLN/EUR aktywne,
  USD nieaktywna, zachowanie USD przy zapisie innych pól, wybór i oznaczenia
  limitów, stabilność kolekcji, przejściowe callbacki ComboBox, ochronę szkiców,
  archiwum USD, zachowanie dat/tabeli/sortowania po zmianie aktywności i brak NBP.
- Rzeczywisty MainWindow Avalonia z atrapą API i pełnym słownikiem 149 pozycji:
  wybrana USD pozostała widoczna jako nieaktywna w warunkach i limicie;
  bez fałszywego IsDirty po odświeżeniu, nowe limity tylko PLN/EUR.
  W filtrze kursów tylko EUR i wszystkie kursy; archiwum umożliwiło wybranie
  historycznego USD. Wyłączenie archiwum zachowało wybrany kod, a świadomy
  wybór wszystkich kursów usunął nieaktywną pozycję z listy wyboru.
  Nieaktywna USD nadal dostępna w ustawieniach, NBP nie został wywołany.
  Zrzuty: `TestResults/active-currency-finance-initial.png`,
  `TestResults/active-currency-limit.png`, `TestResults/active-currency-archive.png`,
  `TestResults/active-currency-rate-default-final.png`.
- Testów SQL w tym etapie nie uruchamiano. Bez zapisów do istniejących baz,
  resetu, destrukcyjnych fixture, migracji, Commit i Push.

## Daty, źródło i wartości

Wywołujemy wyłącznie `https://api.nbp.pl/api/exchangerates/tables/{A|B|C}/`,
z `?format=json`, Accept application/json. Bez klucza, przekierowań i retry.
Nie są to kursy banków komercyjnych. Import pobiera całe wybrane typy tabel,
a zapisuje wyłącznie skonfigurowane aktywne waluty danej firmy. Nie ma żądań
HTTP osobno dla każdej waluty. Zakres jest dzielony na rozłączne części po
maksymalnie **93 dni włącznie**. 404 przy prawidłowym zakresie oznacza brak
publikacji, nie awarię. Nie uzupełniamy weekendów, świąt ani rzadszych dat B.

EffectiveDate to data publikacji z odpowiedzi. TradingDate to opcjonalna data
notowania C. Numer tabeli jest przechowywany jako tekst dokładnie z odpowiedzi,
nie generujemy go z daty. Mid lub Bid/Ask są za jednostkę waluty według API;
nie stosujemy mnożników z prezentacji HTML NBP. Nie mieszamy średniego A/B z C.
UTC pobrania jest zapamiętywane po otrzymaniu partii, przed zapisem SQL;
akceptacja historycznego konfliktu zachowuje czas pobrania propozycji, a audyt
ma osobny czas późniejszej decyzji. Identyczny ponowny import nie zmienia kursu
ani jego pierwotnego czasu pobrania.

## Tabele i integralność

W każdej bazie firmy, bez powielonego CompanyId:

| Tabela | Kolumny i reguły |
|---|---|
| ref.Currencies | Code char(3) PRIMARY KEY, bez Id, Name nvarchar(120), IsActive, DownloadA/B/C bit, RowVersion; trzy flagi to lokalne ustawienia firmy bez zbędnej tabeli 1:1 |
| core.ExchangeRates | Id bigint IDENTITY, CurrencyCode char(3) FK do Currencies.Code bez kaskady, Source varchar(8)=NBP, TableType char(1), TableNumber nvarchar(32), EffectiveDate date, TradingDate date NULL, Mid/Bid/Ask decimal(24,12) NULL, RetrievedAtUtc datetimeoffset, RowVersion |
| audit.CurrencyOperations | Id bigint IDENTITY, OperationId, RequestHash, Kind, OccurredAtUtc, ActorId, ActorIssuerId, ActorName, DetailsJson |

Code jest jedynym identyfikatorem waluty. Jest niezmienny: init w encji,
klucz EF ValueGeneratedNever oraz trigger SQL blokują jego edycję. Code i FK
CurrencyCode mają Latin1_General_100_BIN2; małe litery nie wskazują dużych.
CHECK wymaga trzech liter A–Z. Kod w API nie jest polem edycji ustawień.

Unikalny kurs: CurrencyCode + Source + TableType + EffectiveDate. Dodatkowy indeks
po publikacji wspiera przeglądanie. CHECK wymaga dodatniego Mid i pustych Bid/Ask
dla A/B, dodatnich Bid ≤ Ask i pustego Mid dla C; opcjonalne TradingDate nie
przekracza EffectiveDate. Dla A/B TradingDate jest puste. Precyzja to 12 cyfr
całkowitych i 12 ułamkowych. Adapter odrzuca liczby niereprezentowalne, także
zapis wykładniczy i zbyt długą frakcję, zamiast po cichu je zaokrąglać.

Audyt ma trwałą przestrzeń tożsamości instalacji i historyczną nazwę wykonawcy,
bez FK do konta centralnego. Trigger i kontekst blokują UPDATE/DELETE historii.
Konfiguracja ma snapshot przed/po. Import ma liczniki, wejście, błędy, braki i
konflikty z oryginalną wersją oraz pobraną propozycją. Rozstrzygnięcie zapisuje
decyzję i wartości przed/po w tej samej transakcji co zmiana kursu.
Nie dodajemy osobnej tabeli konfliktów ani historii kontrahentów.

GUID-y nie są PK nowych tabel: OperationId to techniczny klucz ponowienia
żądania, a ActorId/ActorIssuerId zachowują istniejący format tożsamości, nie są
zbędnymi identyfikatorami słownika. OperationId jest unikalny w zakresie
wykonawcy i instalacji; zmiana treści pod tym samym kluczem daje 409. Skuteczne
ponowienie zwraca ten sam zapisany wynik bez kolejnego HTTP/audytu. Równoległe
żądania mogą wykonać ten sam bezskutkowy GET, ale tylko jeden wynik i audyt są
zatwierdzone. Unikalność kursu i blokada transakcyjna chronią też różne operacje.

HTTP odbywa się poza transakcją SQL. Zmiana konfiguracji podczas pobierania
blokuje zapis kursów i jest raportowana; pozostaje audyt próby. Błąd zapisu
audytu wycofuje cały zapis kursów lub ustawień. Dat nie dopasowujemy do faktur.

## API i uprawnienia

Poniższe ścieżki zaczynają się od `/api/v1/companies/{companyId}`:

| Endpoint | Znaczenie |
|---|---|
| GET /currencies | lokalny słownik i ustawienia, ViewExchangeRates=64 |
| GET /currencies?withRatesOnly=true | to samo DTO i uprawnienie; wyłącznie kody z istniejącą historią kursów, niezależnie od aktywności; zwykły słownik nadal jest pełny |
| PUT /currencies/{currencyCode} | np. /currencies/EUR; ustawienia wskazanej kodem waluty, ViewExchangeRates + ManageCurrencies=128, CSRF i RowVersion |
| POST /currencies/activity | zbiorowa aktywacja/dezaktywacja, pełny zakres Code/RowVersion, potwierdzenie, ViewExchangeRates + ManageCurrencies, CSRF, jedna transakcja i trwała idempotencja |
| GET /exchange-rates | code/table/from/to/offset/limit/descending; filtry i stronicowanie SQL, maks. 100 |
| POST /exchange-rates/imports | OperationId i opcjonalne From/To; ViewExchangeRates + ImportExchangeRates=256, CSRF |
| POST /exchange-rates/imports/{longAuditId}/resolve | OperationId, ConflictIndex, AcceptIncoming, Confirm; odczyt + zarządzanie + import oraz CSRF |
| GET /exchange-rates/history | lokalna stronicowana historia, odczyt |
| GET /exchange-rates/lookup | code/table/date i before; dokładna publikacja lub ostatnia ściśle przed datą |

Operator A otrzymuje maskę 511, B 67 (dawne 3 + odczyt kursów), czytelnik A 65.
Rola globalna nie zastępuje praw firmy. Centralny System przechowuje tylko
uprawnienia; słownik, kursy i audyt pozostają lokalne. Resolver nadal kontroluje
profil i pełną historię migracji; bez domyślnej bazy ani transferu kursów A → B.

Waluta w odpowiedzi słownika ma Code, bez Id. Odpowiedź kursu i konfliktu
ma CurrencyCode zamiast dawnego Code DTO kursu. Id kursu i RateId konfliktu
pozostają identyfikatorami publikacji; nigdy nie identyfikują waluty. Filtry
`code=EUR`, lookup i ustawienia posługują się kodem, bez liczbowych aliasów.

## Wdrożenie i testy ADR-023 — zapis poprzedniego etapu

Migracje firmowe: `20261022205922_CurrenciesAndNbpRates` i
`20261022211121_OptionalNbpTradingDate` (doprecyzowanie opcjonalności daty C).
System: `20261022205924_CurrencyPermissions`. Są addytywne; nie zmieniają
kontrahentów i istniejących integracji. Down usuwa słownik/kursy/audyt i nie
jest bezpiecznym wycofaniem z danymi; ograniczenie praw też wymaga analizy.

Przed migracją zatrzymano API/desktop, zweryfikowano lokalną instancję,
dokładne trzy nazwy, znaczniki, profile i znane schematy; sprawdzono SQL offline,
wykonano COPY_ONLY/CHECKSUM/VERIFYONLY. Potem jawnie `migrate-contractors` z narzędzia
DatabaseTools. Start API nie migruje. System ma 7 migracji, firmy po 6;
System nadal 14 tabel, każda firma teraz 14 tabel.

Pełny runner nadal czyści fikcyjne dane demonstracyjne i inicjalizuje je po
testach. Zastany klucz DataPort jest zachowywany wyłącznie jako szyfrogram
w pamięci procesu, po wcześniejszych zweryfikowanych kopiach wszystkich trzech
baz, i przywracany po inicjalizacji do zgodnego profilu. Nigdy nie jest
odszyfrowywany, wypisywany ani zapisywany do repozytorium. W razie przerwania
procesu dostępna jest kopia SQL sprzed testów, z niezmienioną ochroną DPAPI
poza bazą. Samodzielne `prepare-tests` nadal odmawia czyszczenia zapisanej
konfiguracji klucza; pełny `test` wykonuje zabezpieczony przebieg.

Końcowa weryfikacja 2026-10-23:

- `dotnet build Vatowiec.slnx --no-restore`: 0 ostrzeżeń, 0 błędów.
- Pełny runner: **242/242**, bez pominięć: **93 SQL/API i 149 bez SQL**.
  Raport lokalny: `TestResults/database-per-company-5d0b25e656a54102902a2bb9e2deebbb.trx`;
  log: `TestResults/nbp-acceptance-tests.log`.
- `dotnet format --verify-no-changes` oraz `git diff --check`: poprawne.
- Testy NBP: mapowanie A/B/C i precyzja, opcjonalne TradingDate, 93 dni,
  błędne daty/JSON/HTTP/timeout, 404 i brak waluty, wybór walut i PLN,
  deduplikacja i trwałe ponowienia, równoległe żądania, oba rozstrzygnięcia
  konfliktu oraz CAS, wycofanie kursów/konfiguracji przy błędzie audytu,
  niezmienność historii, CSRF, prawa, obca firma, filtry i lookup SQL.
- Testy modelu klienta obejmują pracę przez rzeczywiste API/SQL z atrapą NBP,
  brak automatycznego importu, historyczne konflikty bez HTTP, sesję, szkic
  oraz regresję stabilności listy po powiadomieniu edytora.
- Rzeczywiste okno Avalonia uruchomiono i obejrzano. Sprawdzono zmianę
  ustawień EUR i ich zapis/przywrócenie, zachowanie edytora po zapisie,
  decyzję o niezapisanym szkicu, blokadę PLN, filtr tabeli A oraz wyłączenie
  zapisu/importu w B i dla czytelnika. Początkowe zapętlenie wiązania listy
  wykryte w tej próbie poprawiono i ponownie sprawdzono w oknie.

**Próba rzeczywista:** wykonano dokładnie jeden GET oficjalnego API NBP,
przyciskiem w Avalonia, w trybie najnowszych tabel i wyłącznie dla A.
Pobrano 1 tabelę `206/A/NBP/2026`, EffectiveDate `2026-10-22`, zapisano 4
rekordy, pominięto 0, konfliktów 0, błędów 0. RetrievedAtUtc zapisanych kursów:
`2026-10-23T03:47:44.4559662+00:00`. Mid: CHF 4,7424; EUR 4,3685;
GBP 5,2133; USD 3,8886. SQL przechowuje je jako decimal(24,12).
Audyt importu nr 176 wskazuje `operator.m0`; firma B nadal ma 0 kursów.
W A pozostały też dwa audyty kontrolnej zmiany i przywrócenia konfiguracji EUR.
Zrzut obejrzanego okna: `TestResults/nbp-ui-result.png` (poza repozytorium).

Rzeczywiste B/C, dłuższe okresy, awarie i konflikty nie były odpytywane w NBP;
te przypadki sprawdzono na atrapach HTTP. Nie wykonywano dodatkowych prób
zewnętrznych MF/GUS/VIES/DataPort. Zastany szyfrogram DataPort zachowano.
Nie wykonano Commit ani Push.

## Korekta klucza waluty ADR-024, 2026-10-23

Migracja `20261023041721_CurrencyIsoCodePrimaryKey` usuwa i odtwarza tylko
ref.Currencies, core.ExchangeRates i audit.CurrencyOperations. Nie zawiera
INSERT SELECT, mapowania Id ani zachowywania demonstracyjnej historii.
Historyczne migracje pozostają, a nowe firmy otrzymują poprawny końcowy model
przez pełny łańcuch. System nadal ma 7 migracji, firmy teraz po 7; liczba tabel
pozostaje 14. Down odmawia powrotu do numerycznego klucza waluty.

Przed wykonaniem ponownie sprawdzono lokalną instancję, dokładne nazwy,
znaczniki, profile i znany schemat. Powstały zweryfikowane kopie wszystkich
trzech baz. Skrypt offline jest w `TestResults/IsoCurrencyReset.sql`.
Liczniki i SHA-256 deterministycznych snapshotów wszystkich tabel spoza modułu
były identyczne przed i bezpośrednio po DDL, także dla konfiguracji DataPort.
Nie odszyfrowywano ani nie kasowano żadnego klucza integracji. Nie wykonano
resetu innych kartotek, kont ani całych baz.

| Baza / tabela | Przed korektą | Bezpośrednio po DDL | Po ręcznych próbach |
|---|---:|---:|---:|
| A: ref.Currencies | 149 | 149 | 149 |
| A: core.ExchangeRates | 413 | 0 | 415 |
| A: audit.CurrencyOperations | 12 | 0 | 13 |
| B: ref.Currencies | 149 | 149 | 149 |
| B: core.ExchangeRates | 0 | 0 | 0 |
| B: audit.CurrencyOperations | 0 | 0 | 0 |

Stare kursy **usunięto**, następnie pobrano z oficjalnego NBP przez
uwierzytelnione API: EUR A/C za 02.01–22.10.2026, po czym A dla domyślnych
czterech walut z 22.10.2026. Nie kopiowano starych rekordów z SQL. Porównanie
potwierdziło wszystkie 413 dawnych kombinacji kod/źródło/typ/data bez różnic
w numerze tabeli, dacie notowania i Mid/Bid/Ask. Dodatkowo źródło zwróciło
205/A/NBP/2026 i 205/C/NBP/2026 dla EUR z 21.10.2026; stąd 415, nie 413.
Oryginalne Id publikacji, rowversion i UTC pobrania nie są zachowywane.
Przykład: EUR, 30.09.2026, 190/A/NBP/2026, Mid 4,3672.

Ponowny import A z 22.10: 0 nowych, 4 pominięte, brak konfliktów/błędów.
Powtórzenie tego samego OperationId zwróciło ten sam AuditId bez nowego audytu.
Nowa historia obejmuje 8 zmian ustawień podczas próby API, 3 importy oraz
2 zmiany/przywrócenia EUR w oknie. Na koniec przywrócono domyślne 4 waluty z A.
Rzeczywiste wywołania były osobną ręczną próbą, nie automatycznym testem.

Kompilacja: 0 błędów/ostrzeżeń. Testy bez SQL: 150/150 bez pominięć.
Ręczna próba SQL w A i B potwierdziła PK/FK, brak Id słownika, binarne kody,
odrzucanie małych liter/obcej waluty i trigger niezmienności kodu (próby w rollback).
Rzeczywiste HTTPS API sprawdziło kody zamiast liczb, odmowę dodatkowego pola
zmiany kodu, odmowę zapisu/importu w B i zapisu dla czytelnika oraz filtry SQL.
Avalonia uruchomiona: zapis/przywrócenie EUR oraz filtr CHF z jednym wynikiem;
obejrzano `TestResults/iso-ui-filter.png`.

Po powyższych próbach użytkownik odwołał początkowe ograniczenie resetu innych
fikcyjnych danych i zatwierdził pełny przebieg istniejącego runnera `test`.
Nie tworzymy osobnych *_Test ani nowego mechanizmu testów. Runner najpierw robi
zweryfikowane kopie, zabezpiecza szyfrogram DataPort bez odczytu sekretu,
czyści fixture, testuje i inicjalizuje demonstrator. To osobna operacja DML,
nie część migracji klucza. Automatyczne HTTP pozostają wyłącznie atrapami.

Końcowy pełny wynik: **244/244, bez pominięć — 94 SQL/API i 150 bez SQL**.
Raport: `TestResults/database-per-company-b41b723aa3f244c6abbf7a917bb74080.trx`,
log `TestResults/iso-all-tests.log`. Obejmuje nowy test fizycznego PK/FK i kodów
oraz dotychczasowe konflikty, CAS, atomowość audytu, idempotencję, uprawnienia,
sesje, CSRF, izolację i filtrowanie. Szyfrogram DataPort przywrócono.
Po czyszczeniu ponownie pobrano kursy i sprawdzono API oraz rzeczywiste okno:
stan końcowy jest zgodny z tabelą powyżej (A 149/415/13, B 149/0/0).
W Avalonia sprawdzono ponownie zapis EUR i filtr C z kupnem/sprzedażą;
zrzut `TestResults/iso-ui-final.png`. Konta demonstracyjne i fikcyjne kartoteki
zostały odtworzone; wcześniejsze demonstracyjne szczegóły usunięto zgodnie ze
zgodą na pełny reset testowy. Nie utworzono nowych baz ani nie zmieniono innych
aplikacji. Nie wykonano Commit/Push.

## Przyszłe użycie

Application.IExchangeRateReader udostępnia OnDate i Before (ściśle `<`),
bez fallbacku między A/B/C i bez połączenia z NBP. Zwraca walutę, wartości,
źródło, datę oraz numer publikacji albo brak danych. Korzysta wyłącznie z
autoryzowanego CompanyDbContext. PLN nie ma sztucznej tabeli NBP. Moduł księgowy
będzie osobno określał regułę doboru i zapisywał własny snapshot przeliczenia.

Adapter i serwis importu są oddzielone od wyzwalającego endpointu. Późniejsza
automatyzacja może wywołać ten sam autoryzowany, idempotentny proces z trwałym
aktorem technicznym, własnym harmonogramem i kontrolą błędów. Obecnie nic nie
uruchamia importu w tle, przy starcie ani otwieraniu ustawień/kartotek.
