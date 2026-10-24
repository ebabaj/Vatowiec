# ADR-039: lokalny transport i cykl życia API Personal

Data: 2026-10-24. Status: wdrożony, sprawdzony na Windows/LocalDB 17.
Wymagania ORG-003/004/008/009/011, T-012/T-016. Rozszerza ADR-037/038 i zachowuje
Identity, sesje, CSRF, CompanyAccess, politykę haseł oraz ochronę tożsamości ADR-032/035.

## Wybór transportu

Wybieramy HTTP/1.1 jako format istniejącego API **wewnątrz Windows Named Pipe**.
To IPC, nie publiczny HTTP ani HTTPS na lokalnym porcie. Personal nie tworzy
nasłuchu TCP, nie korzysta z DNS/proxy/certyfikatu .NET i nie potrzebuje UAC ani
ręcznego ustanawiania zaufania. Alternatywa HTTPS wymagałaby własnego cyklu
certyfikatu i zaufania; nie daje tu korzyści wobec prywatnego transportu Windows.
Nie wprowadzamy drugiego kontraktu biznesowego, gRPC ani bezpośredniego SQL w Desktop.

Kestrel tworzy losową nazwę kanału przy każdym starcie. Jawny DACL pozwala tylko
SID konta uruchamiającego API i odmawia Network SID, także dla połączeń przez SMB.
Przy własnym PipeSecurity opcja Kestrela CurrentUserOnly musi być false — jest to
wymóg API, nie przyznanie dostępu innym kontom. Ograniczenie realizuje jawny DACL.
Kestrel zachowuje mechanizm pierwszej instancji kanału, zapobiegający przejęciu
wcześniej zajętej nazwy. Źródła Microsoft:
[konfiguracja i ograniczenia Named Pipes](https://learn.microsoft.com/aspnet/core/grpc/interprocess-namedpipes),
[bezpieczeństwo transportu Kestrela](https://learn.microsoft.com/aspnet/core/fundamentals/servers/kestrel/security-considerations).

Desktop otrzymuje nazwę, InstallationId i PID przez istniejące odziedziczone
anonimowe potoki ADR-038. Sprawdza je względem własnego obiektu Process. Przy
**każdym nowym połączeniu** Windows GetNamedPipeServerProcessId musi wskazać ten
proces; klient dodatkowo używa CurrentUserOnly i poziomu Identification.
Inny serwer pod poprawną nazwą/SID nie wystarcza. Nie ma callbacku TLS, wyjątków
certyfikatowych, tokenu bootstrapu w argumentach ani sekretów w repozytorium.

HTTP Host to syntetyczna nazwa instalacji w `.invalid`, używana tylko wewnątrz
potoku i CookieContainer. SocketsHttpHandler ma wyłączone proxy/przekierowania
i zawsze łączy się z `.` przez NamedPipeClientStream, nigdy przez TCP/DNS.
Serwer rozpoznaje zaufany transport po obiekcie feature ustawionym wyłącznie
w middleware połączenia dedykowanego listenera. Żaden nagłówek nie nadaje tej cechy.
Nie fałszujemy Request.IsHttps. W tym kanale cookies nie mają Secure ani prefiksu
__Host; nadal są HttpOnly/SameSite=Strict, a cookie/CSRF pozostają wyłącznie w pamięci.
Network nadal wymaga rzeczywistego HTTPS, ma Secure cookies, walidację TLS i stare
7243/7443. Publiczny konstruktor klienta nadal odrzuca HTTP.

Konto Windows jest granicą zaufania plików/LocalDB. Nie deklarujemy izolacji od
administratora Windows lub kodu przejmującego pamięć/uchwyty bieżącego konta.
Test ograniczonego tokenu sprawdza rzeczywistą odmowę DACL; nie udaje pełnego testu
innego konta Windows, którego poświadczeń nie posiadamy.

## Uruchomienie i pojedyncza instancja

Skrót uruchamia normalny Desktop.exe `--personal`, bez dotnet run/SDK/terminala.
Plik `personal-client.lock` z FileShare.None zapewnia jedną aplikację Personal
dla konta Windows. Drugi start pokazuje informację i nie uruchamia API. Nie
usuwamy blokady na podstawie daty/PID: zamknięcie/awaria procesu zwalnia uchwyt
w jądrze Windows. Osobna blokada instalacji ADR-038 chroni także provisioning.

Controller sprawdza profil i dozwolony katalog przed uruchomieniem API.exe jako
bieżący użytkownik, UseShellExecute=false/CreateNoWindow=true. Host Personal
nie ładuje konfiguracji Network. Status sprawdza LocalDB i własność istniejących
baz; pusta instalacja przechodzi do kreatora, gotowa do normalnego API. Migracji
nie wykonuje się przy zwykłym starcie. Bootstrap pozostaje prywatnym protokołem
z checkpointami i autorytatywnym zamknięciem w SQL po pierwszym administratorze.

ApplicationStarted przekazuje deskryptor konkretnego kanału. Następnie controller
łączy się z przypiętym procesem i wymaga HTTP 200 `/health` przez ten kanał,
zanim pokaże logowanie. Status/uruchomienie mają limit 30 s, połączenie 5 s,
zwykłe żądanie 20 s. Provisioning ma limit 180 s oczekiwania pomiędzy odpowiedziami
postępu; narzędzie LocalDB ma własny limit. Błędy trafiają po polsku do Avalonia.

## Zamykanie i awarie

Zwykłe zamknięcie respektuje dotychczasową ochronę szkiców i aktywnych operacji.
Potem okno pokazuje kończenie zapisów, zamyka kanał sterowania i asynchronicznie
czeka na własne API. EOF po nagłym zakończeniu klienta uruchamia tę samą ścieżkę
w serwerze. Kestrel ma 30 s na zakończenie aktywnych żądań; dopiero po zatrzymaniu
hosta kończymy prywatną instancję LocalDB normalnym `SqlLocalDB stop`, bez `-k`.
Maksymalny czas stopu LocalDB to 15 s, oczekiwanie controllera 60 s. Awaryjne
zakończenie po przekroczeniu limitu dotyczy wyłącznie obiektu Process utworzonego
przez controller, nigdy procesów znalezionych po nazwie/PID ani SQL Server.
Nie gwarantujemy powodzenia operacji, która przekroczyła limit; zapis SQL i audyt
pozostają transakcyjne. Brak potwierdzenia zamknięcia powoduje ostrzeżenie.

Awaria API natychmiast blokuje formularze i usuwa sesję/dane bieżącej firmy;
przy aktywnym żądaniu sprzątanie kończy się po jego odpowiedzi/błędzie. Klient
pokazuje przyczynę lokalną, bez porad o LAN/certyfikacie. Nie restartuje po cichu
API w trakcie zapisu. Użytkownik zamyka program i uruchamia go ponownie; identyczne
bazy i checkpointy pozostają. Gdy samo API zostanie zabite, LocalDB może chwilowo
pozostać uruchomiony; następny poprawny start/zamknięcie sprząta własną instancję.
Zanik zasilania/hang systemu nie jest równoważny przetestowanemu crashowi procesu.

## Stabilna tożsamość i zgodność

Kluczem preferencji i Credential Manager jest `vatowiec-personal://<InstallationId>/`
plus login i niezmienny UserId ADR-032. Losowa nazwa potoku/PID nie uczestniczą
w tym kluczu. Tożsamość pochodzi z trwałego manifestu i zweryfikowanego właściciela
bazy, przekazana przez własny proces. Inna instalacja lub Network mają inne klucze.

Niesekretne preferencje prototypu ADR-038 z jego własnego pliku, dla dawnego
localhost:18443, zachowują login, UserId, flagi i firmę domyślną pod nowym kluczem.
Stare poświadczenie ze wspólnego originu **nie jest kopiowane ani odczytywane**.
Pierwsze logowanie po zmianie transportu wymaga wpisania hasła; poprawna sesja
potwierdza UserId, a zaznaczone zapamiętywanie zapisuje je już dla tej instalacji.
Niezgodność UserId nadal usuwa autologowanie i poprzednią firmę przed jej otwarciem.
W kolejnych startach zachowane autologowanie działa bez dodatkowej czynności.

Wyniki rzeczywistych prób, instrukcja i pozostałe ograniczenia:
[Personal — uruchamianie i odzyskiwanie](../personal-running.md).
