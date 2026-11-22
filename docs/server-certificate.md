# Certyfikat serwera API w sieci prywatnej (ADR-047)

Decyzja: [ADR-047](adr/ADR-047-certyfikat-serwera-w-sieci-prywatnej.md). Dotyczy wariantu
Network; Personal nie używa TLS (Named Pipe, ADR-039).

Stacje łączą się z API wyłącznie przez HTTPS z pełną walidacją certyfikatu. Program
obsługuje dwa tryby:

| Tryb | Dla kogo | Zaufanie na stacji |
|---|---|---|
| **B — CA instalacji** (domyślny) | sieć bez własnego CA, mikrofirma | kod albo plik połączenia, przypięty dla dokładnego adresu API |
| **A — certyfikat własny** | firmowe CA (AD CS), certyfikat publiczny, Tailscale `*.ts.net` | standardowe zaufanie Windows |

Program **nigdy** nie instaluje CA instalacji w magazynie „Zaufane główne urzędy” i nie
należy robić tego ręcznie. Nie ma TOFU ani przycisku „zaakceptuj mimo to”.

## Jak to działa (tryb B)

1. Usługa **VATowiec Operations** przy pierwszym uruchomieniu (po migracji System) tworzy
   CA instalacji (ECDSA P-256, 10 lat) i certyfikat serwera (1 rok). Certyfikat serwera jest
   odnawiany automatycznie 30 dni przed końcem ważności oraz po zmianie nazw lub adresów.
2. Certyfikat CA podpisuje jednorazowy klucz startowy, niszczony od razu po podpisie.
   Dzięki temu serwer wysyła CA w uzgadnianiu TLS jako certyfikat pośredni, a stacja z samym
   kodem połączenia może zbudować łańcuch. O zaufaniu decyduje wyłącznie przypięty skrót
   klucza CA (SHA-256 z SubjectPublicKeyInfo), nie podpis startowy.
3. Kod połączenia `VTW1-…` to pierwsze 128 bitów tego skrótu w Crockford base32 ze znakiem
   kontrolnym (literówka jest wykrywana przed połączeniem). Plik `*.vatowiec-polaczenie`
   zawiera adresy API, pełny skrót i certyfikat CA (bez klucza). Oba mają tę samą moc
   i muszą dotrzeć do stacji autentycznym kanałem (osobiście, wydruk, pendrive).
4. Klient sprawdza: zgodność nazwy hosta z SAN (bez symboli wieloznacznych i CN), terminy
   ważności, podpisy, EKU „Uwierzytelnianie serwera”, a ostatni element łańcucha musi mieć
   przypięty skrót. Jeśli Windows sam ufa certyfikatowi, wystarcza walidacja standardowa.

## Konfiguracja serwera

Klucze konfiguracji (poza repozytorium: user-secrets w Development, plik/zmienne usługi
w produkcji):

| Klucz | Proces | Znaczenie |
|---|---|---|
| `ServerTls:Endpoints` | API | nazwy endpointów `Kestrel:Endpoints:*`, które podają certyfikat zarządzany przez VATowiec; endpoint nie może mieć własnej sekcji `Certificate` |
| `ServerTls:Root` | API i Operations | katalog plików; domyślnie `%ProgramData%\Vatowiec\Server\Tls` |
| `ServerTls:ApiAccount` | Operations | konto procesu API (np. `NT SERVICE\VatowiecApi`, `DOMENA\konto`) z prawem odczytu `server\` |
| `ServerTls:PublicPort` | Operations | port w adresach pliku połączenia zapisywanego na dysku serwera; domyślnie 7443 |

Przykład (Development, scalająco do user-secrets API, przy zatrzymanym API):

```json
{
  "Kestrel:Endpoints:LocalHttps:Url": "https://localhost:7243",
  "Kestrel:Endpoints:LocalHttp:Url": "http://localhost:5243",
  "Kestrel:Endpoints:LanHttps:Url": "https://<LAN-IP>:7443",
  "ServerTls:Endpoints:0": "LanHttps",
  "AllowedHosts": "localhost;127.0.0.1;[::1];<nazwa-serwera>;<FQDN>;<LAN-IP>",
  "HTTPS_PORT": "7243"
}
```

Endpoint `LanHttps` **nie** może mieć `Certificate:Path` — przy próbie API odmawia startu
z czytelnym komunikatem. Endpointy niewymienione (np. localhost z certyfikatem
deweloperskim) działają jak dotąd. API odczytuje tryb z bazy System co 30 s; odnowienie
i zmiana trybu nie wymagają restartu.

### Katalog plików i uprawnienia

Usługa Operations zakłada katalogi z chronionym ACL (bez dziedziczenia):

| Katalog | Zawartość | Dostęp |
|---|---|---|
| `ca\` | `installation-ca.cer`, klucz `installation-ca.key.protected` (PKCS#8 chroniony Data Protection + DPAPI konta usługi), `keyring\`, `retired\` | konto usługi Operations, SYSTEM, Administratorzy |
| `server\` | `server-current.pfx` (certyfikat serwera z kluczem + certyfikat CA) | jak wyżej + odczyt dla `ServerTls:ApiAccount` |
| `connection\` | `VATowiec.vatowiec-polaczenie`, `kod-polaczenia.txt` (dane publiczne) | jak wyżej + odczyt dla Użytkowników |

Klucz CA jest związany z profilem konta usługi Operations (DPAPI). Konto API nie ma do niego
dostępu. W środowisku developerskim API i Operations działają na tym samym koncie, więc ta
separacja nie jest tam egzekwowana — dotyczy wdrożenia z osobnymi kontami usług.

Kopia katalogu `ServerTls:Root` bez profilu konta usługi nie odtworzy klucza CA. Utrata klucza
oznacza wymianę CA i nowy kod dla wszystkich stacji (usługa nie tworzy po cichu nowego CA,
gdy baza zna poprzednie — zgłasza `tls_ca_missing`).

## Administracja (Avalonia)

**Konto → Ustawienia instalacji → Bezpieczeństwo → Certyfikat serwera** (wyłącznie
administrator instalacji; zapis z CSRF, kontrolą wersji i niezmiennym audytem
`audit.ServerTlsChanges`):

- kod połączenia, terminy ważności CA i certyfikatu serwera, certyfikat faktycznie podawany
  przez API, ostrzeżenia (m.in. 180 dni przed końcem ważności CA);
- tryb, nazwy DNS i adresy IP (tryb B: puste nazwy = nazwa komputera i FQDN; zawsze także
  `localhost`, `127.0.0.1`, `::1`), w trybie A opcjonalny odcisk wystawcy;
- **Zapisz plik połączenia…** — plik dla stacji (adres, pod którym administrator jest
  połączony, oraz skonfigurowane nazwy z tym samym portem);
- **Wymień CA instalacji…** — awaryjnie (kradzież serwera, podejrzenie wycieku klucza) albo
  przed końcem ważności CA. Po wykonaniu każda stacja potrzebuje nowego kodu lub pliku.
  Planowa rotacja bez ponownego parowania nie wchodzi do pierwszego wydania.

Tryb A: przy zapisie API (konto, które musi mieć dostęp do klucza) wyszukuje w
`LocalMachine\My` najnowszy ważny certyfikat zawierający wszystkie podane nazwy, z EKU
„Uwierzytelnianie serwera” i dostępnym kluczem; bez takiego certyfikatu zapis jest odrzucany
(`tls_own_not_found`, `tls_own_key_inaccessible`, `tls_own_eku_missing`, `tls_own_expired`,
`tls_own_issuer_mismatch`). Automatyczne odnowienie przez AD CS lub narzędzie ACME
zapisujące do magazynu nie wymaga zmian. Źródło PFX (serwer Linux) nie jest jeszcze wdrożone.

## Parowanie stacji

Ekran logowania → **Ustawienia połączenia**:

1. Wpisz adres HTTPS serwera, np. `https://celina.tenjin.local:7443`.
2. Wpisz kod połączenia i wybierz **Zapisz kod dla tego adresu** albo wybierz **Otwórz plik
   połączenia…** (ustawia też adres).
3. Zaloguj się. Status nad polem kodu pokazuje, czy dla adresu obowiązuje CA instalacji czy
   standardowe zaufanie Windows.

Kotwica dotyczy dokładnie tego originu (schemat, nazwa, port) i jest zapisywana w
`%LOCALAPPDATA%\Vatowiec\Desktop\server-trust.json` (dane publiczne). Inna nazwa tego samego
serwera (np. adres IP zamiast nazwy) wymaga osobnego wpisu i musi być w SAN certyfikatu.
**Usuń zaufanie dla adresu** przywraca walidację standardową.

## Odzyskiwanie i diagnostyka

Na serwerze, w katalogu usługi Operations (konfiguracja jak usługi):

```powershell
Vatowiec.Operations.exe tls status         # tryb, kod połączenia, terminy, ostatni błąd
Vatowiec.Operations.exe tls reset-mode-b   # jako administrator Windows: powrót do CA instalacji
```

`reset-mode-b` służy do wyjścia z błędnej konfiguracji trybu A, która odcięła stacje; zapisuje
wpis audytu `LocalRecovery`. Nie ma zdalnej ścieżki obejścia. W Development:
`dotnet run --project src/Server/Operations -c Debug --no-build --launch-profile Development -- tls status`.

Kody błędów stanu usługi: `tls_ca_missing`, `tls_ca_mismatch`, `tls_ca_key_unreadable`
(np. inne konto usługi niż przy tworzeniu CA), `tls_api_account_invalid`,
`tls_storage_access_denied`, `tls_storage_error`, `tls_installation_missing`.

## Wdrożenie i migracja

Migracja System `20261122180338_ServerTlsCertificate` (addytywna): `security.ServerTlsConfiguration`
(jeden wiersz, domyślnie tryb B), `security.ServerTlsState` (zapisuje tylko Operations),
`audit.ServerTlsChanges` z triggerem niezmienności. Wykonanie jawne:
`dotnet run --project tools/database/Vatowiec.DatabaseTools -c Debug -- migrate-system`.
Bez migracji API i Operations działają jak dotąd (Operations pomija tę część, zlecenia firm
są wykonywane normalnie).

## Testy

- `ServerTlsTests` — kod i plik połączenia, walidacja z kotwicą i wszystkie przypadki
  negatywne ADR-047, wybór certyfikatu własnego, magazyn kotwic klienta, rzeczywiste
  uzgadnianie TLS z Kestrelem (para klient–serwer, odmowa bez kotwicy, z obcą kotwicą,
  pod adresem spoza SAN, odnowienie bez restartu).
- `ServerTlsSqlTests` (`VATOWIEC_PROVISIONING_SQL=1`, wycofywana transakcja na
  `Vatowiec_System`) — uprawnienia, CSRF, wersja, audyt, uzgadnianie stanu w Operations
  (utworzenie, brak zmian, zmiana nazw, odnowienie, utrata plików CA, wymiana CA) i lokalne
  odzyskiwanie.
