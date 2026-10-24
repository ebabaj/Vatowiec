# Logowanie i autoryzowany wybór firmy — M0

Decyzja: [ADR-015](adr/ADR-015-identity-i-autoryzowany-wybor-bazy.md).
Uwierzytelnianie, role, przypisania i sesje są w Vatowiec_System.
CompanyProfile i Contractors pozostają wyłącznie w bazach firmowych.
Interfejs logowania, wyboru firmy i odczytu kartoteki opisuje [desktop](desktop.md).
Nie ma operacji księgowych ani provisioningu firm.

Aktualizacja ADR-033: [Użytkownicy i uprawnienia](users-and-permissions.md) opisuje
administrację Identity/CompanyAccess w Avalonia, hasła tymczasowe i obowiązkową
zmianę własnego hasła, natychmiastowe unieważnianie sesji i niezmienny audyt System.
`seed-users` od tego etapu inicjalizuje wyłącznie brakujące konta.

ADR-034 dodaje globalną politykę haseł w `security.PasswordPolicy` z RowVersion
i atomowym, niezmiennym audytem. Początkowo minimum 12 i wszystkie cztery klasy;
po potwierdzonym wyłączeniu każde niepuste hasło. Jedyny walidator Identity czyta
bieżący rekord przy tworzeniu, resecie i obu rodzajach zmiany hasła, bez restartu.
Neutralne PasswordOptions nie tworzą drugiego, sztywnego wymagania 12 znaków.
Zmiana polityki nie unieważnia dotychczasowych haseł, stempli ani sesji.
Hashowanie, blokady, CSRF i autoryzacja pozostają aktywne. Generator nadal jest
kryptograficzny, domyślnie 16 znaków, również po wyłączeniu złożoności.

Istniejącą rolę administratora można nadać/odebrać innemu kontu w osobnej sekcji
Role systemowe. API ponownie sprawdza aktora w bazie po uzyskaniu wspólnej blokady
transakcyjnej, weryfikuje wersję i potwierdzenie, unieważnia sesje celu oraz
zapisuje trwały audyt. Nie można odebrać roli sobie ani ostatniemu aktywnemu
administratorowi; nadanie wymaga aktywnego celu. Nie powstają CompanyAccess.
Szczegóły i wdrożenie: [instrukcja administratora](users-and-permissions.md).

Przed użyciem ADR-034 wymagana jest jawna migracja System po zweryfikowanym backupie.
Brak jej schematu lub rekordu konfiguracji nie uruchamia domyślnej polityki w API.
Endpointy konfiguracji i historii zwracają konkretny błąd instalacyjny, a formularz
bez udanego odczytu pozostaje niedostępny. Diagnostyka SQL zapisuje tylko numer,
ścieżkę operacji bez query oraz korelację, bez parametrów i tekstu SQL.

## Opcjonalne zapamiętanie w kliencie Windows — ADR-032

[ADR-032](adr/ADR-032-lokalne-logowanie-i-firma-domyslna.md) dodaje opcjonalne
zapamiętanie hasła wyłącznie w Windows Credential Manager bieżącego użytkownika
Windows na tym komputerze. Cel poświadczenia wiąże dokładny HTTPS origin
(łącznie z portem) i login. Nie używamy plików, rejestru ani user-secrets API do
zapisu hasła klienta. Niesekretne preferencje są w
`%LOCALAPPDATA%\Vatowiec\Desktop\login-preferences.json`; cookie i CSRF nadal tylko
w pamięci. Obie opcje zapamiętywania i autologowania są początkowo wyłączone.

Zapis następuje dopiero po udanym loginie. Maska „••••••••” w pustym polu i status
„Hasło zapisane na tym komputerze” zależą od rzeczywistej obecności wpisu, nie
od checkboxa. Maska nie jest wartością TextBox ani hasłem wysyłanym do API.
Wpisane nowe hasło ma pierwszeństwo, a zmiana celu czyści je i odświeża status.
Przy starcie autologowanie wykonuje jedną próbę zwykłego /csrf + /login.
Po każdym udanym logowaniu, także ręcznym i z wyłączonym autologowaniem, klient
pobiera bieżące firmy i otwiera zapisany CompanyId wspólną logiką OpenCompany(),
jeśli wciąż jest dostępny. Checkbox steruje wyłącznie startem. Bez ustawienia lub
uprawnienia pozostaje wybór firmy, bez arbitralnego fallbacku. Firma domyślna
jest osobna dla API/login; nie zmienia CompanyAccess ani wyboru bazy na serwerze.
Zmiana adresu, użytkownika i wygaśnięcie sesji nigdy nie uruchamiają prób w tle.

Nieudana próba wraca do ręcznego formularza, bez ponowień. Niedostępny magazyn
nie blokuje logowania wpisanym hasłem i nie powoduje zapisu w innym miejscu.
**Wyloguj** wyłącza autostart profilu, oprócz dotychczasowego unieważnienia sesji
i czyszczenia pamięci. **Zapomnij dane logowania** usuwa właściwe poświadczenie
i wyłącza zapamiętywanie/autologowanie, bez ingerencji w konto i uprawnienia.
Na współdzielonym lub niezaufanym koncie Windows pozostaw obie opcje wyłączone.

Weryfikacja ADR-032, 2026-10-24:

- Build rozwiązania i klienta w Debug: 0 błędów i ostrzeżeń. Testy
  `Category!=SqlIntegration` w Debug: **438/438**, bez pominięć;
  `TestResults/remembered-login-fixes-debug.trx`. Działające API blokuje swój
  katalog Debug; pełny build/test użył `-p:BaseOutputPath=bin/Adr032Verification/`,
  bez zatrzymywania serwera. Klient i próba UI korzystały ze zwykłego Debug.
- Atrapy magazynów sprawdzają ręczny login, zapis wyłącznie po sukcesie,
  ponowny start, domyślny GUID, brak dostępu i odmowę przy odczycie profilu,
  świeże uprawnienia, błędne hasło/limit/awarię API, wylogowanie, zapomnienie,
  oddzielenie serwerów/loginów, brak podwójnych operacji oraz awarie magazynów.
  Dodatkowo sprawdzono maskę zależną od obecności wpisu (bez odczytu hasła do VM),
  pierwszeństwo nowego hasła i aktualizację po sukcesie, ręczne otwieranie domyślnej
  niezależnie od autostartu, oczekiwanie na świeżą listę i serializację otwarcia,
  brak hasła w polach ViewModelu i schemat niesekretnego JSON.
- Osobny test natywnego Windows Credential Manager zapisuje, czyta, zmienia
  i usuwa wyłącznie unikalny fikcyjny wpis. Nie listuje ani nie odczytuje
  istniejących poświadczeń użytkownika.
- Rzeczywisty MainWindow Avalonia w Debug, atrapa API, oddzielny testowy plik
  preferencji i fikcyjny wpis Credential Manager, cztery uruchomienia procesu:
  checkbox bez wpisu nie tworzy maski; po restarcie ręcznym maska i status są
  widoczne, a TextBox.Text nadal ma długość 0. Przycisk używa poświadczenia
  i otwiera domyślną B dokładnie raz, nadal tylko do odczytu. Ręczna zmiana na A
  zachowuje domyślną B. Zmiana API/loginu usuwa maskę i wpisane nowe hasło.
  Sprawdzono logowanie wpisanym nowym hasłem, restart z autologowaniem do B raz
  i zapomnienie z kolejnym restartem bez maski ani próby API. Fikcyjne poświadczenie
  usunięto. Zrzuty lokalne: `TestResults/login-fixes-manual-mask-debug.png`,
  `TestResults/login-fixes-manual-default-debug.png`,
  `TestResults/login-fixes-auto-default-debug.png`,
  `TestResults/login-fixes-forgotten-debug.png`.

Nie zmieniano uwierzytelniania serwera ani baz SQL; testów SQL/destrukcyjnych
i resetującego runnera nie uruchamiano. Bez Commit i Push.

## Dwa konta demonstracyjne

| Konto | Rola Identity | Firma A | Firma B |
|---|---|---|---|
| operator.m0 | Operator | ViewProfile, ViewContractors, EditContractors, AcceptWarnings, VerifyRegistries, ManageCompanyIntegrations, ViewExchangeRates, ManageCurrencies, ImportExchangeRates | ViewProfile, ViewContractors, ViewExchangeRates |
| czytelnik.m0 | Reader | ViewProfile, ViewExchangeRates | brak dostępu |

EditContractors od etapu ADR-017 pozwala na dodawanie i edycję z CSRF i rowversion;
zapis wymaga także ViewContractors. Instrukcja: [kartoteka](contractors.md).
AcceptWarnings od ADR-020 pozwala świadomie potwierdzić ostrzeżenie podstawowej
kontroli rachunku, z audytem i zgodnym hashem; samo EditContractors nie wystarcza.
ADR-021 rozszerza ten sam mechanizm o identyfikatory; VerifyRegistries (16)
jest osobnym prawem do ręcznego sprawdzania rejestrów z CSRF. W B operator nie
może ich inicjować. Środowiska i konfiguracja: [rejestry](identifiers-and-registries.md).
Rola Identity nie daje dostępu do firmy bez przypisania CompanyAccess.

ADR-023 dodaje firmowe ViewExchangeRates=64, ManageCurrencies=128 i
ImportExchangeRates=256. Zapis konfiguracji i import wymagają także odczytu
oraz CSRF; konfiguracja używa rowversion. Rozstrzyganie konfliktów wymaga
obu praw zapisu i świadomego potwierdzenia. Maski demonstratora: operator A
511, B 67, czytelnik A 65. B i czytelnik nie mogą zapisywać ani importować
kursów również przez bezpośrednie API. [Obsługa walut](currencies-and-nbp.md).

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

Inicjalizacja wyłącznie brakujących kont z lokalnej konfiguracji:

```powershell
dotnet run --project tools/database/Vatowiec.DatabaseTools --no-build -- seed-users
```

Narzędzie używa UserManager/RoleManager, weryfikuje politykę haseł i nie wypisuje
ich wartości. Istniejące konto pomija w całości, zachowując hasło, role, prawa
i stan ustawiony przez administratora. Zmiana hasła w konfiguracji nie resetuje
konta. Początkowe przypisania z powyższej tabeli dotyczą wyłącznie nowych kont.
Reset hasła i edycja praw są jawne w module administracji ADR-033.

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
| GET /api/v1/companies/{id}/contractors | Wymaga ViewContractors; filtry q/country/active oraz offset/limit, maks. 100 pozycji; bez wyszukiwania PESEL |
| POST /api/v1/companies/{id}/contractors/search | ViewContractors i CSRF; filtry w ciele zamiast URL; PESEL tylko z ManageContractorPesel |
| GET /api/v1/companies/{id}/contractors/{contractorId} | Odczyt aktualnej wersji i rowversion; ViewContractors |
| POST /api/v1/companies/{id}/contractors | ViewContractors + EditContractors i CSRF; atomowy audyt utworzenia |
| PUT /api/v1/companies/{id}/contractors/{contractorId} | Te same prawa i CSRF, wymagane rowversion; 409 przy konflikcie |

Własny klient utrzymuje osobny magazyn cookie dla każdego użytkownika. Najpierw
pobiera /csrf, wysyła token w X-CSRF-TOKEN razem z JSON logowania, zachowuje cookie
sesji, a przed logout pobiera /csrf ponownie dla zalogowanej tożsamości.
JSON logowania nie przyjmuje CompanyId ani dodatkowych pól. Nie wysyłaj haseł
w URL. Serwer nie ma globalnego wyboru domyślnej firmy. CompanyId w lokalnych
preferencjach klienta ADR-032 nie zastępuje firmy w ścieżce ani autoryzacji API.

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
zmienia tabel firmowych. W tym historycznym etapie System miał dwa wpisy historii
EF, bazy firmowe po jednym. Po ADR-021 mają odpowiednio pięć i po trzy wpisy;
aktualny stan: [bazy](database-per-company.md).
Down usuwa konta, uprawnienia i sesje, więc nie należy go używać jako rollbacku z danymi.

Powtarzalne polecenia administracyjne:

```powershell
dotnet run --project tools/database/Vatowiec.DatabaseTools --no-build -- backup-system
dotnet run --project tools/database/Vatowiec.DatabaseTools --no-build -- migrate-system
dotnet run --project tools/database/Vatowiec.DatabaseTools --no-build -- seed-users
dotnet run --project tools/database/Vatowiec.DatabaseTools --no-build -- verify
```

Nie ma migracji ani seedowania kont przy starcie API. Nie wdrażamy hasła z repozytorium.

Pełne testy aktualnego modelu ADR-018/019/020 używają wyłącznie trzech lokalnych baz
demonstracyjnych. **Zatrzymaj API i desktop: runner czyści fikcyjne dane i sesje
przez DML, bez DROP/CREATE, a po teście inicjalizuje ponownie demonstrator i konta.** Nie tworzy *_Test.

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
Raporty TRX są w ignorowanym TestResults. Bieżący wynik to 180/180 bez pominięć
(76 SQL/API i 104 bez SQL); szczegóły: [kartoteka](contractors.md).
Audyt księgowy, backup/restore z aplikacji i pełne M0
nadal wymagają kolejnych etapów.

Wynik końcowy (2026-10-21): **70/70 testów zaliczonych**, w tym 38 bez SQL i 32
na rzeczywistym SQL/API, bez pominięć. Kompilacja ośmiu projektów: zero błędów
i ostrzeżeń. Formatowanie i git diff --check poprawne. Osobno wykonano verify-users
na rzeczywistym HTTPS localhost:7245 z domyślną walidacją TLS; oba konta i wszystkie
odmowy zachowały się zgodnie z tabelą. Nie zmieniano polityki PowerShell ani zaufania
certyfikatów. Po próbie wylogowano oba konta. Nie wykonano commit ani push.

ADR-030 dodaje firmowe **ManageContractorPesel=512**. ViewContractors jest nadal
wymagane do odczytu, EditContractors do zapisu, AcceptWarnings do odstępstwa.
Nie nadano nowego prawa automatycznie operator.m0 ani innym istniejącym kontom;
maski demonstratora pozostają bez zmian. Bez prawa odpowiedzi API pomijają PESEL,
edycja innych pól zachowuje jego wartość, a jawne próby zmiany dają 403.
Dotyczy także konfliktów, wyszukiwania i ponowienia operacji tworzenia.
Szczegóły: [standardowe identyfikatory](standard-identifiers.md).

ADR-031 udostępnia Finansowe przez istniejące **ViewContractors** (odczyt)
i dodatkowo **EditContractors** (zapis warunków, limitów i słownika płatności).
Zapis wymaga CSRF i rowversion; tworzenie ma trwałą idempotencję. Nie są wymagane
ManageCurrencies ani ViewExchangeRates do samego wyboru waluty w kartotece.
Nie nadano nowych praw i nie zmieniono przypisań demonstratora. Operator A może
edytować, operator B tylko przeglądać; konto z samym profilem nie czyta finansów.
[Instrukcja](contractor-finance.md).
