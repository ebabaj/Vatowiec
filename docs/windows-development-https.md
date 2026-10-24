# Dwa endpointy HTTPS lokalnego API Debug na Windows

Konfiguracja wdrożeniowa, zgodna z ADR-015/016 i ORG-003/008. Nie zmienia
Identity, sesji, CSRF, SQL ani CompanyAccess. Jeden proces API obsługuje
dotychczasowy loopback z certyfikatem deweloperskim i drugi, jawny adres LAN
z osobnym certyfikatem serwerowym. Nie jest to instalacja usługi produkcyjnej.

Rzeczywisty adres LAN, port, nazwa DNS i ścieżka PFX należą do lokalnej
konfiguracji tego komputera, **poza repozytorium**, nie do `appsettings.json`,
`launchSettings.json` ani kodu. Poniżej znaczniki w nawiasach `<...>` należy
zastąpić właściwymi wartościami. PFX z kluczem prywatnym nigdy nie trafia do Git,
paczek klienta ani logów; publiczny thumbprint/Subject służy kontroli certyfikatu.

## 1. Sprawdzenie przed zmianą

Sprawdź właściciela procesu i gniazda przez `Get-CimInstance Win32_Process`,
`Get-NetTCPConnection -State Listen`, rzeczywisty adres interfejsu przez
`Get-NetIPAddress` i profil przez `Get-NetConnectionProfile`. Nie zakładaj,
że prywatny zakres IP oznacza profil Private — Windows może mieć Public.
Zapisz thumbprint certyfikatu z działającego localhost przez zwykły, weryfikowany
handshake TLS, aby po restarcie porównać jego tożsamość.

PFX sprawdź bez importowania klucza do trwałego magazynu (EphemeralKeySet):
Subject/SAN, thumbprint, termin, `HasPrivateKey` oraz łańcuch Windows.
Bezhasłowy PFX nie wymaga fikcyjnego Password w konfiguracji. Nie wyłączaj
walidacji certyfikatów i nie dodawaj certyfikatu Let's Encrypt jako nowego
zaufanego root. Przy problemach popraw łańcuch/DNS/czas/system zaufania.

## 2. Uprawnienia PFX i zapora — podniesione uprawnienia

Uruchom **tylko krok zabezpieczeń** w PowerShell jako administrator, po
sprawdzeniu i zatwierdzeniu parametrów:

```powershell
& .\tools\deployment\Set-DevelopmentHttpsSecurity.ps1 `
  -PfxPath '<pełna ścieżka istniejącego PFX>' `
  -ApiAccount '<komputer\konto uruchamiające API>' `
  -ApiExecutable '<pełna ścieżka src\Server\Api\bin\Debug\net10.0\Vatowiec.Api.exe>' `
  -LocalAddress '<LAN-IP>' -Port <LAN-PORT> -RemoteSubnet '<dozwolony-IPv4-CIDR>'
```

Skrypt nie przenosi PFX. Ustawia chroniony ACL bez dziedziczenia: tylko konto API
z Read oraz grupa Administratorzy i SYSTEM z FullControl. Zachowuje poza repo
kopię metadanych wcześniejszego ACL (nie klucza) w
`%LOCALAPPDATA%\Vatowiec\Deployment`. Wpisy Everyone/Users/Authenticated Users
nie pozostają na pliku. Nie zmienia ACL całego dysku ani katalogów win-acme.

**Ten skrypt nie wpisuje ustawień Kestrela i nie uruchamia API.** Samo
zabezpieczenie PFX i dodanie reguły zapory nie tworzy listenera sieciowego.
Następnie trzeba wykonać krok 3 i sprawdzić rzeczywisty nasłuch po restarcie.

Reguła `Vatowiec-Development-LAN-HTTPS` obejmuje wyłącznie wybrany interfejs,
lokalny adres, port TCP, program API, dozwoloną podsieć i profil **Private**,
z wyłączonym edge traversal. Nie otwiera profilu Public/Domain, innych portów
ani SQL. Skrypt aktualizuje tylko własną regułę, bez usuwania obcych reguł.
Należy też sprawdzić, czy inne reguły nie dopuszczają szerszego ruchu.

Jeśli interfejs ma Public, krok domyślnie odmawia działania. Dopiero po
potwierdzeniu, że to zaufany LAN, dodaj **jawny** przełącznik
`-SetNetworkPrivate`. Zmiana profilu wpływa też na inne istniejące reguły
Windows, więc nie wykonuj jej na niezaufanej sieci. Nie poszerzaj reguły
VATowca do Public w celu obejścia tej kontroli.

win-acme powinien odnawiać/eksportować jako Administrator lub SYSTEM. Konto
API nie potrzebuje zapisu klucza. Jeżeli w przyszłości win-acme będzie działać
jako inne konto, osobno rozpatrz minimalny dostęp tego konta. Po zastąpieniu
pliku nowy plik może odziedziczyć zbyt szerokie ACL katalogu nadrzędnego: ponownie
zastosuj i sprawdź ACL przed uruchomieniem/przeładowaniem certyfikatu. Nie zmieniaj
ścieżki bez uzgodnienia z konfiguracją eksportu win-acme.

## 3. Lokalne user-secrets i kontrolowany restart

Kroki wykonaj jako **dotychczasowe konto API**, w zwykłym PowerShell.
Nie używaj user-secrets innego konta administratora. API używa istniejącego
`UserSecretsId=vatowiec-api-development`, wczytywanego w Development.

Najpierw zabezpiecz PFX. Następnie zatrzymaj zweryfikowany PID API, wykonaj
standardowy build Debug i wymagane testy, bez resetów SQL. Dopiero przy
zatrzymanym API zmień user-secrets: konfiguracja może być obserwowana i
przeładowywana przez Kestrel, więc nie zapisuj częściowej konfiguracji
do działającego serwera.

Dodaj scalająco poniższe klucze (np. JSON przez stdin do `dotnet user-secrets set
--project src/Server/Api`). Nie zastępuj całego pliku i nie wyświetlaj pozostałych
sekretów. W przykładzie `<...>` wymaga podmiany:

```json
{
  "Kestrel:Endpoints:LocalHttps:Url": "https://localhost:7243",
  "Kestrel:Endpoints:LocalHttp:Url": "http://localhost:5243",
  "Kestrel:Endpoints:LanHttps:Url": "https://<LAN-IP>:<LAN-PORT>",
  "Kestrel:Endpoints:LanHttps:Certificate:Path": "<PFX-PATH>",
  "AllowedHosts": "localhost;127.0.0.1;[::1];<DNS-NAME>",
  "HTTPS_PORT": "7243"
}
```

Jawne `Kestrel:Endpoints` zastępują `applicationUrl`/ASPNETCORE_URLS, dlatego
trzeba wymienić również localhost. Zachowano także dotychczasowy HTTP loopback
5243; API nadal odmawia operacji `/api/v1` przez HTTP. `HTTPS_PORT=7243` zachowuje
jednoznaczne lokalne przekierowanie middleware mimo dwóch portów HTTPS.
Nie zmieniamy ustawień rejestrów, SQL ani haseł demonstracyjnych.

Certyfikat jest przypisany **tylko do LanHttps**. Nie ustawiaj go globalnie
w `Kestrel:Certificates:Default`: localhost musi zachować certyfikat deweloperski
z dotychczasowego magazynu konta API. Nie dodawaj Password dla bezhasłowego PFX.
Nie przypisuj listenera do nazwy DNS: Kestrel traktuje zwykłe nazwy DNS jako
wildcard binding. Użyj konkretnego adresu IPv4 interfejsu LAN, a nazwę DNS
w AllowedHosts i w URL klienta.

Po zakończeniu kompilacji/testów uruchom jeden serwer w osobnym, widocznym
i trwałym terminalu PowerShell, z katalogu repozytorium:

```powershell
dotnet run --project src/Server/Api -c Debug --no-build --launch-profile https
```

Nie uruchamiaj API jako administrator. Nie wykonuj login/logout/resetu,
migracji ani czyszczenia sesji dla próby endpointów. Klucze Data Protection
i konto procesu pozostają te same. Restart nie jest operacją unieważnienia
istniejących sesji; nie usuwaj ich rekordów ani plików kluczy.

## 4. Weryfikacja

```powershell
& .\tools\deployment\Test-DevelopmentHttps.ps1 `
  -NetworkHost '<DNS-NAME>' -LocalAddress '<LAN-IP>' -NetworkPort <LAN-PORT> `
  -ExpectedThumbprint '<thumbprint zatwierdzonego certyfikatu LAN>' `
  -ExpectedLocalThumbprint '<thumbprint localhost sprzed zmiany>'
```

Skrypt sprawdza oba `/health` z normalną walidacją HTTPS, Subject i thumbprint,
zachowanie certyfikatu localhost, łańcuch/nazwę i revocation przez SslStream,
jednego właściciela obu portów, dokładny binding LAN i odmowę połączenia przez
pozostałe lokalne adresy. Weryfikuje również odrzucenie obcego HTTP Host przy
poprawnym SNI/TLS. Wynik: `TestResults/development-https.json`, ignorowany przez Git.
Nie pomija TLS i nie wywołuje logowania ani integracji produkcyjnych.

Osobno administrator sprawdza efektywną regułę i profil zapory. Próba z tego
samego komputera nie dowodzi dostępności z drugiego stanowiska ani blokady
z zewnętrznej sieci. Wymagana jest próba klienta z dozwolonego LAN, a następnie
z niedozwolonego źródła. Nie zmieniaj MikroTika, NAT, portów SQL ani routera
na potrzeby tego wdrożenia. Publiczny DNS sam w sobie nie oznacza udostępnienia
usługi: listener i zapora pozostają ograniczone do LAN.

## 5. Ręczne odnawianie DNS-01

Obecny sposób uzyskania certyfikatu to **ręczna walidacja DNS-01**. Samo zadanie
harmonogramu win-acme nie zapewnia odnowienia: manual DNS nie obsługuje trybu
bezobsługowego. Administrator musi przed terminem ważności uruchomić
interaktywnie istniejącą konfigurację odnowienia, dodać wymagany publiczny
rekord TXT `_acme-challenge` zgodnie z wyzwaniem i poczekać na propagację.
Nie odtwarzaj rekordu z wcześniejszego wyzwania i nie loguj tokenu DNS.

Nie uruchamiaj tu automatycznej próby odnowienia na produkcyjnym ACME.
W zaplanowanym oknie zatrzymaj API przed eksportem zastępującym używany PFX
(Kestrel może obserwować plik certyfikatu). W win-acme zachowaj istniejącą
konfigurację eksportu PFX i tę samą ścieżkę. Eksport obejmuje klucz i łańcuch;
nie rozsyłaj PFX do klientów. Zastosuj ACL po eksporcie, sprawdź nowy termin,
SAN/Subject i łańcuch, a po uruchomieniu API sprawdź certyfikat podawany przez
endpoint. **Thumbprint po odnowieniu będzie inny** i trzeba użyć nowej,
sprawdzonej wartości w teście. localhost nadal powinien używać swojego certyfikatu.

Instrukcje źródłowe: [endpointy Kestrela](https://learn.microsoft.com/aspnet/core/fundamentals/servers/kestrel/endpoints),
[reguła Windows Firewall](https://learn.microsoft.com/powershell/module/netsecurity/new-netfirewallrule),
[win-acme: manual DNS](https://www.win-acme.com/reference/plugins/validation/dns/manual),
[win-acme: eksport PFX](https://www.win-acme.com/reference/plugins/store/pfxfile).

## Weryfikacja lokalnego wdrożenia — 2026-10-24

Przyczyną początkowego braku połączenia z LAN był brak `Kestrel:Endpoints`
w aktywnych user-secrets. Zabezpieczenia PFX i reguła zapory były już wykonane,
ale dotychczasowy proces słuchał wyłącznie na loopback. Zatrzymano zweryfikowany
proces, wykonano standardowy build Debug (0 błędów, 0 ostrzeżeń), scalono sześć
kluczy z kroku 3 z zachowaniem pozostałych sekretów i uruchomiono jedno API
w widocznym, trwałym terminalu PowerShell. Kod i publiczne konfiguracje API
nie były modyfikowane.

Test `Test-DevelopmentHttps.ps1`: **15/15 kontroli poprawnych**. Oba HTTPS
`/health` zwróciły 200 z normalną walidacją certyfikatów. Potwierdzono zachowanie
certyfikatu localhost, zgodny Subject/Thumbprint i zaufany certyfikat LAN,
jednego właściciela portów, binding LAN tylko na zadanym adresie IPv4,
brak połączenia na 7443 przez IPv4/IPv6 loopback i pozostałe lokalne adresy
oraz HTTP 400 dla niedozwolonego Host przy poprawnym TLS.
Szczegółowe, lokalne adresy, PID i publiczne metadane certyfikatów są w
`TestResults/development-https.json`, poza śledzeniem Git.

Zachowano także dotychczasowy HTTP loopback 5243. Nie wykonywano operacji SQL,
logowania/wylogowania, resetowania ani unieważniania sesji. Nie zmieniano routera,
NAT, portów SQL ani konfiguracji integracji. Testy z drugiego stanowiska i ze
źródła spoza dozwolonego LAN pozostają osobną próbą administratora.
