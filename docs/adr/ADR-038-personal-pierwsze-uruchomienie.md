# ADR-038: bezpieczny kreator pierwszego uruchomienia Personal

Data: 2026-10-24. Status: przyjęty i zweryfikowany na odizolowanym LocalDB 17
oraz w natywnym Avalonia; bez gotowego instalatora i provisioningu certyfikatu.
Wymagania ORG-003/004/008/009/011 oraz T-012/T-016. Rozszerza ADR-037;
zachowuje ADR-015/016/032/034/035 i model database-per-company.

Aktualizacja: [ADR-039](ADR-039-personal-cykl-zycia-api.md) zastępuje opisany niżej
transport HTTPS localhost:18443 lokalnym Named Pipe, bez certyfikatu, i rozszerza
controller o pojedynczą instancję, nadzór procesu i zatrzymywanie LocalDB.
Zasady inicjalizacji, checkpointów i trwałego zamknięcia bootstrapu nie zmieniają się.

## Decyzja

Jeden klient Avalonia obsługuje oba warianty. Jawny argument `--personal`
otwiera kreator Personal; uruchomienie bez argumentu nadal otwiera Network.
Kreator uruchamia własny proces istniejącego API bez konsoli, w kontekście
bieżącego użytkownika Windows. Klient nie otwiera połączeń SQL. Provisioning
pozostaje po stronie Infrastructure/API, korzysta z istniejących migracji,
UserManager, hashera, walidatora polityki haseł i CompanyAccess.

Nie powstaje endpoint HTTP inicjalizacji. Desktop tworzy dwa anonimowe potoki
Windows, a proces potomny dziedziczy ich uchwyty. Hasło administratora przechodzi
wyłącznie przez ten kanał i pamięć; nie występuje w argumentach procesu,
środowisku, plikach ani logach. Nazwy/numery uchwytów nie są tokenami HTTP.
Worker usuwa dziedziczenie uchwytów przed uruchamianiem dalszych procesów.
Samo połączenie z localhost, znajomość portu lub loginu nie otwierają kreatora.

Granica zaufania obejmuje konto Windows: szkodliwy kod dysponujący możliwością
czytania pamięci/duplikowania uchwytów procesów tego konta lub administrator
Windows nie są izolowani przez protokół aplikacji. Nie deklarujemy odporności
na przejęte konto Windows. Dostęp sieciowy i niezwiązany proces, który jedynie
łączy się z lokalnym API, nie otrzymują kanału inicjalizacji.

## Trwałość i wznowienie

`%LOCALAPPDATA%\Vatowiec\Personal` otrzymuje ACL bieżącego konta, Administratorów
i SYSTEM. Wyłączna blokada pliku obejmuje cały czas życia workera/serwera;
drugi klient nie uruchomi konkurencyjnego procesu dla tej instalacji.
Manifest `installation.json` zapisuje niezmienne InstallationId, CompanyId,
etapy zamiaru utworzenia baz i dane pierwszej firmy, **bez hasła**. Zapis pliku
przechodzi przez plik tymczasowy, flush i atomową zamianę. Brak konfiguracji
w niepustym katalogu oznacza odmowę, a nie utworzenie instalacji zastępczej.
Dowiązania katalogu są odrzucane.

Instancja `VatowiecPersonal_<InstallationId>` i bazy
`Vatowiec_Personal_<InstallationId>_System` oraz
`Vatowiec_Personal_<InstallationId>_Company` należą wyłącznie do tej instalacji.
Przed CREATE sprawdzamy dokładną nazwę i stan; istniejący obcy cel jest błędem.
Każde otwarcie provisioningowe sprawdza IsLocalDB, wersję 17, DB_NAME i znacznik
własności. Nowa instancja nie może zawierać baz spoza checkpointów.

CREATE DATABASE nie może należeć do zwykłej transakcji. Przerwę pomiędzy CREATE
a nadaniem znacznika rozpoznajemy tylko dla utrwalonego zamiaru, dokładnych
dwóch plików w prywatnym katalogu oraz pustego schematu. Inny stan kończy się
odmową przejęcia. Migracje można wznowić wyłącznie w tak oznaczonych bazach;
nie ma DROP, resetu, odtwarzania kont ani migracji Network przy starcie.

Najpierw przygotowujemy System i kompletny profil fizycznej bazy firmy.
Następnie jedna transakcja System, pod istniejącą blokadą administracji,
zapisuje własne hasło pierwszego administratora, rolę, katalog firmy,
CompanyAccess, niezmienny audyt oraz znacznik zakończenia w extended properties.
Nie nadajemy dostępu do firmy przed gotowością jej bazy. Pierwszy administrator
otrzymuje jawnie pokazane wszystkie uprawnienia do pierwszej firmy, w tym PESEL.
Podane przez niego docelowe hasło nie jest hasłem demonstracyjnym/tymczasowym.

Znacznik SQL i audyt są autorytatywne. Awaria po COMMIT, przed końcowym zapisem
manifestu, wymaga jedynie odczytu i opublikowania zakończonego stanu. Nie pozwala
ponownie utworzyć administratora. Istnienie kont również blokuje bootstrap,
nawet przy niespójnym stanie. Wznowienie przed COMMIT ponownie prosi o hasło;
nie odtwarzamy go z pliku. Zapisane dane profilu firmy zostają zachowane.

## Normalne uruchomienie i granice etapu

Po potwierdzeniu zakończenia ten sam proces przechodzi do normalnego hosta API
Personal na HTTPS localhost:18443. Dopiero potwierdzenie ApplicationStarted
przez prywatny kanał otwiera dotychczasowy formularz logowania. Nie ma
automatycznego logowania hasłem z kreatora. Kolejny start pomija edycję kreatora.
Host Personal nie ładuje konfiguracji Network, używa własnych cookies,
key ringów DPAPI i preferencji logowania. Network zachowuje 7243/7443 i certyfikaty.
Standardowa walidacja TLS pozostaje włączona. Ten etap nadal wymaga istniejącego
zaufanego certyfikatu localhost; instalowanie certyfikatów bez SDK jest następnym
zadaniem. Również instalator Personal i automatyczne aktualizacje są poza zakresem.

EOF prywatnego kanału po zamknięciu/crashu klienta wywołuje StopApplication.
Przy zwykłym zamknięciu klient czeka do 10 s na graceful shutdown; awaryjne
zakończenie dotyczy wyłącznie własnego, utworzonego procesu. Nie zatrzymuje
Network ani istniejących instancji LocalDB. Dane nie są usuwane po wyjściu.

Wyniki i ograniczenia weryfikacji: [kreator Personal](../personal-first-run.md).
