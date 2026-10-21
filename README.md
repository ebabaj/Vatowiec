# Otwarty następca VATowca

Projekt wielostanowiskowej aplikacji dla polskich mikrofirm. Obecny stan to
**pierwsza techniczna część M0**: szkielet API, okno desktopowe i konfiguracja
EF Core. Nie jest to jeszcze program do prowadzenia księgowości.

Wymagania produktu: [SPECYFIKACJA.md](SPECYFIKACJA.md).
Architektura i granice modułów: [ADR-013](docs/adr/ADR-013-architektura-modularnego-monolitu.md).

## Wymagane narzędzia

- Windows (na nim sprawdzamy ten etap).
- [.NET SDK 10.0.402](https://dotnet.microsoft.com/download/dotnet/10.0) lub nowsza
  poprawka z pasma 10.0.4xx, zgodnie z `global.json`. Potrzebny jest SDK, nie sam runtime.
- [Visual Studio Code](https://code.visualstudio.com/) i bezpłatne rozszerzenie
  **C#** firmy Microsoft (`ms-dotnettools.csharp`). VS Code zaproponuje je po
  otwarciu katalogu. Rozszerzenie nie jest konieczne do poleceń w terminalu.
- Dostęp do nuget.org podczas pierwszego pobrania pakietów.
- Git do pracy z repozytorium.

Nie trzeba instalować SQL Server, `dotnet-ef`, szablonów Avalonia ani dodatkowych
workloadów .NET. Avalonia i EF Core pobierają się jako pakiety NuGet.
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
    Domain/           # domena bez UI, HTTP i ORM
    Application/      # przyszłe przypadki użycia i interfejsy
    Infrastructure/   # rejestracja EF Core i pusty CoreDbContext
  Server/Api/         # host ASP.NET Core i endpointy techniczne
  Desktop/            # Avalonia UI
  SharedContracts/    # DTO publicznego API
tests/Vatowiec.Tests/  # testy konfiguracji i hosta API bez SQL Server
docs/adr/             # decyzje architektoniczne
```

Domain i Application nie zawierają jeszcze reguł ani modelu danych.
Application zależy od Domain; Infrastructure od Application. API składa serwer
i korzysta z kontraktów. Desktop ma dostęp wyłącznie do SharedContracts.
Nie ma bezpośredniego dostępu klienta do SQL Server.

## Konfiguracja EF Core bez bazy

`ConnectionStrings:Vatowiec` w `src/Server/Api/appsettings.json` ma wartość `null`.
Infrastructure rejestruje provider Microsoft SQL Server i `CoreDbContext`
w zakresie pojedynczego żądania. Konfiguracja nie otwiera połączenia.
Nie ma encji, migracji, inicjalizacji schematu ani danych demonstracyjnych.

W dalszym etapie connection string będzie pochodził z sekretów deweloperskich
lub zmiennej `ConnectionStrings__Vatowiec`. Nie wpisuj haseł ani adresów
produkcyjnych baz do śledzonych plików. W tym etapie nie konfiguruj połączenia.

## Sprawdzanie zmian

```powershell
dotnet build Vatowiec.slnx
dotnet test Vatowiec.slnx --no-build
dotnet format Vatowiec.slnx --verify-no-changes --no-restore
```

Testy sprawdzają odpowiedzi API, dostępność OpenAPI tylko w Development,
rejestrację providera SQL Server, brak otwartego połączenia i czas życia
DbContext. Host testowy działa w pamięci procesu, z pustym connection stringiem.
Nie potrzebuje certyfikatu HTTPS ani bazy. Nie są to jeszcze testy transakcji,
izolacji firm czy konfliktów zapisu na rzeczywistym SQL Server.

### Ponowna weryfikacja szkieletu M0 (2026-10-21)

- .NET SDK 10.0.402, runtime 10.0.13.
- `dotnet test Vatowiec.slnx --logger 'console;verbosity=normal'`: wszystkie
  9 przypadków xUnit przeszło, bez pominiętych i nieudanych testów.
- `dotnet build Vatowiec.slnx --no-restore`: wszystkie siedem projektów
  skompilowano, zero błędów i ostrzeżeń.
- `dotnet run --project src/Desktop --no-build`: uruchomiono okno
  „Otwarty następca VATowca — M0”. System potwierdził uchwyt okna i odpowiadający
  proces, z załadowanymi `Avalonia.Skia.dll`, `SkiaSharp.dll` i `libSkiaSharp.dll`.
  W tym przebiegu wcześniejsza blokada Windows App Control nie wystąpiła.
- API uruchomiono profilem `https`: nasłuchuje na localhost:7243 i localhost:5243.
  W profilu użytkownika jest ważny certyfikat deweloperski `localhost`, lecz
  brakuje zaufania. Pozostaje wykonać opisane wyżej `dotnet dev-certs https --trust`
  i potwierdzić komunikat Windows. Próba HTTPS z pełną walidacją certyfikatu
  zakończyła się `SEC_E_UNTRUSTED_ROOT` (curl: 60); po zaufaniu certyfikatowi
  trzeba ponowić sprawdzenie.
- `git check-ignore -v` potwierdził wykluczenia `bin`, `obj`, `TestResults`,
  lokalnych ustawień, logów i plików PFX. `git ls-files --cached --ignored
  --exclude-standard` nie wykazał śledzonych plików objętych wykluczeniami;
  wśród nieignorowanych plików nie ma wyników kompilacji.

Nie zmieniano zabezpieczeń Windows ani zaufania certyfikatów. Nie były potrzebne
poprawki kodu aplikacji ani `.gitignore`; zaktualizowano instrukcję i wynik
weryfikacji. SQL Server nie był używany ani potrzebny podczas tych sprawdzeń.
To weryfikacja pierwszego szkieletu technicznego, nie ukończenie całego M0.

## Dalsze kroki

Kolejna część M0 obejmie uzgodnienie modelu i izolacji firm (ADR-005), logowanie,
role, audyt, migracje i minimalną kartotekę kontrahentów z kontrolą konfliktów.
Nie zaimplementowano podatków, KSeF, JPK ani wtyczek. Nazwa projektu pozostaje
robocza, a licencja źródeł wymaga decyzji ADR-002; nie dodajemy jeszcze `LICENSE`.
