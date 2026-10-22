# ADR-021 — identyfikatory i rejestry zewnętrzne w środowiskach testowych

Status: przyjęty i wdrożony na podstawie zlecenia użytkownika, 2026-10-22.
Wymagania: ORG-007, §8.3 SPECYFIKACJA.md i bieżące zlecenie. ADR-018 pozostaje
źródłem modelu biznesowego; rozszerzamy ADR-019/020, nie zmieniamy ich historii.

## Rozwiązanie i kryteria akceptacji

Edytujemy trzy opcjonalne identyfikatory w istniejącym formularzu. API normalizuje,
pokazuje zakres sprawdzenia i duplikaty; zapis powtarza kontrolę. Istniejące
AcceptWarnings/WarningHash obejmują dokładne dane, rowversion, reguły i duplikaty.
Akceptacja oraz zmiana i audyt są jedną transakcją. Format nie potwierdza rejestracji.

ref.TaxIdentifierSchemes znajduje się w Vatowiec_System: wspólne, wersjonowane
reguły bez danych kontrahentów. W bazach firmowych powstaje osobne, niezmienne
audit.ExternalVerifications z faktycznym wejściem, środowiskiem, wynikiem,
referencją, trwałym aktorem, wersją adaptera/reguł i niezbędnymi polami wyniku.
Nie utrwalamy SOAP/JSON w całości, SID ani kluczy usług. Zapis historii nie zmienia
kontrahenta. Dane pobrane z GUS wymagają osobnego wyboru i zwykłego audytowanego zapisu.

Adaptery serwerowe mają Test/Production/Mock, domyślnie Test. Adresy są ograniczone
do konkretnych oficjalnych endpointów; brak przekierowań, dowolnych URL i fallbacku
do produkcji. Produkcja wymaga jawnego dodatkowego zezwolenia konfiguracji.
Test/Mock nigdy nie stanowią urzędowego potwierdzenia produkcyjnego. Historia
zachowuje środowisko, nie jest konwertowana przy zmianie konfiguracji.

MF: API 1.6.1 (12.10.2026), nowy testowy host z /api i /api2. GUS: BIR1.2,
publiczny klucz testowy wyłącznie dla testowego hosta. VIES: oficjalny testowy
WSDL i adres test-services/checkVatTestService, bez płatnych bramek. Błędy,
timeout i brak obsługi są odrębne od odpowiedzi negatywnej. Ręczne zapytania,
krótki timeout, ograniczenie częstotliwości, brak automatycznych ponowień
obciążających limit dostawcy. Każde ponowne świadome sprawdzenie jest nowym zdarzeniem.

Kryteria: wszystkie dotychczasowe testy i nowe SQL/API/adapterów; odmowy praw/CSRF,
konflikty i ostrzeżenia związane z danymi, izolacja oraz niezmienna historia.
Próby sieciowe wyłącznie z testowymi usługami, osobno raportowane od atrap.
Pełna próba edycji, ostrzeżenia i historii w rzeczywistym oknie Avalonia.
Jawne migracje trzech zweryfikowanych baz po backupie, bez ich odtwarzania.

## Źródła reguł i kontraktów

- [MF API 1.6.1, adresy i dane testowe](https://www.gov.pl/web/kas/api-wykazu-podatnikow-vat).
- [GUS BIR1.2 i dokumentacja 1.4](https://api.stat.gov.pl/Home/RegonApi).
- [VIES test WSDL](https://ec.europa.eu/taxation_customs/vies/checkVatTestService.wsdl).
- [VIES WSDL](https://ec.europa.eu/taxation_customs/vies/checkVatService.wsdl).
- [Formaty VAT UE, administracja podatkowa Finlandii](https://www.vero.fi/en/businesses-and-corporations/taxes-and-charges/vat/international-commerce/vat-number/vat-numbers-issued-by-different-eu-countries/).
- [NIP mod 11 w oficjalnym kliencie MF, MiscellaneousUtils](https://github.com/CIRFMF/ksef-client-csharp/blob/main/KSeF.Client.Tests.Utils/MiscellaneousUtils.cs).
- [Oficjalny OpenAPI KSeF, InternalId](https://api.ksef.mf.gov.pl/docs/v2/openapi.json).

Implementujemy sumę NIP mod 11 (wagi 6,5,7,2,3,4,5,6,7, reszta 10 odrzucana).
Pozostałe formaty VAT UE nie mają zaimplementowanej sumy kontrolnej: wynik jawnie
informuje o niepełnej weryfikacji. Zagranicznych krajowych TaxId nie utożsamiamy
automatycznie z VAT UE. IDWew sprawdza format, nie nadanie w KSeF; nie generujemy go.
Daty wersji oznaczają zakres zastosowania reguł VATowca, nie potwierdzenie
historycznego stanu przepisów. Brak reguły dla dnia nie uruchamia zgadywania.

## Wdrożenie i wynik

Jawnie zastosowano 20261022175431_TaxIdentifierRules w System oraz
20261022175433_ExternalVerifications osobno w A i B. Po sprawdzeniu lokalnej
instancji, markerów, dokładnych nazw, schematów i fikcyjnych danych wykonano
COPY_ONLY/CHECKSUM oraz RESTORE VERIFYONLY dla każdej bazy. Bez DROP/CREATE
i bez zmiany zatwierdzonych kolumn adresów/kontaktów/rachunków. Nowa maska praw
to 31, operator A=31, B=3. Zwykłe API nadal nie wykonuje migracji przy starcie.

SourceUrl w słowniku umożliwia identyfikację źródła. Wynik reguły zawiera także
hash jej definicji (wzorzec, algorytm, okres i źródło); zmiana treści bez zmiany
etykiety wersji nie pozwala użyć wcześniejszego potwierdzenia. Kolejne reguły
należy dodawać jako nowe wersje/okresy, nie zastępować historycznych definicji.
Wersje początkowe obowiązują w aplikacji od 2026-10-22; nie zgadujemy historii.

Historia ma unikalny klucz aktor/instalacja/OperationId. Ponowienie skutecznego
żądania zwraca zapisany wynik; odmienna treść daje 409. Nie trzymamy transakcji
SQL podczas zapytania do urzędu. Rzadkie równoległe ponowienia mogą wykonać
dwa bezpieczne odczyty zewnętrzne przed zapisem; unikalność zapewnia jeden wpis
historii. Nie deklarujemy dokładnie jednokrotnego wykonania po stronie urzędu.
Ponowna świadoma kontrola używa nowego klucza i tworzy nowe zdarzenie.

Kontekst i trigger blokują modyfikację/usuwanie historii. Testowy runner po
preflight jawnie czyści wyłącznie fikcyjne fixture w transakcji z tymczasowym
wyłączeniem triggera i jego ponownym włączeniem; nie ma takiej operacji w API.
Próby nie wykorzystują baz innych aplikacji. GUS SID, klucze i surowe odpowiedzi
nie są utrwalane. Jawne allowlisty fikcyjnych fixture obejmują wskazane publiczne
numery testowe MF/GUS; nie traktujemy dowolnego NIP lub rachunku jako testowego.

Regresja: **180/180** bez pominięć (76 SQL/API, 104 bez SQL), build i formatowanie
rozwiązania oraz narzędzia administracyjnego poprawne. W prawdziwym oknie
sprawdzono edycję, normalizację, ostrzeżenie, duplikat, historię, izolację i odmowę
w B, porównanie GUS i wybrany import do szkicu, a także jawny zapis nazwy i części
adresu zwykłym API. Test wykrył i naprawił przywracanie starego wyboru historii
podczas zmiany ItemsSource; przypadek jest objęty regresją.

Rzeczywiste połączenia TEST zakończyły się odpowiedziami: MF search i WL2 ze
statusem, MF check z NIE i RequestId, GUS login/search/logout z danymi,
VIES 100/200 z odpowiedziami pozytywną/negatywną. Błędy i timeouty testowano
na atrapach. Nie wykonywano sprawdzeń w rejestrach produkcyjnych. Przykład
pozostawiony w A: GUI-ID-DEB122, Id 1229, pięć zdarzeń Test. SQL potwierdził
29 reguł, cztery audytowane odstępstwa tego kontrahenta i brak tych danych w B.

Konfiguracja, pełny zakres algorytmów i ograniczenia:
[instrukcja](../identifiers-and-registries.md). Liczniki limitów adapterów są
lokalne dla procesu; przed wdrożeniem produkcyjnym na kilku serwerach trzeba
zapewnić wspólny budżet. Nie wykonano próby produkcyjnej ani zmiany DPI Windows.
