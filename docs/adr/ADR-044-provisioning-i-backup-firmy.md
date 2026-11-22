# ADR-044: wznawialne tworzenie firmy oraz backup i odtworzenie jednej firmy

Data: 2026-11-22. **Status: PRZYJĘTY przez właściciela 2026-11-22.**
Wdrożenie w trzech krokach: (1) tworzenie firmy, (2) kopia z rotacją
i harmonogramem, (3) odtworzenie. Każdy krok ma własną addytywną migrację System
i testy.

## Decyzje właściciela (2026-11-22)

1. **Tworzenie firmy i odtworzenie kopii** wykonuje wyłącznie administrator
   instalacji (rola sprawdzana świeżo na serwerze). Bez osobnego prawa firmy.
2. **Kopię na serwerze** może zlecić każdy użytkownik z dostępem do danej firmy.
   Kopii **nie można pobrać** przez API ani klienta; plik pozostaje na serwerze.
3. Administrator instalacji może **wyłączyć** zlecanie kopii przez użytkowników.
   Sam może zlecić kopię zawsze.
4. **Harmonogram kopii firmy:** program pamięta datę ostatniej udanej kopii każdej
   firmy. Parametr firmy określa częstotliwość — **codziennie / co 7 dni /
   co 30 dni / nigdy** — oraz tryb po upływie okresu: **przypomnienie** użytkownikowi
   albo **kopia automatyczna** na serwerze. „Nigdy” jest pełnoprawnym wyborem
   (są scenariusze użycia bez kopii). Program nie pyta o kopię przy każdym
   zamknięciu; przypomina najwyżej raz po upływie ustawionego okresu.
5. Parametr harmonogramu zmienia administrator instalacji oraz użytkownik
   z nowym prawem firmy `ManageCompanyBackups`.
6. Kopie podlegają **rotacji z automatycznym sprzątaniem**, żeby nie gromadzić
   dziesiątek dużych plików (przykład właściciela: kopia raz na dobę z rotacją 7 dni).
7. Operacje wykonuje **osobna usługa Windows** (Operations Worker, §1).
8. Administrator instalacji, który tworzy firmę, **od razu dostaje pełne prawa**
   do tej firmy (wszystkie uprawnienia `CompanyAccess`). To jawne, audytowane
   nadanie przy tworzeniu, a nie automatyczne prawo wynikające z roli: rola
   administratora instalacji nadal nie daje dostępu do danych innych firm (ADR-033).

Wymagania: ORG-010 (backup, odtworzenie, przeniesienie jednej firmy),
SPECYFIKACJA §3.4 (cykl życia bazy), §11.3 (backup i odtwarzanie), §13 M0
(„wznawialny provisioning i niezależny backup/restore”, warunek „odtworzenie
kopii jest udokumentowane”), T-018. Rozwija ADR-014 (sekcje „Automatyczne
tworzenie firmy” i „Backup i samodzielne odtworzenie”). Zachowuje ADR-015
(resolver), ADR-033/034 (administrator instalacji bez automatycznych praw do
danych firm) i zasady AGENTS.md §3 pkt 10–12. Nie zmienia edycji Personal
(ADR-037–041), która ma własny bootstrap.

## Kontekst

Dziś bazy firm Network powstają wyłącznie przez `DatabaseTools initialize`
dla trzech stałych baz demonstracyjnych. Nie ma operacji „Utwórz firmę”,
nie ma backupu ani odtworzenia jednej firmy z aplikacji. `DatabaseTools`
potrafi wykonać `BACKUP … WITH COPY_ONLY, CHECKSUM` i `RESTORE VERIFYONLY`
dla zweryfikowanych baz demonstracyjnych, ale tylko jako narzędzie
developerskie. To ostatnie brakujące funkcje M0 obok CI.

## Proponowana decyzja

### 1. Osobny proces techniczny: Vatowiec Operations Worker

Nowy projekt `src/Server/Operations` (Worker Service .NET, ten sam modularny
monolit, bez nowego frameworka). Działa jako osobny proces/usługa Windows
z **własną tożsamością SQL**, która ma prawa `dbcreator` i
`db_backupoperator`/DDL potrzebne do tworzenia, migracji, backupu i odtworzenia.
Konto zwykłego API nie dostaje CREATE DATABASE, DDL ani BACKUP.

API jedynie **zapisuje zlecenie** w bazie System (z CSRF, kontrolą uprawnień
i audytem) i udostępnia jego stan. Worker pobiera zlecenia z dzierżawą (lease)
i wykonuje je. Jest to jedyny nowy proces; uzasadnieniem jest wymóg oddzielnej
tożsamości z SPECYFIKACJA §3.4 i §11.3, a nie skalowanie.

W środowisku developerskim API i Worker mogą działać na tym samym koncie
Windows. Rozdzielenie uprawnień SQL dokumentuje skrypt wdrożeniowy ról.
Testy architektury pilnują, że projekt API nie zawiera kodu wykonującego DDL.

### 2. Model w System (jedna jawna migracja System, bez zmian baz firm)

Nowy schemat `ops`:

- `ops.CompanyOperations`: `Id` (GUID operacji), `Kind`
  (`Provision`/`Backup`/`Restore`), `CompanyId`, `RequestedBy` (UserId)
  i `ActorIssuerId`, klucz idempotencji `(RequestedBy, OperationKey)` unikalny,
  `RequestHash`, niezmienny plan docelowy (serwer, profil połączenia, nazwa bazy,
  ścieżka pliku), `Stage`, `Status` (`Running`/`Failed`/`NeedsReconciliation`/
  `Completed`), `Attempt`, `LastErrorCode` (bez treści SQL i sekretów),
  `LeaseOwner`, `LeaseExpiresAtUtc`, daty UTC i `RowVersion`.
- `ops.CompanyOperationEvents`: niezmienna historia przejść etapów
  (trigger blokujący UPDATE/DELETE, jak w istniejących tabelach audytu).
- `CompanyDatabaseRegistration.Status`: `Ready` / `Maintenance`.
  Resolver odmawia (`503 company_maintenance`) dla `Maintenance`.

Nazwę bazy wyznacza serwer — **zmienione przez [ADR-045](ADR-045-nazwy-baz-i-usuwanie-firmy.md):**
stały prefiks `Vatowiec_` i oczyszczona nazwa firmy (pierwotnie `Vatowiec_Company_{CompanyId:N}`);
zajęta nazwa oznacza komunikat „Nazwa zajęta…”. Docelowy serwer
wybierany jest wyłącznie z serwerowej listy profili `CompanyConnections:*`.

### 3. Tworzenie firmy

Etapy zgodne ze SPECYFIKACJA §3.4:
`Requested → DatabaseCreated → SchemaApplied → ProfileInitialized → Ready`.

- Zleca administrator instalacji (świeże sprawdzenie roli na serwerze) przez
  `POST /api/v1/installation/company-operations/provision` z kluczem operacji,
  nazwą firmy i profilem serwera. Zgodne ponowienie zwraca tę samą operację,
  inna treść dla tego samego klucza → `409 operation_payload_mismatch`.
- `DatabaseCreated`: `CREATE DATABASE` i natychmiast znacznik własności
  (rozszerzone właściwości bazy: `Vatowiec.CompanyId`, `Vatowiec.OperationId`),
  ten sam wzorzec co znaczniki w `DatabaseTools` i Personal.
- `SchemaApplied`: migracje `CompanyDbContext` wyłącznie dla tej bazy.
- `ProfileInitialized`: `CompanyProfile` i dane początkowe Network
  (słownik walut jak dotychczasowa inicjalizacja firm demonstracyjnych).
- `Ready`: w jednej transakcji System wpis `Company`,
  `CompanyDatabaseRegistration (Ready)`, `CompanyAccess` z pełnymi prawami dla
  administratora, który zlecił utworzenie (decyzja właściciela), domyślny
  harmonogram kopii firmy i zdarzenia audytu (operacja oraz
  `UserAdministrationChanges` dla nadania praw). Firma jest widoczna dla resolvera
  dopiero od tego momentu.
- Po awarii każdy etap **sprawdza rzeczywisty stan** (istnienie bazy, znacznik,
  historia migracji, profil), a nie sam checkpoint. Baza o tej nazwie bez
  zgodnego znacznika → `NeedsReconciliation`, bez DROP i bez przejmowania.
- Innym użytkownikom prawa do nowej firmy nadaje się istniejącym ekranem
  użytkowników (ADR-033).

### 4. Backup jednej firmy

- Zlecenie: `POST /api/v1/companies/{companyId}/operations/backup` z CSRF.
  Wystarcza dowolny `CompanyAccess` do tej firmy, o ile polityka kopii instalacji
  na to pozwala; administrator instalacji może zlecić kopię zawsze. Nie ma nowego
  prawa firmy. Dla jednej firmy działa najwyżej jedna kopia naraz: kolejne
  zlecenie w trakcie zwraca istniejącą operację.
- **Brak pobierania:** API udostępnia tylko listę kopii (data, rozmiar, wynik
  weryfikacji, kto zlecił), nigdy ścieżki pliku ani jego treści.
- Polityka kopii instalacji (jeden wiersz w System, `RowVersion`, niezmienny
  audyt zmian, edycja tylko przez administratora instalacji):
  `UsersMayRequestBackups` (domyślnie włączone) i `RetainedBackupsPerCompany`
  (domyślnie 7, zakres 1–365).
- **Rotacja:** po udanej weryfikacji nowej kopii Worker usuwa najstarsze kopie
  tej firmy ponad limit. Usuwa wyłącznie pliki utworzone przez tę instalację,
  rozpoznane po manifeście z `InstallationId` i `CompanyId`, w katalogu
  `Operations:BackupRoot`. Obcych plików nie dotyka. Usunięcie jest zapisane
  w audycie. Nieudana kopia nigdy nie powoduje usunięcia starszych.
- Kopie bezpieczeństwa wykonywane automatycznie przed odtworzeniem (§5) mają
  osobny limit: zachowujemy dwie ostatnie.
- **Harmonogram firmy** (osobny wiersz na firmę w System, `RowVersion`, audyt
  zmian): `Frequency` = `Never` / `Daily` / `Every7Days` / `Every30Days`,
  `Mode` = `Reminder` / `Automatic`, `PreferredHour` (0–23) dla kopii
  automatycznej. Ustawienia domyślne nowej firmy (decyzja właściciela):
  **co 30 dni, przypomnienie**. Zmiana: administrator instalacji albo prawo firmy
  `ManageCompanyBackups = 1024` (nowe; istniejące konta go nie otrzymują
  automatycznie), z CSRF i kontrolą wersji.
- Datą odniesienia jest ostatnia **udana i zweryfikowana** kopia firmy, niezależnie
  od tego, kto ją zlecił.
- Tryb `Automatic`: Worker co kilka minut sprawdza firmy, dla których okres minął,
  i od `PreferredHour` wykonuje kopię jako operację z aktorem technicznym
  (Worker) w audycie. Działa niezależnie od przełącznika zlecania kopii przez
  użytkowników. Nieudana kopia jest ponawiana najwyżej raz na godzinę i widoczna
  na liście kopii.
- Tryb `Reminder`: po otwarciu firmy klient pyta API o stan; jeżeli okres minął,
  a użytkownik może zlecić kopię, widzi jedno nieblokujące powiadomienie
  z „Utwórz kopię teraz” i „Przypomnij później” (ponownie najwcześniej
  następnego dnia). Bez pytań przy zamykaniu programu.
- `Never`: brak przypomnień i kopii automatycznych; kopię nadal można zlecić ręcznie.
- Worker: pełny `BACKUP DATABASE … WITH CHECKSUM, COPY_ONLY` do serwerowego
  katalogu `Operations:BackupRoot`, następnie `RESTORE VERIFYONLY … WITH CHECKSUM`.
- Obok pliku `.bak` manifest JSON: CompanyId, nazwa bazy, lista migracji,
  wersja aplikacji, SHA-256 pliku, czas UTC, aktor. Załączników jeszcze nie ma
  (manifest zapisuje to jawnie). Klucz DataPort (Data Protection/DPAPI poza bazą)
  nie jest eksportowany: odtworzenie na tym samym serwerze go zachowuje,
  przeniesienie na inny serwer wymaga ponownego wpisania klucza.
- Kopia bazy System pozostaje osobna (`DatabaseTools backup-system` i procedura
  w dokumentacji); backup firmy nigdy nie zawiera kont ani uprawnień.

### 5. Odtworzenie jednej firmy

- Zlecenie: `POST /api/v1/installation/company-operations/restore` z CSRF,
  wyłącznie przez administratora instalacji (decyzja właściciela), ze wskazaniem
  firmy i kopii z jej listy oraz jawnym potwierdzeniem. Rola administratora nie
  daje przy tym dostępu do odczytu danych firmy.
- Worker odtwarza do **nowej bazy roboczej** z unikalną nazwą, sprawdza
  `DBCC CHECKDB`, SHA-256 z manifestu, `CompanyProfile.CompanyId` oraz zgodność
  historii migracji z bieżącą wersją aplikacji. Starsza historia → odmowa
  (bez automatycznej migracji w M0).
- Przełączenie: `Registration.Status = Maintenance` (zapisy tej firmy dostają
  503, inne firmy działają), kopia bezpieczeństwa dotychczasowej bazy,
  zmiana `DatabaseName` na bazę odtworzoną, `Status = Ready`. Poprzednia baza
  **zostaje zachowana** (bez automatycznego DROP). Ponieważ resolver tworzy
  kontekst dla każdego żądania, kolejne żądania używają nowej bazy.
- Identyfikacja aktorów w historii firmy (ActorId, ActorIssuerId) pochodzi z bazy
  firmy, więc po odtworzeniu pozostaje kompletna (T-018).

### 6. Avalonia

Ustawienia instalacji → **Firmy**: lista operacji, „Utwórz firmę”, postęp etapów,
„Wznów” dla operacji przerwanej, „Odtwórz kopię…” z potwierdzeniem.
Ustawienia instalacji → **Kopie zapasowe**: przełącznik zlecania kopii przez
użytkowników i liczba zachowywanych kopii na firmę. Menu FIRMA → **Kopie zapasowe**:
lista kopii, data ostatniej udanej kopii, „Utwórz kopię na serwerze” (ukryte, gdy
polityka nie pozwala; serwer i tak sprawdza politykę) oraz harmonogram firmy
(edytowalny z `ManageCompanyBackups` lub przez administratora instalacji).
Klient tylko odpytuje stan przez API. Przypomnienie pojawia się wyłącznie w trybie
`Reminder` po upływie okresu, nigdy przy zamykaniu programu.

## Poza zakresem M0

Dokładniejsze harmonogramy (dni tygodnia, kilka kopii dziennie), kopie poza
serwerem, RPO/RTO (ADR-011 pozostaje otwarty w tym zakresie), szyfrowanie plików
kopii (do sprawdzenia możliwości SQL Server Express; w M0 ochrona ACL katalogu),
przeniesienie firmy na inną instancję, klon jako nowa firma, eksport kluczy,
załączniki (brak modułu), automatyczna migracja starszych kopii.

## Kryteria akceptacji

1. Administrator instalacji tworzy firmę z Avalonia bez SSMS; po `Ready` firma
   działa w resolverze, a przed `Ready` jest niewidoczna.
2. Ponowienie z tym samym kluczem i treścią nie tworzy drugiej firmy ani bazy;
   inna treść → 409.
3. Przerwanie po każdym etapie (test z wstrzykniętą awarią na każdej granicy)
   wznawia tę samą operację i bazę.
4. Zastana baza o docelowej nazwie bez zgodnego znacznika → `NeedsReconciliation`,
   baza nienaruszona.
5. Dwa Workery nie wykonują jednocześnie tej samej operacji (lease + rowversion).
6. Konto API nie ma uprawnień DDL/BACKUP (skrypt ról + test architektury);
   API nie wykonuje migracji przy starcie.
7. Backup ma CHECKSUM, przechodzi VERIFYONLY i ma manifest z SHA-256.
8. Odtworzenie do nowej bazy odrzuca niezgodny CompanyId, uszkodzony plik
   i niezgodną historię migracji; po przełączeniu dane pochodzą z kopii,
   stara baza istnieje, druga firma pracowała bez przerwy.
9. Historia kontrahentów po odtworzeniu identyfikuje aktorów (T-018).
10. Każda operacja i jej etapy są w niezmiennym audycie System; brak sekretów
    i connection stringów w logach i błędach.
11. `docs/backup-restore.md` opisuje backup i odtworzenie firmy oraz bazy System
    (warunek M0 „odtworzenie kopii jest udokumentowane”).
12. Użytkownik z dostępem do firmy zleca kopię; po wyłączeniu polityki dostaje
    odmowę z serwera, a administrator instalacji nadal może zlecić kopię.
    Tworzenie firmy i odtworzenie bez roli administratora → 403.
13. Żaden endpoint nie zwraca treści ani ścieżki pliku kopii.
14. Po przekroczeniu limitu zostaje dokładnie N najnowszych zweryfikowanych kopii
    firmy; nieudana kopia nie usuwa starszych; obce pliki w katalogu pozostają.
15. Harmonogram (testy z kontrolowanym zegarem): `Automatic` wykonuje kopię
    po upływie okresu od `PreferredHour` i nie wcześniej; `Reminder` zgłasza
    potrzebę kopii tylko po upływie okresu i nie częściej niż raz dziennie
    po „Przypomnij później”; `Never` nie zgłasza niczego. Zmiana harmonogramu
    bez `ManageCompanyBackups` i bez roli administratora → 403.
16. Administrator tworzący firmę ma po `Ready` pełne prawa do niej, zapisane
    w audycie; nie zyskuje praw do innych firm.

## Wdrożenie i ryzyko

Addytywne migracje System, po jednej na krok wdrożenia (schemat `ops`
z operacjami, historią, polityką kopii instalacji i harmonogramami firm, kolumna `Status` z domyślnym `Ready`
dla istniejących rejestracji, nowe prawo `ManageCompanyBackups` w katalogu uprawnień). Bazy firm bez zmian. Istniejące bazy
developerskie można zmigrować jawnie lub odtworzyć (AGENTS.md). Ryzyko:
dodatkowy proces do instalacji i nadzoru; w M0 akceptujemy ręczne uruchamianie
Workera i opisujemy instalację usługi Windows.

## Stan wdrożenia

**Krok 1 — tworzenie firmy: wdrożony 2026-11-22.** Migracja System
`20261122123552_CompanyProvisioning`, usługa `src/Server/Operations`, endpointy
`/api/v1/installation/company-operations`, ekran Avalonia „Ustawienia instalacji → Firmy”.
Spełnione kryteria 1–6, 10 i 16 (testy `CompanyProvisioningTests` i
`CompanyProvisioningSqlTests`). Rozstrzygnięcie techniczne: wykonawca nie zapisuje
osobnego zdarzenia `StageReached` dla `Ready` — `Ready` i `Completed` powstają w tej samej
transakcji co katalog. Ograniczenie: nadanie dostępu osobnemu loginowi SQL API do nowej bazy
jest na razie ręczne (docs/company-provisioning.md). Kryterium 6 w części „skrypt ról”
pozostaje do kroku z osobnym loginem SQL. Instrukcja: [docs/company-provisioning.md](../company-provisioning.md).

**Krok 2 — kopia z rotacją i harmonogramem: wdrożony 2026-11-22.** Migracja System
`20261122135208_CompanyBackups`, kopie w usłudze Operations (BACKUP COPY_ONLY/CHECKSUM,
VERIFYONLY, manifest z SHA-256, rotacja tylko plików potwierdzonych manifestem), harmonogram
automatyczny, przypomnienie w Avalonia, prawo `ManageCompanyBackups = 1024`, polityka
instalacji. Spełnione kryteria 7, 12–15 (testy `CompanyBackupTests`, `CompanyBackupSqlTests`).
Rozstrzygnięcia techniczne: brak wiersza harmonogramu = ustawienia domyślne; pliki w
`{BackupRoot}\{baza}\`; wykonawca obsługuje oba rodzaje operacji jedną kolejką
(`CompanyOperationExecutor`). Instrukcja: [docs/company-backups.md](../company-backups.md).

Krok 3 (odtworzenie) — do wykonania; przed nim usuwanie firmy wg ADR-045.

## Pytania do właściciela

Rozstrzygnięte 2026-11-22 (patrz „Decyzje właściciela”): wykonawca — usługa
Windows; odtworzenie i tworzenie firmy — administrator instalacji; twórca firmy —
pełne prawa do niej; harmonogram — codziennie / 7 / 30 dni / nigdy, z wyborem
przypomnienia lub kopii automatycznej, ustawiany przez administratora instalacji
lub prawo `ManageCompanyBackups`.

Przy akceptacji 2026-11-22 właściciel potwierdził kolejność wdrożenia (tworzenie
firmy → kopia z rotacją i harmonogramem → odtworzenie, każdy krok osobno
z testami) i ustawił domyślny harmonogram nowej firmy: co 30 dni, przypomnienie.
