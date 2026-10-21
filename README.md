# Otwarty następca VATowca

Projekt wielostanowiskowej aplikacji dla polskich mikrofirm. Obecny stan to
**przebudowa persystencji M0 na database-per-company**: szkielet API, okno
desktopowe, osobne SystemDbContext i CompanyDbContext oraz migracje przygotowane
offline. Nowe bazy nie zostały jeszcze utworzone ani zmigrowane. Nie jest to
jeszcze program do prowadzenia księgowości.

Wymagania produktu: [SPECYFIKACJA.md](SPECYFIKACJA.md).
Architektura i granice modułów: [ADR-013](docs/adr/ADR-013-architektura-modularnego-monolitu.md).
Model i izolacja firm: [ADR-014](docs/adr/ADR-014-database-per-company.md).
[ADR-005](docs/adr/ADR-005-izolacja-firm-i-pierwszy-model.md) jest historyczny.
Plan baz, konfiguracja i testy: [database-per-company](docs/database-per-company.md).

## Wymagane narzędzia

- Windows (na nim sprawdzamy ten etap).
- [.NET SDK 10.0.402](https://dotnet.microsoft.com/download/dotnet/10.0) lub nowsza
  poprawka z pasma 10.0.4xx, zgodnie z `global.json`. Potrzebny jest SDK, nie sam runtime.
- [Visual Studio Code](https://code.visualstudio.com/) i bezpłatne rozszerzenie
  **C#** firmy Microsoft (`ms-dotnettools.csharp`). VS Code zaproponuje je po
  otwarciu katalogu. Rozszerzenie nie jest konieczne do poleceń w terminalu.
- Dostęp do nuget.org podczas pierwszego pobrania pakietów.
- Git do pracy z repozytorium.

Do późniejszych testów SQL potrzebny będzie lokalny **SQL Server 2025 Express**,
instancja `localhost\SQLEXPRESS`, Windows Authentication i trzy dedykowane bazy
`*_Test`. Obecny etap nie tworzy baz i nie wykonuje migracji. SQL nie jest potrzebny
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

Otworzy się proste polskie okno startowe. Na tym etapie klient działa samodzielnie,
nie wywołuje API i nie łączy się z bazą. Zamknij go przyciskiem okna.

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
  Server/Api/         # host ASP.NET Core i endpointy techniczne
  Desktop/            # Avalonia UI
  SharedContracts/    # DTO publicznego API
tests/Vatowiec.Tests/  # testy domeny/API i jawnie włączane testy SQL
docs/adr/             # decyzje architektoniczne
tools/database/       # historyczny skrypt odczytu Vatowiec_Dev (nieużywany przez nowe modele)
```

Application zależy od Domain; Infrastructure od Application. API składa serwer
i korzysta z kontraktów. Desktop ma dostęp wyłącznie do SharedContracts.
Nie ma bezpośredniego dostępu klienta do SQL Server.

## Persystencja database-per-company

`SystemDbContext` zawiera centralny katalog firm i wskazania baz.
`CompanyDbContext` zawiera lokalny profil i kontrahentów jednej firmy.
Użytkownicy/role, autoryzowany resolver, provisioning oraz audyt/backup/restore
pozostają kolejnymi zadaniami. W bazie systemowej nie ma kartoteki kontrahentów
ani dokumentów księgowych.

API czyta `ConnectionStrings__VatowiecSystem` z konfiguracji serwera.
Oba appsettings domyślnie pozostawiają ten klucz pusty. Stary klucz
`ConnectionStrings__Vatowiec` jest ignorowany. Nie ma domyślnej bazy firmowej.
Kontekst firmy wymaga jawnego CompanyDatabaseSelection zgodnego z ICompanyContext,
dostarczonego w scope przez przyszły serwerowy resolver po autoryzacji.
Rejestracja DI nie implementuje jeszcze autoryzacji ani weryfikacji profilu w SQL.
Sekrety i połączenia nie trafiają do klienta Avalonia.

Każdy kontekst ma własną migrację początkową i snapshot. Fabryki projektowe
generują model i SQL bez połączenia z bazą. Polecenia offline oraz plan późniejszego
utworzenia Vatowiec_System i dwóch baz firmowych opisuje
[instrukcja](docs/database-per-company.md).

Dotychczasowa migracja i snapshot Vatowiec_Dev są zachowane bez zmiany treści
w `Persistence/LegacyMigrations`, poza kompilacją i historią nowych kontekstów.
Nie uruchamiaj nowych migracji w Vatowiec_Dev ani historycznego Down.
Baza i jej tabele core.Companies/core.Contractors pozostają niezmienione.

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

Dziewięć testów SQL jest domyślnie pomijanych. Wymagają wcześniej zmigrowanych
Vatowiec_System_Test oraz Vatowiec_Company_A_Test/B_Test. Nowa flaga
`VATOWIEC_DATABASE_PER_COMPANY_SQL=1` włącza je jawnie; stara flaga
`VATOWIEC_SQL_INTEGRATION` nie działa. Szczegóły i nazwy zmiennych połączeń:
[instrukcja testów](docs/database-per-company.md#testy).
Testy nie tworzą baz i nie wykonują migracji; zapisują tylko w wycofywanych
transakcjach. Pominięcie nie jest zaliczeniem działania nowej struktury SQL.

## Dalsze kroki

Zaimplementować logowanie, użytkowników i role, serwerową autoryzację i resolver
z kontrolą fizycznego CompanyId/wersji bazy, potem wznawialny provisioning firmy
z aplikacji, audyt z trwałą identyfikacją aktorów oraz niezależny backup/restore.
Osobno wdrożyć nowe bazy po przeglądzie SQL i zgodzie na wykonanie.
Nie przeniesiono danych z Vatowiec_Dev.

Nie zaimplementowano podatków, KSeF, JPK ani wtyczek. M0 nie jest ukończone;
nazwa pozostaje robocza, a licencja źródeł wymaga decyzji ADR-002.
