# Tworzenie firmy (ADR-044, krok 1)

Decyzja: [ADR-044](adr/ADR-044-provisioning-i-backup-firmy.md). Dotyczy VATowiec Network.

## Jak to działa

1. Administrator instalacji otwiera **Ustawienia instalacji → Firmy → Tworzenie nowej
   firmy** i wpisuje tylko nazwę firmy; pod polem widzi podgląd nazwy bazy. Serwer baz
   wybiera wyłącznie wtedy, gdy API ma więcej niż jeden profil. Gdy nazwa bazy jest zajęta
   (katalog, niezakończone zlecenie, istniejąca baza na serwerze lub nazwa zastrzeżona),
   API odpowiada „Nazwa zajęta…” i nie przyjmuje zlecenia.
2. API zapisuje zlecenie w `ops.CompanyOperations` bazy `Vatowiec_System` (CSRF,
   świeże sprawdzenie roli, klucz idempotencji, niezmienna historia
   `ops.CompanyOperationEvents`). **API nie tworzy bazy ani nie wykonuje migracji.**
3. Usługa **VATowiec Operations** (`src/Server/Operations`) przejmuje zlecenie z dzierżawą
   (lease 5 min, `RowVersion`) i wykonuje etapy:
   `Requested → DatabaseCreated → SchemaApplied → ProfileInitialized → Ready`.
   - `DatabaseCreated`: `CREATE DATABASE` o nazwie wyznaczonej z nazwy firmy
     ([ADR-045](adr/ADR-045-nazwy-baz-i-usuwanie-firmy.md): „Tenjin” → `Vatowiec_Tenjin`,
     „DOMAGAŁA sp. z o.o.” → `Vatowiec_DOMAGALA_sp_z_o_o`) i znaczniki
     (rozszerzone właściwości) `Vatowiec.CompanyId`, `Vatowiec.OperationId`,
     `Vatowiec.InstallationId`.
   - `SchemaApplied`: migracje `CompanyDbContext` tylko tej bazy (w tym słowniki).
   - `ProfileInitialized`: `CompanyProfile` z `CompanyId` i nazwą.
   - `Ready`: w jednej transakcji System — `Company`, `CompanyDatabases`, pełne
     `CompanyAccess` twórcy, wpis `CompanyCreatorAccessGranted` w historii administracji
     użytkownikami i zakończenie operacji. Dopiero wtedy firma jest widoczna dla resolvera
     i na liście firm.
4. Każde wykonanie sprawdza rzeczywisty stan (istnienie bazy, znaczniki, historia migracji,
   profil). Baza o docelowej nazwie bez zgodnych znaczników, zniknięcie bazy, obcy profil
   albo konflikt w katalogu → **NeedsReconciliation**: bez automatycznych prób, bez DROP
   i bez przejmowania. Błędy przejściowe (np. chwilowa niedostępność SQL) są ponawiane
   automatycznie (do 5 prób, przerwa rosnąca do 15 min), potem stan **Failed**.
   „Ponów wybraną operację” w Avalonia ponownie weryfikuje stan od początku.

## Uruchomienie w środowisku developerskim

API i usługa muszą działać jednocześnie:

```powershell
dotnet run --project src/Server/Api -c Debug --no-build --launch-profile https
dotnet run --project src/Server/Operations -c Debug --no-build --launch-profile Development
```

`src/Server/Operations/appsettings.Development.json` wskazuje lokalne `Vatowiec_System`
i profil `OperationsConnections:LocalWindows` (Windows Authentication). Nazwy profili
muszą być takie same jak `CompanyConnections:*` w API, a serwer (`Data Source`) zgodny —
inaczej operacja kończy się błędem `server_profile_*`.

## Instalacja jako usługa Windows

```powershell
dotnet publish src/Server/Operations -c Debug -o <katalog-usługi>
sc.exe create "VATowiec Operations" binPath= "<katalog-usługi>\Vatowiec.Operations.exe" start= auto obj= "<DOMENA\konto-usługi>" password= "<hasło>"
sc.exe start "VATowiec Operations"
```

Konfigurację (`ConnectionStrings:VatowiecSystem`, `OperationsConnections:*`,
`Operations:BackupRoot` — patrz [kopie zapasowe](company-backups.md)) podaj w
`appsettings.json` katalogu usługi albo w zmiennych środowiskowych usługi. Nie zapisuj
haseł SQL w repozytorium.

## Rozdzielenie tożsamości SQL

| Proces | Wymagane uprawnienia |
|---|---|
| API (`CompanyConnections:*`) | odczyt/zapis danych w `Vatowiec_System` i bazach firm, odczyt `sys.databases` do kontroli zajętości nazwy (domyślne `VIEW ANY DATABASE`); **bez** `CREATE DATABASE`, DDL i `BACKUP` |
| VATowiec Operations (`OperationsConnections:*`) | `dbcreator` na serwerze firm, zapis w `Vatowiec_System` (`ops`, katalog, `CompanyAccess`, audyt) |

W środowisku developerskim oba procesy działają na tym samym koncie Windows, więc
rozdzielenie nie jest egzekwowane. Test architektury pilnuje, że API nie zawiera kodu
tworzenia baz.

**Ograniczenie kroku 1:** gdy API używa osobnego loginu SQL, po utworzeniu bazy firmy
administrator SQL musi nadać temu loginowi dostęp do nowej bazy (`CREATE USER … FOR LOGIN …`,
role `db_datareader` i `db_datawriter`). Automatyczne nadanie zostanie dodane wraz z testami
na osobnym loginie SQL.

## Testy

- Bez SQL (także w CI): `CompanyProvisioningTests` — przejścia etapów, ponowienia,
  nazwa bazy i polityka połączeń, test architektury, 401 dla anonimowych, model widoku.
- Z SQL: `VATOWIEC_PROVISIONING_SQL=1`, klasa `CompanyProvisioningSqlTests`. Zmiany w
  `Vatowiec_System` są wycofywane transakcją; fizyczne bazy `Vatowiec_Company_{guid}`
  utworzone przez test są usuwane po sprawdzeniu znacznika operacji.

```powershell
$env:VATOWIEC_PROVISIONING_SQL = '1'
dotnet test Vatowiec.slnx -c Debug --no-build --filter "FullyQualifiedName~CompanyProvisioningSqlTests"
```

## Wdrożenie migracji

Migracja `20261122123552_CompanyProvisioning` jest addytywna (schemat `ops`, dwie tabele,
trigger niezmienności historii) i nie zmienia baz firm. Wykonanie jawne, po kopii System:

```powershell
dotnet run --project tools/database/Vatowiec.DatabaseTools -c Debug --no-build -- migrate-system
```

Start API ani usługi nie wykonuje migracji `Vatowiec_System`.
