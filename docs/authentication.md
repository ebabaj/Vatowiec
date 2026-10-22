# Logowanie i autoryzowany wybór firmy — M0

Decyzja: [ADR-015](adr/ADR-015-identity-i-autoryzowany-wybor-bazy.md).
Uwierzytelnianie, role, przypisania i sesje są w Vatowiec_System.
CompanyProfile i Contractors pozostają wyłącznie w bazach firmowych.
Interfejs logowania, wyboru firmy i odczytu kartoteki opisuje [desktop](desktop.md).
Nie ma operacji księgowych ani provisioningu firm.

## Dwa konta demonstracyjne

| Konto | Rola Identity | Firma A | Firma B |
|---|---|---|---|
| operator.m0 | Operator | ViewProfile, ViewContractors, EditContractors | ViewProfile, ViewContractors |
| czytelnik.m0 | Reader | ViewProfile | brak dostępu |

EditContractors od etapu ADR-017 pozwala na dodawanie i edycję z CSRF i rowversion;
zapis wymaga także ViewContractors. Instrukcja: [kartoteka](contractors.md).
Rola Identity nie daje dostępu do firmy bez przypisania CompanyAccess.

Firma A: aaaaaaaa-0000-4000-8000-000000000001, Vatowiec_Company_A_Dev.
Firma B: bbbbbbbb-0000-4000-8000-000000000002, Vatowiec_Company_B_Dev.

Konta utworzono z losowymi hasłami zapisanymi wyłącznie w .NET user-secrets API
(UserSecretsId: vatowiec-api-development). Hasła nie są w repozytorium ani logach.
Magazyn Development to lokalny plik poza repozytorium; nie jest szyfrowanym sejfem
produkcyjnym. Nie wklejaj wyniku user-secrets list do zgłoszeń ani czatu.

Klucze konfiguracji:

- DemoUsers:Operator:UserName i DemoUsers:Operator:Password.
- DemoUsers:Reader:UserName i DemoUsers:Reader:Password.

Można nadpisać je zmiennymi środowiskowymi z podwójnym podkreśleniem, np.
DemoUsers__Operator__Password. Aby ustawić własne hasło bez wpisywania go do historii
poleceń, w lokalnym PowerShell użyj Read-Host -AsSecureString, zbuduj JSON w pamięci
i przekaż go na stdin dotnet user-secrets set --project src/Server/Api.
Nie zapisuj JSON z hasłami w plikach projektu.

Ponowne ustawienie kont/uprawnień z lokalnej konfiguracji:

```powershell
dotnet run --project tools/database/Vatowiec.DatabaseTools --no-build -- seed-users
```

Narzędzie używa UserManager/RoleManager, weryfikuje politykę haseł i nie wypisuje
ich wartości. Jeśli lokalne hasło różni się od zapisanego, resetuje je przez Identity;
zmiana SecurityStamp odbiera ważność starym sesjom. Polecenie dotyczy lokalnego
demonstratora i przywraca zdefiniowane wyżej przypisania dwóch kont.

## Szybka próba dwóch użytkowników

1. Zbuduj i uruchom API w jednym terminalu:

```powershell
dotnet build Vatowiec.slnx --no-restore
dotnet run --project src/Server/Api --no-build --launch-profile https
```

2. Certyfikat deweloperski localhost musi być ważny i zaufany lokalnemu klientowi.
   Sprawdź dotnet dev-certs https --check --trust; w razie potrzeby samodzielnie
   wykonaj dotnet dev-certs https --trust i potwierdź komunikat systemu.
   Skrypt nie wyłącza sprawdzania TLS i nie zmienia zaufania certyfikatów.

3. W drugim lokalnym terminalu PowerShell uruchom:

```powershell
dotnet run --project tools/database/Vatowiec.DatabaseTools --no-build -- verify-users
```

Narzędzie odczytuje hasła z lokalnego user-secrets lub środowiska, przechowuje cookie tylko w pamięci
i pokazuje wyłącznie statusy HTTP. Weryfikuje dwie sesje jednocześnie:

- Operator widzi A i B, odczytuje oba profile oraz kontrahentów A.
- Czytelnik widzi tylko A, odczytuje profil A, otrzymuje 403 dla B i kontrahentów A.
- Próba podstawienia query companyId zwraca 400, nie przestawia bazy.
- Logout operatora daje mu 401 przy następnym żądaniu; czytelnik nadal odczytuje A.

Narzędzie wysyła dane logowania wyłącznie przez HTTPS do localhost. Dla innego portu
tej samej lokalnej aplikacji podaj adres jako drugi argument:

```powershell
dotnet run --project tools/database/Vatowiec.DatabaseTools --no-build -- verify-users https://localhost:7245
```

Ten wariant został wykonany na działającym API; oba logowania i wszystkie
oczekiwane odmowy przeszły. Po próbie wylogowano oba konta i zatrzymano testowy proces API.

Opcjonalnie można użyć ./tools/database/Test-Authentication.ps1, który czyta
user-secrets i wykonuje tę samą próbę. Na systemie z zablokowanymi skryptami .ps1
użyj narzędzia .NET powyżej; nie trzeba zmieniać polityki PowerShell.

## Kontrakt HTTP

| Żądanie | Zasady i odpowiedź |
|---|---|
| GET /api/v1/auth/csrf | Anonimowe; ustawia cookie antiforgery i zwraca token/headerName, no-store |
| POST /api/v1/auth/login | JSON userName/password, cookie antiforgery i X-CSRF-TOKEN; 200 z datą wygaśnięcia i cookie sesji |
| GET /api/v1/auth/me | 200: nazwa, aktualne role i wygaśnięcie; 401 bez sesji |
| POST /api/v1/auth/logout | Ważna sesja i nowy token CSRF pobrany po logowaniu; 204 |
| GET /api/v1/companies | Dostępne firmy i uprawnienia; nigdy lokalizacje SQL ani dane dostępowe |
| GET /api/v1/companies/{id}/profile | Wymaga ViewProfile; 200 po kontroli profilu i schematu bazy |
| GET /api/v1/companies/{id}/contractors | Wymaga ViewContractors; filtry q/country/active oraz offset/limit, maks. 100 pozycji |
| GET /api/v1/companies/{id}/contractors/{contractorId} | Odczyt aktualnej wersji i rowversion; ViewContractors |
| POST /api/v1/companies/{id}/contractors | ViewContractors + EditContractors i CSRF; atomowy audyt utworzenia |
| PUT /api/v1/companies/{id}/contractors/{contractorId} | Te same prawa i CSRF, wymagane rowversion; 409 przy konflikcie |

Własny klient utrzymuje osobny magazyn cookie dla każdego użytkownika. Najpierw
pobiera /csrf, wysyła token w X-CSRF-TOKEN razem z JSON logowania, zachowuje cookie
sesji, a przed logout pobiera /csrf ponownie dla zalogowanej tożsamości.
JSON logowania nie przyjmuje CompanyId ani dodatkowych pól. Nie wysyłaj haseł
w URL. Nie ma wyboru domyślnej firmy ani CompanyId zapamiętanego globalnie.

401 oznacza brak ważnej sesji lub niepoprawne/zablokowane konto. 403 oznacza brak
uprawnienia do firmy/czynności. 400 dotyczy CSRF, HTTPS, JSON i manipulacji
CompanyId (query companyId lub X-Company-Id są zabronione). 503 oznacza niedostępną
lub niezgodną bazę/profil/schemat. 429 oznacza limit prób logowania.
Odmowa nie zwraca connection stringa ani surowego wyjątku SQL.

Sesja ma 30 minut bez automatycznego przedłużania. Authentication:SessionMinutes
można ustawić lokalnie w zakresie 1–120. Sprawdzane są zarówno bilet cookie, jak
i centralny rekord sesji oraz SecurityStamp. Logout unieważnia sesję także przy
próbie ponownego użycia skopiowanego cookie. Pięć błędnych haseł blokuje konto na
pięć minut; endpoint logowania ma dodatkowo limit 20/minutę/IP.
Konto z włączonym drugim składnikiem otrzyma odmowę logowania samym hasłem;
przepływ MFA pozostaje poza zakresem tego etapu.

## Wybór bazy

API odczytuje CompanyAccess, aktywność firmy i CompanyDatabases. Następnie używa
serwerowego CompanyConnections:LocalWindows z konfiguracji Development. ServerName
musi zgadzać się z katalogiem; DatabaseName dobiera wyłącznie serwer. Resolver
odmawia baz systemowych SQL i Vatowiec_System jako celu firmy. Sprawdza rzeczywisty
CompanyProfile.CompanyId i fizyczną historię migracji przed odczytem kontrahentów.
Każda operacja ma nowy kontekst; równoległe sesje nie nadpisują wyboru firmy.

Profile połączeń innych instancji należy przechowywać po stronie serwera.
Hasła SQL, jeśli potrzebne, należą do sekretów serwera. Klient Avalonia otrzyma
wyłącznie kontrakty API i cookie sesji, nigdy połączenie SQL.

## Jawna migracja i testy

Przed wdrożeniem przejrzano obj/identity.sql, wykonano kopię Vatowiec_System
COPY_ONLY z CHECKSUM i RESTORE VERIFYONLY. Migracja
20261021114324_IdentityAndCompanyAccess dodaje dziewięć tabel security i nie
zmienia tabel firmowych. System ma teraz dwa wpisy historii EF; bazy firmowe po jednym.
Down usuwa konta, uprawnienia i sesje, więc nie należy go używać jako rollbacku z danymi.

Powtarzalne polecenia administracyjne:

```powershell
dotnet run --project tools/database/Vatowiec.DatabaseTools --no-build -- backup-system
dotnet run --project tools/database/Vatowiec.DatabaseTools --no-build -- migrate-system
dotnet run --project tools/database/Vatowiec.DatabaseTools --no-build -- seed-users
dotnet run --project tools/database/Vatowiec.DatabaseTools --no-build -- verify
```

Nie ma migracji ani seedowania kont przy starcie API. Nie wdrażamy hasła z repozytorium.

Pełne testy (dedykowane, tymczasowe bazy *_Test):

```powershell
dotnet build Vatowiec.slnx --no-restore
dotnet run --project tools/database/Vatowiec.DatabaseTools --no-build -- test
dotnet format Vatowiec.slnx --verify-no-changes --no-restore
```

Testy używają losowych haseł wyłącznie w pamięci i osobnego providera Data Protection.
Weryfikują sesje, CSRF, role/przypisania, zmianę uprawnień i SecurityStamp, prawidłową
bazę i obcy profil, manipulację CompanyId, wygasły bilet i sesję SQL, replay po
logout oraz dwóch równoległych użytkowników firmy A. Wygaśnięcie sprawdzane jest
przez testowy TimeProvider, bez czekania i bez publicznego endpointu zegara.
Raporty TRX są w ignorowanym TestResults. Bazy *_Test są sprzątane, a dane
demonstracyjne pozostają. Audyt księgowy, backup/restore z aplikacji i pełne M0
nadal wymagają kolejnych etapów.

Wynik końcowy (2026-10-21): **70/70 testów zaliczonych**, w tym 38 bez SQL i 32
na rzeczywistym SQL/API, bez pominięć. Kompilacja ośmiu projektów: zero błędów
i ostrzeżeń. Formatowanie i git diff --check poprawne. Osobno wykonano verify-users
na rzeczywistym HTTPS localhost:7245 z domyślną walidacją TLS; oba konta i wszystkie
odmowy zachowały się zgodnie z tabelą. Nie zmieniano polityki PowerShell ani zaufania
certyfikatów. Po próbie wylogowano oba konta. Nie wykonano commit ani push.
