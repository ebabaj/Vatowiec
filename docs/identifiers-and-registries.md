# Identyfikatory i rejestry zewnętrzne — ADR-021

## Uruchomienie i konta

W katalogu repozytorium uruchom API, a w drugim terminalu Avalonia:

```powershell
dotnet build Vatowiec.slnx --no-restore
dotnet run --project src/Server/Api --no-build --launch-profile https
```

```powershell
dotnet run --project src/Desktop --no-build
```

HTTPS: https://localhost:7243, zwykła walidacja zaufanego certyfikatu. Zaloguj
operator.m0 lokalnym hasłem, wybierz firmę A, otwórz kartotekę i kontrahenta.
Operator ma w A ViewProfile/ViewContractors/EditContractors/AcceptWarnings/
VerifyRegistries (maska 31); w B tylko profil i kartotekę (3), bez zapisów
i inicjowania weryfikacji. Historia jest dostępna do odczytu kartoteki.
Czytelnik.m0 nadal widzi tylko profil A. Hasła pozostają poza repozytorium;
[konfiguracja kont](authentication.md). Nie jest potrzebny produkcyjny klucz GUS.

## Edycja, normalizacja i ostrzeżenia

TaxId, VatEuId i KsefIdWew są oddzielnymi opcjonalnymi polami do 100 znaków.
VAT UE wpisujesz w całości z prefiksem. Nie ma automatycznego dziedziczenia,
generowania IDWew ani wymogu zgodności IDWew jednostki z jej własnym TaxId.

1. Wpisz wartości i wybierz **Normalizuj i sprawdź identyfikatory**. API pokaże
   znormalizowaną postać, ocenę, wersję i zakres kontroli oraz duplikaty z tej firmy.
2. Sprawdź postać przed zapisem. Jeśli zapis sam zmienił postać, formularz wymaga
   ponownego wyboru Zapisz po jej pokazaniu.
3. Niepoprawny format/suma, brak pełnego algorytmu lub duplikat wywołują ostrzeżenie.
   Użytkownik z AcceptWarnings może wybrać **Zapisz mimo ostrzeżenia identyfikatorów**,
   bez uzasadnienia. Brak prawa oznacza odmowę również w bezpośrednim API.
4. Zmiana treści, wersji rekordu, reguły albo listy duplikatów wymaga nowego
   potwierdzenia. Zapis ponownie sprawdza dane. Ostrzeżenie jest atomowo zapisane
   w ContractorChanges wraz z wartościami przed/po i trwałym autorem.

Polski TaxId: usunięcie odstępów, myślników, kropek i opcjonalnego prefiksu PL;
nie usuwa się pozostałych liter, aby nie zamienić błędnej treści w pozornie poprawny
NIP. Zagraniczny TaxId bez obsługiwanej krajowej reguły zachowuje litery i znaki
wewnętrzne, usuwa tylko skrajne odstępy. VAT UE: wielkie litery, bez odstępów,
kropek i myślników; zachowane są m.in. irlandzkie + i *. IDWew: bez odstępów,
z zachowanym myślnikiem między NIP a pięcioma cyframi.

Poprawny format i suma nie oznaczają aktywnego podatnika. **Unknown** oznacza
brak możliwości pełnej kontroli, **Invalid** konkretny błąd, **Empty** pole
opcjonalne. Błąd usługi/timeout nigdy nie jest wynikiem negatywnym rejestru.
Nieznana data wersji reguł nie uruchamia zgadywania reguły historycznej.

## Ręczne sprawdzanie rejestrów

Zapisz dane i otwórz pozycję ponownie. Zapytania korzystają z zapisanej wersji,
nie ze szkicu. Nie wykonują się automatycznie przy otwieraniu ani edycji pól.
Każdy przycisk wywołuje serwerowe API z CSRF i VerifyRegistries.

- **Pobierz dane z GUS** — dla PL i krajowego NIP. Podgląd zawiera zwróconą nazwę,
  dostępne części adresu i REGON (bez nowej kolumny Contractors). Dane TEST są
  stare/częściowo zanonimizowane; nie są aktualnymi danymi przedsiębiorcy.
- **Sprawdź VAT UE** — oficjalny VIES, w tym EL i XI. Wynik pokazuje ważność,
  czas, nazwę/adres, jeśli usługa je udostępniła, albo błąd/niedostępność.
  Nie zmienia identyfikatora w kartotece.
- **Sprawdź podatnika VAT** — MF search według krajowego NIP i wskazanego dnia:
  status oraz dostępne dane. Nie potwierdza konkretnego rachunku.
- **Status VAT (WL2)** — uproszczona odpowiedź statusowa API2, z referencją urzędu.
- W zakładce **Rachunki bankowe** zaznacz konkretny zapisany rachunek, wybierz
  datę i **Sprawdź rachunek w wykazie VAT**. Wysyłany jest NRB z 26 cyfr; prefiks
  PL IBAN jest usuwany. Inny rachunek daje brak możliwości sprawdzenia, a nie NIE.

Wynik otwiera zakładkę **Weryfikacje**. Można odświeżać i stronicować historię,
wybierać zdarzenia i przeglądać faktyczne wejście, dzień stanu, czas UTC, wynik,
status usługi, błąd, referencję, wersję adaptera i reguł oraz środowisko.
TEST/MOCK są wyraźnie oznaczone i nie są potwierdzeniem produkcyjnym.
Kolejne świadome sprawdzenie dopisuje nowy wpis. Ponowienie tego samego żądania
po utracie odpowiedzi odczytuje trwały wynik operacji zamiast dublować historię.

Po GUS porównaj obecną nazwę ze zwróconą i zaznacz wartości do przeniesienia.
Przycisk przenosi je **tylko do szkicu**. Wybrane części adresu tworzą nowy szkic
adresu: nie nadpisują istniejącego adresu. Następnie sprawdź i zapisz normalnymi
przyciskami, z uprawnieniami, rowversion i audytem. Żadna odpowiedź nie powoduje
automatycznego zapisu. W demonstratorze zachowuj wyraźne oznaczenie fikcyjnych
rekordów, np. dopisek „dane testowe”; nie wpisuj danych rzeczywistych podatników.

## Konfiguracja serwera i ograniczenia ruchu

appsettings.json zawiera domyślnie **Test** dla każdego adaptera:

| Adapter | Oficjalny adres TEST |
|---|---|
| MF | https://wykazpodatnikowvat-test.mf.gov.pl/api/ |
| MF WL2 | https://wykazpodatnikowvat-test.mf.gov.pl/api2/ |
| GUS BIR1.2 | https://wyszukiwarkaregontest.stat.gov.pl/wsBIR/UslugaBIRzewnPubl.svc |
| VIES SOAP | https://ec.europa.eu/taxation_customs/vies/test-services/checkVatTestService |

Klucze: Registries:MF:Environment, Registries:GUS:Environment,
Registries:VIES:Environment, odpowiednie BaseUrl oraz MF:Wl2Url. Dozwolone tryby:
Test, Production, Mock. Zmienne środowiskowe używają __ zamiast :.
Mock nie wykonuje połączeń i jest oznaczony jako lokalna atrapa.

Testowy klucz GUS jest publiczny: abcde12345abcde12345, używany wyłącznie na
testowym endpointcie. Klucz produkcyjny, gdy będzie potrzebny, należy umieścić
w user-secrets lub środowisku serwera jako Registries:GUS:ApiKey, nigdy w repo.
MF i podstawowe checkVat VIES nie wymagają klucza. SID GUS jest tylko w pamięci,
sesja jest zamykana po próbie. Nie logujemy treści SOAP ani URL identyfikatorów.

Produkcja wymaga **świadomej zmiany** danego Environment, BaseUrl/Wl2Url na dokładny
oficjalny adres produkcyjny oraz Registries:AllowProduction=true. Brak zgodności
blokuje adapter. Nie ma fallbacku TEST→PROD ani przyjmowania URL od klienta;
przekierowania HTTP są wyłączone. W bieżącym demonstratorze nie włączamy produkcji.
Zmiana konfiguracji nie przepisuje środowiska dawnych zdarzeń ani nie przenosi
testowych wyników do produkcyjnych potwierdzeń.

Timeout to 12 sekund dla operacji, bez automatycznych ponowień. Bramka procesu
wymusza co najmniej 2 sekundy między próbami danego dostawcy/środowiska i liczy
dzienne limity MF (search 100, check 5000, WL2 50000). API dodatkowo ogranicza
inicjowanie do 20/min na użytkownika. Liczniki procesu nie są globalnym licznikiem
wielu instancji i zerują się po restarcie; przed użyciem produkcyjnym na wielu
serwerach potrzebna jest wspólna kontrola budżetu. Kod nie twierdzi, że lokalny
limit gwarantuje brak blokady po stronie dostawcy.

## Wdrożone reguły

ref.TaxIdentifierSchemes jest w **Vatowiec_System**, bez danych kontrahentów.
Wersja początkowa 2026-10-22.1, ValidFrom=2026-10-22 oznacza datę zastosowania
reguły w VATowcu, nie datę wejścia w życie prawa. Zachowane są państwo wydające,
typ, prefiks, regex, klucz algorytmu, wersja, okres, aktywność i URL źródła.

| Typ / państwa | Zakres |
|---|---|
| TaxId / PL | format 10 cyfr, pl-nip-mod11-v1: wagi 6,5,7,2,3,4,5,6,7; reszta mod 11 równa ostatniej cyfrze; reszta 10 odrzucana |
| TaxId / pozostałe państwa | brak algorytmu krajowego; wynik Unknown, bez zgadywania z formatu VAT UE |
| VatEuId / PL | prefiks PL + kontrola NIP jak wyżej |
| VatEuId / AT BE BG HR CY CZ DK EE FI FR DE GR HU IE IT LV LT LU MT NL PT RO SE SK SI ES oraz XI | formaty wszystkich 27 państw UE i Irlandii Północnej, z EL dla Grecji; format-only-v1, **brak kontroli sumy**, jawny wynik Unknown po zgodnym formacie |
| KsefIdWew | ksef-internalid-2026-10-22: oficjalny format NIP–5 cyfr; brak kontroli nadania, generowania i integracji KSeF |

Przykłady testów: normalizacja PL 324-517-45-04 → 3245174504 (numer z publicznej
listy testowej MF); błąd sumy 3245174505; litery w AB-12.3 pozostają; formaty
EL123456789 i XI123456789 nie stają się automatycznie wynikiem „poprawny”.
Pełne regexy są w tabeli i TaxSchemeSeed; źródła i ograniczenia w
[ADR-021](adr/ADR-021-identyfikatory-i-rejestry-testowe.md). Nie wdrożono innych
algorytmów sum, ContractorTaxIdentifiers, nadawania IDWew, faktur ani księgowań.

## SQL, API i sprawdzanie

Jawne migracje: System 20261022175431_TaxIdentifierRules (słownik i maska praw 31),
firmy A/B 20261022175433_ExternalVerifications. Każda baza firmy zachowuje własną
historię migracji. Nowa historia ma snapshot wejścia/środowiska, wynik i trwałego
aktora bez FK do centralnego użytkownika. Chronią ją kontekst oraz trigger UPDATE/
DELETE. Czyszczenie fikcyjnych fixture testowych jawnie wyłącza trigger w transakcji
tylko na trzech zweryfikowanych bazach; zwykłe API nie ma takiego obejścia.
Nie odtwarzano baz. Przed migracjami wykonano COPY_ONLY/CHECKSUM i VERIFYONLY.

W obrębie /api/v1/companies/{companyId}:

- POST /contractors/identifiers/validate — kontrola szkicu z EditContractors i CSRF.
- POST/PUT /contractors[/id] — nowe trzy pola i AcceptWarning/WarningHash;
  ponowna kontrola w transakcji, 422 dla ostrzeżeń, 403 dla braku prawa, 409 dla konfliktu.
- GET /registry-environments — rzeczywista konfiguracja środowisk do oznaczenia UI.
- POST /contractors/{id}/verifications — VerifyRegistries + ViewContractors + CSRF;
  OperationId, Operation, RowVersion; dla MfAccount także BankAccountId,
  BankRowVersion i StateDate. Bez możliwości podania dowolnego URL/identyfikatora.
- GET /contractors/{id}/verifications — historia SQL z offset/limit, ViewContractors.

Pełne testy (zatrzymaj API i desktop; runner czyści fikcyjne dane):

```powershell
dotnet build Vatowiec.slnx --no-restore
dotnet build tools/database/Vatowiec.DatabaseTools --no-restore
dotnet run --project tools/database/Vatowiec.DatabaseTools --no-build -- test
dotnet format Vatowiec.slnx --verify-no-changes --no-restore
```

Automatyczne testy nie pytają produkcji ani testowych rejestrów: używają lokalnych
atrap i sprawdzają protokoły/parse/timeouty/odmowy. Osobna jawna próba sieci TEST:

```powershell
dotnet run --project tools/database/Vatowiec.DatabaseTools --no-build -- probe-registries
```

Wynik 2026-10-22: **180/180** bez pominięć (76 SQL/API, 104 bez SQL), build
rozwiązania i narzędzia bez błędów/ostrzeżeń, obie kontrole formatowania poprawne.
Raport: TestResults/database-per-company-a0d90d8f59ac4a79b3150d0dfbd80650.trx.

| Próba rzeczywistego TEST | Wynik |
|---|---|
| MF search po NIP z opublikowanej listy testowej | Odpowiedź z danymi/statusem i RequestId |
| MF WL2 po tym samym NIP | Status Czynny i RequestId |
| MF check NIP–NRB na wskazany dzień | NIE, z RequestId; to odpowiedź merytoryczna, nie błąd połączenia |
| GUS BIR1.2 | Logowanie, wyszukanie przykładu dokumentacji po NIP i wylogowanie; stare dane TEST |
| Oficjalny VIES test, PL100 / EL200 | Odpowiednio ważny/nieważny w testowej usłudze |

Próby błędów, timeoutów, limitów i przypadków brzegowych wykonano na lokalnych
atrapach. Testy automatyczne nie odpytywały zewnętrznych usług. Nie przeprowadzono
weryfikacji produkcyjnych i nie jest wymagany prywatny klucz GUS.

W rzeczywistym oknie Avalonia sprawdzono normalizację wszystkich trzech pól,
zapis pomimo błędu, listę duplikatów, VIES/MF/WL2/GUS i rachunek, powrót do
historii, brak automatycznego nadpisywania oraz świadomy wybór i zapis danych GUS.
Firma B zachowała izolację i brak edycji/inicjowania sprawdzeń. Dodano regresję
wyboru ostatniej odpowiedzi po odświeżeniu historii. Osobnej próby po zmianie DPI
Windows ani na Linuksie nie wykonano.

Pozostawiony przykład to **GUI-ID-DEB122**, Id 1229, w firmie A. Ma pięć wpisów
Test: VIES, MF search, WL2, MF rachunek i GUS. TaxId zmieniono między sprawdzeniami;
historia zachowała pierwotne NIP i rachunek. SQL potwierdził cztery audytowane
odstępstwa, włączony trigger niezmienności, 29 reguł w System i brak historii/
kontrahenta w B. Po ponownym wykonaniu pełnego runnera dane przykładu zostaną
usunięte jako fikcyjne fixture. Zrzut okna: TestResults/avalonia-registries-final.png.
