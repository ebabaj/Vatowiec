# ADR-015: Identity, sesje API i autoryzowany wybór bazy firmy

Data: 2026-10-21  
Status: przyjęty w zakresie zleconego etapu M0.  
Wymagania: ORG-003, ORG-004, ORG-008, T-012; SPECYFIKACJA §3, §8, §11.1, M0.  
Uzupełnia ADR-014; nie zmienia fizycznego rozdzielenia baz.

## Decyzja i model

ASP.NET Core Identity 10.0.13 działa w SystemDbContext jako
IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>. Hasła obsługuje
UserManager i standardowy PasswordHasher Identity; nie implementujemy własnej
kryptografii haseł. Użytkownik ma niepowtarzany Guid. Tabele security.Users,
Roles, UserRoles, UserClaims, UserLogins, UserTokens i RoleClaims należą wyłącznie
do Vatowiec_System. Klucze dostawców loginów/tokenów mają długość 128 znaków.

security.CompanyAccess ma PK (UserId, CompanyId), lokalne FK do centralnego
użytkownika i katalogu firm, rowversion oraz uprawnienia bitowe ViewProfile,
ViewContractors i EditContractors. Brak przypisania oznacza odmowę. Rola globalna
Identity nie nadaje automatycznie praw do żadnej firmy. W tym etapie API udostępnia
tylko odczyt profilu i kartoteki; EditContractors jest kontraktem na przyszłość.
Nie dodajemy edycji księgowości, rejestracji publicznej ani zarządzania kontami UI.

security.UserSessions przechowuje identyfikator sesji, użytkownika, datę
utworzenia, absolutne wygaśnięcie, unieważnienie i rowversion. To centralne dane
bezpieczeństwa, nie historia księgowa. Przyszły audyt księgowy i historyczny
opis aktorów nadal należą do baz firmowych, bez FK do centralnego użytkownika.

## Sesja i logowanie

API używa chronionego przez ASP.NET Core Data Protection ciasteczka aplikacji
Identity: __Host-Vatowiec.Session, Secure, HttpOnly, SameSite=Strict, Path=/,
bez Domain. Cookie zawiera identyfikator centralnej sesji, nigdy połączenie SQL.
Nie jest utrwalane w przeglądarce jako remember-me. Sesja ma domyślnie absolutne
30 minut, bez przesuwania terminu; konfiguracja Authentication:SessionMinutes
jest ograniczona do 1–120 minut. TimeProvider umożliwia testowanie bez czekania.

Przy każdym uwierzytelnionym żądaniu serwer sprawdza konto, SecurityStamp,
blokadę użytkownika i ważność centralnej sesji. Brak katalogu nie daje ważnej
tożsamości. Logout unieważnia konkretną sesję w SQL przed usunięciem cookie;
odtworzenie starego cookie nie przywraca dostępu. Inne sesje mogą działać dalej.
Zmiana hasła/stempla lub usunięcie konta unieważnia stare uwierzytelnienie.
Konto wymagające drugiego składnika nie otrzymuje sesji na podstawie samego
hasła; dopóki nie ma osobnego przepływu MFA, logowanie takiego konta jest odrzucane.

POST login i logout wymagają tokenu antiforgery w X-CSRF-TOKEN, pobranego razem
z cookie antiforgery przez GET /api/v1/auth/csrf. Po zalogowaniu trzeba pobrać nowy
token, związany z uwierzytelnionym użytkownikiem. Cookie antiforgery również jest
Secure/HttpOnly/SameSite=Strict. HTTPS jest wymagane dla API. Nie konfigurujemy
CORS ani logowania przez parametry URL. JSON odrzuca nieznane pola.

Hasła mają co najmniej 12 znaków, cyfrę, małą i wielką literę oraz znak specjalny.
Pięć błędnych prób blokuje konto na pięć minut; limit login to 20 żądań/minutę
na adres IP, bez kolejki. Błędne, nieznane i zablokowane konto daje tę samą
odpowiedź 401; nieznane konto wykonuje porównanie z fikcyjnym hashem, aby ograniczyć
różnicę czasu. Nie logujemy haseł, cookie, tokenów ani connection stringów.
Zdarzenia logowania/wylogowania i odmów mają strukturalne logi z identyfikatorem
aktora, sesji/firmy i korelacji, bez treści danych firmowych. UserSessions daje
trwały stan sesji; nie zastępuje przyszłego audytu biznesowego i jego retencji.

## Resolver i granice żądania

Jedynym źródłem CompanyId dla operacji jest ścieżka
/api/v1/companies/{companyId}/... . Query companyId i nagłówek X-Company-Id
są odrzucane; ich wartość nie może przestawić kontekstu. Id pusty jest błędem.
Tożsamość użytkownika pochodzi z chronionej sesji, nie z treści żądania.

Resolver dla każdej operacji:

1. Sprawdza aktualny CompanyAccess i wymagane uprawnienie oraz aktywność firmy.
2. Odczytuje lokalizację z centralnego katalogu, bez domyślnej firmy/bazy.
3. Dobiera jawny serwerowy CompanyConnections:{ConnectionProfileKey}. Serwer
   profilu musi być zgodny z katalogiem; hasła SQL, jeśli później potrzebne,
   pozostają w konfiguracji sekretów serwera. Nie ufamy connection stringowi klienta.
4. Buduje nowe połączenie z katalogiem wskazanej firmy i waliduje politykę SQL.
5. Tworzy nowy CompanyDbContext z niezmiennym CompanyId i odczytuje rzeczywisty
   CompanyProfile poza filtrem. Musi istnieć dokładnie jeden, ze zgodnym CompanyId.
6. Porównuje fizyczną historię migracji z obsługiwanym zestawem, a dopiero potem
   udostępnia dane biznesowe. Zwalnia kontekst po operacji.

Brak uprawnienia/nieaktywna lub nieznana firma: 403. Brak konfiguracji, błąd SQL,
obcy profil lub nieobsługiwany schemat: 503 z kodem biznesowym, bez surowych
wyjątków i szczegółów połączenia. Brak/wygaśnięcie sesji: 401. Błędny CSRF,
JSON i alternatywne CompanyId: 400. Limit login: 429. Nie ma fallbacku.

Resolver jest scoped i tworzy odrębny kontekst na operację. Nie przechowuje
globalnej aktywnej firmy. Kilku operatorów może równolegle korzystać z tej samej
bazy przez niezależne konteksty. Dotychczasowe bezpośrednie uzyskanie CompanyDbContext
z DI bez jawnego kontekstu i wyboru nadal jest odrzucane. W API ścieżką dostępu
do danych jest resolver, nie konfiguracja testowego CompanyContext.

## Migracja i demonstrator

Nowa migracja IdentityAndCompanyAccess jest dodatkiem do historii systemowej;
nie zmienia ani nie przenosi tabel firmowych. Dodaje dziewięć tabel security,
FK, indeksy i CHECK. Wykonuje się jawnie po przeglądzie SQL i kopii bazy,
poleceniem narzędzia migrate-system. API nigdy nie wywołuje Migrate/EnsureCreated.
Down usuwa dane kont, przypisań i sesji, więc nie jest bezpiecznym rollbackiem
używanego systemu; zachowujemy kopię. Starsza aplikacja nie korzysta z nowych
tabel, ale powrót wersji nie może oznaczać pominięcia nowych zabezpieczeń.

seed-users używa UserManager/RoleManager i lokalnych DemoUsers:Operator/Reader:
UserName/Password z user-secrets API albo środowiska. Nie ma haseł w kodzie,
appsettings ani repozytorium. User-secrets to plik Development poza repozytorium,
nie produkcyjny szyfrowany sejf. Dwa konta mają odrębne hasła i role Operator/Reader.
Operator ma uprawnienia 7 w A i 3 w B; Reader ma tylko ViewProfile w A. Hasło
zmienione w konfiguracji jest ustawiane ponownie przez Identity, ze zmianą stempla.

Instrukcja i ręczna próba dwóch sesji: [logowanie](../authentication.md).

## Weryfikacja

Testy na dedykowanych bazach *_Test obejmują pełny HTTP login/CSRF/cookie,
role i widoczność firm, odmowę obcej firmy i czynności, fizyczne dane obu baz,
manipulację CompanyId, obcy profil, brak rejestracji/profilu połączenia,
zakaz bazy systemowej jako firmowej, cofnięcie uprawnień, SecurityStamp,
wygaśnięcie biletu i sesji SQL, logout i replay cookie, blokadę logowania,
HTTPS i równoczesne żądania dwóch użytkowników. Zegara testowego nie konfiguruje
się z publicznego API. SQL fixture są serializowane i sprzątane; współbieżność
dwóch klientów jest osobnym testem wewnątrz tej grupy.

Nie implementujemy jeszcze logowania Avalonia, audytu księgowego, funkcji
podatkowych, automatycznego tworzenia firm, backup/restore w UI ani kolejki zadań.

## Źródła techniczne

- [ASP.NET Core Identity](https://learn.microsoft.com/aspnet/core/security/authentication/identity?view=aspnetcore-10.0)
- [Cookie authentication](https://learn.microsoft.com/aspnet/core/security/authentication/cookie?view=aspnetcore-10.0)
- [Antiforgery](https://learn.microsoft.com/aspnet/core/security/anti-request-forgery?view=aspnetcore-10.0)
