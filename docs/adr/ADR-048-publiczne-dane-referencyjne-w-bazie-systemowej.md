# ADR-048: publiczne dane referencyjne w bazie systemowej (kursy NBP)

Data: 2026-11-22. **Status: PRZYJĘTY — akceptacja właściciela 2026-11-22; wdrożony 2026-11-22**
(sekcja „Wdrożenie”). Decyzje właściciela w sekcji „Decyzje właściciela”.
Zmienia [ADR-023](ADR-023-waluty-i-reczne-kursy-nbp.md) i
[ADR-024](ADR-024-kod-iso-jako-klucz-waluty.md) w zakresie miejsca przechowywania kursów,
sposobu ich pobierania i praw. Wymaganie DOC-006, SPECYFIKACJA §3.12 i §5.3.
Powiązane: database-per-company (ADR-014), usługa Operations ([ADR-044](ADR-044-provisioning-i-backup-firmy.md)),
miejsce logiki ([ADR-046](ADR-046-warstwa-aplikacyjna-i-splata-dlugu.md)).

## Kontekst

ADR-023 umieścił `ref.Currencies`, `core.ExchangeRates` i `audit.CurrencyOperations`
w **każdej bazie firmy**. Kursy pobiera ręcznie użytkownik firmy z prawem
`ImportExchangeRates`, według firmowych znaczników A/B/C przy walutach; nie ma harmonogramu.

Przy wielu firmach (np. 100) oznacza to:

- te same publiczne tabele NBP pobierane i przechowywane 100 razy,
- 100 osobnych rozstrzygnięć tej samej korekty kursu przez NBP,
- 100 historii importu,
- konieczność pamiętania o pobraniu kursów w każdej firmie osobno.

Kursy NBP są danymi publicznymi, identycznymi dla wszystkich firm; firma ich nie tworzy
ani nie zmienia. Wspólny słownik w `Vatowiec_System` już istnieje: `ref.TaxIdentifierSchemes`.

**Terminy publikacji NBP** (sprawdzone 2026-11-22,
[Informacja o terminach publikacji kursów walut](https://nbp.pl/statystyka-i-sprawozdawczosc/kursy/informacja-o-terminach-publikacji-kursow-walut/)):

| Tabela | Dni | Godziny (czas polski) |
|---|---|---|
| A — kursy średnie | każdy dzień roboczy | 11:45–12:15 |
| B — kursy średnie | każda środa; gdy środa jest wolna — dzień wcześniej | 11:45–12:15 |
| C — kupno/sprzedaż | każdy dzień roboczy | 7:45–8:15 |

Kontrakt API ([api.nbp.pl](https://api.nbp.pl/), sprawdzony 2026-11-22): archiwum kursów
od 2 stycznia 2002 r.; pojedyncze zapytanie najwyżej 93 dni; brak danych dla poprawnego
zakresu = `404 Not Found`; błędne zapytanie lub przekroczony limit = `400 Bad Request`;
wyłącznie HTTPS. Zapytanie o tabelę zwraca wszystkie jej waluty naraz.

## Decyzja

### 1. Reguła podziału danych (obowiązuje także przyszłe moduły)

**W `Vatowiec_System`:** publiczne dane referencyjne, takie same dla wszystkich firm,
które firma wyłącznie odczytuje — kursy NBP, a w przyszłości np. kody pocztowe / TERYT,
kody urzędów skarbowych, kalendarz dni wolnych, PKD. Każdy kolejny zbiór danych
wymaga osobnego etapu (i ADR, jeśli wprowadza nowe źródło lub proces); ten ADR
wdraża wyłącznie kursy NBP.

**W bazie firmy:** dane, ustawienia i decyzje firmy (w tym to, które waluty firma widzi
na listach wyboru), otwarte słowniki firmy (`ref.AddressTypes`, `ref.PaymentMethods`)
oraz wszystko, co jest dowodem księgowym.

**Dokument zapisuje kopię zastosowanego kursu** (DOC-006, SPECYFIKACJA §5.3): walutę,
wartość, typ i numer tabeli, datę publikacji i źródło — nie klucz obcy do tabeli kursów
w System. Dzięki temu:

- niezależny backup/restore firmy (§3 pkt 11) pozostaje samowystarczalny,
- księgowanie (§3 pkt 10) nie wymaga transakcji obejmującej System: kurs jest
  odczytywany przed transakcją w bazie firmy,
- późniejsza korekta kursu w System nie zmienia zaksięgowanego dokumentu.

Baza firmy może leżeć na innej instancji SQL. API odczytuje dane referencyjne własnym
połączeniem z System; **zakazane są** zapytania między bazami (JOIN, FK, synonimy,
widoki łączące System z bazą firmy). Gdy potrzebne są dane z obu baz (np. kursy walut
aktywnych w firmie), API najpierw czyta listę kodów z bazy firmy, a potem kursy z System.

### 2. Model danych

**`Vatowiec_System`** — wyłącznie dane wspólne, bez ustawień poszczególnych firm:

- `ref.Currencies` — główny słownik ISO 4217: `Code char(3)` PK (porównanie binarne,
  wielkie litery, niezmienny, jak w ADR-024), `Name`. Seed 151 pozycji migracją (149 + BGN i ANG z historii NBP od 2025);
  zmiany słownika wyłącznie migracją.
- `ref.ExchangeRates` — struktura dotychczasowego `core.ExchangeRates` bez zmian
  merytorycznych: `bigint IDENTITY`, `CurrencyCode` FK, `Source = NBP`, `TableType`
  A/B/C, `TableNumber`, `EffectiveDate`, `TradingDate` (tylko C), `Mid/Bid/Ask
  decimal(24,12)`, `RetrievedAtUtc`, `RowVersion`; te same CHECK i unikalność
  `CurrencyCode/Source/TableType/EffectiveDate`. Bez `CompanyId`.
- `ops.ExchangeRateSync` — stan pobierania, jeden wiersz instalacji (`SingletonKey = 1`):
  `HistoryStartDate` (NULL = domyślna, utrwalana przy pierwszym przebiegu),
  `CheckedThroughA/B/C` (ostatni dzień zamknięty dla tabeli, patrz pkt 3),
  `RunRequestedAtUtc` (ręczne zlecenie), `LastAttemptUtc`, `LastSuccessUtc`,
  `LastErrorCode`, `RowVersion`.
- `ops.ExchangeRateConflicts` — rozbieżności danych NBP z zapisanym kursem: `RateId` (FK),
  propozycja NBP (numer tabeli, data notowania, Mid/Bid/Ask), `IncomingHash`,
  `DetectedAtUtc` oraz rozstrzygnięcie (`ResolvedAtUtc`, `Decision`
  AcceptIncoming/KeepExisting, `ResolvedById/Name`), `RowVersion`. Unikalność
  `RateId/IncomingHash`: ta sama propozycja, wykrywana ponownie przez zakładkę kolejnych
  przebiegów, jest zapisywana raz (także po rozstrzygnięciu). Odrębna tabela jest
  potrzebna, bo przebiegi automatyczne powtarzają ostatni tydzień — w ADR-023 konflikt
  żył tylko w wyniku pojedynczego ręcznego importu.
- `audit.ExchangeRateOperations` — niezmienna historia (EF + trigger SQL, jak
  dotychczasowe `audit.CurrencyOperations`): odcinki pobierania, które coś zmieniły
  (liczba publikacji, nowych, pominiętych, konfliktów, kody spoza słownika), nieudane
  pobrania z opisem błędu, zlecenia „Pobierz teraz”, zmiany daty początkowej historii
  i rozstrzygnięcia konfliktów (wartość przed/po). Przebieg automatyczny zapisuje aktora
  technicznego „VATowiec Operations (kursy NBP)” (`ActorId` NULL), działania
  administratora — jego `UserId` i login.

**Baza firmy:**

- `ref.Currencies` zostaje: lokalny, seedowany migracją słownik ISO (`Code`, `Name`)
  z firmowym **`IsActive`** (widoczność waluty na listach wyboru) i `RowVersion`. Jest też
  celem kluczy obcych z `ContractorTradeTerms` i `ContractorCreditLimits`. Usuwamy
  wyłącznie `DownloadA/B/C`. PLN zawsze aktywny. Domyślnie aktywne: PLN, EUR, USD, GBP, CHF.
- `audit.CurrencyOperations` zostaje dla zmian aktywności walut firmy; wpisy importów
  i konfliktów nie są już w niej tworzone.
- Usuwamy `core.ExchangeRates`.

### 3. Pobieranie przez usługę Operations

**Kto łączy się z NBP:** wyłącznie usługa VATowiec Operations. Zwykłe API przestaje
wykonywać żądania do NBP (mniej ruchu wychodzącego z procesu obsługującego użytkowników).
Adapter jak w ADR-023: stały adres
`https://api.nbp.pl/api/exchangerates/tables/{A|B|C}/{od}/{do}/`, JSON, bez klucza,
bez przekierowań, z limitem czasu, odrzucanie danych o większej precyzji niż
`decimal(24,12)` zamiast cichego zaokrąglania. Awarie przejściowe (fałszywe 404 HTML,
429, 5xx, timeout, błąd połączenia) są ponawiane w ramach przebiegu: do 3 prób odcinka
co 5 s (`AttemptsPerChunk`, `RetryDelaySeconds`; decyzja właściciela 2026-11-22 po
pierwszym rzeczywistym przebiegu). „Brak danych”, 400 i niepoprawne dane — bez ponowień.

**Co pobieramy:** zawsze wszystkie trzy tabele A, B i C i **wszystkie waluty** w nich
zawarte. Nie ma ustawień firm ani instalacji wybierających tabele lub waluty. Kod spoza
słownika `ref.Currencies` w System jest pomijany i odnotowany w historii przebiegu
(słownik uzupełnia migracja). Skala: najwyżej trzy zapytania dziennie (plus ponowienia),
szacunkowo ok. 17 tys. wierszy rocznie dla A+B+C. Firma, która włączy nową walutę,
od razu widzi jej historię od daty początkowej.

**Czas:** strefa `Europe/Warsaw` (Windows: `Central European Standard Time`), niezależnie
od strefy serwera, bo NBP publikuje według czasu polskiego. (Harmonogram kopii z ADR-044
nadal używa czasu lokalnego serwera.)

**Kiedy przebieg jest należny** (sprawdzane w istniejącym rytmie harmonogramu usługi,
`Operations:ScheduleMinutes`, domyślnie co 5 minut):

1. **Nadrabianie zaległości:** punkt kontrolny którejkolwiek tabeli jest starszy niż
   przedwczoraj (czyli wczorajszy przebieg dzienny się nie odbył) albo go brak — przebieg
   od razu, o dowolnej godzinie (np. po starcie usługi, która nie działała przez kilka dni).
2. **Przebieg dzienny:** od 13:00 (`Operations:ExchangeRates:DailyHour`, domyślnie 13),
   jeżeli dzisiejsza tabela A nie została jeszcze otrzymana.
3. **Ponowienia:** gdy dzisiejszej tabeli A nadal brak, kolejna próba najwcześniej
   po 60 minutach, ostatnia w godzinie 16:00 (`Operations:ExchangeRates:LastHour`).
   Później dzień uznaje się za dzień bez publikacji (weekend, święto) i nie ponawia się.
   Wyjątek: usługa uruchomiona po 16:00, która dziś nie próbowała w oknie, wykonuje jedną
   próbę.
4. **Ręczne zlecenie** administratora instalacji („Pobierz teraz”) — API zapisuje
   `RunRequestedAtUtc`, przebieg wykonuje usługa przy najbliższym sprawdzeniu harmonogramu
   (do 5 minut). Zlecenie jest zużywane niezależnie od wyniku.
5. Po nieudanej próbie (poza ręcznym zleceniem) kolejna najwcześniej po 60 minutach,
   także przy zaległości.

Nie prowadzimy kalendarza świąt: brak publikacji wynika z odpowiedzi NBP.

**Zakres przebiegu:** dla każdej tabeli od `CheckedThroughDate − 7 dni` (lub od
`HistoryStartDate`, gdy brak punktu kontrolnego) do dziś. Siedmiodniowa zakładka nie
kosztuje dodatkowych żądań przy przebiegu dziennym, wychwytuje publikacje spóźnione
po 16:00 i korekty NBP z ostatniego tygodnia (jako konflikty). Zakres dzielony na odcinki
po najwyżej 93 dni włącznie; najwyżej 40 odcinków (ok. 10 lat) na tabelę w jednym
przebiegu — resztę dociąga następny przebieg.

**Punkt kontrolny:** każdy odcinek zapisuje się w jednej transakcji System: nowe kursy,
nowe konflikty, wpis historii (gdy odcinek coś zmienił) i przesunięcie punktu kontrolnego
tabeli — do końca odcinka, ale nigdy dalej niż wczoraj i nigdy wstecz (dzień bieżący nie
jest zamykany). Zmiana daty początkowej przez administratora w trakcie przebiegu przerywa
go bez zapisu odcinka. `404` = brak publikacji w odcinku, poprawny
wynik. Błąd sieci, timeout, `400`, `5xx` lub niepoprawny JSON przerywa przebieg dla tej
tabeli bez przesuwania punktu kontrolnego; następna próba w kolejnym należnym przebiegu
(najwyżej raz na 60 minut). Przerwany przebieg wznawia się od ostatniego zapisanego odcinka.

**Data początkowa historii:** `HistoryStartDate` domyślnie 1 stycznia poprzedniego roku
(pokrywa dokumenty z przełomu roku i korekty poprzedniego roku). Administrator instalacji
może przesunąć ją wstecz (najwcześniej 2002-01-02); dociągnięcie historii wykonuje usługa,
w operacjach obejmujących najwyżej 10 lat (limit z ADR-023).

**Deduplikacja i konflikty:** identyczne wartości są pomijane. Różna wartość dla tego
samego klucza (`CurrencyCode/Source/TableType/EffectiveDate`) nigdy nie nadpisuje
zapisanego kursu: trafia do `ops.ExchangeRateConflicts`. Rozstrzyga administrator
instalacji (zachowaj / przyjmij wartość NBP) z CSRF, wersją konfliktu i wersją kursu oraz
wpisem historii — bez force-update, jak w ADR-023, ale raz dla całej instalacji. Także
„zachowaj” zmienia wersję kursu, więc inne propozycje dla tego kursu wymagają ponownego
porównania.

**Współbieżność:** jeden aktywny proces Operations (jak w ADR-044); przebieg kursów pod
blokadą aplikacyjną SQL (`sp_getapplock`), aby przebieg ręczny i harmonogram się nie
nakładały.

**Logi:** bez treści odpowiedzi NBP i szczegółów błędów SQL; kod błędu i liczby.

### 4. Prawa i API

- **Odczyt kursów:** endpointy w kontekście firmy zostają (`ViewExchangeRates = 64`)
  i czytają kursy z System. Filtr domyślnie obejmuje waluty aktywne w firmie (z bazy firmy),
  opcja archiwum — pozostałe waluty mające kursy. Ekran pokazuje datę ostatniej otrzymanej
  tabeli A/B/C i stan ostatniego przebiegu.
- **Aktywność walut firmy:** `ManageCurrencies = 128`, zapis w bazie firmy z CSRF,
  `RowVersion` i `audit.CurrencyOperations` — jak dotąd, bez znaczników A/B/C.
- **`ImportExchangeRates = 256` zostaje wycofane:** usunięte z katalogu praw i z istniejących
  `CompanyAccess` (migracja czyści bit), bit zarezerwowany — nie używa się go ponownie
  do innego prawa.
- **Administrator instalacji** (rola sprawdzana świeżo w DB, ADR-033): Ustawienia instalacji →
  Kursy NBP — stan pobierania, „Pobierz teraz”, data początkowa historii, rozstrzyganie
  konfliktów, historia przebiegów. Brak automatycznych praw do danych firm.
- `IExchangeRateReader` (dokładna data publikacji, ostatni kurs ściśle przed datą,
  z pochodzeniem) czyta z System, niezależnie od aktywności waluty w firmie. Nadal bez
  reguł podatkowych doboru kursu.

### 5. Cykl życia firmy

System nie przechowuje żadnych danych walutowych konkretnej firmy, więc provisioning,
kopia, odtworzenie i usuwanie firmy (ADR-044/045) nie wymagają zmian w tym zakresie.
Aktywność walut jest w bazie firmy i przechodzi razem z jej kopią, także do innej instalacji.
Usunięcie firmy nie dotyka wspólnych kursów.

### 6. Migracje

- **System — addytywna:** `ref.Currencies` z seedem, `ref.ExchangeRates`,
  `ops.ExchangeRateSync` (pusty stan, domyślna data początkowa),
  `audit.ExchangeRateOperations` z triggerem; czyszczenie bitu 256 w `CompanyAccess`.
- **Firma — usuwająca:** `core.ExchangeRates`, kolumny `DownloadA/B/C` w `ref.Currencies`,
  dotychczasowe wpisy importów i konfliktów w `audit.CurrencyOperations`. Dopuszczalne,
  ponieważ wszystkie bazy są developerskie (AGENTS.md), a kursy nie są jeszcze dowodem
  księgowym — nie ma dokumentów walutowych. Kursy pobierze ponownie usługa. Gdyby taka
  zmiana dotyczyła instalacji produkcyjnej, wymagałaby przeniesienia danych — poza
  zakresem tego ADR.
- Migracje jawne, bez wykonywania przy starcie API; aktualizacja istniejących baz firm
  według obecnej procedury.

### 7. Poza zakresem

- Reguły doboru kursu dla faktury, różnice kursowe (DOC-006 / M1).
- Inne dane referencyjne (kody pocztowe, TERYT itd.) — reguła z pkt 1 dotyczy ich
  przyszłego wdrożenia.
- Kopiowanie kursów do baz firm (poza kopią kursu w dokumencie).
- Kalendarz świąt, drugie źródło kursów (np. EBC), fallback.

## Konsekwencje

Pozytywne:

- jedno pobranie dziennie niezależnie od liczby firm (trzy zapytania plus ponowienia),
  jedna historia, jedno rozstrzygnięcie korekty NBP;
- kursy dostępne bez działania użytkownika, z nadrabianiem po przerwie w pracy usługi;
- nowo włączona waluta od razu ma historię; brak ustawień pobierania do utrzymania;
- ustawienia walut firmy zostają w jej bazie i jej kopii;
- zwykłe API nie łączy się z NBP.

Negatywne / ryzyka:

- kopia firmy nie zawiera kursów (dokumenty mają własne kopie kursów; kursy publiczne
  można pobrać ponownie);
- konto usługi Operations potrzebuje wychodzącego HTTPS do `api.nbp.pl` (zapora,
  serwer proxy — do opisania w dokumentacji wdrożenia);
- świeżość kursów zależy od działania usługi Operations; stan ostatniego przebiegu
  musi być widoczny dla administratora i w ekranie kursów.

## Kryteria akceptacji

1. Testy z kontrolowanym zegarem (strefa Warszawy): brak przebiegu dziennego przed 13:00;
   przebieg od 13:00; ponowienia najwyżej co 60 minut do 16:00 przy braku dzisiejszej
   tabeli A; brak ponowień po 16:00; sobota; środa wolna (tabela B we wtorek);
   zmiana czasu letni/zimowy.
2. Nadrabianie: punkt kontrolny sprzed 200 dni → trzy odcinki ≤ 93 dni; awaria w drugim
   odcinku → punkt kontrolny po pierwszym, wznowienie od drugiego; dzień bieżący nigdy
   nie jest zamykany; pierwsze uruchomienie od 1 stycznia poprzedniego roku.
3. Zapis wszystkich walut z tabel A/B/C; kod spoza słownika pominięty i odnotowany.
4. `404` jako brak danych; `400`/`5xx`/timeout/niepoprawny JSON jako błąd bez przesunięcia
   punktu kontrolnego; nadmiarowa precyzja odrzucona.
5. Deduplikacja; konflikt bez nadpisania; rozstrzygnięcie wyłącznie przez administratora
   instalacji z CSRF/`RowVersion`/historią; niezmienność historii (EF i trigger).
6. Prawa: `ViewExchangeRates` czyta, `ManageCurrencies` zmienia aktywność walut wyłącznie
   własnej firmy (w jej bazie), bit 256 usunięty z `CompanyAccess` i katalogu; projekt API
   nie zawiera klienta HTTP NBP.
7. Ekran kursów firmy: filtr walut aktywnych z bazy firmy, kursy z System, bez zapytań
   między bazami.
8. Automatyczne testy HTTP wyłącznie na atrapach; jeden ręczny rzeczywisty przebieg.
9. Build Debug bez ostrzeżeń, `dotnet format --verify-no-changes`, pełny runner
   `DatabaseTools test` bez pominięć.

## Wdrożenie (2026-11-22)

- Migracja System `20261122202253_PublicExchangeRates` (addytywna): `ref.Currencies`
  (149 kodów; BGN i ANG dodane migracją `ReferenceCurrenciesBgnAng`), `ref.ExchangeRates`, `ops.ExchangeRateSync`, `ops.ExchangeRateConflicts`,
  `audit.ExchangeRateOperations` z triggerem niezmienności, trigger niezmienności kodu ISO,
  czyszczenie bitu 256 w `security.CompanyAccess`.
- Migracja firmy `20261122202256_ExchangeRatesMovedToSystem` (usuwająca): `core.ExchangeRates`,
  kolumny `DownloadA/B/C`, wpisy `Import`/`ResolveConflict` w `audit.CurrencyOperations`.
  Zmiana seedu aktywności (5 walut domyślnie aktywnych) nadpisuje aktywność pozostałych
  144 walut w istniejących bazach firm na „nieaktywna” — dopuszczalne wyłącznie dlatego,
  że wszystkie bazy są developerskie.
- Kod: reguły harmonogramu `ExchangeRateSchedule` (Application, testowane z zegarem),
  `ExchangeRateSyncService` (pobieranie, wyłącznie usługa Operations) i
  `ExchangeRateAdministrationService` (API, administrator instalacji) w
  `Core.Infrastructure.ExchangeRates`; `CurrencySettingsService` (aktywność walut firmy).
  Klient NBP rejestruje tylko `AddNbpClient()` w usłudze Operations.
- API: `/api/v1/installation/exchange-rates` (GET, `/history`, `/run`, `/history-start`,
  `/conflicts/{id}/resolve`); w firmie bez `/exchange-rates/imports`.
- Avalonia: Słowniki → Kursy walut NBP (tylko odczyt i stan pobierania), Ustawienia firmy →
  Waluty (aktywność), Ustawienia instalacji → Dane referencyjne → Kursy NBP
  (`ExchangeRateAdministrationViewModel`, ADR-046).
- **Fałszywe 404 NBP (pierwszy rzeczywisty przebieg, 2026-11-22).** Serwer api.nbp.pl
  chwilowo odpowiada `404` ze stroną HTML serwera WWW („404 - File or directory not
  found”) dla zakresu, który chwilę później zwraca dane; prawdziwy brak publikacji to
  tekst `404 NotFound - Not Found - Brak danych`. Pierwotny adapter (ADR-023) traktował
  każde 404 jako brak publikacji — tabela A została pominięta, a punkt kontrolny
  przesunięty. Adapter uznaje teraz za brak publikacji wyłącznie tekstową odpowiedź
  z „Brak danych”; inne 404 są awarią (punkt kontrolny bez zmian, ponowienie po 60 min).
  Testy jednostkowe i SQL obejmują oba warianty.
  Pomiar 2026-11-22: ok. 30% żądań kończyło się fałszywym 404 — właściciel zatwierdził krótkie
  ponowienia w ramach przebiegu (do 3 prób co 5 s), opisane w pkt 3.
- **Uzupełnienie słownika (2026-11-22).** Pierwszy rzeczywisty przebieg odnotował kody spoza słownika:
  BGN (tabela A do końca 2025) i ANG (tabela B). Migracje `20261122215503_ReferenceCurrenciesBgnAng`
  (System) i `20261122215505_CurrenciesBgnAng` (firma, nieaktywne) dodają je. **Reguła:** migracja
  dodająca kody do słownika System zeruje punkty kontrolne `ops.ExchangeRateSync` — usługa
  pobiera wtedy tabele od daty początkowej i uzupełnia historię nowych kodów, a zapisane kursy
  pomija jako identyczne.
- Instrukcja i wyniki: [docs/currencies-and-nbp.md](../currencies-and-nbp.md).

## Decyzje właściciela (2026-11-22)

1. Kursy przenosimy do `Vatowiec_System`; pobiera je usługa Operations codziennie około
   13:00, z nadrabianiem okresu, w którym usługa nie działała.
2. Data początkowa historii przy pierwszym uruchomieniu: 1 stycznia poprzedniego roku.
3. Zapisujemy wszystkie waluty z pobieranych tabel (nie tylko wybrane w firmach).
4. Ponowienia: od 13:00 najwyżej co 60 minut, ostatnia próba o 16:00.
5. Widoczność (aktywność) walut zostaje w bazie firmy.
6. Usługa zawsze pobiera wszystkie trzy tabele A, B i C; brak ustawień wyboru tabel.
7. Awarie przejściowe NBP ponawiane w ramach przebiegu: do 3 prób odcinka co 5 sekund.
