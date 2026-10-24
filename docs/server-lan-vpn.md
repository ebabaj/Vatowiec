# Serwer API dla drugiego stanowiska Windows

Rozwinięcie dla dodatkowego listenera w lokalnym Development, bez utraty
dotychczasowego localhost: [dwa endpointy HTTPS, ACL PFX, zapora i ręczne odnowienie](windows-development-https.md).

Ta instrukcja opisuje osobny etap wdrożenia administratora. Budowanie klienta
nie wykonuje tych zmian, nie otwiera portów i nie migruje SQL. Nie należy
udostępniać obecnego Development bezpośrednio w Internecie.

## Stan sprawdzony przy przygotowaniu preview

Profil `src/Server/Api/Properties/launchSettings.json` uruchamia Development na
`https://localhost:7243` i `http://localhost:5243`. Bazowe `appsettings.json`
dopuszcza `localhost;127.0.0.1;[::1]` przez AllowedHosts; połączenie System poza
Development jest puste. Klient wymaga HTTPS. Lokalny certyfikat developerski
nie jest certyfikatem dla innej nazwy serwera. Samo zainstalowanie klienta na
drugim komputerze nie zapewni dostępu do obecnego listenera loopback.

## Przygotowanie LAN lub VPN

1. Ustal stabilny adres serwera i nazwę DNS rozwiązywaną przez stanowiska LAN/VPN.
   Dalej `vatowiec.example.local` to **przykład**, który trzeba zastąpić własną
   nazwą. Sprawdź rozwiązywanie DNS na obu komputerach. Nie używaj `localhost`
   na kliencie ani nazwy bez zgodnego certyfikatu.
2. Przygotuj certyfikat serwerowy z kluczem prywatnym na serwerze, poprawnym SAN
   dla rzeczywistej nazwy, ważnym okresem i przeznaczeniem TLS Server
   Authentication. Wszystkie stanowiska muszą ufać jego łańcuchowi, np. przez
   zarządzane wewnętrzne CA. Dystrybuuj zaufany certyfikat CA, nie prywatny klucz
   API ani developerski PFX. Chroń plik/klucz ACL konta uruchamiającego API.
3. Zaplanuj zatrzymanie lokalnej instancji przed uruchomieniem sieciowej.
   Jeden proces API obsługuje instalację (także wspólną bramkę GUS).
   W osobnej, chronionej konfiguracji wdrożenia ustaw HTTPS Kestrela na **adresie
   interfejsu LAN/VPN serwera**, porcie 7243, z przygotowanym certyfikatem.
   Nie polegaj na `launchSettings.json` ani certyfikacie developerskim w docelowym
   wdrożeniu. Można użyć ustawień `Kestrel:Endpoints:Https:Url` oraz
   `Kestrel:Endpoints:Https:Certificate:Path`/`Password` (lub magazynu certyfikatów).
   Dane klucza/hasło konfiguruje administrator poza repozytorium i paczką klienta.
   Nie wpisuj ich do instrukcji, logów ani skryptów wydania.
4. Ustaw `AllowedHosts` na rzeczywistą nazwę DNS (bez schematu, portu i ścieżki),
   ewentualnie jawne inne używane nazwy. Nie stosuj `*` jako obejścia. Listener
   i AllowedHosts to oddzielne ustawienia; AllowedHosts nie otwiera portu.
5. Dodaj świadomie regułę przychodzącą Windows Firewall tylko dla TCP 7243,
   odpowiedniego profilu Private/Domain, właściwego interfejsu i konkretnych
   adresów/podsieci klientów LAN lub VPN. Nie twórz ogólnej reguły dla Public,
   nie przekierowuj routera do Internetu. Klient łączy się tylko z API: nie
   otwieraj dla niego portów SQL/SQL Browser ani udziałów z bazami.
6. API nadal korzysta z istniejącego `ConnectionStrings:VatowiecSystem` i
   autoryzowanego katalogu połączeń firm. Konfigurację serwera zachowaj w
   chronionym środowisku uruchomieniowym; niczego nie kopiuj do klienta.
   Przy Windows Authentication SQL ocenia konto procesu API. Zmiana konta
   wymaga osobnego nadania minimalnego dostępu i sprawdzenia istniejących
   kluczy Data Protection/DPAPI dla GUS/DataPort. Zachowaj ich backup i kontekst
   ochrony; nie zastępuj kluczy ani nie resetuj sekretów dla testu sieci.
7. Sprawdź z drugiego stanowiska TLS i `https://<rzeczywisty-DNS>:7243/health`,
   następnie login w zainstalowanym kliencie, aktualne firmy i kontrolę odmowy
   dostępu. `health` potwierdza żywotność HTTP, **nie** pełną zgodność SQL ani
   uprawnienia użytkownika. Sprawdź też brak dostępu spoza dozwolonej sieci.

Nie wyłączaj walidacji TLS, CSRF, cookies HttpOnly/Secure, blokad kont,
unieważniania sesji ani serwerowej autoryzacji. CORS nie jest potrzebny do
natywnego klienta HttpClient. Nie dodawaj publicznego endpointu logowania
omijającego Identity. Przy błędzie zaufania napraw certyfikat/DNS, zamiast
wprowadzać wyjątek w kliencie.

## Usługa serwera w następnym etapie

Obecny proces uruchamiany przez `dotnet run` w widocznym terminalu to tryb
deweloperski. Docelowa usługa Windows wymaga osobnego pakietu serwera, integracji
cyklu życia z Service Control Manager, tożsamości usługi, ACL, trwałego katalogu
kluczy, bezpiecznej konfiguracji, logów, restartu i testów backup/restore.
Aktualne zadanie nie instaluje usługi ani SQL i nie zakłada, że sama rejestracja
obecnego EXE przez `sc.exe` stanowi gotowe wdrożenie.

Punkty odniesienia: [konfiguracja HTTPS Kestrela](https://learn.microsoft.com/aspnet/core/fundamentals/servers/kestrel/endpoints),
[filtrowanie hostów](https://learn.microsoft.com/aspnet/core/fundamentals/servers/kestrel/host-filtering),
[uwierzytelnianie VATowca](authentication.md) i [izolacja baz](database-per-company.md).
