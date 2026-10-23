# Stan prac — standardowe identyfikatory kontrahenta (ADR-030)

2026-10-23. **Zadanie zakończone. Bez Commit i Push.**
Na początku tego zadania git status był czysty (wcześniejsze prace ADR-028/029
były już zapisane przez użytkownika). Wszystkie bieżące zmiany dotyczą ADR-030.
Przy wznowieniu przeczytaj ten plik i sprawdź git status; nie zaczynaj od nowa.

## Wykonano

- Przeczytano AGENTS, SPEC ORG-003/007, ADR kontrahentów 018–021 i obecny model.
- Regon/Krs/Bdo/Pesel: nvarchar(100) NULL w Contractors, model, DTO, API,
  dane główne Avalonia i audyt. ParentId bez zmian, brak nowych tabel.
- StandardIdentifiers: REGON 9/14 + obie sumy (dla 14 także bazowy 9), PESEL
  11 + suma, KRS 10 i BDO 9 tylko format; jawna normalizacja spacji, kropek,
  typowych myślników. Nieznane znaki i błędne cyfry nie są naprawiane. Same
  separatory są błędem; puste/białe znaki opcjonalnym brakiem.
- Wersja pl-standard-2026-10-23-v1 w Checks/WarningHash. AcceptWarnings nadal
  osobne, z walidacją przy zapisie, rowversion, CSRF i atomowym audytem.
- ManageContractorPesel=512: odczyt i edycja wymagają także podstawowych praw.
  Nie nadano prawa istniejącym użytkownikom. API pomija PESEL bez prawa,
  również konflikt i replay. Jawny tekst PESEL bez prawa =403, null/brak przy
  PUT zachowuje istniejącą wartość. Użytkownik z prawem może ją wyczyścić.
- Nie wypisujemy PESEL w ToString żądań/walidacji/preview ani opisach błędów.
  JSON formularza otrzymuje go tylko uprawniony. Audyt przechowuje wartości
  przed/po w bazie firmy; obecnie brak publicznego endpointu surowego audytu.
  Przyszłe udostępnienie historii musi sprawdzać prawo lub redagować JSON.
- POST contractors/search + CSRF: SQL filtry/strony, wszystkie nowe numery,
  PESEL tylko z prawem; klient i wybór rodzica nie wysyłają tekstu w URL.
  Stary GET zachowano, bez dopasowania PESEL. Nie loguj ciał/parametrów SQL.
- Nowe zmiany danych głównych SnapshotVersion=3; stare i szczegóły =2.
  Zachowano kanoniczny RequestHash dawnych operacji bez nowych pól, aby replay
  działał po addytywnym upgrade. JSON wyników ponownie filtruje PESEL.
- Mała fabryka kontekstu firmy pozwala niedestrukcyjnie testować prawdziwy
  resolver i endpointy w wycofywanych transakcjach z savepoint per żądanie;
  produkcyjne kontrole profilu, migracji i praw pozostały bez zmian.
- Integracje GUS/MF/VIES/DataPort nie zmienione i nie nadpisują nowych pól.
  Istniejąca konfiguracja usług (w tym Production ustawione przez użytkownika)
  zachowana; nie wykonywano zapytań biznesowych do zewnętrznych usług.

## Jawne migracje i zachowanie danych

- 20261023173707_ContractorPeselPermission: System rozszerzenie CHECK do1023,
  bez zmian przypisań. System ma 11 migracji.
- 20261023173705_StandardContractorIdentifiers: osobno A/B, tylko ADD 4 NULL,
  firmy mają po 8 migracji. Brak resetów, aktualizacji historycznych danych,
  wyłączania triggerów i migracji przy uruchomieniu API.
- Zweryfikowano lokalną maszynę/SQL17 SQLEXPRESS Express, Windows Authentication,
  dokładne nazwy trzech baz i znaczniki; wykonano backupy COPY_ONLY/CHECKSUM
  i RESTORE VERIFYONLY przed migracją. Ścieżki: TestResults/standard-identifiers-backup.log.
- System backup: Vatowiec_System_before_standard_identifiers_db65527a00c5472c81160ea67550bb9d.bak
- A backup: Vatowiec_Company_A_Dev_before_standard_identifiers_549340070d414449a04804b0240f0f2d.bak
- B backup: Vatowiec_Company_B_Dev_before_standard_identifiers_8c56b5e41579482f805f10216e8a1fc3.bak
  Wszystkie w domyślnym katalogu SQL Backup C:\Program Files\Microsoft SQL Server\MSSQL17.SQLEXPRESS\MSSQL\Backup.
- 42 istniejące tabele miały zgodne liczności/SHA256 po migracji; po testach/GUI
  41 nadal identycznych, z wyjątkiem zwykłych, potem unieważnionych UserSessions.
  GUS/DataPort, konta, prawa i konfiguracja rejestrów zachowane.
  Kontrahenci A=4, B=1 przed i po; nowe kolumny wcześniejszych rekordów NULL.
- Testowe root transakcje wycofują wszystkie nowe fixture. Liczniki IDENTITY
  mogą mieć luki po rollback, bez wpływu na istniejące rekordy.

## Wyniki

- Cały solution: build **0 błędów, 0 ostrzeżeń**.
- **359/359, 0 pominiętych**: 352 bez SQL i 7 niedestrukcyjnych SQL/API.
  Ostatni przebieg przy zatrzymanym API/desktop.
- Nowe StandardIdentifierTests + StandardIdentifierSqlTests: sumy i długości,
  formaty/znaki/separatory/zera, model i fizyczny schemat SQL, uprawnienia,
  zachowanie/wyczyszczenie PESEL, brak wycieku listą/szczegółem/konfliktem/replay,
  cofnięcie prawa, CSRF, AcceptWarnings i hash, konflikty rowversion, izolacja A/B,
  replay nowych i historycznych operacji. Wymuszona awaria drugiego SaveChanges
  (audyt) wycofała wcześniejszy INSERT kontrahenta — sprawdzono na SQL.
- 103 stare SQL/API z resetującymi fixture **NIE wykonane i NIE zaliczone**.
  Dostosowano ich liczniki migracji i oczekiwania Checks do nowych opcjonalnych pól.
  NIE uruchamiaj DatabaseTools test/prepare-tests/initialize na aktualnych bazach.
- Rzeczywisty GUI HTTPS operator.m0/A: nowe pola REGON/KRS/BDO, normalizacja,
  błędna suma REGON, PESEL niewidoczny bez osobnego prawa; anulowano szkic i
  wylogowano. Bez zapisu kontrahenta ani zapytań do rejestrów. Odczyt/edycja PESEL
  z prawem sprawdzona na SQL/API i modelu widoku, nie przez osobne konto w GUI.
- Formatowano zmienione C#; git diff --check poprawny. Wcześniejszych problemów
  formatowania niezwiązanych plików EWIB nie poprawiano.
- Pierwszy przebieg SQL wykrył losową kolejność Guid w asercji historii i stare
  założenie testu konfiguracji, że użytkownik zawsze ma TEST. Poprawione:
  wybór audytu aktualizacji po BeforeJson; tryb TEST test ustawia tylko wewnątrz
  swojej transakcji i wycofuje. Wszystkie aktualne testy powyżej przechodzą.

## Dokumentacja i pozostałe kwestie

AGENTS, SPEC 0.18, README, ADR-030, docs/standard-identifiers.md,
docs/contractors.md, docs/authentication.md opisują wykonanie, ochronę,
źródła/ograniczenia reguł i samodzielne sprawdzenie. Nie nadpisano historii ADR.
KRS/BDO pozostają kontrolą formatu, REGON/PESEL formatu i sumy; brak potwierdzania
nadania, daty urodzenia i automatycznego importu. Nie dodano szyfrowania PESEL
ani ekranu zarządzania uprawnieniami. Ochrona SQL/backupów jest istotna, gdyż
wartości historyczne są w audycie firmy. Nowe prawo administrator nadaje osobno.

Implementacja ukończona. Pozostaje ocena użytkownika i jego własny Commit/Push.
Dowody w ignorowanym TestResults: standard-identifiers-build.log, -format.log,
-preserved.log/.trx, -ui.log, -backup.log, -company.sql, -system.sql,
-{before,after,final}-data.json. Nie usuwaj danych ani sekretów.

## Zmienione pliki (git status z zakończenia)
 M AGENTS.md
 M README.md
 M SPECYFIKACJA.md
 M docs/CODEX_PROGRESS.md
 M docs/authentication.md
 M docs/contractors.md
 M src/Desktop/MainWindow.axaml
 M src/Desktop/Services/VatowiecApiClient.cs
 M src/Desktop/ViewModels/MainViewModel.Contractors.cs
 M src/Desktop/ViewModels/MainViewModel.Verifications.cs
 M src/Modules/Core/Application/TaxIdentifiers.cs
 M src/Modules/Core/Domain/CompanyAccess.cs
 M src/Modules/Core/Domain/Contractor.cs
 M src/Modules/Core/Domain/ContractorChange.cs
 M src/Modules/Core/Infrastructure/Persistence/CompanyConfigurations/ContractorConfiguration.cs
 M src/Modules/Core/Infrastructure/Persistence/CompanyDatabaseResolver.cs
 M src/Modules/Core/Infrastructure/Persistence/Migrations/Company/CompanyDbContextModelSnapshot.cs
 M src/Modules/Core/Infrastructure/Persistence/Migrations/System/SystemDbContextModelSnapshot.cs
 M src/Modules/Core/Infrastructure/Persistence/SystemConfigurations/CompanyAccessConfiguration.cs
 M src/Server/Api/Security/ApiSecurity.cs
 M src/Server/Api/Security/ContractorEndpoints.cs
 M src/Server/Api/Security/IdentifierChecks.cs
 M src/SharedContracts/CompanyResponses.cs
 M src/SharedContracts/ContractorRequests.cs
 M src/SharedContracts/VerificationContracts.cs
 M tests/Vatowiec.Tests/CompanyModelTests.cs
 M tests/Vatowiec.Tests/DesktopTests.cs
 M tests/Vatowiec.Tests/IdentifierRegistryIntegrationTests.cs
 M tests/Vatowiec.Tests/OfficialVatRulesSqlTests.cs
 M tests/Vatowiec.Tests/PreservedSystemTransaction.cs
 M tests/Vatowiec.Tests/RegistrySettingsSqlTests.cs
 M tests/Vatowiec.Tests/RegistrySettingsTests.cs
 M tests/Vatowiec.Tests/SqlServerPersistenceTests.cs
 M tools/database/Vatowiec.DatabaseTools/LocalDatabases.cs
?? docs/adr/ADR-030-standardowe-identyfikatory-kontrahenta.md
?? docs/standard-identifiers.md
?? src/Modules/Core/Application/StandardIdentifiers.cs
?? src/Modules/Core/Infrastructure/Persistence/Migrations/Company/20261023173705_StandardContractorIdentifiers.Designer.cs
?? src/Modules/Core/Infrastructure/Persistence/Migrations/Company/20261023173705_StandardContractorIdentifiers.cs
?? src/Modules/Core/Infrastructure/Persistence/Migrations/System/20261023173707_ContractorPeselPermission.Designer.cs
?? src/Modules/Core/Infrastructure/Persistence/Migrations/System/20261023173707_ContractorPeselPermission.cs
?? tests/Vatowiec.Tests/StandardIdentifierSqlTests.cs
?? tests/Vatowiec.Tests/StandardIdentifierTests.cs
