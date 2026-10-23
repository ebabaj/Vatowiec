# REGON, KRS, BDO i PESEL kontrahenta — ADR-030

W **Danych głównych** kontrahenta dodano cztery opcjonalne pola tekstowe.
Zera wiodące pozostają częścią numeru. ParentId i zasady grupowania nie zmieniają się.

| Pole | Kontrola lokalna |
|---|---|
| REGON | 9 lub 14 cyfr, suma mod 11; dla 14 także suma pierwszych 9 cyfr |
| KRS | 10 cyfr, wyłącznie format |
| BDO | 9 cyfr, wyłącznie format |
| PESEL | 11 cyfr i suma kontrolna; bez potwierdzania tożsamości lub nadania numeru |

Poprawny format lub suma nie oznaczają istnienia wpisu w rejestrze.
Nie dodano pobierania tych pól z GUS/MF/VIES/DataPort ani ich automatycznego zapisu.
Dotychczasowy świadomy import wybranych wartości do szkicu pozostaje bez zmian.

## Obsługa w Avalonia

1. Zaloguj operator.m0 i wybierz firmę A, następnie **Pokaż kontrahentów**.
2. Utwórz pozycję (Ctrl+N) albo otwórz istniejącą. Uzupełnij kod/nazwę/kraj
   i wybrane opcjonalne identyfikatory w **Danych głównych**.
3. **Normalizuj i sprawdź identyfikatory** pokazuje wynik i postać numeru.
   Spacje, kropki i typowe myślniki są usuwane; zera pozostają. Nieznane znaki
   oraz błędne cyfry nie są naprawiane. Sama kontrola nie zapisuje kontrahenta.
4. Ctrl+S zapisuje przez API, z rowversion, CSRF i atomowym audytem. Jeśli przy
   zapisie pokazano nową postać numeru, przejrzyj ją i wybierz Zapisz ponownie.
5. Błędny format lub suma wyświetlają konkretne ostrzeżenie. **Świadomy zapis
   mimo ostrzeżenia** wymaga firmowego AcceptWarnings i aktualnego WarningHash.
   Każda zmiana danych/wersji/reguł wymaga ponownej kontroli; akceptacja jest w audycie.
6. W polu wyszukiwania wpisz także REGON/KRS/BDO (lub PESEL z dodatkowym prawem).
   Filtry i strony nadal wykonują się w SQL; Avalonia wysyła wyszukiwanie w ciele
   POST, bez numerów w URL. Wyszukiwanie nowych identyfikatorów usuwa te same separatory.

Do prób REGON użyj przykładu technicznego `000000023` albo `00000002300041`;
zmiana ostatniej cyfry powinna dać ostrzeżenie. Nie wpisuj rzeczywistych danych
osób w testach. Syntetyczne fixture PESEL użyte w automatycznych testach mają
niemożliwą datę urodzenia i służą tylko kontroli algorytmu sumy.

Konflikt: otwórz tego samego kontrahenta w dwóch klientach, zapisz zmianę w jednym,
próbuj zapisać w drugim. Drugi klient otrzyma 409 i nie nadpisze pierwszego.
Porównanie pokazuje publiczne identyfikatory; wartości PESEL nie są wypisywane
w ogólnym opisie konfliktu. Można wczytać wersję serwera do formularza (z prawem
PESEL) albo świadomie zachować własne pola i ponowić zwykły zapis.

## Prawo do PESEL i ochrona

**ManageContractorPesel=512** jest osobnym prawem konkretnej firmy w CompanyAccess.
Odczyt wymaga dodatkowo ViewContractors, edycja także EditContractors. AcceptWarnings
nie nadaje prawa do PESEL. Rola administratora instalacji również go nie nadaje.
Istniejących przypisań nie zmieniono: operator.m0 nie otrzymuje go automatycznie.
Prawo nadaje administrator zarządzający firmowymi uprawnieniami CompanyAccess;
nie dodano osobnego ekranu administracji uprawnieniami w tym zadaniu.

Bez prawa API pomija właściwość pesel w listach, szczególe, odpowiedziach zapisu,
konflikcie i odczycie trwałego wyniku POST. Próba jawnego wysłania niepustej lub
pustej wartości tekstowej daje 403 pesel_permission_denied, także na validate.
Brak pola/null przy PUT bez prawa **zachowuje istniejący PESEL**; nie kasuje go.
Z prawem pole/null może wyczyścić wartość. Serwer sprawdza prawa przy każdym
żądaniu; ukrycie kontrolki jest tylko dodatkową wygodą klienta.

PESEL nie jest wyszukiwany przez dotychczasowy GET q ani przez POST zwykłego
użytkownika. Żądanie POST search wymaga CSRF. Nie włączaj logowania ciał żądań,
odpowiedzi, wartości parametrów SQL ani zrzutów formularzy. ToString żądania,
wyniku kontroli i podglądu nie wypisuje PESEL. Komunikaty błędów opisują regułę,
bez numeru. Odczyt DTO z PESEL przez uprawnionego użytkownika jest celowym
udostępnieniem danych formularza, nie komunikatem diagnostycznym.

Audyt firmy przechowuje prawdziwe wartości przed/po, w tym PESEL, aby zachować
historię. Surowy audyt nie jest wystawiony przez API. Dostęp do SQL, backupów
oraz przyszłego odczytu historii musi obejmować ochronę tych danych; przyszły
endpoint historii musi sprawdzać ManageContractorPesel lub usuwać chronione
pola także z JSON i WarningJson. Nie dodano szyfrowania PESEL w tym zadaniu.

## API i model

Nowe właściwości JSON: regon, krs, bdo, pesel w istniejących żądaniach i DTO.
POST/PUT kontrahenta i POST identifiers/validate zachowują poprzednie trasy.
Wynik kontroli dodaje cztery elementy Checks. KRS/BDO mają FormatOnly,
REGON/PESEL Valid/Invalid przy sprawdzeniu sumy, puste pola Empty.
Version `pl-standard-2026-10-23-v1` jest częścią WarningHash. Kolejna zmiana
reguł wymaga nowej wersji i testów. Nie dodano unikalności nowych numerów.

Nowa trasa `POST /api/v1/companies/{companyId}/contractors/search` przyjmuje:

```json
{"query":"000000023","country":"PL","active":true,"offset":0,"limit":50}
```

Obsługuje także parentId/excludeId. Dotychczasowy GET z filtrami pozostaje
zgodny dla publicznych identyfikatorów. Limity stron, izolacja firm i resolver
pozostają. Trwałe wyniki POST ponownie stosują aktualne prawo PESEL. Kanoniczny
hash dawnych operacji bez nowych pól pozostaje zgodny; nie przepisujemy historii
ani rekordów operacji. Nowe snapshoty zmian danych głównych mają wersję 3,
stare i snapshoty szczegółów zachowują wersję 2.

W A i B dodano tylko Regon/Krs/Bdo/Pesel **nvarchar(100) NULL**. To limit techniczny
jak dla TaxId, umożliwiający zachowanie nieprawidłowej wartości po ostrzeżeniu.
Oczekiwane długości nie są sztywnym CHECK w SQL. Centralna migracja rozszerza
CHECK maski praw do 1023, bez zmiany istniejących uprawnień.

## Wdrożenie i sprawdzenie

Jawnie wykonano System `20261023173707_ContractorPeselPermission` i osobno A/B
`20261023173705_StandardContractorIdentifiers`. System ma 11 migracji, firmy po 8.
Nie wykonano resetów, zmian danych kontrahentów ani produkcyjnych zapytań.
Przed wdrożeniem sprawdzono lokalny SQL17 Express, Windows Authentication,
maszynę/instancję, dokładne nazwy trzech baz i ich znaczniki. Wykonano kopie
COPY_ONLY/CHECKSUM i RESTORE VERIFYONLY. Ścieżki są w lokalnym
TestResults/standard-identifiers-backup.log.

Porównanie liczności i SHA256 **42 istniejących tabel** po migracji potwierdziło
zachowanie wszystkich wcześniejszych danych, w tym GUS/DataPort, kont i uprawnień.
Nowe kolumny wszystkich wcześniejszych kontrahentów zainicjalizowano NULL.
Po testach **41 tabel nadal identycznych**; istniejące testy logowania i próba
GUI dopisały tylko zwykłe, następnie unieważnione sesje. Nowe SQL/API wycofują
własne konta, kontrahentów, audyt i operacje w transakcjach. Mogą pozostać luki
w licznikach IDENTITY, co jest normalnym skutkiem wycofania transakcji.
Nie wyłączano triggerów ani nie czyszczono kartotek.

**359/359 testów:** 352 bez SQL, 7 niedestrukcyjnych SQL/API, 0 pominiętych.
Sprawdzono sumy/długości/znaki/zera, normalizację, ostrzeżenia i hash, uprawnienia
PESEL/AcceptWarnings, CSRF, ochronę listy/szczegółu/konfliktu i cofnięcie prawa,
rowversion, replay nowych i dawnych operacji, SQL wyszukiwanie i izolację A/B,
fizyczne kolumny SQL oraz atomowość przy wymuszonym błędzie audytu po zapisie
kontrahenta. Build całego rozwiązania: 0 błędów i ostrzeżeń.

Nie uruchomiono **103 starszych testów SQL/API** korzystających z resetujących
fixture; nie są zaliczone w tym przebiegu. Testy używają atrap HTTP, bez zapytań
produkcyjnych. Próba rzeczywistego GUI: pola REGON/KRS/BDO, normalizacja, błąd
sumy, ukrycie PESEL bez prawa, anulowanie szkicu i wylogowanie. Nie zapisano
kontrahenta w próbie GUI; rzeczywisty zapis i edycję API/SQL wykonano w wycofywanych
transakcjach testowych. Formularz z prawem PESEL sprawdzono testem modelu widoku
oraz autoryzację rzeczywistym SQL/API, nie logowaniem osobnego konta w GUI.

```powershell
dotnet build Vatowiec.slnx --no-restore
$env:VATOWIEC_PRESERVED_VAT_SQL = '1'
dotnet test Vatowiec.slnx --no-build --filter '(Category!=SqlIntegration)|FullyQualifiedName~StandardIdentifierSqlTests|FullyQualifiedName~OfficialVatRulesSqlTests|FullyQualifiedName~GusConfigurationSqlTests|FullyQualifiedName~RegistrySettingsSqlTests' --logger 'trx;LogFileName=standard-identifiers-preserved.trx' --results-directory TestResults
Remove-Item Env:VATOWIEC_PRESERVED_VAT_SQL
```

NIE uruchamiaj DatabaseTools test/prepare-tests/initialize na aktualnych bazach.
Migracje Up są addytywne; downgrade usuwałby nowe pola lub zawężał dozwolone prawa,
więc nie wykonuj go na danych z nowymi identyfikatorami/uprawnieniami. Wymaga
osobnej decyzji operacyjnej i bezpiecznego backupu. Brak migracji przy starcie API.

Raporty lokalne: standard-identifiers-build.log, standard-identifiers-format.log,
standard-identifiers-preserved.trx, standard-identifiers-ui.log,
standard-identifiers-company.sql, standard-identifiers-system.sql oraz
standard-identifiers-{before,after,final}-data.json w TestResults.
[ADR-030](adr/ADR-030-standardowe-identyfikatory-kontrahenta.md).

## Źródła sprawdzone 23.10.2026

- [REGON — urzędowa definicja numeru 9/14 i powiązanie pierwszych 9 cyfr, §12](https://eli.gov.pl/api/acts/DU/2015/2009/text.html).
- [PESEL — urzędowy opis wag i cyfry kontrolnej](https://www.gov.pl/web/gov/czym-jest-numer-pesel).
- [BDO — opis dziewięciu cyfr](https://samorzad.gov.pl/web/gov/wykaz-rejestrow-i-ewidencji-panstwowych).
- [Implementacja referencyjna autora biblioteki identyfikatorów](https://github.com/devmuffy/polish-identification-numbers/blob/main/src/regon/index.ts) jako porównanie algorytmu REGON; nie dodano zależności i nie skopiowano kodu.

Wagi REGON są regułą techniczną walidacji lokalnej. Cytowana definicja urzędowa
potwierdza strukturę, nie publikuje wag. Nie przedstawiamy kontroli jako zapytania
lub potwierdzenia GUS. Oficjalne strony BIP/API GUS były niedostępne podczas
odczytu dokumentacji; nie wykonano zapytań o podatników.
