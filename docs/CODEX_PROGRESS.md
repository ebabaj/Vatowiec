# Stan prac

## Bieżący stan (2026-11-22)

Ten plik jest dziennikiem pojedynczych zadań agentów. Aktualny stan projektu
i etapu M0 opisuje [SPECYFIKACJA.md §13](../SPECYFIKACJA.md#m0--repozytorium-i-architektura),
zasady pracy i ustalenia — [AGENTS.md](../AGENTS.md).

Po ADR-031 wdrożono ADR-032–043 (m.in. użytkownicy i hasła, Personal, widoki
tabel, numery telefonów); ich wyniki są w odpowiednich ADR i dokumentach w `docs/`.

Zadanie 2026-11-22 (domknięcie M0, bez Commit i Push): CLAUDE.md importujący
AGENTS.md, poprawki niespójności dokumentacji (licencja MIT/ADR-002, stan M0,
liczby testów), CI w `.github/workflows/ci.yml`, CONTRIBUTING.md, SECURITY.md,
`.gitattributes` (LF), formatowanie istniejącego kodu (`dotnet format`, wyłącznie
białe znaki i usunięcie BOM z trzech migracji System) oraz propozycja
[ADR-044](adr/ADR-044-provisioning-i-backup-firmy.md) czekająca na akceptację.
Weryfikacja: build Debug 0 ostrzeżeń/0 błędów; testy bez SQL i bez Personal
541 zaliczonych, 0 błędów, 136 pominiętych; `dotnet format --verify-no-changes`
i skan podatnych pakietów bez uwag. Pełnego runnera SQL nie uruchamiano.
Logi lokalne: `TestResults/m0-docs/`.

Zadanie 2026-11-22 (ADR-044 krok 1 — tworzenie firmy, bez Commit i Push):
domena `CompanyOperation`/`CompanyOperationEvent`, migracja System
`CompanyProvisioning` (wykonana jawnie `migrate-system` na developerskim
`Vatowiec_System`), usługa `src/Server/Operations`, endpointy administratora
instalacji, ekran Avalonia „Ustawienia instalacji → Firmy”, testy bez SQL i SQL.
Poprawiono też nieaktualne liczby migracji w `OfficialVatRulesSqlTests` i
`TableViewSqlTests` (porównanie z listą migracji zamiast stałej liczby).
Weryfikacja: build 0/0; 581 zaliczonych, 0 błędów, 109 pominiętych (bez Personal,
z `VATOWIEC_PRESERVED_VAT_SQL=1` i `VATOWIEC_PROVISIONING_SQL=1`); formatowanie
bez uwag; po testach brak pozostawionych baz i wierszy; usługa uruchomiona
próbnie w Development. Nie wykonano próby w oknie Avalonia ani pełnego runnera
`DatabaseTools test` (jego preflight oczekuje nieaktualnej liczby tabel System —
znana wcześniejsza niezgodność). Logi: `TestResults/m0-provisioning/`.

Zadanie 2026-11-22 (ADR-045 — nazwy baz z nazwy firmy, bez Commit i Push):
`CompanyDatabaseNames` przeniesione do SharedContracts (podgląd w Avalonia, ta sama
reguła na serwerze), kontrola „Nazwa zajęta…” (katalog, zlecenia, `sys.databases`,
nazwy zastrzeżone), formularz pyta tylko o nazwę firmy. Jednorazowo zmieniono nazwę
bazy Tenjin `Vatowiec_Company_0057827c8b3045d599b1230c1db2e92b` → `Vatowiec_Tenjin`
po sprawdzeniu znaczników, profilu i rejestracji; zaktualizowano `core.CompanyDatabases`
i `ops.CompanyOperations` w jednej transakcji. Pliki .mdf/.ldf zachowały poprzednie
nazwy fizyczne. Weryfikacja: build 0/0; 588 zaliczonych, 0 błędów, 109 pominiętych;
formatowanie bez uwag. Do zatrzymania przed buildem: API, usługa Operations i klient
Desktop użytkownika (blokował pliki); API i usługę przywrócono.

Zadanie 2026-11-22 (ADR-044 krok 2 — kopie z rotacją i harmonogramem, bez Commit i Push):
domena `CompanyBackup`, `BackupPolicy`, `CompanyBackupSchedule`, `BackupSettingChange`,
`BackupReminderSnooze`, reguły terminów; migracja System `CompanyBackups` (wykonana
`migrate-system`); wykonawca `CompanyOperationExecutor` (wspólna kolejka, tworzenie firmy
i kopia), `BackupStorage`, `BackupScheduler`; API kopii firmy i polityki instalacji;
Avalonia: FIRMA → Kopie zapasowe, przypomnienie po otwarciu firmy, polityka w
Ustawieniach instalacji → Firmy. Prawo `ManageCompanyBackups = 1024`.
Środowisko: utworzono `C:\ProgramData\Vatowiec\Backups` z prawem Modify dla
`NT Service\MSSQL$SQLEXPRESS`. `DatabaseTools verify` dopuszcza teraz firmy utworzone
z aplikacji obok A/B. Weryfikacja: build 0/0; 604 zaliczonych, 0 błędów, 109 pominiętych;
formatowanie bez uwag; po testach brak plików i wierszy testowych; usługa uruchomiona
próbnie. Nie wykonano próby w oknie Avalonia.

Zadanie 2026-11-22 (ADR-045 — usuwanie firmy, bez Commit i Push): operacja `Delete`
(dezaktywacja, archiwum, DROP, czyszczenie w jednej transakcji System), status `Cancelled`
i anulowanie, `ops.DeletedCompanies`, triggery historii z kontrolowanym DELETE przez
`SESSION_CONTEXT`, migracja `CompanyDeletion` (wykonana `migrate-system`), endpointy
usuwania/anulowania/katalogu firm/rejestru usuniętych, sekcja „Usuwanie firmy” w Avalonia.
Weryfikacja: build 0/0; 610 zaliczonych, 0 błędów, 109 pominiętych; formatowanie bez uwag;
po testach brak baz, plików i wierszy testowych. Kopia Tenjin zlecona przez właściciela
z aplikacji zakończyła się poprawnie (plik i manifest w katalogu kopii).

Zadanie 2026-11-22 (ADR-044 krok 3 — odtwarzanie; ADR-045 uzupełnienia, bez Commit i Push):
operacja `Restore` (baza robocza, CHECKDB, migracja starszego schematu, przełączenie nazw,
kopia `PreRestore`, dotychczasowa baza `…_przed_…`), przywrócenie usuniętej firmy i
przeniesienie z `_import` (`BackupImportScanner`, `ops.RestoreImportFiles`), migracja
`CompanyRestore` (wykonana `migrate-system`), sekcja „Odtwarzanie z kopii” w Avalonia.
ADR-045: 10 minut zwłoki przy usuwaniu bez archiwum (tylko dokumentacja techniczna),
archiwum historii `.history.json`, dezaktywacja przy przyjęciu zlecenia.
Weryfikacja: build 0/0; 616 zaliczonych, 0 błędów, 109 pominiętych; formatowanie bez uwag;
po testach brak baz, plików i wierszy testowych. Do formalnego zamknięcia M0: pełny runner
SQL (preflight `DatabaseTools` do aktualizacji) i próba dwóch operatorów z właścicielem.

Zadanie 2026-11-22 (pełny runner SQL, bez Commit i Push): runner `DatabaseTools test`
przebudowany — testy niszczące na tymczasowych bazach `Vatowiec_System_Test`,
`Vatowiec_Company_A_Test/B_Test` (tworzone, migrowane, sprawdzane i zawsze usuwane);
usunięto przestarzały preflight danych demonstracyjnych, który oczekiwał 13–14 tabel
System i czyścił z `Vatowiec_System` wszystkie konta i firmy (w tym konta właściciela
i firmy z aplikacji). Testy wskazują bazy przez `SqlFixtureDatabases`. Poprawiono
nieaktualne asercje: liczby migracji (porównanie z listą programu), `RowVersion` = 8
bajtów (błędna zmiana na 9 w 3b52486), limit strony 201 (ADR-042), oczekiwanie na
odświeżenie listy po zamknięciu edytora i jawny podgląd `ShowContractor` w firmie
tylko do odczytu. Wynik: **141/141 testów SQL, 0 pominiętych**; bez SQL 578 zaliczonych,
0 błędów; formatowanie bez uwag. Bazy developerskie nietknięte (konta, A/B, Tenjin, kopia).
Logi: `TestResults/m0-sql/`.

Zadanie 2026-11-22 (ADR-046 — spłata długu technicznego z analizy, bez Commit i Push):
endpointy przeniesione z `Api/Security` do `Api/Endpoints`, `IdentifierChecks` do
`Core.Infrastructure.Contractors`, wspólne `EndpointResults` zamiast zduplikowanych
pomocników, `Program.cs` uporządkowany (`Hosting/ApiPipeline.cs`, kody odpowiedzi bez
zmian); klient: 128 obsług `async void` zastąpionych `UiActions.Run` z komunikatem
o nieoczekiwanym błędzie zamiast zamknięcia programu, plus siatka
`Dispatcher.UIThread.UnhandledException`; resolver bazy firmy pamięta udaną weryfikację
migracji przez 30 s (profil firmy i prawa nadal przy każdym żądaniu). Reguła miejsca
logiki dla M1 (ADR-046) przyjęta przez właściciela 2026-11-22.
Wynik: build 0/0; bez SQL 580 zaliczonych, 0 błędów; **SQL 141/141**; natywny test okna
1/1; formatowanie bez uwag.

## Archiwum: naprawa zakładki Finansowe

2026-10-23. Ukończone. BEZ Commit i Push.
Na początku tego zadania repozytorium zawierało niezacommitowane, ukończone
wdrożenie ADR-031 z poprzedniego zadania; zachowano wszystkie te zmiany.
Nie traktować ich jako nowych zmian API/SQL w tej naprawie.

Przeczytano AGENTS.md, zapis postępu poprzedniego etapu, SPEC ORG-012,
ADR-031, dokumentację finansów i implementację od SQL do kontrolek.

Przyczyna: dynamiczne listy z nowymi pustymi opcjami plus dwukierunkowe
SelectedItem podczas odświeżania. Puste opcje trafiały do szkicu i wywoływały
fałszywą różnicę względem snapshotu. Dodatkowo odrzucona zmiana kierunku mogła
pozostawić błędną etykietę kierunku w kontrolce. API i SQL były poprawne.

Naprawa: stabilne kolekcje, wybór po Code, kontrolowana publikacja słowników,
jednokierunkowy odczyt kontrolek i przekazywanie tylko interaktywnego wyboru.
Jawna synchronizacja odrzuconego kierunku. Nieznany kod zachowany i opisany,
bez zamiany na NULL; blokada zapisu do świadomego wyboru/odświeżenia.
Snapshot danych serwera pozostaje wzorcem IsDirty i właściwej RowVersion.
Układ tylko Finansowe: do 860 px, dwie kolumny, liczby 140 px, uwagi, tabela
limitów, osobne przyciski, pomocnicze odświeżanie, scroll i Tab order.

Zmodyfikowane w SAMEJ NAPRAWIE:
- src/Desktop/MainWindow.axaml
- src/Desktop/MainWindow.axaml.cs
- src/Desktop/ViewModels/MainViewModel.Finance.cs
- tests/Vatowiec.Tests/ContractorFinanceTests.cs (rozszerzenie atrapy)
- tests/Vatowiec.Tests/ContractorFinanceBindingTests.cs (nowe 7 regresji)
- docs/contractor-finance.md (przyczyny, weryfikacja, instrukcja)
- docs/CODEX_PROGRESS.md

Wyniki końcowe: build całego solution 0 błędów/0 ostrzeżeń, 390/390 testów
(381 bez SQL, 9 bezpiecznych SQL/API), 0 pominiętych w wykonanym zestawie.
103 stare resetujące SQL/API WYŁĄCZONE, nie są zaliczone.
Formatowanie zmienionych C# i diff-check prawidłowe.
Bez migracji, resetów, DDL i zmian modelu/API. Integracje bez zmian.

Rzeczywista Avalonia przez HTTPS:
- 4132 tylko odczyt: Sale30/TRANSFER/PLN, Purchase45/TRANSFER/PLN;
  przełączanie, refresh i reopen bez fałszywego pytania o szkic. SHA256 wszystkich
  kolumn jego ContractorTradeTerms wraz z RowVersion identyczne przed/po.
- NOWY fikcyjny FIN-UI-1023212310, Id=4173: faktyczne zapisy i ponowny odczyt
  obu zestawów, zmiana i przywrócenie CASH/EUR -> TRANSFER/PLN, ochrona zmian
  (KeepEditing także przywraca widoczny kierunek), niezależność głównego szkicu.
- Faktyczne limity: Sale EUR2600/PLN10000, Purchase PLN5000, tworzenie/edycja
  w GUI, niezależne listy. Kontrahent 4173 po próbie dezaktywowany; audyt i dane
  pozostały. Nie usuwać ich fizycznie w ramach sprzątania.
- Firma B: tylko odczyt, przełączanie bez szkicu. Logout.
- Obejrzano zrzut okna. Sekcja 860 px, liczby 140; okno 920x650 daje824px,
  działa scroll bez poziomego wyjścia. Tab: dni/metoda/waluta/rabat/uwagi/zapis/anuluj.
- Pierwsza próba GUI wykryła nieskuteczność samego stabilizowania tablic;
  dopiero OneWay + interaktywna aktualizacja usunęły wewnętrzny stan bindingu.
  Druga wykryła etykietę kierunku po KeepEditing; poprawiono i ponowiono.
  Braki w automatyzacji wirtualizowanej listy PLN i panelu bez UIA peer poprawiono
  w lokalnych skryptach. To nie są pozostałe błędy aplikacji.

Logi ignorowane w TestResults: finance-repair-build.log, -format.log,
-preserved.log/.trx; finance-repair-ui*.log; finance-repair-data-final.log,
finance-repair-ui.png, finance-repair-4132.hash. Lokalne skrypty nie zawierają
haseł; odczytują istniejący user-secrets do pamięci. Nie logują sekretów.

Brak pozostałej implementacji i znanych nieusuniętych usterek w zleconym zakresie.
Do przeglądu: docs/contractor-finance.md, sekcja naprawy formularza.
Commit i Push wyłącznie po osobnym poleceniu. Nie uruchamiać resetujących testów.
Dalsze testowanie UI z zapisem prowadzić na fikcyjnym rekordzie, nigdy 4132.
Po końcowym buildzie i testach ponownie uruchomiono API (HTTPS 7243) oraz
Avalonia, pozostawioną na ekranie logowania. Przy wznowieniu sprawdzić procesy.

Poniżej historyczny stan poprzedniego wdrożenia, sprzed tej naprawy:

## Poprzedni etap — warunki finansowe kontrahenta (ADR-031)

2026-10-23. Zadanie ukończone. Bez Commit i Push. Na początku repozytorium
było czyste; wcześniejsze zmiany ADR-030 były już zatwierdzone przez użytkownika.
Nie zaczynaj implementacji od nowa. Wymagania ORG-003/012; PAY-001 pozostaje
przyszłym, szerszym zakresem dokumentów i rozrachunków.

## Co wykonano

- Trzy nowe tabele w każdej bazie firmy: core.ContractorTradeTerms,
  core.ContractorCreditLimits i ref.PaymentMethods. Bez zmian SystemDbContext.
- Opcjonalne warunki Sale/Purchase, dni 0–3650, rabat decimal(7,4) 0–100,
  uwagi do 2000 znaków, nullable waluta/metoda. Puste wartości nie oznaczają PLN.
- Waluty FK do istniejącego Currencies.Code char(3), identyczna kolacja
  Latin1_General_100_BIN2. Limity decimal(24,8), nieujemne; jeden aktywny
  na kontrahenta/kierunek/walutę, filtrowany indeks unikalny.
- Otwarty słownik ze stabilnym Code, edytowalnymi Name/IsActive; pięć pozycji
  początkowych. Brak fizycznego usuwania. Nowe odwołania tylko do aktywnych pozycji;
  istniejące nieaktywne odwołanie można zachować.
- API odczytu/samodzielnego zapisu warunków, limitów i słownika; ViewContractors,
  zapis także EditContractors, CSRF, RowVersion, autoryzowany resolver firmy.
  Trwała idempotencja nowych rekordów i atomowy ContractorChanges z wykonawcą.
- Avalonia: szósta zakładka Finansowe, Sprzedaż/Zakup, osobne szkice warunków,
  limitu i słownika. Ochrona niezapisanych zmian, anulowanie, ręczne porównanie
  konfliktu. Zapis finansów nie wysyła szkicu danych głównych.
- Bez faktur, sald, wykorzystania limitów, blokad sprzedaży i zmian integracji.

## Wdrożenie i zachowanie danych

- Zweryfikowano lokalną maszynę/localhost\SQLEXPRESS, SQL Server 17 Express,
  Windows Authentication, dokładne nazwy, znaczniki i historię trzech baz.
- Przed migracją utworzono kopie COPY_ONLY/CHECKSUM A/B i wykonano VERIFYONLY:
  C:\Program Files\Microsoft SQL Server\MSSQL17.SQLEXPRESS\MSSQL\Backup\
  Vatowiec_Company_A_Dev_before_contractor_finance_ac0acb4d204f480fafe4d4ba14f0f183.bak
  Vatowiec_Company_B_Dev_before_contractor_finance_b65ac532ee69412db19ad21b1d7c0a25.bak
- Jawnie zastosowano 20261023182240_ContractorFinance osobno w A/B. Obie mają
  po 9 migracji. System nadal 11, bez DDL. Nie resetowano baz ani nie uruchamiano
  DatabaseTools test/initialize/prepare-tests.
- Pierwsza próba CREATE została wycofana przez niezgodną kolację FK waluty;
  poprawiono jeszcze niezastosowaną migrację/mapowanie/snapshot oraz test kolacji.
  Ponowne wdrożenie przeszło. Nie zmieniono starszych migracji ani pól walut.
- Po migracji liczności/SHA256 wszystkich 42 starych tabel zgodne, także
  konfiguracje GUS/DataPort. Po testach i GUI 41 zgodnych: jedyna różnica to zwykłe,
  następnie unieważnione UserSessions. Dane nowych testów wycofano transakcyjnie.
  IDENTITY może mieć luki po rollback.
- Stan końcowy: A kontrahenci=4, warunki=0, limity=0, sposoby=5;
  B kontrahenci=1, warunki=0, limity=0, sposoby=5.
- Down usuwa nowe tabele: nie traktować go jako bezpiecznego rollbacku po
  rozpoczęciu pracy użytkowników; wtedy wymagana osobna decyzja i ochrona danych.

## Weryfikacja

- Kompilacja całego Vatowiec.slnx: 0 błędów, 0 ostrzeżeń.
- 383/383 wykonanych testów: 374 bez SQL i 9 niedestrukcyjnych SQL/API,
  0 błędów, 0 pominiętych w wykonanym zestawie.
- 103 starszych SQL/API z resetującymi fixture WYŁĄCZONO z przebiegu.
  Nie były uruchomione i nie są zaliczone.
- Nowe testy: walidacja/precyzja, null/zero, fizyczne FK/checks/indeksy/kolacja,
  uprawnienia i CSRF, A/B, Sale/Purchase, PLN/EUR, RowVersion, idempotencja,
  audyt, awaria drugiego SaveChanges i rollback, niezależność szkiców klienta.
  Dwa niezależne połączenia SQL konkurują o ten sam aktywny limit; drugie czeka
  na zakończenie pierwszej transakcji. Obie transakcje wycofane.
- Nowe SQL plus istniejące bezpieczne klasy: ContractorFinanceSqlTests,
  StandardIdentifierSqlTests, OfficialVatRulesSqlTests, GusConfigurationSqlTests,
  RegistrySettingsSqlTests. Uruchomienie z VATOWIEC_PRESERVED_VAT_SQL=1;
  dokładne polecenie i filtr w docs/contractor-finance.md.
- Formatowanie zmienionych C# i git diff --check poprawne.
- Rzeczywista Avalonia przez HTTPS: operator A, Finansowe/Sprzedaż/Zakup,
  brak PLN, błędne dni, ochrona szkiców/anulowania warunków, limitu i słownika,
  zachowanie niezapisanej nazwy kontrahenta; firma B tylko odczyt; logout.
  Próba GUI NIE zapisywała finansów. Realne zapisy/edycje sprawdzono API/SQL
  z rollback; rozdzielenie zapisu klienta także testami modelu widoku.
- Nie wykonywano produkcyjnych zapytań do rejestrów.
- Po próbie API i Desktop uruchomione (sesje narzędzia 59483 i 7774),
  Desktop wylogowany. Przy wznowieniu sprawdzić procesy zamiast zakładać ich stan.

Logi lokalne, ignorowane przez Git: TestResults/finance-build.log,
finance-format.log, finance-preserved.log/.trx, finance-ui.log, finance-backup.log,
finance-migration.sql, finance-{before,after,final}-data.json.

## Zmienione pliki

Dokumentacja:
- AGENTS.md
- README.md
- SPECYFIKACJA.md
- docs/CODEX_PROGRESS.md
- docs/authentication.md
- docs/contractors.md
- docs/contractor-finance.md (nowy)
- docs/adr/ADR-031-warunki-finansowe-kontrahenta.md (nowy)

Model, API, migracje:
- src/Modules/Core/Domain/ContractorFinance.cs (nowy)
- src/Modules/Core/Infrastructure/Persistence/CompanyConfigurations/ContractorFinanceConfiguration.cs (nowy)
- src/Modules/Core/Infrastructure/Persistence/CompanyDbContext.cs
- src/Modules/Core/Infrastructure/Persistence/Migrations/Company/20261023182240_ContractorFinance.cs (nowy)
- src/Modules/Core/Infrastructure/Persistence/Migrations/Company/20261023182240_ContractorFinance.Designer.cs (nowy)
- src/Modules/Core/Infrastructure/Persistence/Migrations/Company/CompanyDbContextModelSnapshot.cs
- src/SharedContracts/ContractorFinanceContracts.cs (nowy)
- src/Server/Api/Security/ContractorFinanceEndpoints.cs (nowy)
- src/Server/Api/Security/CompanyEndpoints.cs
- tools/database/Vatowiec.DatabaseTools/LocalDatabases.cs (weryfikacja schematu)

Desktop:
- src/Desktop/MainWindow.axaml
- src/Desktop/MainWindow.axaml.cs
- src/Desktop/Services/VatowiecApiClient.cs
- src/Desktop/ViewModels/MainViewModel.Contractors.cs
- src/Desktop/ViewModels/MainViewModel.Details.cs
- src/Desktop/ViewModels/FinanceEditors.cs (nowy)
- src/Desktop/ViewModels/MainViewModel.Finance.cs (nowy)

Testy:
- tests/Vatowiec.Tests/ContractorFinanceTests.cs (nowy)
- tests/Vatowiec.Tests/ContractorFinanceSqlTests.cs (nowy)
- tests/Vatowiec.Tests/CompanyModelTests.cs
- tests/Vatowiec.Tests/OfficialVatRulesSqlTests.cs
- tests/Vatowiec.Tests/SqlServerPersistenceTests.cs
- tests/Vatowiec.Tests/StandardIdentifierTests.cs

## Pozostałe kroki i ograniczenia

Brak pozostałej implementacji w zleconym zakresie. Do przeglądu użytkownika:
1. Przeczytać docs/contractor-finance.md oraz ADR-031.
2. Opcjonalnie wykonać opisany scenariusz ręczny: zapis warunków Sprzedaż/Zakup,
   wielowalutowe limity, słownik i konflikt dwóch klientów.
3. Commit/Push wyłącznie po osobnym poleceniu użytkownika.

Nie wykonywać ponownie zakończonych migracji, testów ani kopii bez nowej potrzeby.
Nie resetować aktualnych baz. Nie deklarować zaliczenia 103 wyłączonych testów
ani ręcznego zapisu GUI; brak tych przebiegów opisany w raporcie.
