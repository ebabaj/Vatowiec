# Stan pracy Codex — kod ISO jako klucz waluty (ADR-024)

## Publikacja zlecona przez użytkownika — 2026-10-23

Użytkownik jawnie zlecił Commit i Push do main z komunikatem
„M1 - slownik walut ISO 4217 i kursy NBP”. Wcześniejsze zakazy Commit/Push
opisują poprzedni etap i nie blokują tej konkretnej publikacji.
DataPort jest już w fc7f0f310c297663984893bb80df10218756a730; nie twórz go ponownie.
Sprawdzono, że lokalny main i origin/main wskazują ten commit. Zakres nowej
publikacji to ADR-023/024: waluty/NBP, migracje, ich podłączenie do UI/API,
uprawnień, dokumentacji i istniejącego runnera testów. Brak nowej implementacji
DataPort i zmian innych modułów biznesowych. TestResults, kopie i sekrety
pozostają poza repozytorium. Przed publikacją ponownie wykonano build
(0 błędów/ostrzeżeń) oraz 150 testów bez SQL; potwierdzono wcześniejszy pełny TRX
244/244 bez pominięć. SHA publikacji odczytaj z historii Git.


2026-10-23. ZADANIE UKOŃCZONE. Bez Commit/Push.
Przed wznowieniem przeczytaj ten plik i git status. Zachowaj wcześniejsze
niezatwierdzone zmiany ADR-023; nie zaczynaj implementacji od nowa.

## Zlecenie i obowiązująca zgoda

Walutę identyfikuje wyłącznie ISO Code char(3) jako PK, bez numerycznego/GUID
Id waluty. Kurs zachowuje własny bigint Id i wskazuje CurrencyCode char(3) FK.
Bez migracji danych demonstracyjnych i mapowania dawnych identyfikatorów.

Po początkowym zakazie czyszczenia innych modułów użytkownik jawnie odpowiedział:
„Kasuj wszystko. Odwołuję poprzednie ograniczenie. Klucz API skonfiguruję - to
nie problem.” Zgodę wykorzystano do pełnego runnera fixture na trzech znanych
lokalnych bazach VATowca. Nie trzeba ponawiać pytania. Nie rozszerzaj zgody na
inne aplikacje, SQL systemowy ani serwery zdalne. Nie tworzono *_Test, nie
wykonano DROP/CREATE baz. Pomysł dodatkowego runnera izolowanego usunięto.
Klucz DataPort mimo zgody na usunięcie zachowano istniejącym mechanizmem
szyfrogramu w pamięci i verified backupów; nie odszyfrowywano go ani nie wypisano.

## Wykonano

- Currency.Code init jest jedynym PK, char(3), ValueGeneratedNever, BIN2,
  wielkie litery. SQL trigger blokuje zmianę kodu także bez powiązanych kursów.
- ExchangeRate.CurrencyCode char(3) FK do Code. Własny bigint IDENTITY kursu
  i unikalność CurrencyCode/Source/TableType/EffectiveDate zachowane.
- Domyślny słownik149 i EUR/USD/GBP/CHF A, PLN bez pobierania.
- API PUT /currencies/{currencyCode}; brak Id w CurrencyResponse. RateResponse
  i RateConflict używają CurrencyCode. Serwisy, idempotencja, klient API,
  wybór/cache/lista i filtry Avalonia używają kodów. Brak liczbowych aliasów.
- Współbieżność, trwałe ponowienie, audyt atomowy, CSRF, sesje, autoryzacja i
  izolacja firm zachowane. Nie zmieniano reguł księgowych/architektury.
- Rachunki już miały CurrencyCode, bez CurrencyId; zasady ich edycji niezmienione.
- Nowa migracja 20261023041721_CurrencyIsoCodePrimaryKey: DROP/CREATE tylko
  ref.Currencies, core.ExchangeRates, audit.CurrencyOperations + seed, indeksy,
  FK i triggery. Bez INSERT SELECT/mapowania danych. Down odmawia numerycznego PK.
- Historyczne migracje i ADR-023 zachowano; aktywny snapshot już bez CurrencyId.
- Jawne wdrożenie A/B po preflight/zweryfikowanych COPY_ONLY/CHECKSUM/VERIFYONLY.
  localhost\SQLEXPRESS, Windows Authentication. Każda baza ma14tabel,
  System i obie firmy po7migracji. Nowe firmy kończą pełny łańcuch na tym modelu.
- Bezpośrednio po DDL hashe/liczniki WSZYSTKICH tabel poza modułem były identyczne,
  w tym DataPort. Dopiero osobny, potem zatwierdzony pełny runner zresetował
  inne fikcyjne kartoteki i konta. Migracja sama ich nie zmieniała.
- Dokumentacja: ADR-024, adnotacja historycznego ADR-023, SPECYFIKACJA 0.12.1,
  AGENTS, README, docs/currencies-and-nbp.md i database-per-company.md.

## Końcowe wyniki

- Build całego solution:0błędów,0ostrzeżeń. TestResults/iso-build.log.
- **244/244 testy, bez pominięć:94 SQL/API +150 bez SQL.**
- Raport TestResults/database-per-company-b41b723aa3f244c6abbf7a917bb74080.trx.
- Log TestResults/iso-all-tests.log. Wszystkie automatyczne HTTP są atrapami.
- Nowe testy klucza/DTO, fizycznego PK/FK, braku Id/CurrencyId, niezmienności,
  małych liter i liczbowych ścieżek. Pozostałe konflikty/CAS/audyt/rollback,
  deduplikacja/ponowienia, uprawnienia/CSRF/sesje/izolacja i filtry przechodzą.
- dotnet format --verify-no-changes i STANDARDOWE git diff --check:0.
  Nie wyłączaj autocrlf podczas diff-check, bo CRLF dają pozorne błędy whitespace.
- Ręczne SQL A/B w rollback: duże litery, FK i trigger niezmienności potwierdzone.
- Rzeczywiste HTTPS API: login/CSRF, brak Id waluty, kod zamiast liczby, odmowa
  edycji kodu, B i czytelnik, import, deduplikacja, replay oraz filtr EUR:
  30.09.2026,190/A/NBP/2026,Mid4,3672. Wszystko poprawne.
- Rzeczywiste okno Avalonia uruchomione/obejrzane przed i po pełnym runnerze.
  Zapis/przywrócenie EUR, filtr CHF z1kursem, końcowy filtr C z Bid/Ask/daty/strony.
  Zrzuty TestResults/iso-ui-filter.png i iso-ui-final.png.

## Liczniki przed/po i rzeczywisty NBP

                         Przed DDL     Po DDL     Stan końcowy
A ref.Currencies             149         149          149
A core.ExchangeRates         413           0          415
A audit.CurrencyOperations    12           0           13
B ref.Currencies             149         149          149
B core.ExchangeRates           0           0            0
B audit.CurrencyOperations     0           0            0

Stare kursy usunięto, nie migrowano. Ponownie pobrano EUR A/C za02.01–22.10.2026
(412tabel/412kursów), potem A dla4domyślnych walut22.10 (+3,1pominięty).
Powtórny import0nowych/4pominięte; replayten sam AuditId bez nowego HTTP/audytu.
Porównano413dawnych kombinacji kod/źródło/typ/data:0braków i0zmian numeru tabeli,
daty notowania i Mid/Bid/Ask. Dodatkowe2to EUR205/A i205/C z21.10.2026.
Id publikacji, rowversion i UTC pobrania są nowe. Dane porównawcze nie były
importowane z pliku; źródłem zapisanych kursów był wyłącznie oficjalny NBP.
Po pełnym runnerze ponownie wykonano te ręczne próby; stan końcowy jest415/13.
Nowa historia:8zmian ustawień w API,3importy,2zmiany/przywrócenia w oknie.

Próby rzeczywiste nie należą do automatycznego zestawu. Brak nowych zapytań
MF/GUS/VIES/DataPort. Szyfrogram DataPort w A przywrócony przez runner.
Konta operator.m0 i czytelnik.m0 odtworzone, hasła z lokalnego user-secrets.

## Artefakty i ponowne otwarcie

TestResults/IsoCurrencyReset.sql — przejrzany SQL offline.
TestResults/iso-before-counts.json, iso-before-rates.json — wyłącznie baseline
liczników/hashów i publicznych kursów bez numerycznych Id walut.
TestResults/iso-after-ddl-*, iso-after-download-*, iso-final-* — porównania.
TestResults/iso-live-probe-final.log — ostatnia rzeczywista próba API/NBP.
TestResults/Capture-IsoBaseline.ps1 i Probe-IsoCurrencyApi.ps1 — lokalne pomocnicze
skrypty poza repo. Nigdy nie wypisuj/utrwalaj haseł, cookie, CSRF ani kluczy API.

API i Avalonia uruchomione do przeglądu; końcowo operator w A, filtr tabeli C.
Sesja wygasa normalnie; procesy mogą zakończyć się z sesją narzędzi. Start:

dotnet run --project src/Server/Api --no-build --launch-profile https
dotnet run --project src/Desktop --no-build

Logowanie lokalnym hasłem, firmaA, Słowniki → Waluty i kursy NBP. B tylko odczyt.

## Problemy i dalsze kroki

Brak znanych blokujących błędów i brak pozostałej pracy dla tego zadania.
Nie trzeba ponawiać testów ani odczytów NBP. Nie ogłaszamy gotowości księgowej
ani produkcyjnej. Nie wykonano Commit ani Push. Przy kolejnym zadaniu zachowaj
cały dotychczasowy diff (obejmuje również poprzedni niezatwierdzony etap ADR-023).

## Zmienione pliki
```text
 M AGENTS.md
 M README.md
 M SPECYFIKACJA.md
 M docs/authentication.md
 M docs/database-per-company.md
 M docs/dataport.md
 M docs/desktop.md
 M src/Desktop/App.axaml
 M src/Desktop/MainWindow.axaml
 M src/Desktop/MainWindow.axaml.cs
 M src/Desktop/Services/VatowiecApiClient.cs
 M src/Desktop/ViewModels/MainViewModel.Contractors.cs
 M src/Desktop/ViewModels/MainViewModel.Details.cs
 M src/Desktop/ViewModels/MainViewModel.cs
 M src/Modules/Core/Domain/CompanyAccess.cs
 M src/Modules/Core/Infrastructure/DependencyInjection.cs
 M src/Modules/Core/Infrastructure/Persistence/CompanyDbContext.cs
 M src/Modules/Core/Infrastructure/Persistence/Migrations/Company/CompanyDbContextModelSnapshot.cs
 M src/Modules/Core/Infrastructure/Persistence/Migrations/System/SystemDbContextModelSnapshot.cs
 M src/Modules/Core/Infrastructure/Persistence/SystemConfigurations/CompanyAccessConfiguration.cs
 M src/Modules/Core/Infrastructure/Registries/RegistryAdapters.cs
 M src/Server/Api/Security/CompanyEndpoints.cs
 M tests/Vatowiec.Tests/AuthenticationIntegrationTests.cs
 M tests/Vatowiec.Tests/CompanyModelTests.cs
 M tests/Vatowiec.Tests/DesktopTests.cs
 M tests/Vatowiec.Tests/SqlServerFactAttribute.cs
 M tests/Vatowiec.Tests/SqlServerPersistenceTests.cs
 M tools/database/Vatowiec.DatabaseTools/LocalDatabases.cs
?? docs/CODEX_PROGRESS.md
?? docs/adr/ADR-023-waluty-i-reczne-kursy-nbp.md
?? docs/adr/ADR-024-kod-iso-jako-klucz-waluty.md
?? docs/currencies-and-nbp.md
?? src/Desktop/ViewModels/MainViewModel.Currencies.cs
?? src/Modules/Core/Application/ExchangeRates.cs
?? src/Modules/Core/Domain/Currency.cs
?? src/Modules/Core/Infrastructure/Persistence/CompanyConfigurations/CurrencyConfigurations.cs
?? src/Modules/Core/Infrastructure/Persistence/CurrencySeed.cs
?? src/Modules/Core/Infrastructure/Persistence/ExchangeRateService.cs
?? src/Modules/Core/Infrastructure/Persistence/Migrations/Company/20261022205922_CurrenciesAndNbpRates.Designer.cs
?? src/Modules/Core/Infrastructure/Persistence/Migrations/Company/20261022205922_CurrenciesAndNbpRates.cs
?? src/Modules/Core/Infrastructure/Persistence/Migrations/Company/20261022211121_OptionalNbpTradingDate.Designer.cs
?? src/Modules/Core/Infrastructure/Persistence/Migrations/Company/20261022211121_OptionalNbpTradingDate.cs
?? src/Modules/Core/Infrastructure/Persistence/Migrations/Company/20261023041721_CurrencyIsoCodePrimaryKey.Designer.cs
?? src/Modules/Core/Infrastructure/Persistence/Migrations/Company/20261023041721_CurrencyIsoCodePrimaryKey.cs
?? src/Modules/Core/Infrastructure/Persistence/Migrations/System/20261022205924_CurrencyPermissions.Designer.cs
?? src/Modules/Core/Infrastructure/Persistence/Migrations/System/20261022205924_CurrencyPermissions.cs
?? src/Modules/Core/Infrastructure/Registries/NbpClient.cs
?? src/Server/Api/Security/CurrencyEndpoints.cs
?? src/SharedContracts/CurrencyContracts.cs
?? tests/Vatowiec.Tests/CurrencyDesktopTests.cs
?? tests/Vatowiec.Tests/CurrencyIntegrationTests.cs
?? tests/Vatowiec.Tests/NbpTests.cs
```
