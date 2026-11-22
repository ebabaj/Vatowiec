# ADR-044: wznawialne tworzenie firmy oraz backup i odtworzenie jednej firmy

Data: 2026-11-22. **Status: PROPOZYCJA — czeka na akceptację właściciela.**
Do czasu akceptacji nie dodajemy tabel, migracji, endpointów ani procesów
opisanych poniżej.

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

Nazwę bazy wyznacza serwer z identyfikatora firmy
(`Vatowiec_Company_{CompanyId:N}`), nigdy z tekstu użytkownika. Docelowy serwer
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
  `CompanyDatabaseRegistration (Ready)` i zdarzenie audytu. Firma jest widoczna
  dla resolvera dopiero od tego momentu.
- Po awarii każdy etap **sprawdza rzeczywisty stan** (istnienie bazy, znacznik,
  historia migracji, profil), a nie sam checkpoint. Baza o tej nazwie bez
  zgodnego znacznika → `NeedsReconciliation`, bez DROP i bez przejmowania.
- Brak automatycznego `CompanyAccess`: administrator nadaje prawa istniejącym
  ekranem użytkowników (ADR-033). Patrz pytanie otwarte 3.

### 4. Backup jednej firmy

- Zlecenie: `POST /api/v1/companies/{companyId}/operations/backup` z CSRF
  i nowym prawem firmy `BackupCompany = 1024`.
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

- Zlecenie: `POST /api/v1/companies/{companyId}/operations/restore` z CSRF,
  prawem firmy `RestoreCompany = 2048` **i** rolą administratora instalacji
  (patrz pytanie otwarte 2), wskazaniem kopii z listy backupów tej firmy
  i jawnym potwierdzeniem.
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
„Wznów” dla operacji przerwanej. Menu FIRMA → **Kopie zapasowe**: lista kopii,
„Utwórz kopię”, „Odtwórz…” z potwierdzeniem. Klient tylko odpytuje stan przez API.

## Poza zakresem M0

Harmonogramy, retencja, RPO/RTO (ADR-011 pozostaje otwarty), szyfrowanie plików
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

## Wdrożenie i ryzyko

Jedna addytywna migracja System (schemat `ops`, kolumna `Status` z domyślnym
`Ready` dla istniejących rejestracji). Bazy firm bez zmian. Istniejące bazy
developerskie można zmigrować jawnie lub odtworzyć (AGENTS.md). Ryzyko:
dodatkowy proces do instalacji i nadzoru; w M0 akceptujemy ręczne uruchamianie
Workera i opisujemy instalację usługi Windows.

## Pytania otwarte do właściciela

1. **Proces wykonawczy:** osobny Worker (usługa Windows, propozycja) czy
   komendy administracyjne `DatabaseTools` uruchamiane ręcznie?
2. **Uprawnienia odtworzenia:** samo prawo firmy `RestoreCompany` czy
   (propozycja) prawo firmy **i** rola administratora instalacji?
3. **Prawa twórcy firmy:** brak automatycznego `CompanyAccess` (propozycja,
   zgodnie z ADR-033) czy pełne prawa dla administratora tworzącego firmę?
4. **Kolejność wdrożenia:** propozycja — najpierw tworzenie firmy, potem backup,
   na końcu odtworzenie; każdy krok jako osobny commit z testami.
