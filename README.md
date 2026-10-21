# Otwarty następca VATowca

Projekt wielostanowiskowej aplikacji dla polskich mikrofirm. Obecny stan to
**etap bazy danych M0**: szkielet API, okno desktopowe oraz model firm
i kontrahentów z pierwszą migracją EF Core. Nie jest to jeszcze program do
prowadzenia księgowości.

Wymagania produktu: [SPECYFIKACJA.md](SPECYFIKACJA.md).
Architektura i granice modułów: [ADR-013](docs/adr/ADR-013-architektura-modularnego-monolitu.md).
Model i izolacja firm: [ADR-005](docs/adr/ADR-005-izolacja-firm-i-pierwszy-model.md).

## Wymagane narzędzia

- Windows (na nim sprawdzamy ten etap).
- [.NET SDK 10.0.402](https://dotnet.microsoft.com/download/dotnet/10.0) lub nowsza
  poprawka z pasma 10.0.4xx, zgodnie z `global.json`. Potrzebny jest SDK, nie sam runtime.
- [Visual Studio Code](https://code.visualstudio.com/) i bezpłatne rozszerzenie
  **C#** firmy Microsoft (`ms-dotnettools.csharp`). VS Code zaproponuje je po
  otwarciu katalogu. Rozszerzenie nie jest konieczne do poleceń w terminalu.
- Dostęp do nuget.org podczas pierwszego pobrania pakietów.
- Git do pracy z repozytorium.

Do migracji i testów SQL potrzebny jest lokalny **SQL Server 2025 Express**,
instancja `localhost\SQLEXPRESS`, oraz Windows Authentication z uprawnieniem
utworzenia bazy `Vatowiec_Dev` i zmiany jej schematu. SQL nie jest potrzebny do
samej kompilacji, testów bez bazy i uruchomienia obecnego okna desktopowego.
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
    Domain/           # Company, Contractor, walidacja strukturalna bez ORM
    Application/      # kontrakt kontekstu firmy
    Infrastructure/   # EF Core, mapowania SQL, filtry i migracje
  Server/Api/         # host ASP.NET Core i endpointy techniczne
  Desktop/            # Avalonia UI
  SharedContracts/    # DTO publicznego API
tests/Vatowiec.Tests/  # testy domeny/API i jawnie włączane testy SQL
docs/adr/             # decyzje architektoniczne
tools/database/       # odczyt schematu Vatowiec_Dev
```

Application zależy od Domain; Infrastructure od Application. API składa serwer
i korzysta z kontraktów. Desktop ma dostęp wyłącznie do SharedContracts.
Nie ma bezpośredniego dostępu klienta do SQL Server.

## Lokalna baza SQL Server

API w Development odczytuje `src/Server/Api/appsettings.Development.json`:

```text
Server=localhost\SQLEXPRESS;Database=Vatowiec_Dev;Integrated Security=True;Encrypt=True;TrustServerCertificate=True
```

To konfiguracja lokalna bez hasła: SQL uwierzytelnia konto Windows uruchamiające
proces. Desktop nie otrzymuje tego połączenia. Poza Development podstawowy
`appsettings.json` pozostawia połączenie puste; nie ma domyślnego adresu produkcji.
Można nadpisać konfigurację zmienną `ConnectionStrings__Vatowiec` lub user secrets
API. Fabryka migracji i testy SQL celowo korzystają wyłącznie ze zmiennej.

`TrustServerCertificate=True` dotyczy tylko lokalnego SQL w Development;
`Encrypt=True` zachowuje szyfrowanie. Warstwa konfiguracji EF odrzuca akceptację
niezweryfikowanego certyfikatu poza Development, a w Development ogranicza ją
do wskazanego serwera i bazy. Nie zmieniamy magazynu zaufania Windows ani
ustawień serwera. Na produkcji potrzebny będzie poprawnie weryfikowany
certyfikat SQL z `TrustServerCertificate=False`. Szczegóły:
[TLS w SqlClient](https://learn.microsoft.com/sql/connect/ado-net/encryption-and-certificate-validation).
Certyfikat HTTPS API jest odrębnym certyfikatem opisanym wyżej.

### Ręczne wykonanie migracji

W PowerShell z głównego katalogu repozytorium:

```powershell
dotnet tool restore
dotnet restore Vatowiec.slnx
$env:DOTNET_ENVIRONMENT = 'Development'
$env:ConnectionStrings__Vatowiec = 'Server=localhost\SQLEXPRESS;Database=Vatowiec_Dev;Integrated Security=True;Encrypt=True;TrustServerCertificate=True'
dotnet build Vatowiec.slnx --no-restore
dotnet ef migrations script 0 --project src/Modules/Core/Infrastructure --no-build --output src/Modules/Core/Infrastructure/obj/initial.sql
```

Przejrzyj wygenerowany `initial.sql`, a następnie wykonaj:

```powershell
dotnet ef database update --project src/Modules/Core/Infrastructure --no-build
```

Fabryka projektowa odrzuca inne środowisko, serwer, bazę, brak szyfrowania i SQL
Authentication. EF utworzy `Vatowiec_Dev`, jeśli nie istnieje; do tej operacji
technicznie łączy się z `master`, bez zmian innych baz użytkownika. Kolejne
wykonanie pomija już zastosowane migracje. API nie uruchamia migracji, nie
wywołuje `EnsureCreated` i nie seeduje danych przy starcie.

Pierwszą migrację zastosowano do nowej, pustej bazy. Przed migracją istniejącej
bazy z danymi wykonaj jej backup i sprawdź zgodność. Nie uruchamiaj `database
update 0` na używanej bazie: `Down` usuwa tabele wraz z danymi. Ten etap nie
przeprowadza migracji ani importu danych z oryginalnego VATowca.

### Powstałe tabele i ograniczenia

| Tabela | Zawartość i główne ograniczenia |
|---|---|
| `core.Companies` | Firmy; PK `Id`, nazwa, opcjonalny NIP/REGON, status aktywności, daty UTC, `rowversion` |
| `core.Contractors` | Kontrahenci; PK `(CompanyId, Id)`, FK do firmy bez kaskadowego usuwania, unikalny `(CompanyId, Code)`, nazwa, kraj, opcjonalny identyfikator podatkowy, status, daty UTC, `rowversion` |
| `dbo.__EFMigrationsHistory` | Historia zastosowanych migracji EF Core |

Osiem ograniczeń CHECK chroni niepuste identyfikatory i nazwy, kod kontrahenta,
strukturę kodu kraju i kolejność dat. Limity długości pól są techniczne; nie
zaimplementowano walidacji prawnej NIP/REGON ani weryfikacji rejestrów publicznych.
Adresy i rachunki będą osobnymi elementami przyszłych iteracji kartotek.

W SSMS odśwież **Databases → Vatowiec_Dev → Tables**. Możesz też uruchomić
[skrypt inspekcji](tools/database/Inspect-VatowiecDev.sql) w kontekście
`Vatowiec_Dev` albo użyć opcjonalnego `sqlcmd`:

```powershell
sqlcmd -S 'localhost\SQLEXPRESS' -E -d Vatowiec_Dev -b -i tools/database/Inspect-VatowiecDev.sql
```

DbContext otrzymuje niezmienny kontekst firmy, filtruje odczyty i sprawdza
zapisy. Bez firmy wyniki są puste, a zapis jest odrzucany. Logowanie i nadawanie
uprawnień nie zostały jeszcze wdrożone; API nie udostępnia danych firmowych.
Jawny kontekst stosowany w testach nie stanowi dowodu autoryzacji użytkowników.

## Sprawdzanie zmian

```powershell
dotnet build Vatowiec.slnx
dotnet test Vatowiec.slnx --no-build
dotnet format Vatowiec.slnx --verify-no-changes --no-restore
```

Domyślnie wykonuje się 19 przypadków bez bazy, a 9 testów SQL jest oznaczanych
jako pominięte. Testy bez bazy obejmują API, konfigurację, walidację modelu,
odmowę zapisu bez zgodnego kontekstu firmy i ograniczenie zaufania certyfikatu.
Host testowy API działa w pamięci z nadpisanym pustym połączeniem, niezależnie
od konfiguracji developerskiej i zmiennych procesu; nie potrzebuje HTTPS.

Po wykonaniu migracji włącz pełny zestaw w swoim PowerShell:

```powershell
$env:DOTNET_ENVIRONMENT = 'Development'
$env:ConnectionStrings__Vatowiec = 'Server=localhost\SQLEXPRESS;Database=Vatowiec_Dev;Integrated Security=True;Encrypt=True;TrustServerCertificate=True'
$env:VATOWIEC_SQL_INTEGRATION = '1'
dotnet test Vatowiec.slnx --no-build
Remove-Item Env:VATOWIEC_SQL_INTEGRATION
```

Testy integracyjne sprawdzają schemat i historię migracji, odczyt/zapis oraz daty,
izolację dwóch firm, odmowę niezgodnego zapisu, unikalność, FK, CHECK, konflikty
`rowversion`, atomowość zapisu i rollback. Każdy test używa losowych identyfikatorów
i wycofywanej transakcji. Nie tworzą/usuwają baz, nie migrują schematu i nie
usuwają istniejących danych. Włączenie testów SQL bez poprawnej konfiguracji
lub dostępu do bazy powoduje błąd, a nie pominięcie testów.

### Weryfikacja etapu bazy M0 (2026-10-21)

- Utworzono wyłącznie `Vatowiec_Dev` na `localhost\SQLEXPRESS` (SQL Server
  17.0.1000.7), migracja `20261021092204_InitialCompaniesAndContractors` / EF 10.0.13.
- Inspekcja SQL potwierdziła trzy tabele, PK, FK bez kaskady, unikalny indeks,
  osiem aktywnych i zaufanych CHECK oraz dwa pola `rowversion`.
- Wszystkie 28 testów przeszło, w tym 9 na rzeczywistym SQL Server, bez pominięć.
- Test regresyjny potwierdził pusty connection string hosta testowego API
  w Development i Production mimo ustawionej zmiennej połączenia SQL.
- Po testach obie tabele domenowe pozostały puste; rekordy testowe wycofano.
- Całe rozwiązanie buduje się bez błędów i ostrzeżeń. W piaskownicy zadanie
  Avalonia nie mogło zapisać swojego logu w AppData; build wykonano w profilu
  użytkownika bez zmiany uprawnień plików ani zabezpieczeń Windows.

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

Kolejna część M0 obejmie logowanie, role, audyt, API i interfejs kartoteki
kontrahentów z autoryzacją oraz rozbudowę danych firm i kontrahentów.
Nie zaimplementowano podatków, KSeF, JPK ani wtyczek. Nazwa projektu pozostaje
robocza, a licencja źródeł wymaga decyzji ADR-002; nie dodajemy jeszcze `LICENSE`.
