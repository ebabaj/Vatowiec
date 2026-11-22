# Kopie zapasowe firm (ADR-044 krok 2, ADR-045)

Decyzje: [ADR-044](adr/ADR-044-provisioning-i-backup-firmy.md), [ADR-045](adr/ADR-045-nazwy-baz-i-usuwanie-firmy.md).
Dotyczy VATowiec Network. Odtwarzanie kopii to krok 3 ADR-044 (jeszcze niewdrożony).

## Dla użytkownika

**FIRMA → Kopie zapasowe** pokazuje datę ostatniej udanej kopii, listę kopii na serwerze
i harmonogram firmy.

- **Utwórz kopię na serwerze** — może każdy użytkownik z dostępem do firmy, o ile
  administrator instalacji nie wyłączył tej możliwości. Administrator instalacji może
  zawsze. Kopii nie można pobrać przez aplikację; plik zostaje na serwerze.
- **Harmonogram** (prawo firmy „Harmonogram kopii firmy” `ManageCompanyBackups` albo
  administrator instalacji):
  - częstotliwość: codziennie / co 7 dni / co 30 dni / nigdy,
  - po upływie okresu: **przypomnienie** albo **kopia automatyczna na serwerze**
    (od wybranej godziny, czas serwera).
  - Ustawienie domyślne każdej firmy: co 30 dni, przypomnienie.
- **Przypomnienie** pojawia się po otwarciu firmy, tylko gdy minął okres od ostatniej kopii
  (dla firmy bez kopii — od jej utworzenia), z przyciskami „Utwórz kopię teraz” i
  „Przypomnij później” (najwcześniej następnego dnia, osobno dla każdego użytkownika).
  Program nie pyta o kopię przy zamykaniu.

**Ustawienia instalacji → Firmy → Kopie zapasowe firm** (administrator instalacji):
przełącznik zlecania kopii przez użytkowników i liczba zachowywanych kopii każdej firmy
(1–365, domyślnie 7).

## Jak to działa

1. API zapisuje zlecenie `Backup` w `ops.CompanyOperations` (CSRF, sprawdzenie dostępu do
   firmy i polityki). Dla jednej firmy działa najwyżej jedna kopia naraz.
2. Usługa VATowiec Operations wykonuje etapy `Requested → BackupWritten → BackupVerified → Ready`:
   - sprawdza, że baza jest nadal zarejestrowaną bazą tej firmy z jej własnym profilem,
   - `BACKUP DATABASE … WITH COPY_ONLY, CHECKSUM` do `{BackupRoot}\{baza}\{baza}_{UTC}_{operacja}.bak`,
   - `RESTORE VERIFYONLY … WITH CHECKSUM`,
   - manifest `….manifest.json`: instalacja, CompanyId, nazwa firmy i bazy, operacja, czas,
     zlecający, rozmiar, SHA-256, lista migracji, wersja aplikacji,
   - wpis w `ops.CompanyBackups`.
3. **Rotacja** po udanej kopii: zostaje N najnowszych kopii firmy. Usuwane są wyłącznie pliki
   zarejestrowane w `ops.CompanyBackups`, których manifest potwierdza tę instalację, firmę
   i operację. Obce pliki w katalogu nie są ruszane. Nieudana kopia niczego nie usuwa.
   Wpis usuniętej kopii zostaje (`RemovedAtUtc`, `RemovedReason`).
4. **Harmonogram** sprawdzany co 5 minut (`Operations:ScheduleMinutes`) w usłudze. Terminy
   liczone w dniach kalendarzowych czasu lokalnego serwera. Po nieudanej kopii automatycznej
   kolejna próba najwcześniej po godzinie.
5. Zmiany harmonogramów i polityki trafiają do niezmiennej historii `ops.BackupSettingChanges`.

## Konfiguracja katalogu kopii

`Operations:BackupRoot` w konfiguracji usługi (bezwzględna ścieżka lokalna lub UNC).
Katalog musi pozwalać:

- **kontu usługi SQL Server** (np. `NT Service\MSSQL$SQLEXPRESS`) — na zapis plików `.bak`,
- **kontu usługi VATowiec Operations** — na odczyt (SHA-256), zapis manifestu i usuwanie
  (rotacja).

Środowisko developerskie: `C:\ProgramData\Vatowiec\Backups`
(`src/Server/Operations/appsettings.Development.json`), przygotowany poleceniem:

```powershell
New-Item -ItemType Directory -Force C:\ProgramData\Vatowiec\Backups
icacls C:\ProgramData\Vatowiec\Backups /grant 'NT Service\MSSQL$SQLEXPRESS:(OI)(CI)M'
```

Domyślny katalog kopii instancji SQL (`InstanceDefaultBackupPath`) zwykle nie jest dostępny
dla innych kont, dlatego nie jest używany.

## Granice

- Brak pobierania kopii przez aplikację, szyfrowania plików kopii, kopii poza serwerem,
  harmonogramów z dniami tygodnia (ADR-011).
- Klucz DataPort (Data Protection/DPAPI) nie jest częścią kopii — po przeniesieniu na inny
  serwer trzeba go wpisać ponownie.
- Kopia bazy `Vatowiec_System` pozostaje osobna (`DatabaseTools backup-system`).

## Testy

- Bez SQL: `CompanyBackupTests` (terminy, etapy, walidacja, ścieżki, prawo, model widoku).
- Z SQL (`VATOWIEC_PROVISIONING_SQL=1`, opcjonalnie `VATOWIEC_BACKUP_ROOT`):
  `CompanyBackupSqlTests` — API (uprawnienia, CSRF, przełącznik, harmonogram z audytem,
  jedna aktywna kopia), rzeczywisty `BACKUP` firmy demonstracyjnej A do podkatalogu testu
  z wznowieniem po awarii, manifestem, SHA-256 i rotacją, harmonogram. Zmiany w System są
  wycofywane, podkatalog testu usuwany.

## Wdrożenie migracji

`20261122135208_CompanyBackups` (addytywna, System): tabele `ops.CompanyBackups`,
`ops.BackupPolicy` (wiersz domyślny: użytkownicy mogą zlecać, 7 kopii),
`ops.CompanyBackupSchedules`, `ops.BackupSettingChanges` (trigger niezmienności),
`ops.BackupReminderSnoozes`; rozszerzenie `ops.CompanyOperations` o kopie; limit
`CompanyAccess.Permissions` do 2047 (nowe prawo 1024, istniejące konta go nie otrzymują).

```powershell
dotnet run --project tools/database/Vatowiec.DatabaseTools -c Debug --no-build -- migrate-system
```
