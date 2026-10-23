# Otwarty następca VATowca

Projekt wielostanowiskowej aplikacji dla polskich mikrofirm. Obecny stan to
**kartoteka kontrahentów M0 database-per-company**: logowanie Avalonia,
wybór firmy, dodawanie/edycja z audytem i konfliktami oraz wyszukiwanie po stronie serwera.
Osobne SystemDbContext i CompanyDbContext, trzy bazy SQL
Server oraz dwie fikcyjne firmy. Działają Identity, sesje API i autoryzowany resolver
bazy firmy. Pełny zestaw po integracji EWIB ADR-026: **323/323**,
bez pominięć — 103 SQL/API i 220 bez SQL. Kompilacja bez ostrzeżeń i błędów.
Nie jest to jeszcze program do prowadzenia księgowości.

**Aktualny etap: IbanNet i podgląd banku z NBP EWIB (ADR-026).** W zakładce
Rachunki bankowe przycisk **Sprawdź numer** pokazuje kontrolę NRB/IBAN lub
niezweryfikowany format spoza IBAN, normalizuje spacje/myślniki w polu i dla
poprawnego polskiego numeru pobiera propozycje banku/BIC z EWIB przez API.
Puste pola uzupełnia automatycznie, inne wymagają jawnego zastąpienia; kilka BIC
wymaga wyboru. Podgląd zmienia tylko szkic. Ostrzeżenia można świadomie zaakceptować
z osobnym uprawnieniem i audytem. Kontrola nie potwierdza istnienia rachunku,
właściciela, banku ani BIC. [Instrukcja i wyniki](docs/contractors.md),
[licencja Apache-2.0 i autor](licenses/THIRD-PARTY-NOTICES.md).

Moduł walut (ADR-024): każda firma ma lokalny
słownik 149 walut, ustawienia A/B/C, kursy decimal i niezmienną historię.
W Avalonia wybierz **Słowniki → Waluty i kursy NBP**: konfiguracja, filtry,
ręczny import najnowszych publikacji lub okresu oraz jawne rozstrzyganie konfliktów.
Domyślnie EUR/USD/GBP/CHF z A; PLN bez pobierania. Nie ma harmonogramu ani
reguł doboru kursu do faktur. [Instrukcja, API i wyniki](docs/currencies-and-nbp.md).
Walutę identyfikuje wyłącznie niezmienne Code char(3) PK; kursy wskazują
CurrencyCode FK i zachowują własne bigint Id. Reset objął tylko moduł walutowy,
bez mapowania starych identyfikatorów i bez zmian konfiguracji DataPort.

Formularz kontrahenta
ma Dane główne, Adresy, Kontakty i Rachunki bankowe. Można wybierać grupę,
dodawać rodzaje adresów, edytować szczegóły i atomowo zmieniać preferencje.
TaxId/VatEuId/KsefIdWew są edytowalne z normalizacją, wersjonowanymi regułami
i świadomą akceptacją ostrzeżeń. Zakładka Weryfikacje obejmuje ręczne MF/WL2,
VIES i GUS, z historią środowiska i bez automatycznego nadpisywania danych.
Domyślnie Test; TEST/MOCK nie stanowią produkcyjnego potwierdzenia urzędowego.
[Konfiguracja, algorytmy i instrukcja](docs/identifiers-and-registries.md).
Działa osobny [DataPort](docs/dataport.md): szyfrowany klucz firmy, ręczne pobranie
danych pośrednika i selektywny import do szkicu. Rzeczywista usługa DataPort jest
oddzielna od oficjalnych rejestrów TEST; automatyczne próby wyłącznie na atrapach.
Błędny IBAN lub format spoza standardu wymaga świadomej, audytowanej akceptacji
przez uprawnioną osobę. W etapie ADR-021 bazy i kartoteki zachowano; jawnie dodano
słownik reguł w System i historię weryfikacji w każdej bazie firmy, po backupach.
W ADR-022 dodano konfigurację DataPort po kolejnych zweryfikowanych kopiach;
za zgodą właściciela pełny runner ponownie zainicjalizował demonstracyjne dane
i konta. Nie odtwarzano samych baz ani historycznych identyfikatorów.

Wymagania produktu: [SPECYFIKACJA.md](SPECYFIKACJA.md).
Architektura i granice modułów: [ADR-013](docs/adr/ADR-013-architektura-modularnego-monolitu.md).
Model i izolacja firm: [ADR-014](docs/adr/ADR-014-database-per-company.md).
[ADR-005](docs/adr/ADR-005-izolacja-firm-i-pierwszy-model.md) jest historyczny.
Plan baz, konfiguracja i testy: [database-per-company](docs/database-per-company.md).
Logowanie dwóch kont i test odmowy dostępu: [instrukcja](docs/authentication.md),
[ADR-015](docs/adr/ADR-015-identity-i-autoryzowany-wybor-bazy.md).
Scenariusz okienkowy operatora i czytelnika: [desktop](docs/desktop.md),
[ADR-016](docs/adr/ADR-016-desktop-logowanie-i-kontekst-firmy.md).
Dodawanie, edycja, filtry i konflikt dwóch okien: [kartoteka](docs/contractors.md),
[ADR-017](docs/adr/ADR-017-kartoteka-kontrahentow-i-audyt.md).

## Wymagane narzędzia

- Windows (na nim sprawdzamy ten etap).
- [.NET SDK 10.0.402](https://dotnet.microsoft.com/download/dotnet/10.0) lub nowsza
  poprawka z pasma 10.0.4xx, zgodnie z `global.json`. Potrzebny jest SDK, nie sam runtime.
- [Visual Studio Code](https://code.visualstudio.com/) i bezpłatne rozszerzenie
  **C#** firmy Microsoft (`ms-dotnettools.csharp`). VS Code zaproponuje je po
  otwarciu katalogu. Rozszerzenie nie jest konieczne do poleceń w terminalu.
- Dostęp do nuget.org podczas pierwszego pobrania pakietów.
- Git do pracy z repozytorium.

Do pełnych testów SQL potrzebny jest lokalny **SQL Server 2025 Express**,
instancja `localhost\SQLEXPRESS`, Windows Authentication i trzy jawnie dozwolone
bazy demonstracyjne. Pełny runner czyści ich fikcyjne fixture przez DML, bez DROP/CREATE. SQL nie jest potrzebny
do kompilacji, generowania migracji/skryptów, testów bez bazy ani startu okna.
Nie trzeba instalować szablonów Avalonia ani dodatkowych workloadów .NET.
Avalonia i EF Core pobierają się jako pakiety NuGet; lokalne narzędzie `dotnet-ef`
10.0.13 jest przypięte w `dotnet-tools.json`.
Docelowa architektura uwzględnia Linux, ale uruchomienie na nim nie zostało
jeszcze zweryfikowane; zależności systemowe opisuje
[dokumentacja Avalonia](https://docs.avaloniaui.net/docs/get-started/).

## Pierwsze uruchomienie w Visual Studio Code

Otwórz główny katalog repozytorium przez **File → Open Folder** albo `code .`.
Wybierz **Terminal → New Terminal**. Polecenia poniżej wykonuj z katalogu
zawierającego `Vatowiec.slnx`.

```powershell
dotnet --info
dotnet restore Vatowiec.slnx
dotnet build Vatowiec.slnx --no-restore
dotnet test Vatowiec.slnx --no-build
```

API korzysta z lokalnego certyfikatu HTTPS. W swoim terminalu PowerShell
(poza piaskownicą agenta) sprawdź, czy certyfikat istnieje i jest zaufany:

```powershell
dotnet dev-certs https --check --trust
```

Jeśli polecenie zgłasza brak certyfikatu lub brak zaufania, wykonaj:

```powershell
dotnet dev-certs https --trust
```

Windows może wyświetlić prośbę o zaufanie certyfikatowi deweloperskiemu
`localhost` — potwierdź ją. Następnie ponownie wykonaj
`dotnet dev-certs https --check --trust`; polecenie powinno potwierdzić ważny,
zaufany certyfikat i zakończyć się kodem 0. Uruchom ponownie API i otwórz
`https://localhost:7243/api/v1/status` w przeglądarce.

Nie trzeba uruchamiać terminala jako administrator. Jeśli zaufanie blokuje
polityka organizacji, przekaż komunikat administratorowi. Certyfikat służy
lokalnemu środowisku programisty. Nie dodawaj jego plików ani haseł do
repozytorium. Szczegóły: [dokumentacja dotnet dev-certs](https://learn.microsoft.com/dotnet/core/tools/dotnet-dev-certs).

### Serwer API

```powershell
dotnet run --project src/Server/Api --launch-profile https
```

Sprawdź w przeglądarce:

- `https://localhost:7243/api/v1/status` — informacja o etapie aplikacji;
- `https://localhost:7243/health` — `Healthy`, czyli działający proces API;
- `https://localhost:7243/openapi/v1.json` — dokument OpenAPI w Development.

OpenAPI jest dokumentem JSON; nie instalujemy osobnego interfejsu Swagger UI.
`/health` nie sprawdza SQL Server. API nie potrzebuje adresu bazy do startu.
Profil uruchomienia wiąże serwer wyłącznie z localhost; HTTP na porcie 5243
przekierowuje do HTTPS. Zatrzymaj serwer przez `Ctrl+C`.

### Aplikacja desktopowa

W drugim terminalu:

```powershell
dotnet run --project src/Desktop
```

Otworzy się ekran logowania. Wpisz `operator.m0` i hasło z lokalnej konfiguracji,
wybierz firmę A lub B i kliknij **Pokaż kontrahentów / odśwież**. Przyciski
**Zmień firmę** i **Wyloguj** działają bez restartu. Klient komunikuje się wyłącznie
z API HTTPS, domyślnie `https://localhost:7243`; adres można zmienić w oknie
lub przez `VATOWIEC_API_URL`. Cookie i tokeny pozostają w pamięci.
W A operator może użyć **Nowy (Ctrl+N)** lub **Edytuj (F2)**; w B ma tylko odczyt.
Po zapisie wyszukaj pozycję albo odśwież listę. Szczegóły haseł, odmów dostępu i błędów:
[instrukcja desktopu](docs/desktop.md), [kartoteka i konflikt](docs/contractors.md).

### Skróty i debugowanie

`Ctrl+Shift+B` buduje solution. W panelu **Run and Debug** wybierz konfigurację
**API** lub **Desktop** i naciśnij `F5`. Konfiguracja API używa tych samych
lokalnych adresów HTTPS/HTTP co powyższy profil; wcześniej zaufaj certyfikatowi.
Zadanie **test** jest dostępne przez **Terminal → Run Task**.

## Struktura i zależności

```text
src/
  Modules/Core/
    Domain/           # katalog, profil firmy, kontrahenci; bez ORM
    Application/      # kontrakt kontekstu firmy
    Infrastructure/   # dwa konteksty EF, rozłączne mapowania i migracje
  Server/Api/         # host, Identity/cookie/CSRF, autoryzowany odczyt firm
  Desktop/            # Avalonia UI
  SharedContracts/    # DTO publicznego API
tests/Vatowiec.Tests/  # testy domeny/API i jawnie włączane testy SQL
docs/adr/             # decyzje architektoniczne
tools/database/       # narzędzie lokalnego wdrożenia, weryfikacji i pełnych testów SQL
```

Application zależy od Domain; Infrastructure od Application. API składa serwer
i korzysta z kontraktów. Desktop ma dostęp wyłącznie do SharedContracts.
Nie ma bezpośredniego dostępu klienta do SQL Server.

## Persystencja database-per-company

`SystemDbContext` zawiera centralny katalog firm, wskazania baz, Identity,
przypisania/uprawnienia użytkowników i sesje.
`CompanyDbContext` zawiera lokalny profil i kontrahentów jednej firmy.
Audyt kartoteki jest atomowy w audit.ContractorChanges bazy firmy; security.Installation
przechowuje trwałą przestrzeń identyfikatorów aktorów. Provisioning z aplikacji i audyt księgowy/backup/restore pozostają kolejnymi
zadaniami. W bazie systemowej nie ma kartoteki kontrahentów
ani dokumentów księgowych.

API czyta `ConnectionStrings__VatowiecSystem` z konfiguracji serwera.
appsettings.Development.json wskazuje lokalną Vatowiec_System przez Windows
Authentication z Encrypt=True; poza Development połączenie jest puste. Stary klucz
`ConnectionStrings__Vatowiec` jest ignorowany. Nie ma domyślnej bazy firmowej.
API korzysta z resolvera po uwierzytelnieniu sesji i sprawdzeniu CompanyAccess.
Resolver tworzy osobny kontekst i weryfikuje rzeczywisty profil oraz schemat SQL.
Bezpośrednia rejestracja DI nadal odmawia kontekstu firmy bez jawnego wyboru.
Sekrety i połączenia nie trafiają do klienta Avalonia.

Każdy kontekst ma własną historię migracji i snapshot. Fabryki projektowe
generują model i SQL bez połączenia z bazą. Polecenia offline i powtarzalne
wdrożenie Vatowiec_System oraz dwóch baz firmowych opisuje
[instrukcja](docs/database-per-company.md).

Dotychczasowa migracja i snapshot Vatowiec_Dev są zachowane bez zmiany treści
w `Persistence/LegacyMigrations`, poza kompilacją i historią nowych kontekstów.
Nie uruchamiaj nowych migracji w Vatowiec_Dev ani historycznego Down.
Stara baza została usunięta za zgodą użytkownika po sprawdzeniu przeznaczenia,
archiwalnej migracji i braku rekordów. Nie kopiowano danych.

Pozostają trzy bazy testowe projektu: Vatowiec_System (katalog i lokalizacje)
oraz Vatowiec_Company_A_Dev/B_Dev (osobne profile i kontrahenci). Każda ma własną
__EFMigrationsHistory. Dane demonstracyjne nie zawierają autentycznych identyfikatorów.

## Sprawdzanie zmian

```powershell
dotnet build Vatowiec.slnx --no-restore
$env:VATOWIEC_DATABASE_PER_COMPANY_SQL = '0'
dotnet test Vatowiec.slnx --no-build
dotnet format Vatowiec.slnx --verify-no-changes --no-restore
```

Testy offline weryfikują rozłączne modele, lokalny profil/FK, historie i SQL
migracji, zgodność snapshotów, odmowę bez wyboru bazy, izolację scope,
politykę połączeń, filtry, kontrolę zapisu i host API.

Powyższy przebieg bez SQL zalicza 104 testy i pomija 76 testów SQL/API. Pełny przebieg:

```powershell
dotnet run --project tools/database/Vatowiec.DatabaseTools --no-build -- test
```

Runner sprawdza instancję, nazwy, znaczniki i fikcyjne dane, po czym czyści fixture przez DML w
wyłącznie Vatowiec_System i Vatowiec_Company_A_Dev/B_Dev. **Przed testem zatrzymaj
API i desktop; bieżące fikcyjne dane i sesje zostaną usunięte.** Po pełnym
przebiegu inicjalizuje konta i dane demonstracyjne z lokalnej konfiguracji.
Nie odtwarza baz ani tabel, nie tworzy dodatkowych *_Test i nie modyfikuje innych baz. Pełny wynik:
**180/180**, bez pominięć (76 SQL/API, 104 bez SQL). Raport TRX: TestResults.
Flagi SQL i dodatkową zgodę na przygotowane fixture ustawia runner;
[instrukcja testów](docs/database-per-company.md#testy). Pominięcie nie jest zaliczeniem.

## Logowanie demonstracyjne

Konta operator.m0 (A i B) i czytelnik.m0 (tylko profil A) są w Vatowiec_System.
Hasła pochodzą z lokalnego user-secrets poza repozytorium. Uruchom API poleceniem
opisanym wyżej, a w drugim terminalu wykonaj:

```powershell
dotnet run --project tools/database/Vatowiec.DatabaseTools --no-build -- verify-users
```

Narzędzie używa dwóch osobnych magazynów cookie, pobiera CSRF, loguje oba konta,
sprawdza odczyt A oraz 403 dla próby czytelnika w B i wykonuje logout. Nie wypisuje
haseł/tokenów i działa również przy zablokowanych skryptach PowerShell.
Wymaga zaufanego certyfikatu deweloperskiego HTTPS; szczegóły i
kontrakt HTTP: [logowanie](docs/authentication.md).

## Dalsze kroki

Kolejne etapy obejmą wznawialny provisioning firmy
z aplikacji, audyt księgowy z trwałą identyfikacją aktorów oraz niezależny backup/restore.
Narzędzie wdrożeniowe jest dostępne poza API; pełny provisioning z aplikacji
pozostaje osobnym etapem. Nie przeniesiono danych z usuniętej Vatowiec_Dev.

Nie zaimplementowano podatków, KSeF, JPK ani wtyczek. M0 nie jest ukończone;
nazwa pozostaje robocza, a licencja źródeł wymaga decyzji ADR-002.
