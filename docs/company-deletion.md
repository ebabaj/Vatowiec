# Usuwanie firmy (ADR-045)

Decyzja: [ADR-045](adr/ADR-045-nazwy-baz-i-usuwanie-firmy.md). Dotyczy VATowiec Network.
Przywrócenie usuniętej firmy z kopii archiwalnej to ADR-044 krok 3 (jeszcze niewdrożony).

## Dla administratora instalacji

**Ustawienia instalacji → Firmy → Usuwanie firmy**:

1. Wybierz firmę. Pole „Przed usunięciem utwórz kopię archiwalną” jest domyślnie zaznaczone.
2. **Usuń firmę…** → jedno pytanie „Czy na pewno chcesz usunąć firmę …?”.
3. Bez kopii archiwalnej pojawia się dodatkowe ostrzeżenie o nieodwracalności
   („Usuń bez kopii archiwalnej”).
4. Od chwili przyjęcia zlecenia firma jest niedostępna dla wszystkich użytkowników.
   Usuwanie wykonuje usługa VATowiec Operations; postęp widać na liście zleceń.
5. **Anuluj usuwanie** — możliwe, dopóki baza nie została usunięta, a zlecenie nie jest
   właśnie wykonywane (oczekuje, błąd, wymaga sprawdzenia). Anulowanie przywraca dostęp do firmy.
6. Lista **Usunięte firmy** pokazuje rejestr usuniętych (nazwa, NIP, data, kto usunął,
   czy jest kopia archiwalna).

## Etapy w usłudze Operations

`Requested → CompanyDeactivated → ArchiveWritten → ArchiveVerified → DatabaseDropped → czyszczenie`

- **CompanyDeactivated** — `Company.IsActive = 0` już przy przyjęciu zlecenia przez API
  (usługa to potwierdza): resolver, lista firm i kopie odmawiają dostępu.
- **Archiwum** (jeśli wybrane) — `BACKUP COPY_ONLY, CHECKSUM` + `VERIFYONLY` + manifest
  (`kind: Archive`) do `{BackupRoot}\_archive\{baza}\…`. Archiwum nie podlega rotacji
  i nie jest pobierane przez aplikację. Baza jest usuwana dopiero po zweryfikowanej kopii.
- **DatabaseDropped** — tylko gdy rejestracja i profil bazy potwierdzają tę firmę:
  `SINGLE_USER WITH ROLLBACK IMMEDIATE` i `DROP DATABASE`.
- Pliki zwykłych kopii firmy są usuwane (wyłącznie potwierdzone manifestem).
- **Czyszczenie** — jedna transakcja System usuwa: harmonogram, odłożone przypomnienia,
  historię ustawień kopii, wpisy historii administracji użytkownikami dotyczące firmy,
  `CompanyAccess`, rejestr kopii, historię i zlecenia operacji (również bieżące),
  rejestrację bazy i firmę. Dodaje wpis do `ops.DeletedCompanies`.
- **Archiwum historii** (tylko przy usuwaniu z kopią archiwalną) — w tej samej transakcji,
  przed usunięciem wierszy, usuwana historia z bazy System trafia do pliku
  `….history.json` obok kopii archiwalnej (`format: vatowiec-company-history-v1`):
  zlecenia i zdarzenia operacji, wpisy historii administracji użytkownikami dotyczące
  firmy, prawa użytkowników, rejestr kopii, harmonogram i historia ustawień kopii.

Usunięte zostają też dane klienta przechowywane w samej bazie firmy (kontrahenci, kursy,
historia zmian) — razem z bazą. Przy usuwaniu z archiwum zawiera je kopia `.bak`.

## Rejestr usuniętych firm

`ops.DeletedCompanies`: `CompanyId`, nazwa firmy, NIP i REGON z profilu, data, kto usunął,
plik, rozmiar i SHA-256 kopii archiwalnej. `CompanyId` nie zostanie przydzielony nowej
firmie. Nazwa firmy i nazwa bazy są ponownie dostępne.

## Kontrolowany wyjątek od niezmienności historii

Triggery `audit.TR_UserAdministrationChanges_Immutable`, `ops.TR_BackupSettingChanges_Immutable`
i `ops.TR_CompanyOperationEvents_Immutable` nadal zawsze odrzucają UPDATE. DELETE dopuszczają
wyłącznie, gdy `SESSION_CONTEXT('Vatowiec.PurgeCompanyId')` wskazuje firmę, do której należą
**wszystkie** usuwane wiersze. Kontekst ustawia tylko usługa Operations w transakcji czyszczenia
i zeruje go po niej. Ochrona przed innymi kontami SQL wymaga, by konto API nie miało prawa
DELETE na tych tabelach (rozdzielenie tożsamości opisane w [company-provisioning.md](company-provisioning.md)).

## Wznawianie po awarii

Każde wykonanie sprawdza rzeczywisty stan. Awaria po usunięciu bazy, a przed czyszczeniem
— ponowienie dokończy czyszczenie (NIP/REGON mogą wtedy nie trafić do rejestru, bo profilu
nie da się już odczytać). Brak bazy przed zweryfikowanym archiwum → wymaga sprawdzenia.

## Testy

- Bez SQL: `CompanyDeletionTests` (etapy, anulowanie, potwierdzenia w modelu widoku).
- Z SQL (`VATOWIEC_PROVISIONING_SQL=1`): `CompanyDeletionSqlTests` — pełny cykl: utworzenie
  firmy, kopia, harmonogram, przypomnienie i prawa, usunięcie z archiwum (baza usunięta,
  archiwum z manifestem i plikiem historii, kopie usunięte, wszystkie dane System wyczyszczone,
  wpis w rejestrze, nazwa ponownie dostępna, ochrona historii innych firm); anulowanie
  i usunięcie bez archiwum (bez pliku historii); anulowanie po awarii przed usunięciem bazy.

## Wdrożenie migracji

`20261122141147_CompanyDeletion` (System): kolumna `CreateArchive`, nowe wartości w CHECK
zleceń (rodzaj `Delete`, etapy, status `Cancelled`), indeks jednego aktywnego usuwania firmy,
tabela `ops.DeletedCompanies`, zmienione triggery trzech historii.

```powershell
dotnet run --project tools/database/Vatowiec.DatabaseTools -c Debug --no-build -- migrate-system
```
