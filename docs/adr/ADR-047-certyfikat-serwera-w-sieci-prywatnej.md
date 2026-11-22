# ADR-047: certyfikat serwera API i zaufanie klientów w sieci prywatnej

Data: 2026-11-22. **Status: PRZYJĘTY — akceptacja właściciela 2026-11-22; WDROŻONY 2026-11-22**
(kroki 1–3 i 5; rotacja planowa poza pierwszym wydaniem zgodnie z decyzją 3; szczegóły
i odstępstwa techniczne w sekcji „Wdrożenie”). Instrukcja: [docs/server-certificate.md](../server-certificate.md).
Łącznie z rozstrzygnięciem kwestii 1–3 zgodnie z propozycją (sekcja na końcu). ADR-047
uzupełnia ADR-016: klient stosuje standardową walidację certyfikatu systemu albo
walidację z kotwicą CA instalacji według tego ADR, nigdy walidację z pominięciem.

Dotyczy: SPECYFIKACJA §3.3 (HTTPS między klientem i serwerem), ORG-003/008,
ADR-015/016 (transport klienta), ADR-044 (usługa VATowiec Operations),
[docs/server-lan-vpn.md](../server-lan-vpn.md). Nie dotyczy Personal (ADR-039:
Named Pipe, bez TLS) ani certyfikatów do podpisu deklaracji i KSeF (ADR-006).

## Kontekst

Wariant Network wymaga HTTPS ze standardową walidacją certyfikatu (ADR-016).
Próba w sieci lokalnej (2026-11-22) pokazała, że uzyskanie certyfikatu, któremu
stacje ufają bez dodatkowych kroków, jest dla mikrofirmy praktycznie nieosiągalne:

1. Publiczne urzędy certyfikacji nie wystawiają certyfikatów dla nazw wewnętrznych
   (`celina.tenjin.local`, `*.internal`, `*.home.arpa`, nazwy bez domeny, prywatne IP).
   Wynika to z wymagań CA/Browser Forum i nie da się tego obejść konfiguracją.
2. Let's Encrypt jest możliwy tylko dla subdomeny własnej domeny publicznej z
   weryfikacją DNS-01. Wymaga to domeny, dostępu do DNS przez API i automatycznego
   odnawiania. Maksymalna ważność publicznych certyfikatów maleje (200 dni od 2026 r.,
   docelowo 47 dni), więc ręczna obsługa przestaje być realna.
3. Firmy z domeną Windows mają zwykle własne CA (AD CS) z automatycznym wystawianiem
   certyfikatów i dystrybucją zaufania przez zasady grupy. Program powinien umieć
   użyć takiego certyfikatu.
4. Najprostsze „rozwiązania” spotykane w praktyce są niebezpieczne:
   - wyłączenie walidacji w kliencie — zakazane (AGENTS, ADR-016);
   - zainstalowanie własnego głównego CA w magazynie „Zaufane główne urzędy” każdej
     stacji — CA staje się zaufane dla **całego** HTTPS na komputerze (bankowość,
     poczta); wyciek klucza z serwera mikrofirmy umożliwia podsłuch wszystkiego.
     Wymaga też praw administratora na stacjach, a klient instaluje się per-user
     (ADR-036);
   - zaufanie przy pierwszym połączeniu (TOFU) z oknem „czy ufasz temu odciskowi?” —
     użytkownicy zatwierdzają je bez porównania.

## Decyzja

Serwer Network obsługuje dwa tryby certyfikatu. Wybór trybu należy do administratora
instalacji. Oba tryby zachowują pełną walidację po stronie klienta; różnią się
wyłącznie kotwicą zaufania.

### 1. Tryb A — certyfikat własny (firmowe CA, publiczny, uzyskany zewnętrznie)

- Źródłem jest magazyn `LocalMachine\My` albo plik PFX (PFX głównie dla przyszłego
  serwera Linux). Hasło PFX jest sekretem: przechowywane jak inne sekrety instalacji
  (Data Protection + DPAPI, osobny purpose), nigdy w `appsettings.json`, repozytorium
  ani logach.
- Certyfikat w magazynie wskazuje się **regułą wyboru**, nie samym odciskiem:
  wymagane nazwy SAN i opcjonalnie odcisk wystawcy. Serwer wybiera najnowszy ważny
  certyfikat spełniający regułę i mający dostępny klucz prywatny. Dzięki temu
  automatyczne odnowienie przez AD CS lub zewnętrzne narzędzie ACME (np. win-acme
  zapisujące do magazynu) nie wymaga zmiany konfiguracji.
- Kontrola przy konfiguracji i przy starcie: każda skonfigurowana nazwa/IP jest
  w SAN, EKU zawiera Server Authentication, klucz prywatny jest dostępny dla konta
  API, termin ważności (ostrzeżenie poniżej 30 dni). Brak zaufanego łańcucha na samym
  serwerze to ostrzeżenie, nie błąd — o zaufaniu decydują stacje.
- Klient: standardowa walidacja systemu operacyjnego, dokładnie jak w ADR-016.

### 2. Tryb B — CA instalacji (domyślny dla sieci bez własnego CA)

**Urząd i certyfikat serwera**

- Usługa VATowiec Operations (ADR-044) tworzy jednorazowo **CA instalacji**:
  ECDSA P-256, ważność 10 lat, `BasicConstraints CA=true, pathLen=0`,
  KeyUsage `keyCertSign, cRLSign`. Klucz prywatny jest dostępny wyłącznie dla konta
  usługi Operations (we wdrożeniu: Data Protection + DPAPI konta usługi zamiast CNG
  `LocalMachine`, sekcja „Wdrożenie”, pkt 2). Konto API nie ma do niego dostępu.
- CA instalacji wystawia **certyfikat serwera API**: ECDSA P-256, ważność 1 rok,
  EKU Server Authentication. SAN zawiera skonfigurowane nazwy DNS (krótką i FQDN),
  adresy IP z adresów nasłuchu oraz `localhost`, `127.0.0.1` i `::1` (pierwsze
  połączenie administratora na samym serwerze). Klucz certyfikatu serwera jest
  dostępny dla konta API (odczyt), nie dla zwykłych użytkowników.
- Operations odnawia certyfikat serwera automatycznie na 30 dni przed wygaśnięciem
  i przy zmianie nazw/adresów. Klienci przypinają CA, nie certyfikat serwera, więc
  odnowienie nie wymaga żadnej czynności na stacjach.
- CA **nie** ma rozszerzenia Name Constraints. Przypięcie jest ograniczone do adresu
  API w kliencie, a program nigdy nie instaluje CA w systemowym magazynie zaufania;
  Name Constraints wymuszałyby wymianę CA i ponowne parowanie przy każdej nowej nazwie
  serwera (np. dodaniu VPN). Dokumentacja wprost odradza ręczne dodawanie CA
  instalacji do „Zaufanych głównych urzędów”; firma, która chce zaufania systemowego,
  używa trybu A.
- Brak list CRL/OCSP (`RevocationMode.NoCheck` w kliencie dla tej kotwicy).
  Kompromitacja serwera jest obsługiwana wymianą CA (pkt 4), a krótka ważność
  certyfikatu serwera ogranicza skutki.

**Kod i plik połączenia**

- Tożsamością instalacji dla klienta jest skrót SHA-256 z `SubjectPublicKeyInfo` CA
  instalacji. Nie jest tajny; musi natomiast dotrzeć do stacji **autentycznym kanałem**
  (osobiście, wydruk, pendrive, wiadomość od administratora).
- **Kod połączenia** do przepisania: prefiks wersji `VTW1`, pierwsze 128 bitów skrótu
  w Crockford base32 (26 znaków) i znak kontrolny Crockford (mod 37), w grupach po 5
  znaków. Znak kontrolny wykrywa literówki zanim klient spróbuje się połączyć.
  128 bitów wystarcza przeciw podstawieniu innego klucza (drugi przeciwobraz).
- **Plik połączenia** `*.vatowiec-polaczenie` (JSON, wersjonowany): adresy API, pełny
  skrót SPKI, certyfikat CA (PEM, bez klucza), opisowa nazwa instalacji. Klient
  sprawdza zgodność skrótu z dołączonym certyfikatem. Plik ma tę samą moc co kod.
- Operations zapisuje aktualny plik połączenia w katalogu instalacji serwera
  (`%ProgramData%\Vatowiec\Server`, zapis tylko administratorzy i usługa). Administrator
  instalacji widzi kod i może zapisać plik w Avalonia (Ustawienia instalacji →
  Certyfikat serwera).
- Serwer dołącza certyfikat CA do łańcucha wysyłanego w uzgadnianiu TLS, aby klient
  z samym kodem mógł zbudować i sprawdzić łańcuch (sposób: sekcja „Wdrożenie”, pkt 1).

**Klient**

- Przypięta kotwica jest zapisywana w lokalnych, niesekretnych preferencjach klienta
  (poza repozytorium, jak preferencje ADR-032), **powiązana z dokładnym originem API**.
  Nie jest stosowana dla żadnego innego adresu. Nic nie trafia do systemowego magazynu
  certyfikatów.
- Walidacja dla originu z przypiętą kotwicą: łańcuch budowany przez
  `X509ChainPolicy` z `TrustMode = CustomRootTrust` (we wdrożeniu: pusty magazyn
  i kontrola przypiętego skrótu ostatniego elementu, sekcja „Wdrożenie”, pkt 1); wymagane: zgodność nazwy hosta, terminy ważności, EKU
  Server Authentication, poprawne podpisy; korzeń łańcucha musi mieć przypięty skrót.
  Błąd niezgodności nazwy (`RemoteCertificateNameMismatch`) zawsze odrzuca połączenie.
- Jeśli łańcuch jest zaufany przez system (np. serwer przeszedł na tryb A z firmowym
  CA), standardowa walidacja również wystarcza.
- Niezgodność odrzuca połączenie z polskim komunikatem: certyfikat nie pochodzi od
  urzędu zaufanego dla tej instalacji; poproś administratora o nowy kod połączenia.
  **Nie ma przycisku „zaakceptuj mimo to”** ani TOFU. Zmiana kotwicy wymaga nowego
  kodu/pliku albo planowanej rotacji (pkt 4).
- Ekran logowania otrzymuje akcje „Wpisz kod połączenia” i „Otwórz plik połączenia”.
  Hasło, cookie i CSRF bez zmian (ADR-016/032).

### 3. Zmiana konfiguracji, audyt i odzyskiwanie

- Zmiana trybu, reguły wyboru certyfikatu (A), nazw/adresów w SAN (B), wymiana CA:
  wyłącznie administrator instalacji (rola sprawdzana świeżo w DB), CSRF i kontrola
  wersji, zlecenie wykonywane przez Operations (istniejący mechanizm `ops`
  z osobnym rodzajem operacji instalacji). API nie dostaje praw do kluczy CA ani do
  zmiany ACL magazynu.
- Każda zmiana, utworzenie CA i odnowienie certyfikatu serwera trafia do niezmiennego
  audytu w `Vatowiec_System`: aktor, czas, tryb, odciski i SAN, daty ważności.
  **Bez kluczy, haseł PFX i ich skrótów.**
- Kestrel pobiera certyfikat przez `ServerCertificateSelector` z pamięcią podręczną
  odświeżaną przy zmianie; odnowienie i zmiana trybu nie wymagają restartu API.
- Błędna konfiguracja trybu A może odciąć wszystkie stacje. Lokalna komenda
  administracyjna Operations na serwerze (uruchamiana jako administrator Windows)
  przywraca tryb B z istniejącym CA instalacji, z wpisem audytu. Nie ma zdalnej
  ścieżki obejścia.
- Pierwsze uruchomienie: przy braku konfiguracji TLS Operations tworzy CA instalacji
  i certyfikat serwera (tryb B), dzięki czemu HTTPS działa od razu po instalacji
  serwera. Środowisko developerskie na `localhost:7243` zostaje przy `dotnet dev-certs`.

### 4. Rotacja CA instalacji

- **Planowa** (przed upływem 10 lat albo na żądanie bez podejrzenia kompromitacji):
  Operations tworzy następne CA; endpoint API dostępny po zalogowaniu zwraca skrót
  i certyfikat następnego CA. Klient, łącząc się przez kanał uwierzytelniony obecną
  kotwicą, dopisuje następną kotwicę dla tego originu. Po okresie przejściowym serwer
  przełącza certyfikat serwera na nowe CA, a stara kotwica wygasa. Stacje nieużywane
  w okresie przejściowym wymagają nowego kodu.
- **Awaryjna** (podejrzenie wycieku klucza, kradzież serwera): nowe CA bez okresu
  przejściowego; wszystkie stacje wymagają nowego kodu/pliku.
- Administrator dostaje ostrzeżenie na 180 dni przed wygaśnięciem CA.

## Rozważone i odrzucone

| Wariant | Powód odrzucenia |
|---|---|
| Wyłączenie walidacji / akceptacja dowolnego certyfikatu | Zakazane; podatne na podsłuch w każdej sieci. |
| Instalacja CA instalacji w systemowym magazynie zaufania | Zaufanie dla całego HTTPS komputera, prawa administratora na stacjach. |
| TOFU z oknem potwierdzenia odcisku | W praktyce zatwierdzane bez porównania. |
| Przypinanie certyfikatu serwera zamiast CA | Każde odnowienie wymagałoby ponownego parowania stacji. |
| Wbudowany klient ACME/Let's Encrypt (DNS-01) | Wymaga domeny i integracji z wieloma dostawcami DNS; tryb A pozwala użyć takiego certyfikatu uzyskanego zewnętrznie. Możliwe później jako osobny ADR. |
| Name Constraints w CA instalacji | Uzasadnienie w pkt 2; zysk tylko przy ręcznym, odradzanym dodaniu CA do systemu. |

## Konsekwencje

- Mała firma uruchamia serwer w LAN bez domeny, publicznego DNS i własnego CA;
  stacja potrzebuje adresu i kodu połączenia.
- Firma z AD CS lub certyfikatem publicznym nie zmienia procedur (tryb A).
- Klient zyskuje drugą ścieżkę walidacji TLS; wymaga ona testów negatywnych
  (poniżej). AGENTS.md i ADR-016 trzeba po akceptacji uzupełnić: „standardowa
  walidacja albo walidacja z kotwicą CA instalacji wg ADR-047, nigdy z pominięciem”.
- Usługa Operations otrzymuje nowe zadania (CA, wystawianie, odnowienie, ACL);
  nowe operacje `ops` i audyt wymagają addytywnej migracji System.
- Serwer Linux (audyt linux-network) wymaga później osobnej implementacji
  magazynu kluczy (pliki z uprawnieniami 0600 lub PKCS#11); kod, kontrakt pliku
  połączenia i klient są przenośne.
- Dostęp zdalny przez VPN działa bez zmian: nazwa/IP VPN dopisuje się do SAN (B)
  albo certyfikatu firmowego (A). Usługi typu Tailscale z certyfikatem `*.ts.net`
  pasują do trybu A.

## Kolejność wdrożenia

1. **Klient:** walidacja z przypiętą kotwicą, kod i plik połączenia, komunikaty,
   zapis kotwicy per origin. Testy na CA i certyfikatach generowanych w teście
   (`CertificateRequest`), bez plików kluczy w repozytorium.
2. **Serwer:** Operations — tworzenie CA, wystawianie i odnawianie certyfikatu,
   ACL, plik połączenia; API — `ServerCertificateSelector`, łańcuch z CA; migracja
   System (rodzaj operacji, audyt).
3. **Avalonia — administracja:** ekran „Certyfikat serwera” (tryb, kod, zapis pliku,
   nazwy/adresy, wybór certyfikatu trybu A), lokalna komenda odzyskiwania.
4. **Rotacja planowa CA** i ostrzeżenia o terminie.
5. Aktualizacja [docs/server-lan-vpn.md](../server-lan-vpn.md) i instrukcji
   użytkownika (parowanie stacji).

## Kryteria akceptacji (testy)

Klient:

- poprawny kod/plik → połączenie i logowanie działają;
- błędny kod (znak kontrolny) → odrzucenie przed połączeniem;
- poprawny format, inny skrót → odrzucenie połączenia;
- certyfikat serwera wystawiony przez inne CA z identycznym Subject → odrzucenie;
- poprawne CA, nazwa hosta spoza SAN → odrzucenie;
- certyfikat serwera wygasły lub jeszcze nieważny → odrzucenie;
- certyfikat bez EKU Server Authentication → odrzucenie;
- kotwica originu A nie jest stosowana dla originu B (inna nazwa lub port);
- serwer z certyfikatem zaufanym przez system → połączenie bez kodu (ADR-016);
- brak ścieżki kodu akceptującej połączenie mimo błędu walidacji (przegląd kodu
  + test, że callback nie zwraca `true` przy żadnym błędzie poza obsłużonym
  `RemoteCertificateChainErrors` z poprawnym łańcuchem do kotwicy);
- odnowiony certyfikat serwera z tego samego CA → połączenie bez czynności na stacji.

Serwer:

- pierwsze uruchomienie bez konfiguracji → CA i certyfikat, HTTPS na skonfigurowanym
  adresie, plik połączenia;
- konto API nie odczyta klucza CA; konto użytkownika nie odczyta żadnego klucza;
- odnowienie i zmiana trybu bez restartu API;
- tryb A: odrzucenie certyfikatu bez wymaganej nazwy w SAN, bez EKU lub bez dostępu
  do klucza; wybór najnowszego z kilku pasujących;
- zmiany wyłącznie przez administratora instalacji, z CSRF, wersją i audytem bez
  sekretów; lokalna komenda odzyskiwania przywraca tryb B;
- klucze, hasła PFX i PEM kluczy nie pojawiają się w logach ani repozytorium.

## Wdrożenie (2026-11-22)

Zakres: wspólny kod i plik połączenia oraz walidator (`SharedContracts/ServerTls`), migracja
System `ServerTlsCertificate`, usługa Operations (`src/Server/Operations/ServerTls`), dostawca
certyfikatu i endpointy API, parowanie na ekranie logowania i ekran „Certyfikat serwera”
w Avalonia, komenda `tls status | reset-mode-b`. Odstępstwa techniczne od tekstu decyzji,
wynikające z prób na Windows (kierunek i gwarancje bez zmian):

1. **Przekazanie CA stacji z samym kodem.** TLS (SChannel/.NET) nigdy nie wysyła certyfikatu
   samopodpisanego korzenia łańcucha, a Windows CryptoAPI nie dekoduje OID z łukiem
   128-bitowym (`2.25.<UUID>`), więc odpadło też niestandardowe rozszerzenie z kopią CA.
   Certyfikat CA instalacji jest więc **podpisany jednorazowym kluczem startowym**,
   niszczonym od razu po podpisie; serwer wysyła go jako certyfikat pośredni. Klient buduje
   łańcuch z pustym własnym magazynem zaufania i `AllowUnknownCertificateAuthority`,
   a następnie wymaga dokładnie dwóch elementów, CA (BasicConstraints) i przypiętego skrótu
   klucza ostatniego elementu. Terminy, podpisy, nazwa hosta i EKU są sprawdzane zawsze
   (testy negatywne w `ServerTlsTests`). Zaufanie wynika wyłącznie z przypiętego klucza CA.
2. **Ochrona klucza CA.** Zamiast nieeksportowalnego klucza CNG w `LocalMachine` (wymaga
   praw administratora, nieprzenośne) klucz CA jest zapisany jako PKCS#8 chroniony
   Data Protection + DPAPI **konta usługi Operations** (osobny purpose związany
   z identyfikatorem instalacji), w katalogu z chronionym ACL — ten sam wzorzec co klucz GUS.
   Konto API nie odszyfruje klucza nawet przy błędnym ACL.
3. **Zlecenia zmian.** Zamiast kolejki `ops` z nowym rodzajem operacji: administrator zapisuje
   konfigurację docelową (`security.ServerTlsConfiguration`, rowversion, audyt), a Operations
   doprowadza stan do konfiguracji (`security.ServerTlsState` zapisuje wyłącznie usługa;
   sprawdzenie przy zmianie wersji konfiguracji i co 5 minut). Wymiana CA = zwiększenie
   `CaGeneration`. API nadal nie ma dostępu do kluczy CA ani ACL.

Dodatkowo: certyfikat zarządzany podają tylko endpointy Kestrela wymienione
w `ServerTls:Endpoints` (pozostałe, np. localhost z certyfikatem deweloperskim, bez zmian);
w trybie A źródłem jest wyłącznie `LocalMachine\My` (PFX dla serwera Linux — później);
przy niedostępnej bazie API zachowuje ostatnio znany tryb i nadal wczytuje odnowiony plik;
utrata plików znanego CA daje błąd `tls_ca_missing` zamiast cichego utworzenia nowego CA.

## Decyzje właściciela (2026-11-22)

1. Ważność: CA 10 lat, certyfikat serwera 1 rok z odnowieniem na 30 dni przed
   końcem — **przyjęte**.
2. Kod i plik połączenia są równorzędnymi sposobami parowania stacji — **przyjęte**.
3. Rotacja planowa (pkt 4) **nie** wchodzi do pierwszego wydania; do czasu jej
   wdrożenia każda wymiana CA wymaga nowego kodu/pliku na stacjach — **przyjęte**.
   Ostrzeżenie o zbliżającym się końcu ważności CA wchodzi do pierwszego wydania.
