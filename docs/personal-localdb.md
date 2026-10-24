# VATowiec Personal — eksperyment LocalDB 2025

Data: 2026-10-24. Decyzja: [ADR-037](adr/ADR-037-personal-localdb.md).
**Wynik: działający prototyp istniejącego API z rzeczywistym LocalDB.**
Nie jest to jeszcze instalator Personal 0.1.0-preview. Nie zbudowano nowego
instalatora, nie publikowano Release i nie instalowano dodatkowego oprogramowania.

## Środowisko i zabezpieczenia zakresu

Zastano LocalDB **17.0.4025.3** i instancję `MSSQLLocalDB`. Istniejąca instancja
pozostała nietknięta. Utworzono wyłącznie nową prywatną instancję
`VatowiecPersonal_Prototype`, należącą do `lenovo\admin`, oraz:

- `Vatowiec_Personal_Prototype_System`;
- `Vatowiec_Personal_Prototype_Company_A`;
- `Vatowiec_Personal_Prototype_Company_B`.

Narzędzie sprawdza dostępność nazw i katalogu przed tworzeniem. Nigdy nie
zastępuje zajętych baz. Rzeczywista ponowna próba została odrzucona; hashe
MDF/LDF i raportu zakończonego eksperymentu pozostały identyczne.
Każde otwarcie celu przed DDL/DML sprawdza IsLocalDB,
wersję 17, DB_NAME oraz własny znacznik po utworzeniu. Stała allowlista nie
obejmuje SQLEXPRESS. Nowa komenda jest oddzielona od historycznego runnera
resetów; nie uruchamiano go ani SQL fixtures dotychczasowych baz.

Nie wykonano żadnej operacji SQL na `Vatowiec_System`, `Vatowiec_Company_A_Dev`
i `Vatowiec_Company_B_Dev` w SQLEXPRESS. Nie zmieniano certyfikatów, DNS, zapory,
routera ani user-secrets Network. Wymiana bibliotek standardowego Debug wymagała
krótkiego zatrzymania i ponownego uruchomienia dotychczasowego API z tą samą
konfiguracją. Przed eksperymentem potwierdzono 200 na obu dotychczasowych
`/health` i identyczny hash pliku user-secrets. Nie logowano się do Network.
Po końcowym buildzie ponownie przeszło 15 kontroli Network: oba `/health`,
dotychczasowe certyfikaty, wiązania interfejsów i AllowedHosts. Pozostawiono
jedno API Network w widocznym terminalu; bieżący PID i wyniki zapisano w
`TestResults/personal-final-network.json`. API Personal i jego LocalDB są zatrzymane.

## Rzeczywisty test

Wykonano komendę:

```powershell
dotnet run --project tools/database/Vatowiec.DatabaseTools -c Debug --no-build --no-launch-profile -- personal-prototype
```

**41 sprawdzeń zakończyło się powodzeniem**, raport ma `completed=true`.
Nie są to testy na atrapach. Uruchomiono API jako osobny proces bez okna konsoli
na `https://localhost:17443`, z normalnym HTTPS, a następnie wykonano:

1. Utworzenie trzech pustych baz w nowej instancji i wszystkich migracji EF.
2. Założenie dwóch profili firm i katalogu System z nowymi CompanyId.
3. Utworzenie fikcyjnego użytkownika przez istniejący UserManager/Identity,
   roli administratora instalacji i jawnych CompanyAccess (A edycja, B odczyt).
   Losowe hasło pozostało tylko w pamięci sterownika, nie w konfiguracji/logu.
4. Odczyt CSRF, zwykłe logowanie HTTPS, odczyt UserId, listy i profilu firmy.
5. Odmowę tworzenia kontrahenta bez CSRF.
6. Dodanie kontrahenta przez dotychczasowy endpoint i idempotentne ponowienie
   tego samego OperationId, bez drugiego rekordu.
7. Aktualizację i zmianę rowversion; ponowny zapis ze starą wersją dał **409**.
8. Odczyt tego samego Id w B dał **404**, a próba zapisu w B **403**.
9. Potwierdzenie jednej kartoteki i dokładnie dwóch zdarzeń audytu w A,
   z trwałymi identyfikatorami aktora; replay i konflikt nie dodały historii.
10. Rzeczywiste próby naruszenia FK, hierarchii i niezmiennego kodu waluty oraz
    zmiany systemowego audytu, każda w wycofywanej transakcji prototypu.
11. Zakończenie i ponowny start procesu API, odczyt zmienionych danych przy
    zachowaniu wcześniejszej sesji Personal — potwierdzenie trwałego key ringu.
12. Zwykły logout fikcyjnej sesji, a następnie **401** przy jej ponownym użyciu.
13. Trzy backupy `COPY_ONLY, CHECKSUM` i `RESTORE VERIFYONLY WITH CHECKSUM`.
14. Zatrzymanie własnego API i nowej instancji LocalDB, z zachowaniem danych.

Wszystkie schematy korzystają ze **starych, wspólnych migracji**; nie dodano
migracji Personal. Zastosowano również aktualną politykę haseł i model archiwizacji.

| Baza | Migracje | Tabele aplikacji + historia EF | FK | Indeksy aplikacji | Unikalne filtrowane indeksy | rowversion | Aktywne triggery |
|---|---:|---:|---:|---:|---:|---:|---:|
| System | 14 | 21 | 11 | 36 | 2 | 7 | 3 |
| Firma A | 9 | 17 | 15 | 40 | 3 | 12 | 4 |
| Firma B | 9 | 17 | 15 | 40 | 3 | 12 | 4 |

Nie stwierdzono wyłączonych/niezaufanych FK i CHECK. Zachowano kolacje kolumn,
indeksy filtrowane i mechanizmy audytu. Indeksy aplikacji policzono osobno,
z pominięciem tabel silnika; pierwotny ogólny licznik w raporcie E2E obejmował
również indeksy systemowe. Połączenie inspekcyjne wykazało **Named pipe**.

Artefakty lokalne, ignorowane przez Git:

- `TestResults/personal-localdb.json` — pełny przebieg E2E i metadane;
- `TestResults/personal-schema-inspection.json` — osobna inspekcja indeksów;
- `TestResults/personal-debug.trx` — **498/498** testów Debug bez SQL fixtures,
  bez pominięć, z natywnymi testami Avalonia i 14 nowymi testami polityki Personal.
- `TestResults/personal-final-profile-smoke.json` — start końcowego Debug przez
  jawny profil, poprawne HTTPS i wyłącznie loopback. Sprawdzono także ochronę
  klucza sesji przez DPAPI oraz zakończenie hosta po Ctrl+C; port został zwolniony.

Build całego rozwiązania Debug: **0 błędów i ostrzeżeń**.
Nie wykonano GUI E2E Avalonia podłączonego do LocalDB ani próby na czystym,
drugim Windows bez SDK. Test E2E dotyczył rzeczywistego API/SQL i obecnych DTO.

## Profil i uruchomienie

Jawny profil `PersonalPrototype` jest dodatkowy; domyślny `https` Network nie
zmienia kolejności ani zachowania. Uruchomienie po przygotowaniu prototypu:

```powershell
dotnet run --project src/Server/Api -c Debug --no-build --launch-profile PersonalPrototype
```

Środowisko `PersonalPrototype` nie wczytuje `appsettings.Development.json` ani
user-secrets Network. Wczytuje wymagany lokalny `host.json`; bez pliku lub
z błędnym serwerem, bazą czy listenerem odmawia startu. Nie ma fallbacku na
SQLEXPRESS. W tym etapie port prototypu to tylko localhost:17443; brak listenera
LAN i reguł zapory. Certyfikat localhost jest istniejący i już zaufany, bez
wyłączania weryfikacji TLS i bez zmian w magazynach certyfikatów.

Polityka SQL ma jawny, wąski wyjątek dla Windows Authentication do prywatnego
LocalDB. Nie dopuszcza obcych instancji, AttachDbFilename, SQL loginów ani
Encrypt=False. Nie oznacza to deklaracji szyfrowania IPC SQL: LocalDB to lokalny
named pipe chroniony kontem Windows, odrębny od HTTPS klienta. Narzędzie ODBC 17
`sqlcmd -N` zgłosiło brak obsługi wymuszonego szyfrowania; inspekcja przez lokalny
IPC bez `-N` powiodła się. Używany przez aplikację Microsoft.Data.SqlClient
działał z ustawieniami prototypu przez cały test. Polityka zdalnego SQL Network
i weryfikacja HTTPS nie zostały rozluźnione.

Rzeczywisty headless start nie używa `dotnet run` ani Visual Studio: sterownik
uruchamia standardowy `Vatowiec.Api.exe` z `UseShellExecute=false`,
`CreateNoWindow=true` i środowiskiem PersonalPrototype. W tym eksperymencie
runtime jest już na komputerze; nie jest to dowód samodzielnej dystrybucji API.

Sterownik w `finally` kończy **tylko własny proces** API. To obecnie `Kill`,
a nie graceful shutdown po zamknięciu Avalonia. Nie ma jeszcze kontrolera
unikającego osierocenia po awarii rodzica ani integracji zamykania klienta.
Docelowo potrzebne są: jeden właściciel procesu, IPC ograniczone do SID,
gotowość, kontrolowane wygaszanie hosta i timeout z awaryjnym zakończeniem;
Windows Job Object może zapewnić sprzątanie po awarii. Nie dodajemy w tym celu
publicznego HTTP endpointu obchodzącego uwierzytelnianie.
Osobno potwierdzono standardowe wygaszanie hosta po Ctrl+C w terminalu
przypisanym tylko do procesu Personal. Nie jest to jeszcze wygaszanie sterowane
przez zamknięcie okna klienta ani mechanizm działający bez konsoli.

## Pliki i aktualizacje

Wszystko należy do bieżącego użytkownika Windows, nie SYSTEM ani usługi SQL:

```text
%LOCALAPPDATA%\Vatowiec\PersonalPrototype\
  host.json
  SqlData\              # trzy MDF i trzy LDF
  Keys\Sessions\        # własny Data Protection, DPAPI użytkownika
  Backups\              # trzy nowe, sprawdzone BAK
```

Katalog ma chroniony ACL: użytkownik, Administratorzy i SYSTEM. DataPort/GUS
mają wskazane osobne katalogi Keys pod tym samym profilem; bez skonfigurowanych
sekretów nie były używane ani testowane. Systemowe pliki **nowej** instancji
LocalDB pozostają w standardowym katalogu LocalDB użytkownika. Nie przenoszono
istniejących plików ani kluczy Network. Osobne nazwy cookies sesji/CSRF zapobiegają
kolizjom z Network na tym samym hostname; preferencji i Credential Manager
Avalonia eksperyment w ogóle nie modyfikuje.

Program musi być instalowany osobno od danych. Aktualizacja nie może kasować,
nadpisywać ani zakładać na nowo baz, Installation.Id, kont, kluczy i audytu.
Migracje mają być jawne po zgodnym backupie, z kontrolą wersji i blokadą
równoległych aktualizacji; nie są wykonywane na starcie zwykłego API.
Odinstalowanie powinno zachowywać dane, a ich usunięcie wymaga odrębnej decyzji.

## Instalacja LocalDB i rozmiar

W tym zadaniu LocalDB był już obecny — **niczego nie instalowano**. Skrypt
`tools/personal/Invoke-PersonalPrototype.ps1` wykrywa wersję 17 i uruchamia tylko
guardowaną komendę. Nie instaluje pakietów i nie zmienia istniejących instancji.
Powtórne tworzenie na tym koncie ma przerwać działanie, ponieważ nazwy/katalog
są zajęte. Nie usuwaj ich automatycznie, by wymusić powtórkę.

Gdy LocalDB 17 nie ma: pobierz oficjalne media **SQL Server 2025 Express**
z Microsoft, wybierz LocalDB lub pakiet `SqlLocalDB.msi` z odpowiednich mediów,
zweryfikuj podpis Microsoft/wersję i dopiero po zgodzie użytkownika wykonaj
instalację. Nie używaj przypadkowego MSI ani starszego LocalDB jako dowodu
zgodności z 2025. Procedura i lokalizacja MSI:
[Microsoft LocalDB](https://learn.microsoft.com/sql/database-engine/configure-windows/sql-server-express-localdb?view=sql-server-ver17).

Zmierzono na tym komputerze około **299,4 MiB** samych plików
`170\LocalDB\Binn` — to nie rozmiar MSI ani całej instalacji z cache/systemowymi
bazami. Dane prototypu: **48 MiB** MDF/LDF; cały jego katalog z backupami,
kluczami i konfiguracją około **63,2 MiB**. Osobno pliki systemowe nowej instancji
zajęły około **51,5 MiB**; razem nowe dane eksperymentu około **114,7 MiB**.
Celowo ustawiono limit testowy
256 MiB danych i 128 MiB logu na bazę; nie jest to limit silnika LocalDB.
Nie publikowano API, więc rozmiar przyszłego instalatora Personal nie jest znany.
Obecny instalator klienta nie zawiera ASP.NET Core API ani LocalDB i nie wystarczy.

LocalDB ma ograniczenia Express, jest zależny od konta Windows i nie jest
serwerem LAN. Nie obsługuje m.in. FILESTREAM; nasz model go nie używa.
Oficjalna tabela Express 2025 podaje limit 50 GB bazy; test takiego rozmiaru
nie był wykonywany. [Ograniczenia SQL Server 2025](https://learn.microsoft.com/sql/sql-server/editions-and-components-of-sql-server-2025?view=sql-server-ver17).

## Pierwsza instalacja i warunki kolejnego etapu

Aktualizacja etapu 2: [kreator pierwszego uruchomienia](personal-first-run.md)
oraz [ADR-038](adr/ADR-038-personal-pierwsze-uruchomienie.md) realizują bootstrap
administratora i pierwszej firmy, checkpointy, pojedynczy serwer i jego
poprawne zamknięcie. Sprawdzono je na nowych instancjach LocalDB oraz w Avalonia.
Poniższa lista opisuje braki po eksperymencie etapu 1; punkty 2–3 są obecnie
zaimplementowane w zakresie etapu 2. Instalator, certyfikat bez SDK, aktualizacje
i pełne restore nadal pozostają do wykonania.

Do przygotowania Personal 0.1.0-preview potrzebny jest jeszcze odrębny etap:

1. Instalator rozróżniający wariant, wykrywający LocalDB 17 i oferujący jawnie
   zatwierdzoną instalację oficjalnego składnika. Klient, API i runtime muszą
   działać bez SDK, z właściwymi licencjami i kontrolą dostępnego miejsca.
2. Kontroler procesu API powiązany z klientem, bez konsoli, bez konkurujących
   serwerów i bez kolizji z Network. Należy przetestować zamknięcie, crash,
   drugi start klienta, zajęty port i odzyskiwanie po niepełnej inicjalizacji.
3. Provisioning z kontrolą własności/idempotencją/checkpointami dla nowych baz
   i jawny bootstrap pierwszego administratora przez istniejący UserManager.
   Utworzenie pierwszej firmy powinno używać wspólnego modelu i audytu.
   Nie ma jeszcze produkcyjnego kreatora; obecny seed jest tylko fikcyjną próbą.
4. Certyfikat localhost z bezpiecznym kluczem i normalnym zaufaniem TLS bez SDK.
   Nie można zakładać obecności dev-certs ani wykorzystywać certyfikatu LAN.
   Trzeba zaprojektować instalację, odnowienie i usunięcie zaufania bez bypassu TLS.
5. Obsługa backup/restore i kontrolowanych aktualizacji, z zachowaniem danych
   przy aktualizacji i odinstalowaniu, oraz próba na czystym Windows i w Avalonia.

Testowe hasło nie zostało zachowane do późniejszego ręcznego logowania. Nie
resetuj kont prototypu w ramach zwykłego uruchomienia. Bootstrap użytkownika
docelowego i prezentacja hasła tymczasowego to element kreatora następnego etapu.

## Backup, restore i Personal → Network

Potwierdzono rzeczywiste backupy i VERIFYONLY trzech nowych baz. **Nie wykonano
restore do nowej instancji ani migracji do Network.** VERIFYONLY nie zastępuje
takiej próby. Nie kopiuj otwartych MDF/LDF jako strategii backupu.

Docelowy backup obejmuje System, każdą firmę, konfigurację i wymagane key ringi
oraz załączniki. Potrzebne są retencja, kopia poza komputerem i kontrola dostępu
do kopii. Dla spójnego pakietu instalacji trzeba zatrzymać operacje zapisu,
wykonać kopie wszystkich składników i sprawdzić wersje/sumy. Odtworzenie wymaga
kontroli pustego celu i pełnej próby aplikacji, nie automatycznego nadpisania baz.

Przejście na **nową, odizolowaną** instalację Network: backup/restore na zgodnym
SQL Server 2025 lub nowszym, zachowanie Installation.Id, UserId, CompanyId,
ActorIssuerId, audytu i historii migracji, następnie kontrolowana zmiana
centralnych lokalizacji baz i profili połączeń. Wąska allowlista nazw prototypu
nie jest jeszcze narzędziem relokacji — docelowe nazwy System i polityka połączeń
muszą być zgodne z Network. Nie wykonujemy scalania z obecną instalacją.

Klucze DPAPI nie są przenośne przez samą kopię plików na inne konto/komputer.
Wymagany jest autoryzowany eksport/przechronienie sekretów w starym kontekście
lub ponowne wpisanie kluczy integracji. Sesje po przeniesieniu wymagają nowego
logowania; nie kopiujemy cookies ani poświadczeń Windows Credential Manager.
Preferencje ADR-032 nie przechodzą automatycznie na inny origin ani UserId.
Taki plan pozwala zachować dane SQL, ale jego pełna procedura pozostaje
niezweryfikowana i wymaga osobnego testu przed wydaniem.

## Odpowiedź dla etapu

**Tak, wspólny VATowiec działa z LocalDB po dodaniu jawnego profilu i polityki
połączeń.** SQL, migracje i bezpieczeństwo API zostały sprawdzone rzeczywiście.
Można rozpocząć etap budowy Personal 0.1.0-preview; **nie można jeszcze uznać
tego wydania za gotowe do instalacji na komputerze użytkownika**.
