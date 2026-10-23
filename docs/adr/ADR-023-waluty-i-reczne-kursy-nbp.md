# ADR-023 — waluty i ręczne kursy NBP

Status: wdrożony w demonstratorze 2026-10-23; decyzja na zlecenie użytkownika 2026-10-22.
Decyzja o numerycznym kluczu waluty została zastąpiona przez
[ADR-024](ADR-024-kod-iso-jako-klucz-waluty.md). Poniższy opis i wyniki zachowują
stan historycznego wdrożenia; aktualny PK waluty to Code, FK kursu CurrencyCode.
Wymaganie DOC-006, SPECYFIKACJA §5.3, database-per-company ADR-014.

## Rozwiązanie i akceptacja

Każda firma ma ref.Currencies (int IDENTITY, Code char(3) unikalny, Name,
IsActive, DownloadA/B/C, RowVersion). Trzy flagi są ustawieniami tej firmy,
w tym samym lokalnym wierszu; osobna tabela ustawień powielałaby klucz 1:1.
PLN zawsze aktywny i bez flag NBP. Słownik początkowy obejmuje waluty aktualnych
tabel A i B oraz PLN; nazwy/kody to dane referencyjne, bez kopiowania kursów.
Domyślnie tylko EUR/USD/GBP/CHF z A. Nie zakładamy dostępności waluty w danej tabeli.

core.ExchangeRates: bigint IDENTITY, CurrencyId FK lokalny, Source=NBP,
TableType A/B/C, TableNumber, EffectiveDate, TradingDate NULL (tylko C),
Mid/Bid/Ask decimal(24,12), RetrievedAtUtc i techniczna RowVersion.
Unikalność CurrencyId/Source/TableType/EffectiveDate. A/B wymagają tylko Mid,
C tylko Bid/Ask i opcjonalną datę notowania, jeśli źródło ją poda; wartości dodatnie. Odrzucamy dane o większej
precyzji, zamiast je po cichu zaokrąglać. Kursy API są za jednostkę waluty,
nie kopiujemy mnożników z prezentacji tabel HTML. Brak CompanyId i GUID jako PK.

audit.CurrencyOperations: bigint IDENTITY, rodzaj, czas UTC, trwały aktor,
historyczna nazwa i ActorIssuerId bez centralnego FK, DetailsJson. Techniczne
OperationId i RequestHash służą istniejącej zasadzie idempotencji POST;
nie są identyfikatorami walut ani kursów. Unikalny wynik operacji w zakresie
aktora/instalacji. Historia jest niezmienna w kontekście i triggerze SQL.
Konfiguracja i wynik importu (także brak nowych danych/błędy) zapisują się
atomowo ze zmianami. Nie używamy ContractorChanges ani ExternalVerifications
do historii, która nie dotyczy kontrahenta.

Import pobiera całe skonfigurowane typy tabel poza transakcją SQL, filtruje
lokalnie wybrane aktywne waluty i zapisuje pod blokadą transakcyjną firmy.
Przy zmienionej konfiguracji w czasie HTTP odmawia zapisu; użytkownik ponawia
świadomie. Powtórzenie skutecznego OperationId zwraca zapisany wynik bez HTTP.
Te same wartości/numery/daty są pomijane. Różne pozostają bez zmiany i trafiają
do wyniku/audytu jako konflikt z oryginalną wersją i propozycją NBP. Osobna
komenda wskazuje operację i pozycję konfliktu oraz świadomy wybór zachowania
starej/przyjęcia nowej wartości. Nowa rowversion wymaga ponownego porównania,
bez force-update. Nie dodajemy osobnej tabeli konfliktów.

ViewExchangeRates=64, ManageCurrencies=128, ImportExchangeRates=256 są prawami
firmy. Rozstrzygnięcie wymaga zarządzania i importu. Mutacje wymagają CSRF.
Operator A ma 511, B 67 (dotychczasowe 3 + odczyt kursów), czytelnik A 65.
Resolver nadal kontroluje rzeczywisty profil i migracje. UI czyści dane przy
zmianie firmy/odmowie/wygaśnięciu i nie ma dostępu SQL.

Adapter: stały https://api.nbp.pl/api/exchangerates/tables/, JSON, bez klucza,
przekierowań, retry ani tła. Zakres dzielony na rozłączne odcinki po najwyżej
93 dni włącznie; 404 to brak publikacji. Nie uzupełniamy weekendów/świąt ani
rzadszych publikacji B. Brak waluty i awarie są osobnymi informacjami.
UI pokazuje daty ostatnich tabel otrzymanych w danym przebiegu, a osobno
ostatnie zapisane daty A/B/C. Bez dodatkowych zapytań udających bieżący stan NBP.
Ręczny zakres ograniczamy do 10 lat na operację (ochrona przed nieograniczoną
liczbą HTTP); dłuższą historię można pobrać świadomie w kilku operacjach.

Słowniki → Waluty i kursy NBP oraz Ustawienia firmy → Waluty i kursy NBP:
stronicowana lista SQL, filtry, daty i liczby pl-PL, ustawienia w formularzu,
ręczny import najnowszy/okres, podsumowanie, jawne konflikty i historia.
Bez zmian kontrahentów i bez powiązania ich tekstowej waluty rachunku z nowym FK.
IExchangeRateReader udostępnia dokładną datę publikacji i ostatni kurs ściśle
przed datą, razem z pochodzeniem; nie stosuje reguł podatkowych/faktur.

Akceptacja: build, pełne testy SQL/API i atrap HTTP, precyzja/93 dni/404/braki,
deduplikacja, konflikt/CAS/atomowość/idempotencja, prawa i izolacja, rzeczywista
Avalonia. Jawne migracje po preflight i zweryfikowanych kopiach trzech baz;
bez migracji przy starcie API. Najwyżej jeden rzeczywisty GET NBP, ręcznie w A.
Bez Commit/Push.

Weryfikacja: build bez ostrzeżeń/błędów, 242/242 testy bez pominięć
(93 SQL/API, 149 bez SQL). Jawnie zastosowano migrację praw i dwie firmowe
po preflight oraz zweryfikowanych kopiach. Rzeczywiste okno potwierdziło
ustawienia, ochronę szkicu, PLN, filtry i odczyt bez zapisu dla B/czytelnika.
Próba wykryła i usunęła zapętlenie odtwarzanej kolekcji listy przy powiadomieniu
edytora; stabilna kolekcja i ochrona przed reentrancją mają test regresji.
Jeden rzeczywisty GET A zapisał cztery kursy z 206/A/NBP/2026 (22.10.2026)
w A; B pozostała pusta. Pozostałe warianty NBP testowano na atrapach HTTP.
[Dokładne wyniki i instrukcja](../currencies-and-nbp.md).

Źródło: [oficjalny kontrakt NBP](https://api.nbp.pl/), sprawdzony 2026-10-22;
[tabela A](https://nbp.pl/statystyka-i-sprawozdawczosc/kursy/tabela-a/),
[tabela B](https://nbp.pl/statystyka-i-sprawozdawczosc/kursy/tabela-b/).
