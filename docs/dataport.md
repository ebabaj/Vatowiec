# DataPort — konfiguracja i pojedyncze pobranie

DataPort jest komercyjnym pośrednikiem danych GUS przez REST/JSON. Konto i klucz
użytkownik uzyskuje samodzielnie na [stronie DataPort](https://dataport.pl/api).
[Kontrakt dostawcy](https://dataport.pl/api/reference), sprawdzony 2026-10-22:
GET `https://dataport.pl/api/v1/company/{nip}?format=full`, nagłówki `X-API-Key`
i `Accept: application/json`. Odpowiedzi mogą pochodzić z cache.

## W programie

1. Uruchom API i Avalonia według [instrukcji](desktop.md). Zaloguj się jako
   `operator.m0`, używając własnego hasła z lokalnej konfiguracji, i wybierz A.
2. Otwórz **Ustawienia firmy → Integracje zewnętrzne → DataPort**.
   Status to „Nie skonfigurowano” albo „Skonfigurowano”. Puste, maskowane pole
   służy wyłącznie do wpisania nowego klucza. Zapisz go przyciskiem
   **Zapisz / zastąp klucz**. Nie wywołuje to usługi DataPort.
3. Zmiana wymaga ponownego wpisania całego klucza. Usunięcie wymaga osobnego
   potwierdzenia. Przy konflikcie ustawień odśwież stan i ponownie wpisz klucz;
   stary formularz nie może nadpisać nowszej konfiguracji.
4. Wróć do kartoteki. Otwórz zapisanego polskiego kontrahenta z poprawnym NIP.
   Jeżeli edytowano dane, najpierw zapisz je zwykłym formularzem. W zakładce
   **Weryfikacje** kliknij **Pobierz dane z DataPort**. Każde świadome nowe
   kliknięcie może zużyć limit konta. Nie ma automatycznego odpytywania.
5. Wybierz zdarzenie historii. Źródło to **DataPort (dane GUS)** — rzeczywista
   usługa pośrednika, możliwy cache; nie bezpośrednia odpowiedź urzędu w chwili
   kliknięcia. Numer REGON i ewentualna data zakończenia są tylko podglądem.
6. Zaznacz osobno pola do przeniesienia, a potem **Przenieś wybrane wartości
   do szkicu**. Nazwa trafia do danych głównych, wybrane pola adresu do nowego
   szkicu adresu. Istniejące adresy nie są nadpisywane. Nic nie zapisuje się
   automatycznie. Sprawdź dane, wybierz rodzaj adresu i użyj zwykłych przycisków
   zapisu; obowiązują uprawnienia, rowversion, ostrzeżenia i atomowy audyt.
7. W firmie B operator może zobaczyć stan konfiguracji, ale nie zapisać ani
   usunąć klucza. `czytelnik.m0` ma tylko ViewProfile w A: może zobaczyć stan,
   ale nie zarządza kluczem ani kartoteką. Również bezpośrednie API odmawia.

Nie ma salda „pozostałych zapytań”. Dzienny limit obejmuje całe konto DataPort,
także kilka kluczy i firm; reset o **00:00 UTC**. Komunikat 429 wskazuje ten zakres.
Brak klucza odsyła do ustawień; 401 oznacza nieważny klucz, 404 brak firmy,
400 błędne żądanie, a 500/timeout/błąd sieci/JSON brak wiarygodnej odpowiedzi.
Tylko brak firmy jest wynikiem negatywnym; awaria nim nie jest. Bez automatycznych
ponowień. Dotychczasowe MF/GUS/VIES pozostają domyślnie Test, oddzielne od DataPort.

## Serwer, ochrona i przenoszenie

Każda baza firmy ma wyłącznie lokalne `security.DataPortConfiguration`:
`SingletonKey int = 1`, `ProtectedApiKey nvarchar(max) NULL`, `RowVersion rowversion`.
Po usunięciu pozostaje wiersz z pustym sekretem i nową wersją. Brak CompanyId/GUID,
nowych pól kontrahenta i nowej tabeli historii. Stan konfiguracji nie oznacza
potwierdzenia poprawności klucza przez dostawcę.

ASP.NET Data Protection szyfruje sekret z purpose zawierającym CompanyId.
Materiał kryptograficzny jest w osobnym trwałym katalogu:
`%LOCALAPPDATA%\VATowiec\ServerKeys\DataPort`, w kontekście tożsamości API.
Klucze key ring są chronione Windows DPAPI tej tożsamości. Można ustawić lokalny
absolutny katalog przez `DataPort__KeyRingDirectory`; musi leżeć poza katalogiem
aplikacji/repozytorium. Nie wpisuj tam klucza API. Usługa Windows powinna mieć
stałe konto, załadowany profil i prawa do własnego katalogu. Ochrona DataPort
nie zmienia istniejącego key ring sesji Identity. Bieżąca implementacja wymaga
Windows DPAPI; inne systemy wymagają osobno zaprojektowanej ochrony kluczy.

Sama kopia SQL zawiera szyfrogram i nie wystarczy po przeniesieniu do innego
konta lub komputera. Przywróć dostępny dla tej samej tożsamości katalog ochrony
albo **wpisz klucz DataPort ponownie**. Nie generujemy zastępczego klucza API,
nie odszyfrowujemy go w Avalonia i nie przechodzimy na zapis jawny. Kopia katalogu
z kluczami nie może trafić do repozytorium. Utrata ochrony daje kontrolowany
komunikat w historii i wymaga ponownej konfiguracji.

Klient zna tylko HTTPS API VATowca. Nowy sekret jest przekazywany w treści
uwierzytelnionego PUT, z nowym CSRF; API nigdy nie odsyła wartości ani szyfrogramu.
Maskowane pole jest czyszczone po wysłaniu i opuszczeniu ustawień. Nie włączaj
logowania treści żądań/payloadów ani SensitiveDataLogging EF. Adapter wyłącza
loggery HttpClient, przekierowania i cookie; stały host HTTPS, brak adresu
podanego przez klienta, X-API-Key przypisany do jednego żądania.

## API i wdrożenie

| Endpoint | Wymagania i wynik |
|---|---|
| GET `/api/v1/companies/{companyId}/integrations/dataport` | ViewProfile; tylko CompanyId, IsConfigured, RowVersion |
| PUT ta sama ścieżka | ViewProfile + ManageCompanyIntegrations (32), CSRF; ApiKey + RowVersion; NULL wersja tylko przed pierwszym zapisem |
| DELETE ta sama ścieżka | te same prawa i CSRF; treść RowVersion + Confirm=true |
| POST `…/contractors/{intId}/verifications` | ViewContractors + VerifyRegistries, CSRF; istniejący kontrakt z Operation="DataPort", RowVersion i OperationId |
| GET `…/contractors/{intId}/verifications` | istniejąca lokalna historia, bez sekretów |

Resolver kontroluje profil i migracje fizycznej bazy. Statusy ustawień: 403 prawa,
400 CSRF/klucz/potwierdzenie, 409 niezgodna wersja, 503 ochrona/baza. Wyjątki
od dostawcy nie są przekazywane w surowej postaci. Normalizacja i suma NIP z
bieżącego mechanizmu aplikacyjnego poprzedzają każde nowe sprawdzenie.

Rzeczywiste sprawdzenia zapisują w istniejącym `audit.ExternalVerifications`
Source=DataPort, Operation=DataPort, Environment=Production, faktyczny NIP,
czasy UTC, wynik/status/błąd, uporządkowane dane i trwałego autora. „Production”
opisuje usługę DataPort, **nie urzędowe potwierdzenie**. Atrapy mają Mock.
Sprawdzenia są niezależne od audytu zmian. Ponowienie tego samego OperationId
z tą samą treścią zwraca już zapisane zdarzenie. Nie dopisujemy kolejnego wyniku.

Migracje: systemowa `20261022194948_CompanyIntegrationPermission` rozszerza
CHECK uprawnień do 63; firmowa `20261022194946_DataPortConfiguration` tworzy
jedną tabelę, bez zmiany Contractors/ExternalVerifications. Są addytywne;
Down firmowej usuwa konfigurację, a Down systemowej nie jest bezpieczny przy
aktywnych nowych uprawnieniach. Nie używaj Down jako sposobu zachowania sekretu.

Po zatrzymaniu API i desktopu: build, przejrzenie skryptów offline, kontrola
instancji/znaczników/profilu, backup COPY_ONLY/CHECKSUM/VERIFYONLY, jawne
`dotnet run --project tools/database/Vatowiec.DatabaseTools --no-build -- migrate-contractors`.
Start API nigdy nie migruje. `seed-users` nadaje operatorowi A maskę 63, B maskę 3,
czytelnikowi A maskę 1; hasła nadal tylko lokalne user-secrets/środowisko.

Pełny runner `… -- test` czyści demonstracyjne fixture i ponownie inicjalizuje
konta/dane. Samodzielne **prepare-tests blokuje czyszczenie, jeśli zastanie zapisany klucz DataPort**:
usuń go świadomie w ustawieniach przed takim przebiegiem. Domyślnie nieznane
nazwy/numery też blokują testy. Wyłącznie po wyraźnym potwierdzeniu właściciela,
że bieżące dane są demonstracyjne i mogą zniknąć, lokalny proces administracyjny
może ustawić `VATOWIEC_CONFIRMED_DEMO_DATA=1`. Nie pomija to kontroli instancji,
trzech dokładnych nazw, znaczników, profili, tabel i migracji. Nie ustawiaj tej
zmiennej na stałe. W tym zadaniu użytkownik potwierdził przeznaczenie danych
przed migracjami i pełnymi testami; kopie wykonano przed czyszczeniem.

## Weryfikacja

Testy adaptera i SQL/API używają atrap HTTP. Nie wymagają prywatnego klucza
i nie zużywają limitu DataPort. Pokrywają szyfrowanie, restart ochrony DPAPI,
rotację/usunięcie/CAS, prawa/CSRF/izolację, normalizację NIP, błędy dostawcy,
brak ponowień i ujawnień, historię oraz wybiórczy import z normalnym audytem.
Wynik końcowego pełnego przebiegu: **210/210**, bez pominięć — **83 SQL/API,
127 bez SQL**. Raport:
`TestResults/database-per-company-2d06356da6714eb79614019890c3d8c2.trx`.
Build ośmiu projektów: 0 błędów, 0 ostrzeżeń. Sprawdzono fizyczne schematy,
niezależne migracje, 14 tabel System i po 11 tabel w A/B. Zweryfikowane kopie
COPY_ONLY/CHECKSUM/VERIFYONLY trzech baz poprzedzały migracje i czyszczenie.
Runner odtworzył profile, fikcyjne dane i konta/uprawnienia bez DROP/CREATE baz.

Rzeczywiste okno Avalonia + lokalny API/SQL: logowanie operatora, zapis i zmiana
fikcyjnego klucza, wyczyszczenie pola, potwierdzone usunięcie, blokada zmian w B,
powrót do A, utworzenie fikcyjnego kontrahenta i ręczna próba bez klucza. Historia
ma Source=DataPort, docelowe Production, Executed=false i NotConfigured; żadnego
wywołania rzeczywistego DataPort. Podgląd/import odpowiedzi, wybór wyłącznie nazwy
lub wybranych pól adresu oraz zapis z ostrzeżeniami i audytem sprawdzono na atrapach
HTTP przez rzeczywiste API/SQL i model Avalonia. Próby błędów dostawcy, w tym
401/404/429/500, timeout, zły JSON i sieć, są również testami atrap.

Nie wykonano rzeczywistego wyszukiwania DataPort bez prywatnego klucza.
Po jego wpisaniu użytkownik może świadomie wykonać pojedynczą próbę w UI.

Po końcowych testach w oknie Avalonia potwierdzono też odczyt stanu bez prawa
zapisu przez czytelnik.m0 oraz przewijanie panelu historii. Pozostawiono w A
fikcyjny GUI-DATAPORT-M0 (Id 1741) i jedno zdarzenie NotConfigured/Executed=false;
B ma tylko swój fikcyjny rekord, bez historii DataPort. Obie firmy mają zero
zapisanych kluczy. API i desktop uruchomiono; klucz rzeczywisty należy wpisać
samodzielnie. Zrzuty lokalne: TestResults/avalonia-dataport-settings.png oraz
TestResults/avalonia-dataport-final.png. Formatowanie rozwiązania i git diff --check
przeszły. Bez Commit i Push.

Aktualizacja ADR-023: pełny runner test zabezpiecza zastany szyfrogram w pamięci
i zweryfikowanych kopiach trzech baz, a po inicjalizacji przywraca do zgodnego
profilu. Nie trzeba usuwać rzeczywistego klucza przed pełnym test. Nie jest
on odszyfrowywany ani logowany. Samodzielne prepare-tests nadal odmawia jego
czyszczenia. [Przebieg i odzyskanie po przerwaniu](currencies-and-nbp.md).
