# Odtwarzanie firmy z kopii (ADR-044 krok 3)

Decyzje: [ADR-044](adr/ADR-044-provisioning-i-backup-firmy.md) (decyzje właściciela do kroku 3),
[ADR-045](adr/ADR-045-nazwy-baz-i-usuwanie-firmy.md). Dotyczy VATowiec Network.

## Dla administratora instalacji

**Ustawienia instalacji → Firmy → Odtwarzanie z kopii** pokazuje wszystkie możliwe źródła:

| Źródło | Skąd | Wynik |
|---|---|---|
| Kopia firmy | rejestr kopii (zwykłe i „stan sprzed odtworzenia”) | zastąpienie danych istniejącej firmy |
| Archiwum usuniętej firmy | rejestr usuniętych firm | przywrócenie firmy pod tym samym identyfikatorem |
| Plik z katalogu importu | `{BackupRoot}\_import\*.bak` z manifestem VATowca | identyfikator firmy z manifestu: istniejąca → zastąpienie, usunięta → przywrócenie, nieznana → przeniesienie jako nowa firma (z zachowanym identyfikatorem) |

1. Wybierz kopię → **Odtwórz…** → potwierdź.
2. Zastąpienie danych istniejącej firmy: na czas przełączenia firma jest niedostępna.
   Dotychczasowa baza **zostaje** pod nazwą `{baza}_przed_{RRRRMMDD_GGMM}` (do ręcznego
   usunięcia; usuwa ją też usunięcie firmy) i dodatkowo powstaje kopia bezpieczeństwa
   („stan sprzed odtworzenia”, zachowywane 2 ostatnie).
3. Przywrócenie usuniętej lub przeniesionej firmy: przywracający dostaje pełne prawa,
   innym użytkownikom nadaje się je ponownie. Gdy nazwa bazy jest zajęta — „Nazwa zajęta…”.
   Historia z pliku `.history.json` usuniętej firmy nie jest wczytywana z powrotem.
4. Postęp i błędy — na liście zleceń.

Przeniesienie z innej instalacji: skopiuj plik `.bak` **razem z** `.manifest.json` do
podkatalogu `_import` katalogu kopii serwera. Usługa odczyta manifest w ciągu kilku minut
(razem z harmonogramem kopii) i plik pojawi się na liście.

## Etapy w usłudze Operations

`Requested → DatabaseRestored → RestoreVerified → SchemaUpgraded → Switched → Ready`

1. **Źródło** — plik i manifest istnieją, manifest wskazuje tę firmę, SHA-256 pliku zgadza się
   z manifestem i (dla kopii z rejestru) z rejestrem, `RESTORE VERIFYONLY … WITH CHECKSUM`.
2. **DatabaseRestored** — `RESTORE DATABASE` do bazy roboczej `{baza}_odtwarzanie_{operacja}`
   (`WITH MOVE` do domyślnych katalogów danych instancji, `CHECKSUM`) i znacznik
   `Vatowiec.RestoreOperationId`.
3. **RestoreVerified** — `DBCC CHECKDB`, profil firmy = identyfikator z manifestu, historia
   migracji jest początkiem historii programu. Kopia z nowszej wersji programu albo o obcej
   historii → odmowa (`restore_schema_unknown`).
4. **SchemaUpgraded** — brakujące migracje w bazie roboczej (kopia ze starszej wersji programu).
5. **Switched** — istniejąca firma: dezaktywacja, kopia bezpieczeństwa, zmiana nazwy
   dotychczasowej bazy na `…_przed_…`, zmiana nazwy bazy roboczej na właściwą. Usunięta /
   przeniesiona: zmiana nazwy bazy roboczej na nazwę z nazwy firmy.
6. **Ready** — w jednej transakcji System: aktywacja firmy (istniejąca) albo katalog,
   rejestracja bazy, pełne prawa przywracającego z wpisem w historii administracji
   i usunięcie wpisu z rejestru usuniętych.

Każde wykonanie sprawdza rzeczywisty stan baz (nazwy, znaczniki, profil). Niezgodność →
„wymaga sprawdzenia” bez usuwania czegokolwiek. Uszkodzony plik jest odrzucany, zanim
firma zostanie zablokowana.

## Odtworzenie bazy System (procedura ręczna)

Baza `Vatowiec_System` (użytkownicy, prawa, katalog firm, kolejka operacji) nie jest
odtwarzana z aplikacji. Kopia: `DatabaseTools backup-system` (COPY_ONLY, CHECKSUM,
VERIFYONLY). Odtworzenie po awarii:

1. Zatrzymaj API i usługę VATowiec Operations.
2. W SQL Server: `RESTORE DATABASE [Vatowiec_System] FROM DISK = N'…\Vatowiec_System_….bak' WITH CHECKSUM, REPLACE, RECOVERY;`
3. `dotnet run --project tools/database/Vatowiec.DatabaseTools -c Debug --no-build -- verify`
   (sprawdza schemat, migracje i katalog).
4. Gdy kopia System jest starsza niż program: `… -- migrate-system`.
5. Uruchom API i usługę. Bazy firm odtwarza się osobno, opisanym wyżej sposobem;
   odtworzenie System nie odtwarza danych firm, a odtworzenie firmy — kont i praw.
6. Klucze Data Protection (GUS, DataPort) pochodzą z konfiguracji serwera, nie z kopii SQL —
   po przeniesieniu na inny serwer wpisz je ponownie.

## Testy

- Bez SQL: `CompanyRestoreTests` (plan operacji, nazwy baz roboczej i poprzedniej, potwierdzenia).
- Z SQL (`VATOWIEC_PROVISIONING_SQL=1`): `CompanyRestoreSqlTests` — zastąpienie danych
  istniejącej firmy (stan z kopii, dotychczasowa baza `…_przed_…`, kopia bezpieczeństwa,
  działający resolver), usunięcie z archiwum i przywrócenie pod tym samym CompanyId,
  przeniesienie z „innej instalacji” przez `_import` z automatyczną migracją starszego
  schematu, odrzucenie uszkodzonego pliku przed zmianą firmy.

## Wdrożenie migracji

`…_CompanyRestore` (System): kolumny planu odtworzenia w `ops.CompanyOperations`, rodzaj
`Restore` i jego etapy w CHECK, rodzaj kopii `PreRestore`, indeks jednego niezakończonego
odtworzenia firmy, tabela `ops.RestoreImportFiles`.

```powershell
dotnet run --project tools/database/Vatowiec.DatabaseTools -c Debug --no-build -- migrate-system
```
