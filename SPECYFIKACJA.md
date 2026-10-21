# Otwarty następca VATowca — specyfikacja produktu i architektury

**Wersja dokumentu:** 0.1.0 (wersja inicjalna, do przeglądu)  
**Data:** 2026-10-21  
**Status:** projekt założeń; nie jest potwierdzeniem zgodności podatkowej gotowego oprogramowania  
**Nazwa projektu:** robocza „Otwarty następca VATowca”; docelowa nazwa i użycie nazwy „VATowiec” wymagają odrębnej decyzji i sprawdzenia praw  
**Główna platforma:** Windows; klient i serwer projektowane także z myślą o Linux  
**Główny stos:** C# / .NET 10, Avalonia UI, ASP.NET Core, Microsoft SQL Server

> **Cel:** stworzyć bezpłatny w podstawowym użyciu, publicznie rozwijany i realnie rozszerzalny system księgowo-podatkowy dla polskich mikroprzedsiębiorców, jednoosobowych działalności gospodarczych oraz — docelowo — biur rachunkowych. Priorytetem są KPiR, ryczałt, VAT, JPK, deklaracje i KSeF, a nie pełna księgowość.

---

## 1. Podstawa i granice specyfikacji

Dokument łączy trzy rodzaje informacji:

- **[USTALONE]** — decyzje podjęte w rozmowie: księgowość uproszczona (KPiR i ryczałt), rozliczenia VAT i PIT, JPK i deklaracje, wielostanowiskowość, możliwość tworzenia dodatków bez modyfikacji programu, preferowany SQL Server, dopuszczalne bezpłatne komponenty zamknięte.
- **[REFERENCJA]** — inspiracje funkcjonalne z dokumentu *VATowiec. Instrukcja obsługi, Windows/DBF 3.84* Piotra Kowaluka (71 stron). Jest to historyczna instrukcja działania **cudzego** programu, nie dokumentacja nowego projektu ani aktualne źródło prawa podatkowego.
- **[PROPOZYCJA]** — zaprojektowane tu rozwiązania architektury, modelu danych, zakresu etapów i interfejsów. Można je zmieniać w procesie ADR (Architecture Decision Records).

Części dotyczące podatków i formatów elektronicznych trzeba dodatkowo weryfikować według **daty obowiązywania**, oficjalnych XSD/API i aktualnych przepisów. Historycznych mechanizmów VATowca (np. VAT-7 w formularzach IPS, odliczania składki zdrowotnej według dawnych zasad) **nie implementujemy jako dzisiejszych reguł**.

### 1.1. Inspiracje z dokumentacji oryginalnego VATowca

| Funkcja / zasada | Miejsce w instrukcji | Wniosek dla następcy |
|---|---|---|
| Wiele firm; wspólny silnik dokumentów, VAT, KPiR i FK | s. 3, 6, 12–14 | Wspólny dokument źródłowy oraz niezależne projekcje podatkowe |
| Kategorie księgi dokumentów i rejestrów VAT | s. 49–50 | Konfigurowalne, audytowalne reguły kwalifikacji |
| Księga dokumentów jako rdzeń ewidencji | s. 6, 12–13 | Spójne powiązania dokumentów i wpisów podatkowych |
| Rozliczenia, kompensaty, bank i kasa | s. 19–20 | Osobny, powiązany moduł rozrachunków |
| Autodekrety i wielopoziomowy plan kont | s. 39, 42 | Ogólna idea automatyzacji; FK poza początkowym zakresem |
| Raporty definiowane i formuły | s. 36–38 | Raporty i pola obliczeniowe dostępne bez zmiany kodu |
| Edytowalne kolumny, skróty klawiaturowe | s. 3, 9–10 | Interfejs przyjazny masowemu wprowadzaniu danych |
| Formularze IPS / eksport danych według definicji | s. 67–68 | Konfigurowalne mapowanie XML → formularz / PDF / obcy system |
| Historia zmian i uprawnienia | s. 10, 62 | Pełny audyt i role, bez klienckiego omijania autoryzacji |
| Opis tabel, powiązań i archiwizacji | s. 69–70 | Udokumentowany schemat i odtwarzalne kopie zapasowe |

**Szczególne wymaganie użytkownika:** w pierwotnym VATowcu było możliwe samodzielne przygotowanie XML i otwarcie na jego podstawie wypełnionego formularza ministerialnego PDF (rozwiązanie współtworzone przez użytkownika w 2016 r.). W nowym programie odpowiednik tej elastyczności musi być funkcją **pierwszoplanową**, a nie przypadkowym obejściem.

### 1.2. Czego nie kopiujemy

Nie kopiujemy oryginalnego kodu, binariów, zasobów graficznych, chronionych treści dokumentacji ani nie przenosimy automatycznie historycznej bazy DBF. Nowe moduły tworzymy samodzielnie, na podstawie zebranych wymagań użytkowych i publicznych standardów. Analiza zachowania legalnie posiadanego programu może służyć do opisania wymagań i porównania rezultatów; ewentualne szczegółowe działania dekompilacyjne wymagają odrębnej oceny prawnej. Nie zakładamy prawa do nazwy i znaków oryginalnego programu.

---

## 2. Cele i mierniki powodzenia

### 2.1. Cele produktu

1. **Prowadzenie jednej firmy bez abonamentu** i bez przymusowego przekazywania danych dostawcy aplikacji.
2. **Wielostanowiskowa praca od początku**: dwie lub więcej osób może równolegle wystawiać dokumenty i prowadzić rozliczenia, również dla firmy jednoosobowej.
3. **KPiR i ryczałt** z rzeczywistymi zapisami, korektami, rozliczeniem okresów, naliczaniem podatków i kontrolą zgodności.
4. **VAT**: ewidencje sprzedaży i zakupów, klasyfikacja transakcji, odpowiednie deklaracje/JPK, korekty, wysyłka i UPO.
5. **KSeF**: obsługa faktur ustrukturyzowanych i ich powiązania z dokumentem gospodarczym.
6. **Otwartość funkcjonalna**: raporty, reguły mapowania, dodatkowe formularze, importery, eksportery, skrypty i wtyczki przez stabilne API.
7. **Trwałość danych**: audyt, backup/restore, możliwość eksportu kompletu danych i niezależność od dostawcy usługi chmurowej.
8. **Dobra ergonomia** dla użytkowników księgujących duże serie dokumentów: szybka klawiatura, tabele, filtry i skróty.

### 2.2. Kryterium gotowości do użytku produkcyjnego

Program **nie** jest gotowy produkcyjnie tylko dlatego, że ma działający interfejs, bazę lub generuje poprawny składniowo XML. Oznaczenie wersji jako produkcyjnej wymaga **kompletnego scenariusza dla zadeklarowanego typu firmy i okresu**: wprowadzenie danych → ewidencje → obliczenia podatków → korekty → wymagane pliki i formularze → podpis/wysyłka → status/UPO → kopia i odtworzenie danych. Muszą istnieć testy automatyczne i merytoryczne przypadków granicznych oraz weryfikacja z ekspertem podatkowym.

### 2.3. Wyłączenia z początkowego zakresu

- Pełna księgowość (dziennik, rozbudowany plan kont, bilans, rachunek zysków i strat, konsolidacja).
- Kadry, płace, Płatnik i kompletny moduł ZUS (ewidencja opłaconych składek do obliczeń podatkowych **tak**).
- Magazyny, produkcja, zamówienia i POS/fiskalizacja.
- Integracja z każdym bankiem oraz automatyczne uzgadnianie wszystkich płatności.
- Własna infrastruktura SaaS i hosting danych klientów.
- Gwarancja importu danych ze wszystkich wersji DBF VATowca.

Wyłączenie oznacza brak w pierwszym wydaniu, nie zakaz dodania później.

---

## 3. Odbiorcy, organizacje i uprawnienia

### 3.1. Role użytkowników

- **Właściciel firmy**: dostęp do rozliczeń, zatwierdzeń, uprawnień i danych własnej firmy.
- **Księgowa/księgowy**: ewidencje, kontrola, rozliczenia, przygotowanie JPK i deklaracji w zakresie nadanych ról.
- **Sprzedawca**: wystawianie dokumentów sprzedaży, dostęp ograniczony do części handlowej.
- **Osoba współpracująca**: określone moduły, np. kosztowe, zakupowe, płatności.
- **Administrator techniczny**: instalacja, backup, użytkownicy; brak domyślnego dostępu do wszystkich tajemnic podatkowych bez wyraźnego nadania.
- **Administrator biura rachunkowego** (etap kolejny): przydziały operatorów i firm.

### 3.2. Model wielofirmowości

- Instalacja serwera może obsługiwać **wiele firm** i wielu użytkowników.
- Każda firma ma własne ustawienia podatkowe, kalendarz rozliczeń, numeracje, uprawnienia oraz ewidencję.
- Każdy rekord firmowy ma identyfikator `CompanyId`; serwer automatycznie ogranicza zapytania do uprawnionej firmy.
- Eksport i odtworzenie danych **jednej firmy** powinny być możliwe bez konieczności przenoszenia całej instalacji; techniczna metoda odtworzenia per firma zostanie uściślona w ADR (pełna baza / eksport logiczny).
- Użytkownik może posiadać różne role w różnych firmach.

### 3.3. Wielostanowiskowość — wymóg bezwarunkowy

- Minimum dwa komputery w sieci lokalnej i jednoczesna praca dwóch operatorów.
- Wspólna baza i wspólny stan dokumentów widoczny po odświeżeniu/zdarzeniu.
- Równoczesna edycja tego samego dokumentu nie może niezauważenie nadpisywać zmian: mechanizm `rowversion`/ETag i komunikat konfliktu.
- Numeracja dokumentów musi być odporna na kolizje oraz wycofania transakcji.
- Komunikacja klient–serwer przez HTTPS i uwierzytelnione API; **żadnych poświadczeń SQL w aplikacji użytkownika**.
- Dostęp spoza LAN przez prywatną sieć/VPN albo prawidłowo zabezpieczone publiczne API; nie wystawiamy portu SQL Server do Internetu.
- Przerwy w łączności nie mogą powodować „połowicznego zaksięgowania”. Tryb offline z kolejką synchronizacji nie należy do zakresu 1.0 — tylko wyraźna obsługa zerwanego połączenia.

---

## 4. Decyzje technologiczne

| Element | Decyzja / status | Uzasadnienie |
|---|---|---|
| Język i runtime | **C# / .NET 10** [PROPOZYCJA] | Wydajne tworzenie, testowanie i utrzymanie aplikacji biznesowej |
| Klient desktopowy | **Avalonia UI** [PROPOZYCJA] | Windows i Linux, dobre tabele i formularze |
| Serwer | **ASP.NET Core Web API** [PROPOZYCJA] | Centralna logika, autoryzacja, dostęp z wielu stanowisk, integracje |
| Główna baza | **Microsoft SQL Server 2025 Express** [USTALONE co do preferencji SQL Server, wersja PROPOZYCJA] | Centralna, transakcyjna baza, wygodne narzędzia administracyjne |
| Rozszerzanie bazy | **SQL Server Standard** bez zmian aplikacji [PROPOZYCJA] | Skalowanie po wyczerpaniu limitów Express |
| Dostęp do danych | **EF Core** dla CRUD/migracji + jawne SQL, gdy potrzebne [PROPOZYCJA] | Zachowujemy pełną kontrolę nad zapytaniami i wydajnością |
| Testy | **xUnit**, testy integracyjne na prawdziwym SQL Server [PROPOZYCJA] | Powtarzalna kontrola obliczeń i transakcji |
| API | OpenAPI, wersjonowane endpointy + zdarzenia integracyjne | Możliwość rozbudowy programu bez modyfikacji rdzenia |
| Licencja źródeł | **MPL-2.0 do zatwierdzenia** [PROPOZYCJA] | Open source z możliwością łączenia z odrębnymi komponentami własnościowymi |

**SQL Server Express:** Microsoft dokumentuje limit 50 GB dla SQL Server 2025 Express w głównej tabeli edycji, natomiast osobna dokumentacja *SQL Server 2025 on Linux* podawała w dniu sporządzania specyfikacji 10 GB. Nie uzależniamy projektu od jednego z tych limitów bez weryfikacji konkretnej platformy, wydania i aktualnej licencji. SQL Server Express nie ma usługi SQL Server Agent — automatyzacja kopii zapasowych musi działać przez własny serwis / harmonogram OS. Rozmiar plików PDF w bazie ograniczamy poprzez przechowywanie załączników poza tabelami transakcyjnymi.

**Alternatywne DBMS:** nie implementujemy PostgreSQL w wersji 1.0, ale logika podatkowa nie może zależeć od `DbContext`, T-SQL ani typu SQL Server. Ewentualny adapter PostgreSQL pojawi się dopiero po stabilizacji modelu; nie obiecujemy pełnej przenośności zapytań od pierwszego commita.

### 4.1. Topologia

```text
Komputer A: klient Avalonia ─┐
                             ├──HTTPS──> ASP.NET Core API + silniki domenowe
Komputer B: klient Avalonia ─┘                       │
                                                   ├── SQL Server (dane)
Program zewnętrzny ─── OAuth/API key* ────────────>├── Magazyn załączników (plikowy)
                                                   └── Kolejka zadań/Outbox
* metodę uwierzytelnienia integracji określamy osobno
```

Serwer aplikacji i SQL Server mogą działać na tym samym komputerze. Klient nigdy nie otwiera bazy SQL bezpośrednio.

### 4.2. Architektura kodu

**Modularny monolit**, nie mikrousługi. Jeden wdrażalny serwer ze ścisłymi granicami domeny:

1. `Core` — firmy, osoby, użytkownicy, role, okresy, identyfikatory, audyt.
2. `Documents` — dokument źródłowy, pozycje, załączniki, wersje, zatwierdzenia.
3. `Sales` / `Purchases` — wystawianie i zakup faktur, korekty.
4. `Tax.Vat` — ewidencja i kalkulacje VAT.
5. `Tax.Income` — KPiR, ryczałt, zaliczki, odliczenia.
6. `Tax.Reporting` — JPK, deklaracje, formularze, podpisy, wysyłki i UPO.
7. `Integrations.Ksef` — adapter KSeF 2.0.
8. `Payments` — należności, zobowiązania, powiązania płatności.
9. `Extensions` — definicje, pakiety, uprawnienia dodatków, zdarzenia, API.
10. `Infrastructure` — SQL, pliki, kryptografia, logging, hosting.

Warstwy: `Domain` (bez ORM/UI) → `Application` (przypadki użycia, transakcje) → `Infrastructure` (persystencja/transport) → `API/UI`.

---

## 5. Zasady modelowania dokumentów i księgowania

### 5.1. Jedno źródło, wiele skutków

`Document` jest źródłem zdarzenia gospodarczego. Rejestr VAT, KPiR, ryczałt, rozrachunki oraz raporty są osobnymi, kontrolowanymi zapisami/projekcjami powiązanymi z dokumentem źródłowym. Zatwierdzenie dokumentu może wygenerować wiele skutków o **różnych datach** i zasadach podatkowych.

Nie zakładamy, że:
- data wystawienia = data sprzedaży = obowiązek VAT = rozpoznanie przychodu = data zapłaty;
- kwota brutto = podstawa PIT;
- cała faktura ma jedną stawkę VAT lub jedną stawkę ryczałtu;
- faktura zapłacona jest automatycznie zaksięgowana w tym samym okresie.

### 5.2. Cykl życia dokumentu

`Draft` → `Validated` → `Posted` → `Corrected` / `CancelledWhereAllowed`.

- **Roboczy:** edytowalny, niezobowiązujący podatkowo.
- **Zweryfikowany:** przechodzi walidacje strukturalne, biznesowe i okresowe.
- **Zatwierdzony (zaksięgowany):** skutki powstają w **jednej transakcji SQL**; dokument i ewidencje są spójne.
- **Korekta:** nowy dokument / nowa wersja wpisów z powiązaniem z oryginałem i przyczyną. Nie kasujemy w ciszy historii zaksięgowanej.
- **Okres zamknięty:** zmiana wymaga świadomej procedury otwarcia/korekty i audytu uprawnień, nie zwykłego `UPDATE`.

Dla KSeF odrębnie modelujemy **status techniczny faktury** (przygotowana, wysłana, przyjęta, odrzucona, numer KSeF, UPO) i **status księgowy**. Nie wolno mieszać tych dwóch procesów.

### 5.3. Wymogi matematyczne

- Tylko `decimal` w C# i odpowiednie `decimal(p,s)` w SQL — **nigdy** `float`/`double` do wartości podatkowych.
- Osobne reguły dla ceny jednostkowej, kwoty, kursu i zaokrąglenia podatkowego; precyzje oparte na przepisach i formatach, a nie globalnej stałej „2 miejsca”.
- Sposób liczenia od netto/brutto i agregacji pozycji VAT musi być jawny i testowalny.
- Zapisujemy walutę, kwotę w walucie, kurs, źródło/datę kursu, kwotę PLN i sposób przeliczenia, gdy wymagane.
- Obliczenia mają być **deterministyczne**: te same dane, ten sam zestaw reguł i wersja schematu dają identyczny wynik.
- W procesie księgowania stosujemy idempotencję (ponowione żądanie sieciowe nie tworzy duplikatu dokumentu ani deklaracji).

### 5.4. Kategorie i reguły

- Dla rodzaju transakcji i pozycji dokumentu można zdefiniować kategorie podatkowe VAT/PIT/KPiR/ryczałt.
- Reguła ma okres obowiązywania, priorytet, autora, historię zmian, zestaw warunków i jawny wynik.
- Reguły **proponują** klasyfikację; użytkownik ma prawo świadomie skorygować wynik, jeżeli mieści się to w dopuszczalnych zasadach i ma uprawnienia.
- Każdy zapis ewidencyjny przechowuje odwołanie do źródła i zastosowanej wersji reguły.
- Silnik VAT i PIT nie może wykorzystywać arbitralnego skryptu użytkownika jako nieweryfikowanego zastępnika przepisów.

---

## 6. Wymagania funkcjonalne — identyfikatory i priorytety

Legenda: **P0** — fundament wymagany przed udostępnieniem produkcyjnym; **P1** — zakres wydania 1.0; **P2** — etap po 1.0 lub rozszerzenie. Realizacja P0/P1 może zostać podzielona na wiele iteracji.

### 6.1. Firma, użytkownicy, wspólne kartoteki

| ID | Wymaganie | Priorytet |
|---|---|---|
| ORG-001 | Rejestr wielu firm: NIP, REGON, nazwa, adresy, rachunki, identyfikatory urzędowe | P0 |
| ORG-002 | Ustawienia formy opodatkowania, statusów i okresów z historią od–do | P0 |
| ORG-003 | Wiele osób, role i uprawnienia per firma / per czynność | P0 |
| ORG-004 | Jednoczesna praca i detekcja konfliktów edycji | P0 |
| ORG-005 | Kartoteka kontrahentów, w tym osoby fizyczne, zagraniczni, wielu adresów i kont | P0 |
| ORG-006 | Uprawnienia biura rachunkowego i grupowanie obsługiwanych firm | P2 |
| ORG-007 | Weryfikacja NIP / rejestrów publicznych z datą i zapisem wyniku | P1 |

### 6.2. Dokumenty i fakturowanie

| ID | Wymaganie | Priorytet |
|---|---|---|
| DOC-001 | Faktury sprzedaży krajowej, faktury zakupowe, dowody wewnętrzne, dokumenty bez VAT | P0 |
| DOC-002 | Faktura wielopozycyjna, różne stawki VAT i klasyfikacje podatkowe | P0 |
| DOC-003 | Faktury korygujące z połączeniem do dokumentu pierwotnego | P0 |
| DOC-004 | Wiele dat: wystawienie, zdarzenie, sprzedaż/usługa, zapłata, obowiązek VAT i PIT | P0 |
| DOC-005 | Unikalne serie numeracyjne, odporność na równoczesne wystawienie | P0 |
| DOC-006 | Zapisy w walutach obcych, wersjonowane kursy i rozliczenie kwot PLN | P1 |
| DOC-007 | Powiązane załączniki PDF/XML/obrazy z ograniczeniem dostępu | P1 |
| DOC-008 | Wydruki faktur i zestawień bez zależności od chmury | P1 |
| DOC-009 | Import/eksport CSV i XML z mapowaniem i raportem błędów | P1 |
| DOC-010 | Dokumenty seryjne, szablony cykliczne | P2 |

### 6.3. KPiR i podatek dochodowy

| ID | Wymaganie | Priorytet |
|---|---|---|
| PIT-001 | KPiR zgodna z wymogami obowiązującymi w danym okresie; ewidencja w wymaganych kolumnach | P0 |
| PIT-002 | Mapowanie dokumentów do pozycji KPiR, ręczne dowody, księgowania korygujące | P0 |
| PIT-003 | Skala podatkowa i podatek liniowy (w odpowiednich przypadkach) | P1 |
| PIT-004 | Zaliczki miesięczne/kwartalne liczone metodą zgodną z właściwymi zasadami i uwzględniające wcześniejsze okresy | P1 |
| PIT-005 | Ewidencja składek ZUS i innych odliczeń — z datą zapłaty i rokiem podatkowym | P1 |
| PIT-006 | Remanent początkowy i końcowy, różnice remanentowe | P1 |
| PIT-007 | Obliczenia okresowe i roczne, księga, podsumowania, zestawienie różnic i korekt | P1 |
| PIT-008 | Obsługa stosownych rocznych formularzy PIT (np. PIT-36/PIT-36L) przez odrębne, wersjonowane adaptery | P1* |

`PIT-008`: dokładny zakres danych i formularzy zależy od typu podatnika, rozliczenia wspólnego, dochodów spoza działalności i załączników; wymagane osobne uzgodnienie minimalnego zestawu dla 1.0. Bez tego nie ogłaszamy pełnej obsługi deklaracji rocznych.

### 6.4. Ryczałt od przychodów ewidencjonowanych

| ID | Wymaganie | Priorytet |
|---|---|---|
| RYC-001 | Ewidencja przychodów wg właściwych stawek w obrębie jednego dokumentu | P0 |
| RYC-002 | Okresy miesięczne/kwartalne i prawidłowe rozliczanie narastająco tam, gdzie wymagane | P1 |
| RYC-003 | Zapłacone składki, ulgi, odliczenia, zwroty i korekty wraz z ograniczeniami okresowymi | P1 |
| RYC-004 | Wyliczanie podatku z kontrolą rozliczeń wcześniejszych okresów i datami obowiązywania zasad | P1 |
| RYC-005 | Zestawienie roczne i obsługa PIT-28 w ustalonym zakresie | P1 |
| RYC-006 | Walidacja przychodów o mieszanych stawkach i korektach z poprzednich okresów | P1 |

**Uwaga projektowa:** nie zakodujemy na sztywno stawek ani reguł składek zdrowotnych z 2026 r. Wszystkie zasady mają wersje okresowe i testy wzorcowe. Nie wolno bez analizy kopiować przykładów ze starej instrukcji.

### 6.5. VAT, JPK i deklaracje

| ID | Wymaganie | Priorytet |
|---|---|---|
| VAT-001 | Rejestry sprzedaży i zakupów VAT, z dokumentami wielostawkowymi | P0 |
| VAT-002 | Okres podatkowy, daty obowiązku, odliczenia, oznaczenia i procedury | P0 |
| VAT-003 | Obsługa transakcji krajowych i wybranych zagranicznych, po uprzednim zdefiniowaniu zakresu | P1 |
| VAT-004 | Weryfikacja i zestawienie naliczonego/należnego VAT, kwot do zapłaty/przeniesienia/zwrotu | P1 |
| VAT-005 | `JPK_V7M(3)` / `JPK_V7K(3)` według wersji i daty, walidacja XSD i reguł merytorycznych | P1 |
| VAT-006 | Korekty JPK/deklaracji, powiązanie ze złożoną wersją i przyczyną | P1 |
| VAT-007 | `JPK_PKPIR(3)`, `JPK_EWP(4)`, `JPK_ST(1)` tam, gdzie dotyczą podatnika i okresu | P1 |
| VAT-008 | Rejestr wysyłek, identyfikatory, statusy, UPO, pobieranie i archiwizacja | P1 |
| VAT-009 | VAT-UE i korekty VAT-UEK | P1* |
| VAT-010 | Inne formularze, np. VAT-8, VAT-9M i dokumenty na żądanie | P2 |
| VAT-011 | JPK_FA / inne struktury na żądanie, jeśli właściwe | P2 |

`VAT-009` i `VAT-003`: uruchomienie wymaga zaakceptowanej listy scenariuszy (WDT/WNT, import usług, eksport itd.). **JPK_V7M/V7K jest obecnie także nośnikiem części deklaracyjnej** — nie tworzymy bez potrzeby równoległego modułu „deklaracja VAT-7” według historycznej instrukcji.

### 6.6. KSeF

| ID | Wymaganie | Priorytet |
|---|---|---|
| KSEF-001 | Adapter do aktualnego API KSeF 2.0, osobne środowiska integracyjne / demo / produkcyjne | P1 |
| KSEF-002 | Mapowanie dokumentu do aktualnej struktury FA i walidacja przed wysyłką | P1 |
| KSEF-003 | Podpis/uwierzytelnienie i uprawnienia według obsługiwanych mechanizmów KSeF | P1 |
| KSEF-004 | Obsługa statusów wysyłki, błędów, powtórzeń, numeru KSeF, UPO i identyfikatorów | P1 |
| KSEF-005 | Pobieranie faktur zakupu, deduplikacja i wprowadzanie do ewidencji | P1 |
| KSEF-006 | Archiwum surowego XML, znaczniki integralności i powiązania dokumentów | P1 |
| KSEF-007 | Obsługa scenariuszy offline/awaryjnych, o ile wynikają z przepisów i aktualnego API | P1 |

**Wydanie 1.0:** bez udanych testów integracyjnych w środowisku testowym i bez kontroli autoryzacji integracji KSeF nie może deklarować obsługi produkcyjnej KSeF.

### 6.7. Rozrachunki, bank, środki trwałe, raporty

| ID | Wymaganie | Priorytet |
|---|---|---|
| PAY-001 | Należności/zobowiązania i terminy płatności | P1 |
| PAY-002 | Wprowadzanie płatności i łączenie z dokumentami, również częściowe | P1 |
| PAY-003 | Kompensaty i raporty zaległości | P2 |
| PAY-004 | Import wyciągów (CSV, docelowo ISO 20022) i półautomatyczne kojarzenie | P2 |
| AST-001 | Kartoteka środków trwałych, zdarzenia i plan amortyzacji | P1 |
| AST-002 | Generowanie zapisów KPiR i danych JPK_ST zgodnie z okresem | P1 |
| REP-001 | Podstawowe zestawienia księgi, VAT, przychodów, sald i podatków | P1 |
| REP-002 | Raporty definiowane: pola, filtry, sortowanie, grupowanie, sumy, obliczenia | P1 |
| REP-003 | Eksport CSV/XLSX/XML/PDF z opisanymi formatami | P1 |
| REP-004 | Własne kolumny w tabelach, widoki użytkownika, skróty klawiaturowe | P1 |

---

## 7. System rozszerzeń — obowiązkowy element architektury

### 7.1. Cel

Użytkownik ma móc wykonać czynność analogiczną do historycznego rozwiązania „XML z VATowca → wypełniony ministerialny PDF”, nie uzyskując dostępu do kodu rdzenia. Rozszerzenie powinno być przenośne, eksportowalne i możliwe do udostępnienia innym użytkownikom.

### 7.2. Trzy poziomy

1. **Definicje bez kodowania**: szablony wydruków, mapowania XML/JSON/CSV, zestawienia, niestandardowe kolumny i formuły, konfiguracja skrótów/przycisków.
2. **Skrypty lub wtyczki**: walidatory, operacje w menu, importery/eksportery, integratory, automatyzacje i obróbka danych przez publiczne kontrakty SDK.
3. **Wersjonowane API zewnętrzne**: integracja z aplikacjami firm trzecich przez HTTPS; konta techniczne z ograniczonymi zakresami uprawnień.

### 7.3. Rozszerzenia deklaratywne

Minimalny pakiet `*.vatx` (nazwa robocza) może zawierać:

```text
manifest.json
mappings/*.json
reports/*.json
forms/*.json
assets/*
README.md
```

Przykładowy **projekt** manifestu (nie obowiązująca implementacja):

```json
{
  "id": "pl.example.ministerial-form-xml",
  "version": "1.0.0",
  "apiVersion": "1",
  "name": "Przykładowy eksport formularza",
  "permissions": ["documents.read", "company.read"],
  "contributions": [
    { "type": "export", "name": "Formularz XML", "definition": "mappings/form.xml.json" }
  ]
}
```

Składnia i dostępne prawa podlegają formalnemu zaprojektowaniu, walidacji i wersjonowaniu.

### 7.4. Silnik formuł i mapowań

- Deklaratywny język wyrażeń / AST; **żadnego `eval`, nieograniczonego SQL ani uruchamiania dowolnego C# z definicji użytkownika**.
- Funkcje typowane (tekst, data, liczba, słowniki, operacje arytmetyczne, warunkowe) i jawne źródła danych udostępnione w API.
- Funkcje mają uprawnienia, ograniczenia czasu/zasobów i wersje.
- Definicję można testować na danych fikcyjnych z podglądem rezultatów i raportem błędów.
- Raport/eksport zapisuje numer swojej definicji i (jeśli jest wymagane) wersję formatu prawnego.
- Dla PDF przewidujemy **eksport XML zgodnego z zewnętrznym formularzem**, ewentualnie wypełnianie technicznie dostępnych pól PDF, ale nie obiecujemy pełnej obsługi dowolnych formularzy XFA ani działania w każdej przeglądarce PDF.

### 7.5. Zdarzenia i dodatki

Przykładowe zdarzenia publikowane **po skutecznym zakończeniu transakcji**:

- `DocumentPosted`
- `DocumentCorrected`
- `PaymentRegistered`
- `TaxPeriodClosed`
- `JpkGenerated`
- `SubmissionAccepted`
- `KsefInvoiceReceived`

Dodatki otrzymują identyfikator zdarzenia, czas, firmę i dane ograniczone zgodą operatora. Zdarzenia przez `Outbox` z ponawianiem, deduplikacją i jawnymi błędami. Rozszerzenia **nie mogą** edytować bezpośrednio zapisów historycznych ani omijać walidacji rdzenia.

### 7.6. Izolacja i bezpieczeństwo dodatków

- Wtyczki wykonywalne mają własny proces/worker lub inną rzeczywistą barierę izolacji — **nie** polegamy na `AssemblyLoadContext` jako zabezpieczeniu przed złośliwym kodem.
- Definiowane uprawnienia (`read:invoices`, `write:drafts`, `export`, `network:host`, itd.) i wyraźna zgoda administratora.
- Brak domyślnych praw do całej bazy, systemu plików, kluczy KSeF i sekretów innych dodatków.
- Wtyczka nie może usuwać ani podmieniać audytu.
- Niepodpisane dodatki mogą być dopuszczalne w trybie deweloperskim, ale z komunikatem ryzyka.
- Publiczne SDK, dokumentacja, przykłady i test zgodności API dla autorów dodatków.

### 7.7. Kryterium akceptacji otwartości

Bez rekompilacji aplikacji użytkownik tworzy nową definicję formularza: wybiera zatwierdzony dokument, mapuje pola, generuje XML w swoim formacie, zapisuje go i otwiera wskazany program zewnętrzny za świadomą zgodą. Inny użytkownik instaluje tę definicję w oddzielnej instalacji i otrzymuje ten sam wynik dla tych samych danych. Rdzeń programu nie jest modyfikowany.

---

## 8. Model danych — projekt logiczny, nie gotowy DDL

Każda tabela domenowa z danymi firmy posiada `CompanyId`, identyfikator rekordu, daty utworzenia/modyfikacji i, gdzie możliwe, znacznik konkurencyjności. Poniższe nazwy są robocze.

| Encja | Minimalne istotne pola / relacje |
|---|---|
| `Company` | `Id`, NIP, REGON, nazwa, status, konfiguracja |
| `TaxProfile` | `CompanyId`, typ podatnika, forma PIT, VAT, `ValidFrom`, `ValidTo` |
| `User`, `Role`, `CompanyAccess` | użytkownik, rola, firma, uprawnienia |
| `Contractor`, `ContractorAccount` | dane, identyfikatory, kraj, rachunki, historia zmian |
| `Document` | `Id`, `CompanyId`, typ, numer/seria, kontrahent, daty, waluta, status, wersja, `SourceSystem`, `ExternalId`, `RowVersion` |
| `DocumentLine` | dokument, rodzaj towar/usługa, ilość, cena, podatki, kategorie, kwoty |
| `DocumentCorrection` | dokument oryginalny / korygujący, powód i rodzaj korekty |
| `DocumentAttachment` | typ MIME, hash SHA-256, ścieżka/obiekt, uprawnienia, metadane |
| `VatEntry` | dokument/pozycja, rejestr, okres, stawka/kody, VAT należny/naliczony, wersja reguł |
| `IncomeEntry` | dokument/pozycja, okres, kategoria, kolumna KPiR lub stawka ryczałtu, wartość |
| `TaxRuleVersion` | rodzaj, okres obowiązywania, wersja, źródło prawne, konfiguracja, hash |
| `TaxCalculation` | firma, podatek, okres, źródła, wersje reguł, wynik, status, historia |
| `Payment`, `PaymentAllocation` | kwota, waluta, data, dokumenty, rozliczenia częściowe |
| `FixedAsset`, `DepreciationEntry` | wartość początkowa, historia zmian, metoda, odpis, okres |
| `JpkPackage` | rodzaj, schemat, okres, hash XML, dane wejściowe/snapshot, wynik walidacji |
| `Submission` | kanał, status, identyfikator, historia odpowiedzi, UPO/artefakt |
| `KsefExchange` | XML wejścia/wyjścia, identyfikatory, numer KSeF, statusy, UPO |
| `AuditEvent` | aktor, firma, moment UTC, czynność, identyfikator, przed/po lub delta, korelacja |
| `ExtensionPackage` | manifest, wersja, uprawnienia, publikator, podpis/hash |
| `ExtensionSettings` | firma/użytkownik, ustawienia i referencje do sekretów |
| `OutboxEvent` | typ, identyfikator zdarzenia, payload, próby, status, następne uruchomienie |

**Uwaga o projektowaniu:** kwot historycznych i złożonych deklaracji nie obliczamy ponownie po aktualizacji reguł „w miejscu”. `TaxCalculation` i `JpkPackage` powinny mieć zamrożony zestaw danych wejściowych / identyfikator obliczeń, wersję reguł i wersję schematu. Każda korekta tworzy nową rewizję z odsyłaczem do poprzedniej. Audyt i dane potrzebne do odtworzenia muszą być odporne na usunięcie z UI.

### 8.1. Kluczowe ograniczenia SQL

- Unikalność numerów wystawianych dokumentów w ramach właściwej firmy, rodzaju/serii i okresu.
- Idempotencja importów i wysyłek (unikat na `(CompanyId, SourceSystem, ExternalId)` tam, gdzie stosowne).
- Klucze obce i ochrona spójności, w tym właściwe powiązanie wszystkich relacji z tą samą firmą.
- Zakaz fizycznego usuwania zatwierdzonych dokumentów z publicznego API.
- Audyt z atomowym powiązaniem ze skutkami transakcji.
- Migracje schematu wersjonowane, odwracalne tylko gdy bezpieczne; przed migracją backup i weryfikacja zgodności.

### 8.2. Załączniki

Załączniki mogą być przechowywane w katalogu kontrolowanym przez serwer lub w adapterze object storage. W SQL przechowujemy metadane, identyfikator, hash, rozmiar, ścieżkę logiczną, firmę i uprawnienia. Nie przechowujemy pełnej treści dokumentów w losowych katalogach dostępnych z klienta. Eksport firmy uwzględnia zarówno SQL, jak i załączniki.

---

## 9. Silnik reguł podatkowych i zarządzanie zmianą prawa

### 9.1. Reguła ma zakres czasowy

Każda reguła obliczeniowa i każdy format ma:

`RuleId`, `Revision`, `ValidFrom`, `ValidTo`, zakres podmiotowy, podstawę prawną / źródło, opis, przypadki testowe i datę wprowadzenia do programu.

Ważne jest odróżnienie **daty zmiany przepisów** od **daty wydania aktualizacji programu**. Rozliczenie roku 2025 uruchomione w 2027 powinno używać właściwych zasad dla 2025 r., a nie bezrefleksyjnie bieżących stawek.

### 9.2. Schematy elektroniczne

- Pliki XSD / oficjalne schematy i wersje generatorów są wersjonowane oraz opisane.
- Dobór schematu zależy od rodzaju formularza, daty rozliczenia i reguł przejściowych; nie tylko od numeru zainstalowanej aplikacji.
- Każdy generator ma walidację syntaktyczną (XSD), merytoryczną i zestaw testów regresyjnych.
- Eksportowany XML można wyświetlić, zachować, walidować i podpisać niezależnie od automatycznej wysyłki.
- Zmiany XSD nie wymagają migracji dokumentów źródłowych, jeśli wystarcza nowy adapter eksportu.

### 9.3. Źródła aktualnych standardów — stan rozpoznania 2026-10-21

- JPK_V7M(3), JPK_V7K(3): [MF — pliki do pobrania JPK_VAT](https://www.podatki.gov.pl/podatki-firmowe/jednolity-plik-kontrolny/jpk_vat-z-deklaracja/pliki-do-pobrania) — od 1.02.2026.
- JPK_PKPIR(3), JPK_EWP(4), JPK_ST(1): [MF — JPK_PD](https://podatki.gov.pl/podatki-firmowe/jednolity-plik-kontrolny/jpk_pd/pliki-do-pobrania-jpk_pd). Obowiązek dla danego podatnika i okresu należy sprawdzić osobno.
- KSeF 2.0: [MF — wsparcie dla integratorów](https://ksef.podatki.gov.pl/ksef-na-okres-obligatoryjny/wsparcie-dla-integratorow/) — OpenAPI oraz środowiska integracyjne/demo/produkcyjne.
- e-Deklaracje, XML i UPO: [MF — informacje podstawowe](https://www.podatki.gov.pl/e-deklaracje/informacje-podstawowe) — system FK może w odpowiednim trybie generować i wysyłać XML.
- SQL Server 2025 Express: [Microsoft — edycje i limity](https://learn.microsoft.com/en-us/sql/sql-server/editions-and-components-of-sql-server-2025?view=sql-server-ver17) i [wariant Linux](https://learn.microsoft.com/en-us/sql/linux/sql-server-linux-editions-and-components-2025?view=sql-server-ver17).
- MPL-2.0: [Open Source Initiative — pełny tekst](https://opensource.org/license/mpl-2-0).

**Nie określamy** w tej specyfikacji pełnej listy stawek, ustawowych terminów i zakresu zwolnień — są to zmienne wymagania domenowe, które należy zebrać w katalogu reguł z podstawą prawną i testami.

---

## 10. UX i dostępność

1. Dwie ścieżki: **szybkie księgowanie tabelaryczne** i **pełny formularz dokumentu**.
2. Klawiatura: `Ins` nowa pozycja, `Enter` edycja/zatwierdzenie, `Esc` rezygnacja, `F3` szukanie, `F9` wydruk — z możliwością zmiany skrótów. Nie kopiujemy bezrefleksyjnie wszystkich historycznych zachowań.
3. Tabele z zapisem widoków: kolumny, szerokości, sortowanie, filtrowanie, sumy, widoki per użytkownik.
4. Operacje masowe z **podglądem zmian**, liczbą rekordów i raportem błędów.
5. Kontrolowane zamknięcie okresu: checklista niezaksięgowanych dokumentów, korekt, plików JPK i wysyłek.
6. Zrozumiałe ostrzeżenia: „co jest nie tak”, „który dokument”, „jak poprawić” zamiast technicznych błędów SQL.
7. Współpraca równoległa widoczna: osoba modyfikująca dokument, wersja, konflikt, historia.
8. Wersja polska jako podstawowa; zasoby tłumaczeń w osobnych plikach.
9. Dane poufne niewidoczne użytkownikom bez odpowiednich uprawnień — także w podpowiedziach, wyszukiwarkach i logach.

---

## 11. Bezpieczeństwo, audyt i ochrona danych

### 11.1. Bezpieczeństwo dostępu

- HTTPS/TLS dla API, nawet w LAN jako ustawienie rekomendowane.
- Logowanie użytkownika z bezpiecznym magazynem haseł (hash z solą; sprawdzone biblioteki/ASP.NET Identity) i możliwością dodatkowego uwierzytelnienia w przyszłości.
- Autoryzacja serwerowa dla wszystkich komend i zapytań, **zawsze** z kontrolą firmy i uprawnień.
- Tajne klucze KSeF/API, certyfikaty i hasła poza repozytorium i zwykłymi tabelami konfiguracji; szyfrowane w spoczynku lub przez magazyn sekretów OS.
- Minimalne uprawnienia SQL przypisane serwerowi, oddzielne role backup/migracji.
- Brak produkcyjnych NIP, PESEL, rachunków i faktur w testach publicznego CI.

### 11.2. Audyt

Rejestr: kto, kiedy, z jakiego stanowiska/kontekstu, co utworzył/zmienił/zatwierdził, źródło żądania, identyfikator korelacji, poprzednia i nowa istotna wartość. Odróżniamy log techniczny od audytu biznesowego. Retencja, zakres danych osobowych i prawo do usunięcia są projektowane z uwzględnieniem obowiązków księgowych i RODO, a nie przez proste usuwanie zapisów podatkowych.

### 11.3. Backup i odtwarzanie

- Automatyczne pełne kopie bazy na harmonogramie, archiwizacja załączników oraz bezpieczne przechowywanie kluczy.
- Kopia poza urządzeniem źródłowym lub na zasobie z kontrolą dostępu; możliwość szyfrowania.
- Rotacja i weryfikacja integralności, **regularny test restore**.
- Dla Express harmonogram organizuje własny proces / Windows Task Scheduler / systemd timer, nie SQL Server Agent.
- Instrukcja odtwarzania po awarii: baza + załączniki + konfiguracja + sekrety + zgodna wersja programu.
- Aktualizacja aplikacji nie może automatycznie nadpisać jedynej działającej kopii danych.

### 11.4. Monitoring

Health check API/SQL/magazynu plików, diagnostyka błędów integracji, kolejka Outbox, stan ostatniej kopii, miejsca na dysku, powiadomienia administratora. Telemetria do dostawcy **wyłączona domyślnie**, jeśli w ogóle ją wprowadzimy.

---

## 12. Testy i wymagania jakościowe

### 12.1. Warstwy testów

- **Unit/domain**: obliczenia kwot, zaokrąglenia, daty, odliczenia, kursy i reguły okresowe.
- **Integration**: SQL Server, migracje, transakcje, `rowversion`, izolacja firm, autoryzacja.
- **Contract**: OpenAPI, XSD, formaty importerów/eksporterów, manifesty wtyczek.
- **E2E**: klient → API → księgowanie → JPK → podpis/wysyłka testowa → UPO.
- **Security**: IDOR (firma A nie czyta B), eskalacja uprawnień, złe dodatki, niebezpieczne XML/XXE, wyciek sekretów.
- **Disaster recovery**: odtworzenie bazy i załączników na czystym serwerze.
- **Regression**: anonimowe lub sztuczne przypadki ustalone i zweryfikowane przez praktyka księgowości.

### 12.2. Obowiązkowe scenariusze graniczne

| ID | Scenariusz | Oczekiwanie |
|---|---|---|
| T-001 | Dwóch operatorów próbuje zatwierdzić tę samą fakturę | Jedno zatwierdzenie, brak duplikatów, jasny konflikt |
| T-002 | Dwóch operatorów wystawia faktury w tej samej serii | Brak powtórzonego numeru |
| T-003 | Zanik sieci po wysłaniu komendy `POST` | Bezpieczne ponowienie bez podwójnego księgowania |
| T-004 | Faktura z dwiema stawkami VAT i dwiema stawkami ryczałtu | Prawidłowy podział pozycji i sum |
| T-005 | Data dokumentu w jednym roku, skutek podatkowy w innym | Prawidłowy okres ewidencji |
| T-006 | Korekta faktury z okresu zamkniętego | Nowa rewizja/procedura, bez ukrytej edycji historii |
| T-007 | Płatność częściowa i nadpłata | Spójne rozrachunki |
| T-008 | Dokument walutowy i dwa różne kursy do różnych celów | Jawne źródła przeliczenia i powtarzalny wynik |
| T-009 | Zmiana wersji schematu JPK po złożeniu wcześniejszego pliku | Poprzedni XML i UPO pozostają niezmienne |
| T-010 | Korekta JPK | Powiązanie z pierwotnym plikiem, prawidłowy status |
| T-011 | Import tej samej faktury KSeF dwa razy | Brak duplikatu, zachowany identyfikator KSeF |
| T-012 | Użytkownik firmy A próbuje odczytać identyfikator dokumentu firmy B | Odmowa także przez API/raporty/wtyczki |
| T-013 | Wtyczka odmawia działania lub kończy się błędem | Nie cofa poprawnego zaksięgowania, błąd widoczny do obsługi |
| T-014 | Przeniesienie instalacji na nowy serwer | Poprawnie odtworzona baza, załączniki i połączenia |
| T-015 | Rozliczenie starego roku po aktualizacji reguł | Poprzedni zestaw zasad i powtarzalny wynik |

### 12.3. Niefunkcjonalne cele początkowe [PROPOZYCJA]

- Wsparcie **minimum 5 równocześnie aktywnych użytkowników** w scenariuszach akceptacyjnych na sprzęcie referencyjnym; architektura bez założenia „jeden użytkownik”.
- Operacje zwykłego przeglądania i zapisu bez nieuzasadnionych opóźnień; konkretne czasy i rozmiary testowej firmy określimy po pierwszych pomiarach (nie deklarujemy fikcyjnych SLA).
- Wykonalny eksport wszystkich własnych danych i dokumentów, z dokumentacją formatu.
- Budowanie klienta i serwera przez niezależnego programistę z publicznego repozytorium, bez płatnych narzędzi deweloperskich.
- Brak obowiązkowych zamkniętych bibliotek do **zbudowania** rdzenia; płatne lub zamknięte integracje dopuszczalne jako opcjonalne adaptery po analizie licencji.

---

## 13. Plan etapowania i definicja wersji

### M0 — Repozytorium i architektura

**Cel:** solidna baza techniczna bez deklarowania księgowej użyteczności.

- Publiczne repo, `LICENSE`, `README`, `CONTRIBUTING`, `SECURITY`, `AGENTS.md`.
- Solution .NET 10, serwer ASP.NET Core, klient Avalonia, model modułowy.
- SQL Server: migracje, konfiguracja połączenia, seed fikcyjnej firmy.
- Logowanie, dwie firmy, role, podstawowy audyt.
- Jedna komenda API, ekran klientów, test izolacji danych i równoczesnej edycji.
- CI: build, testy, skan zależności, formatowanie.

**Warunek ukończenia:** dwaj operatorzy z różnych stanowisk logują się i modyfikują różne dane, uprawnienia i konflikt edycji działają, odtworzenie kopii jest udokumentowane.

### M1 — Dokumenty i serce programu

- Firmy, kontrahenci, faktury sprzedaży i zakupu, waluty, korekty.
- Statusy, numery, załączniki, historia, import/eksport podstawowy.
- Silnik kategorii i dokumentów źródłowych; wstępna ewidencja VAT/PIT.
- **Pierwszy publiczny mechanizm definicji XML** i demonstracyjny eksport formularza.

**Warunek:** test T-001…T-006; samodzielne rozszerzenie XML bez przebudowy rdzenia.

### M2 — KPiR, ryczałt, rozliczenia okresowe

- KPiR i ryczałt, stawki/reguły okresowe, ZUS/odliczenia, remanenty.
- Zaliczki i raporty z historią obliczeń.
- Akceptowane przypadki podatkowe, kontrola okresów, procedura zamknięcia.

**Warunek:** testy merytoryczne dla zadeklarowanych form opodatkowania i lat; brak niewyjaśnionych różnic w przykładach referencyjnych.

### M3 — VAT, JPK, deklaracje

- Pełna dla określonego zakresu ewidencja VAT; JPK_V7M/V7K, JPK_PKPIR/EWP/ST stosownie do przypadku.
- Generator XML, walidator, podpis i wysyłka według wspieranych metod, status, UPO, korekty.
- Wybrane deklaracje roczne i VAT-UE według uzgodnionej listy i możliwości integracji.

**Warunek:** z powodzeniem przeprowadzona ścieżka w środowiskach testowych, kompletne wyniki XML + UPO oraz próby błędne.

### M4 — KSeF i gotowość produkcyjna 1.0

- KSeF 2.0: wystawianie, pobieranie, identyfikatory, statusy, błędy, rozliczenie.
- Pełne testy wielostanowiskowości, importu powtórnego, backupu i odtwarzania.
- Dokumentacja administratora i użytkownika, minimalne role, instalatory.
- Niezależna weryfikacja przypadków podatkowych i bezpieczeństwa danych.

**Warunek:** realna firma z jednoznacznie zdefiniowanym scenariuszem podatkowym może zgodnie z przepisami przejść pełny cykl ewidencji i wysyłek na obsługiwany rok. Jeżeli nie spełniono kryterium — pozostajemy przy `beta`, bez sztucznej deklaracji `1.0`.

### Po 1.0

Biura rachunkowe, dodatkowe podatki i typy podmiotów, banki/ISO 20022, rozbudowane środki trwałe i magazyn, publiczny katalog dodatków, alternatywne silniki SQL, web client, pełna FK jako osobny obszar.

---

## 14. Wstępny backlog dla Codexa (kolejność implementacji)

| Epic | Zadanie | Wartość / dowód ukończenia |
|---|---|---|
| E01 | Solution, moduły, zależności, CI | Kompilacja i testy na czystym środowisku |
| E02 | Local dev: SQL Server, konfiguracja, migracje | Instrukcja startu + baza testowa |
| E03 | Firmy i użytkownicy | Przypisanie ról per firma, test bezpieczeństwa |
| E04 | Kontrahenci | CRUD, filtry, NIP, wersjonowanie |
| E05 | Dokumenty i pozycje | Spójne daty, sumy, statusy i historia |
| E06 | Atomowe zatwierdzanie i concurrency | Testy T-001/T-002/T-003 |
| E07 | VAT Entry + Income Entry | Projekcje rozłączne od dokumentu |
| E08 | Kategorie + wersjonowanie reguł | Zmiana reguły nie zmienia archiwum |
| E09 | Framework eksportów XML / manifest dodatku | Działający pakiet przykładowy |
| E10 | KPiR i ewidencja ryczałtu | Tabele, raporty, ręczne korekty |
| E11 | Silniki zaliczek PIT | Testowane obliczenia wersjonowane |
| E12 | VAT okresowy i JPK_V7 | XSD + wzorcowe przypadki |
| E13 | Pozostałe wymagane JPK i deklaracje | Listy kontrolne schematów, XML, UPO |
| E14 | KSeF adapter | Test end-to-end integracyjny |
| E15 | Backup, restore, instalator, hardening | Próba odtworzenia „od zera” |

Każde zadanie ma mieć: opis domenowy, kryteria akceptacji, testy, uwagi o bezpieczeństwie i odniesienie do odpowiedniego `ID` wymagania. Nie rozdzielamy „widoku faktury” od jej integralności transakcyjnej na dwa niezależnie dopuszczane do produkcji zadania.

---

## 15. Decyzje otwarte i ryzyka

| ID | Kwestia | Propozycja / termin rozstrzygnięcia |
|---|---|---|
| ADR-001 | Nazwa i identyfikacja projektu | Przed publicznym ogłoszeniem; roboczo „Otwarty następca VATowca” |
| ADR-002 | MPL-2.0 vs inna licencja | Przed pierwszym publicznym commitem z `LICENSE`; sprawdzić użyte zależności |
| ADR-003 | SQLite/PostgreSQL jako opcja | Dopiero po stabilizacji SQL Server i kontraktów repozytorium |
| ADR-004 | SQL Server na Linux / limit Express | Przed publikacją instrukcji instalacji linuksowej; rozbieżność dokumentacji |
| ADR-005 | Rozdzielenie firm: wspólna baza czy osobne bazy | Przed migracjami schematu produkcyjnego; domyślnie wspólna baza + ścisłe scope |
| ADR-006 | Podpisywanie XML i przechowywanie certyfikatów | Przed implementacją wysyłek MF; nie przechowywać sekretów w zwykłej bazie |
| ADR-007 | API pluginów: manifest, sandbox, granice dostępu | Przed pierwszą wykonywalną wtyczką |
| ADR-008 | Pełny wykaz typów VAT i deklaracji na 1.0 | Przed M3; uzgodnić precyzyjne scenariusze |
| ADR-009 | PIT-28/36/36L, załączniki, roczne deklaracje | Przed M3; granice zastosowania i właściwe podpisy |
| ADR-010 | Migracja starych danych z oryginalnego VATowca | Opcjonalny importer; po uzyskaniu legalnych danych testowych i dokumentacji |
| ADR-011 | Model backupów / RPO / RTO | Przed pierwszym produkcyjnym wdrożeniem |
| ADR-012 | Wtyczki zamknięte i dystrybucja bibliotek | Weryfikacja prawna każdej zależności przed dołączeniem |

### 15.1. Główne ryzyka

- **Zgodność podatkowa**: częste nowelizacje, wyjątki, ulgi, wersje formularzy. Ograniczenie: wersjonowanie reguł i testy eksperckie.
- **KSeF/JPK/API**: zmiany schematów, limity i awarie. Ograniczenie: adaptery, kolejki, idempotencja, eksport XML niezależnie od wysyłki.
- **Wielostanowiskowość**: race conditions i błędne saldo. Ograniczenie: transakcje, konflikty, testy równoczesności.
- **Dodatki użytkowników**: wyciek danych, złośliwe wykonanie. Ograniczenie: izolacja procesu, capabilities, konta integracji, audyt.
- **Zależność od SQL Server**: Microsoft jest właścicielem silnika, limity i licencje mogą się zmieniać. Ograniczenie: otwarty kod programu, export danych, separacja modelu domenowego i SQL, opcja przyszłego adaptera.
- **Rozrost zakresu**: próba implementacji całej historycznej aplikacji na raz. Ograniczenie: wyraźne M0–M4, nie implementować magazynu/FK zanim część podatkowa przejdzie testy.

---

## 16. Definicja ukończenia zadania (Definition of Done)

Zmiana może zostać zaakceptowana, jeśli:

1. Ma identyfikator wymagania lub uzgodnioną decyzję ADR.
2. Nie narusza architektury klient → API → logika domenowa → SQL.
3. Uwzględnia wielofirmowość, wielostanowiskowość i uprawnienia.
4. Dla operacji modyfikujących dane ma prawidłowe transakcje, audyt i obsługę ponowień.
5. Dla obliczeń podatkowych ma testy na konkretne okresy, zaokrąglenia i opis źródła reguły.
6. Dla eksportów elektronicznych ma walidację oficjalną strukturą i zapis wersji XSD.
7. Dla nowego interfejsu publicznego zachowuje kompatybilność lub podnosi wersję kontraktu.
8. Przechodzi testy i nie wprowadza prawdziwych danych osobowych do repozytorium.
9. Posiada dokumentację konfiguracji, wdrożenia i migracji tam, gdzie wymagana.
10. Nie wykorzystuje nielegalnie kodu ani zasobów oryginalnego VATowca lub innych projektów.

---

## 17. Podsumowanie ustaleń

**Podstawa produktu:** księgowość uproszczona + podatki + elektroniczne rozliczenia w Polsce.  
**Podstawa techniczna:** wielostanowiskowy modularny monolit na ASP.NET Core, klient Avalonia, centralny SQL Server, wersjonowane API.  
**Wyróżnik:** użytkownik może dopisać własny formularz, eksport, raport, walidator lub integrację bez czekania na autora i bez przebudowy rdzenia.  
**Granica bezpieczeństwa:** otwarte API nie jest zgodą na niekontrolowane modyfikacje zatwierdzonych zapisów podatkowych.  
**Filozofia wydań:** odpowiedzialna wersja beta jest lepsza niż wersja „produkcyjna” bez zweryfikowanych obliczeń i wysyłek.

---

## 18. Źródła i materiały odniesienia

1. Piotr Kowaluk, *VATowiec. Instrukcja obsługi, Windows/DBF 3.84* — dokument przekazany przez inicjatora projektu, 71 stron; szczególnie s. 3, 6–14, 19, 30–31, 36–39, 42, 49–50, 62, 67–70.
2. Ministerstwo Finansów: [JPK_VAT — pliki do pobrania](https://www.podatki.gov.pl/podatki-firmowe/jednolity-plik-kontrolny/jpk_vat-z-deklaracja/pliki-do-pobrania).
3. Ministerstwo Finansów: [JPK_PD — pliki do pobrania](https://podatki.gov.pl/podatki-firmowe/jednolity-plik-kontrolny/jpk_pd/pliki-do-pobrania-jpk_pd).
4. Ministerstwo Finansów: [KSeF 2.0 — wsparcie dla integratorów](https://ksef.podatki.gov.pl/ksef-na-okres-obligatoryjny/wsparcie-dla-integratorow/).
5. Ministerstwo Finansów: [e-Deklaracje — informacje podstawowe](https://www.podatki.gov.pl/e-deklaracje/informacje-podstawowe).
6. Microsoft Learn: [SQL Server 2025 — edycje i funkcje](https://learn.microsoft.com/en-us/sql/sql-server/editions-and-components-of-sql-server-2025?view=sql-server-ver17).
7. Microsoft Learn: [SQL Server 2025 on Linux — edycje i limity](https://learn.microsoft.com/en-us/sql/linux/sql-server-linux-editions-and-components-2025?view=sql-server-ver17).
8. Open Source Initiative: [MPL-2.0](https://opensource.org/license/mpl-2-0).

**Dokument podlega przeglądowi przed każdą większą iteracją.** Opis funkcji nie jest obietnicą ich zgodności prawnej bez implementacji, weryfikacji i aktualnych testów.
