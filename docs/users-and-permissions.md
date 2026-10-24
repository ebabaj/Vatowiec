# Użytkownicy i uprawnienia — ADR-033/034/035

Moduł rozszerza istniejące ASP.NET Identity, SystemDbContext i CompanyAccess.
Wymagania: ORG-003/004/008 i T-012; [decyzja](adr/ADR-033-uzytkownicy-i-uprawnienia.md).
Nie zmienia ochrony Windows Credential Manager ani danych w bazach firmowych.

## Obsługa

1. Zaloguj się kontem z aktualną rolą InstallationAdministrator. Na liście firm
   lub w obszarze firmy otwórz **Ustawienia instalacji → Użytkownicy i uprawnienia**.
   Administrator bez przypisań firmowych również ma dostęp do tego ekranu.
2. Po lewej wyszukuj login (Enter lub **Szukaj**); lista ma strony po 50 kont.
   Po prawej są dane konta, stan blokady, obowiązek zmiany hasła i rola instalacji.
3. Wpisz login nowego konta i wybierz **Utwórz konto**. Domyślnie serwer generuje
   16 losowych znaków z alfabetu ograniczającego pomyłki, zgodnych z Identity.
   W oknie możesz zamiast tego zaznaczyć **Wpisz własne hasło tymczasowe**.
   Serwer sprawdza je według tej samej polityki i zachowuje dokładnie wpisaną treść.
   Konto nie ma początkowych praw do firm. Okno wyniku zaczyna od maski;
   **Pokaż / Ukryj** pozwala odczytać hasło. Wygenerowane hasło ma na ekranie
   cztery grupy po cztery znaki. Odstępy grupowania nie są częścią hasła.
   **Kopiuj hasło** kopiuje oryginalne 16 znaków bez tych odstępów (własne hasło
   kopiuje dokładnie, również gdy administrator świadomie wpisał spacje);
   przekaż go bezpiecznie, a następnie wyczyść schowek. Po zamknięciu nie ma
   ponownego odczytu hasła. Jeśli odpowiedź została utracona, wykonaj nowy reset.
4. W sekcji **Dostęp do firm** zaznacz firmę i prawa, osobno dla każdej firmy.
   Są wszystkie bieżące CompanyPermission, w tym chroniony PESEL. Polskie nazwy
   mają objaśnienia. Wybór prawa zaznacza wymagany odczyt; odebranie odczytu usuwa
   prawa zależne. Serwer niezależnie od UI odrzuca niespójną kombinację.
   **Zapisz uprawnienia** zapisuje całość atomowo. Brak dostępu usuwa CompanyAccess;
   nie powstają rekordy z maską 0. Dotychczasowe sesje zainteresowanego konta
   przestają działać przy następnym żądaniu.
5. **Zablokuj konto** i **Aktywuj konto** wymagają potwierdzenia. Blokada uniemożliwia
   nowe logowania oraz unieważnia stare sesje. Nie można blokować siebie ani
   ostatniego aktywnego administratora. Automatyczna blokada Identity po błędnych
   hasłach nadal działa niezależnie od blokady administracyjnej.
6. **Resetuj hasło** otwiera ten sam wybór: hasło generowane albo własne.
   Zatwierdzenie przyciskiem **Resetuj hasło** jest potwierdzeniem operacji.
   Unieważnia wszystkie sesje konta; użytkownik musi zmienić hasło po logowaniu.
   Administrator nigdy nie otrzymuje poprzedniego hasła.
   Po wygenerowaniu otwiera się jednorazowe okno pokazania/kopiowania hasła.
   Po wpisaniu własnego hasła nie pokazujemy go ponownie; dopiero po potwierdzeniu
   API pojawia się komunikat: „Hasło użytkownika zostało zmienione. Przy następnym
   logowaniu użytkownik będzie musiał ustawić własne hasło”.
7. Wymagana zmiana hasła pokazuje osobny formularz, bez listy firm i dostępu do
   administracji. API ogranicza taką sesję do CSRF, własnego stanu, wylogowania
   i zmiany hasła. Nie zapisujemy tymczasowego hasła w Credential Manager.
   Zwykłą zmianę można też otworzyć przyciskiem **Zmień własne hasło**.
   Wymagane są bieżące hasło, inne nowe hasło zgodne z Identity i jego powtórzenie.
   Po zapisie trzeba zalogować się ponownie. Stare lokalne poświadczenie pozostaje
   w dotychczasowym magazynie i przestaje działać; wpisanie nowego ma pierwszeństwo.
8. Historia pod formularzem ma wykonawcę, konto, firmę, czas, operację i prawa
   przed/po. Bez wybranego konta **Odśwież historię** pokazuje historię instalacji;
   przy wybranym koncie filtruje ją do tego konta. Historia jest stronicowana.

Ctrl+S zapisuje prawa, Esc wraca do ustawień (albo anuluje zwykłą zmianę hasła).
Zmiana użytkownika, wyszukiwanie, odświeżenie, powrót i zamknięcie okna chronią
szkic potwierdzeniem porzucenia. Konflikt wersji zachowuje szkic; użyj
**Odśwież szczegóły**, świadomie porzuć szkic i porównaj nowy stan.

Rola instalacji nie nadaje praw do danych firm. Nie ma edytora własnych ról
ani fizycznego usuwania kont. Lokalne `grant-installation-admin <login>` pozostaje
sposobem utworzenia pierwszego administratora. Kolejnych można nadać przez UI.

## Role systemowe

Wybierz konto, a następnie w osobnej sekcji **Role systemowe** zaznacz lub odznacz
**Administrator instalacji** i wybierz **Zapisz rolę systemową…**. Nadanie i odebranie
wymagają osobnego potwierdzenia. Można nadać rolę innemu aktywnemu użytkownikowi;
nie można odebrać jej sobie ani ostatniemu aktywnemu administratorowi. Sesje konta
docelowego zostaną unieważnione. Dotychczasowe przypisania firmowe pozostają bez
zmian. Zmianę roli zapisz osobno od szkicu praw firmowych.

API sprawdza aktualne role w bazie, CSRF i ConcurrencyStamp. Nadanie/odebranie,
zmiana stempla bezpieczeństwa, unieważnienie sesji oraz audyt wykonawcy, celu,
operacji i czasu są jedną transakcją. Wspólna blokada transakcyjna SQL serializuje
wszystkie mutacje administracyjne, także równoległe blokowanie i odbieranie roli.

## Polityka haseł

Otwórz **Ustawienia instalacji → Bezpieczeństwo → Polityka haseł**. Konfiguracja
obowiązuje wszystkich użytkowników we wszystkich firmach. Ustaw minimalną
długość (1–1024) oraz niezależne wymagania wielkiej litery, małej litery, cyfry
i znaku specjalnego. Początkowo obowiązuje 12 znaków i wszystkie cztery klasy.
Wielkie i małe litery oraz cyfry odpowiadają dotychczasowym klasom Identity
A–Z, a–z, 0–9. Nie dokonujemy normalizacji treści hasła.

Wyłączenie złożoności pokazuje ostrzeżenie i wymaga świadomego potwierdzenia.
Wówczas każde niepuste hasło spełnia wymagania złożoności, także jednoznakowe; puste nadal
jest odrzucane. Zachowany limit transportowy 1024 znaków służy ochronie żądań.
Hashowanie, CSRF, kontrola dostępu, blokady kont i sesje działają nadal.

Zmiana obowiązuje natychmiast przy tworzeniu konta, administracyjnym resecie,
wymuszonej oraz zwykłej zmianie hasła. Jeden serwerowy walidator Identity czyta
świeżą konfigurację z System; nie ma drugiego walidatora wymuszającego sztywne 12
znaków. Istniejące hasła i sesje pozostają ważne, bez wymuszenia zmiany. Generator
działa również przy wyłączonej złożoności, domyślnie generując 16 znaków; jeśli
aktywna polityka wymaga więcej, generuje odpowiednio dłuższe hasło.

Zapis wymaga aktualnej roli administratora, CSRF i RowVersion. Konfiguracja
i niezmienna historia przed/po z wykonawcą i czasem zapisują się atomowo.
Historia nie zawiera haseł. Konflikt zachowuje szkic; odświeżenie i opuszczenie
formularza wymagają potwierdzenia porzucenia niezapisanych zmian.

## Migracja i uruchomienie istniejącej instalacji

Migracja **20261024080845_UserAdministration**, wyłącznie Vatowiec_System,
Dodaje `security.Users.IsBlocked` i `MustChangePassword`, domyślnie false dla
istniejących kont, oraz niezmienne `audit.UserAdministrationChanges` z triggerem.
Nie przepisuje haseł, ról, praw ani sesji. Nie zmienia CompanyDbContext i jego
migracji. Nie uruchamia się przy starcie API. Do migracji użyj konta technicznego
z DDL; zwykłe API powinno nadal mieć jedynie uprawnienia operacyjne.

ADR-034 dodaje addytywną **20261024100547_InstallationPasswordPolicy** wyłącznie
w System: `security.PasswordPolicy` z początkowym rekordem oraz niezmienne
`audit.PasswordPolicyChanges` z triggerem. Nie zmienia istniejących kont,
hashy, ról, CompanyAccess ani baz firm. Przed wykonaniem wymagany jest sprawdzony
backup. Nie używaj Down do wycofania działającej instalacji: usuwa konfigurację
i nową historię. Poprzedni serwer można uruchomić z pozostawionymi tabelami,
ale nie będzie egzekwował zmienionej polityki, dlatego API i klient aktualizuj razem.

Przy aktualizacji zatrzymaj stary proces API i klienta, zbuduj Debug, zrób
zweryfikowany backup i jawnie wykonaj migrację System:

```powershell
dotnet build Vatowiec.slnx --no-restore -c Debug
dotnet run --project tools/database/Vatowiec.DatabaseTools --no-build -c Debug -- backup-system
dotnet run --project tools/database/Vatowiec.DatabaseTools --no-build -c Debug -- migrate-system
dotnet run --project src/Server/Api --no-build -c Debug --launch-profile https
```

W drugim terminalu: `dotnet run --project src/Desktop --no-build -c Debug`.
API i klient muszą pochodzić z nowej kompilacji. Stary serwer nie obsługuje nowych
operacji i nie sprawdza nowej flagi blokady konta podczas loginu.

Lokalna migracja została już wykonana podczas tego zadania. Przed nią wykonano
COPY_ONLY z CHECKSUM oraz RESTORE VERIFYONLY z CHECKSUM. Backup:
`C:\Program Files\Microsoft SQL Server\MSSQL17.SQLEXPRESS\MSSQL\Backup\Vatowiec_System_before_contractors_bbd4f38ecbd64f919cfac9243bc931c2.bak`.
Nie odtwarzano baz i nie resetowano istniejących kont. Bazy A/B jedynie odczytano
podczas istniejącej weryfikacji narzędzia. Działającego starego procesu API nie
zatrzymywano; przed użyciem nowego modułu trzeba go zastąpić nową kompilacją.

`seed-users` tworzy teraz wyłącznie brakujące konta i ich początkowe przypisania.
Istniejące konta pomija w całości: nie sprawdza ani nie resetuje hasła, nie zmienia
ról, praw, blokady ani obowiązku zmiany. Aktualizacja DemoUsers:* w konfiguracji
nie resetuje konta. Reset wykonuj jawnie w nowym module; nie dodano ukrytej flagi
resetującej zwykłego seeda ani nowego resetu demonstratora.

## Historyczna weryfikacja ADR-033 (przed ADR-034)

- Całe rozwiązanie Debug: 0 błędów, 0 ostrzeżeń. Z powodu plików zablokowanych
  przez działające API pełny build/test używał
  `-p:BaseOutputPath=bin/Adr033Verification/`. Klient oraz rzeczywisty próbnik UI
  zostały także zbudowane w zwykłym `bin/Debug`.

  Ten historyczny sposób budowania został wycofany. Bieżące zmiany budujemy
  wyłącznie w standardowym Debug; blokujący proces musi zostać zatrzymany.
- **448/448** testów, 0 pominiętych: 444 bez SQL oraz 4 nowe niedestrukcyjne
  testy SQL/API, `TestResults/adr033-regression.trx`. Włączenie SQL wyłącznie przez
  `VATOWIEC_PRESERVED_VAT_SQL=1` i filtr UserAdministrationSqlTests; bez runnera test.
- SQL/API: tworzenie, blokada/aktywacja, brak praw administracyjnych, CSRF każdej
  mutacji, potwierdzenia, A bez B i różne prawa A/B, odebranie praw i natychmiastowa
  odmowa starej sesji, reset/wymuszona zmiana, Identity policy, stare hasło, konflikt
  stempla i RowVersion w drugiej pozycji bez częściowego zapisu, brak sztucznego
  audytu przy braku zmiany, niezmienny audyt EF i trigger SQL. Wymuszona awaria
  audytu wycofuje blokadę, stempel i unieważnienie sesji; stara sesja nadal działa.
- Ponowny seed fikcyjnego konta zachowuje ręcznie zmienione hasło, usuniętą rolę,
  zmienione przypisania, blokadę, obowiązek zmiany i wersje. Wszystkie testowe
  zapisy SQL są wycofywane transakcją i savepointami; dotyczą wyłącznie System.
- Reguła ochrony ostatniego administratora ma osobny test jednostkowy, a odmowa
  blokady własnej jest sprawdzona także przez API. Nie modyfikowano istniejącego
  administratora w celu przeprowadzenia tej próby.
- Rzeczywisty MainWindow Avalonia z atrapą API: nawigacja, polskie prawa,
  zależności, wybór konta i porzucanie szkicu, stabilność listy po zapisie/tworzeniu,
  blokada/potwierdzenie/anulowanie, reset/tworzenie z jednorazową maską i jawnym
  kopiowaniem, zwykła i wymagana zmiana własnego hasła. Sprawdzono, że automatyzacja
  UI widzi tylko maskę sekretu tymczasowego i że pola haseł są czyszczone po zapisie.
  Zrzuty: `TestResults/users-editor-dirty-debug.png`, `users-created-debug.png`,
  `users-forced-password-debug.png`; wynik `TestResults/users-ui-result.txt`.

Próba ręczna: użyj nowego fikcyjnego konta, nadaj mu A bez B, sprawdź obowiązek
zmiany hasła i listę firm, potem pozostaw jego drugą sesję i odbierz prawo w sesji
administratora. Przy następnym żądaniu druga sesja musi otrzymać 401. Zakończ próbę
blokując fikcyjne konto; nie ma fizycznego DELETE. Nie używaj istniejących kont
demonstracyjnych do resetów ani `DatabaseTools test` do weryfikacji tego modułu.

Nie wykonywano Commit ani Push. Nie wykonywano testów destrukcyjnych ani zewnętrznych
zapytań produkcyjnych. Nie zmieniano konfiguracji integracji i kluczy.

## Poprawka ergonomii haseł tymczasowych

Debug: **453/453**, bez pominięć (448 bez SQL i 5 niedestrukcyjnych SQL/API),
`TestResults/temporary-passwords-debug.trx`. Generator ma domyślnie 16 znaków,
losuje kryptograficznie zarówno znaki, jak i ich kolejność; testy sprawdzają
alfabet, klasy, długość, surowszą politykę i zgodność z rzeczywistymi walidatorami
Identity. Tworzenie/reset sprawdzono z hasłem generowanym i własnym. Słabe hasło
nie zmienia konta ani audytu; poprawne wymusza zmianę przy logowaniu, a reset
nadal unieważnia sesje. Testy SQL dotyczą wyłącznie fikcyjnych kont i są wycofywane.

Rzeczywista Avalonia z atrapą API: obie operacje w obu trybach, Pokaż/Ukryj,
cztery grupy, kopiowanie bez separatorów, zachowanie treści własnego hasła
i obowiązkowa zmiana. Po Ukryj kontrolka i automatyzacja zawierają ponownie samą
maskę. Nie zapisywano haseł ani zrzutów z odsłoniętym hasłem.
Bez nowych migracji, resetów baz i zmian uprawnień. Po kompilacji uruchom ponownie
API i klienta, aby oba używały nowych kontraktów. Generator respektuje politykę
Identity; jeśli jej minimalna długość przekroczy 16, wygeneruje odpowiednio dłuższe hasło.

## Weryfikacja ADR-034

Całe rozwiązanie kompiluje się w standardowym Debug bez błędów i ostrzeżeń.
Blokujący poprzednio proces API PID 46108 został zatrzymany; nie zastosowano
innych ścieżek wyjściowych. Migracja i weryfikacja SQL/API zostały dokończone
w opisanej poniżej naprawie ekranu polityki haseł.

Wykonano COPY_ONLY z CHECKSUM i RESTORE VERIFYONLY z CHECKSUM dla System:
`C:\Program Files\Microsoft SQL Server\MSSQL17.SQLEXPRESS\MSSQL\Backup\Vatowiec_System_before_contractors_ca9df8e628964b87bd4616b8e5f61f96.bak`.
Log: `TestResults/password-policy-backup.txt`. Addytywny skrypt do przeglądu:
`TestResults/password-policy-migration.sql`. Przed późniejszym wykonaniem migracji
powstał świeży backup, wskazany w sekcji naprawy poniżej.

Po zatrzymaniu API i jawnej migracji System uruchom w standardowym Debug:

```powershell
$env:VATOWIEC_PRESERVED_VAT_SQL = '1'
$env:VATOWIEC_REAL_UI = '1'
$env:VATOWIEC_REAL_POLICY_API = '1'
dotnet test tests/Vatowiec.Tests --no-restore -c Debug --filter 'Category!=SqlIntegration|FullyQualifiedName~UserAdministrationSqlTests' --logger 'trx;LogFileName=password-policy-debug.trx' --results-directory TestResults
```

Filtr nie uruchamia destrukcyjnej fixture SQL. Nowe testy obejmują niezależne
klasy/długość/wyłączenie, brak drugiego walidatora, świeżą politykę w każdej
ścieżce Identity i zachowanie starych haseł/sesji, grant/revoke, CSRF, wersje,
brak CompanyAccess, zablokowany cel i rollback po awarii audytu. Dwa równoległe
żądania używają osobnych rzeczywistych połączeń i wycofywanych transakcji.
Test natywnego okna Avalonia (Windows) sprawdza bindingi, potwierdzenia,
anulowanie, szkic, nadanie i odebranie roli z atrapą API. Flaga REAL_POLICY_API
włącza dodatkowo odczyt/zapis polityki z tego samego okna przez rzeczywisty
Kestrel/HTTPS/Identity/SQL. Nie zmienia istniejących kont ani ich haseł.

## Obsługa błędów zmiany roli

Ogólne HTTP 404 nie oznacza automatycznie braku kontrahenta. Dla administracji
użytkowników rozróżniamy `user_missing` (brak użytkownika) od 404 bez kodu
biznesowego (brak operacji w tej wersji API). Ten drugi przypadek wymaga sprawdzenia
zgodności klienta i serwera oraz uruchomienia aktualnej kompilacji API. Odmowa
uprawnień i konflikt wersji również mają komunikaty dotyczące użytkownika.

Operacja czyści poprzedni status przed żądaniem. Komunikat wskazuje, czy błąd
dotyczy zapisu, ponownego odczytu użytkownika czy odświeżenia historii. Nie
zastępujemy awarii komunikatem sukcesu. Przy zerwanym połączeniu wynik zapisu
może być nieznany; formularz nie twierdzi, że zapis na pewno się nie odbył.

Po niepotwierdzonym zapisie checkbox pokazuje ostatni potwierdzony stan,
wyświetla ostrzeżenie i wymaga **Odśwież szczegóły** przed kolejną zmianą.
W trakcie edycji opis jednoznacznie nazywa zaznaczenie niezapisaną propozycją.
Po poprawnym PUT klient wykonuje osobny GET użytkownika i przyjmuje aktualny
stan oraz wersję z serwera. Znika status **Niezapisane zmiany**. Awaria samej
historii zachowuje odczytaną rolę, ale nadal pokazuje rzeczywisty błąd historii.

Weryfikacja odbywa się bez zmian SQL i migracji: testy transportu z atrapą HTTP,
stanów formularza oraz natywnego MainWindow Avalonia (błąd 404, cofnięcie
checkboxa, ostrzeżenie, odświeżenie i ponowny poprawny zapis).

Wynik: całe rozwiązanie standardowy Debug — 0 błędów, 0 ostrzeżeń;
**468/468 testów**, 0 pominiętych, w tym natywna Avalonia, bez testów SQL.
Raport: `TestResults/user-administration-errors-debug.trx`. Nie wykonano
migracji, resetów SQL, Commit ani Push w ramach tej poprawki.

## Naprawa ekranu polityki haseł — przyczyna i wdrożenie

Rzeczywistą przyczyną HTTP 503 była niewykonana migracja
`20261024100547_InstallationPasswordPolicy` w Vatowiec_System. Historia kończyła
się na UserAdministration; obie tabele polityki i audytu nie istniały (OBJECT_ID
zwracał NULL). Żądanie GET rzeczywistego API potwierdziło błąd SQL **208**.
Nie był to problem uprawnień administratora ani samego wykrywania zmian.
Odczyt nie tworzył edytora, więc zapis pozostawał nieaktywny; historia również
nie miała tabeli. Globalne przechwycenie SQL nie zapisywało diagnostyki, a klient
mapował kod `system_unavailable` na błędny komunikat o bazie firmy.

Przed migracją wykonano świeży COPY_ONLY/CHECKSUM i RESTORE VERIFYONLY/CHECKSUM:
`C:\Program Files\Microsoft SQL Server\MSSQL17.SQLEXPRESS\MSSQL\Backup\Vatowiec_System_before_contractors_3b30f8a6d1d34bc4aaa1088d0cb56ff1.bak`.
Log: `TestResults/password-policy-repair-backup.txt`. Zastosowano wyłącznie
przygotowaną migrację System. Potwierdzono obie tabele, trigger audytu i jeden
rekord: polityka włączona, minimum 12, wszystkie cztery wymagania włączone.
Nie resetowano kont, haseł, danych, sesji ani audytu. Narzędzie odczytało bazy
firm podczas verify, bez migracji lub zapisu w nich.

API rejestruje teraz numer błędu SQL, ścieżkę bez query i identyfikator korelacji,
bez SQL, parametrów i sekretów. Brak schematu lub rekordu konfiguracji oraz awaria
zapisu polityki mają osobne kody `password_policy_*`. Błędy systemowe mają
komunikaty dotyczące instalacji, a nie firmy. Nie tworzymy wartości domyślnych
w obsłudze awarii: jedyną inicjalizacją jest jawna migracja.

Klient ukrywa pola, jeśli nie odczytał konfiguracji, i pokazuje brak odczytu.
Odświeżenie po awarii usuwa poprzedni edytor. Zapis wymaga zmiany i poprawnego
formularza; wykonuje PUT, ponowny GET konfiguracji oraz GET historii. Historia
ma osobny stan odczytu i przycisk odświeżenia, więc błąd nie udaje pustej historii.

Weryfikacja natywnej Avalonia z rzeczywistym Kestrelem przez HTTPS obejmuje
logowanie, GET, zmianę parametru, aktywację przycisku, PUT, ponowny GET,
dezaktywację przycisku, ponowne otwarcie i rzeczywisty wpis audytu. Wszystkie
zmiany pozostają w odizolowanej transakcji SQL z rollbackiem. Po zakończeniu
osobne połączenie potwierdza identyczną konfigurację, RowVersion i liczbę wpisów
audytu. Test nie zatwierdza próbnej zmiany polityki instalacji.
Ślad: `TestResults/password-policy-native-api-complete.txt`;
raport: `TestResults/password-policy-repair-debug.trx`.

Końcowy wynik naprawy: standardowy Debug całego rozwiązania, **0 błędów i 0
ostrzeżeń**, **481/481 testów**, 0 pominiętych (470 bez SQL, 10 niedestrukcyjnych
SQL/API i 1 natywny scenariusz Avalonia z rzeczywistym HTTPS API/SQL).
Nie uruchamiano resetującego runnera. Nie wykonano Commit ani Push.

Poprawka prezentacji resetu hasła: standardowy Debug — 0 błędów i ostrzeżeń;
**474/474 testy bez SQL**, bez pominięć. Natywna Avalonia potwierdza okno wyniku
wyłącznie przy generowaniu oraz brak ponownego pokazania ręcznie wpisanego hasła.
Testy asynchroniczne sprawdzają komunikat dopiero po odpowiedzi API oraz brak
sukcesu po odrzuceniu zapisu. Bez zmian serwera, bazy i mechanizmów Identity.
Raport: `TestResults/password-reset-presentation-debug.trx`.

## Historia i ważność haseł — ADR-035

W **Ustawienia instalacji → Bezpieczeństwo → Polityka haseł** są dwie dodatkowe,
niezależne od złożoności opcje. Domyślnie obie są wyłączone:

- **Nie pozwalaj ponownie używać poprzednich haseł**: ustaw liczbę 1–20
  (wartość pomocnicza 5). Zablokowane są aktualne hasło i N poprzednich.
- **Wymuszaj okresową zmianę hasła**: ustaw ważność 1–3650 dni
  (wartość pomocnicza 90). Włączenie daje dotychczasowym kontom pełny okres
  od daty włączenia; późniejsze zmiany hasła rozpoczynają nowy okres.

Przy wyłączonej złożoności nadal obowiązują włączone historia i ważność.
System przechowuje do 20 hashy zastąpionych haseł Identity na UserId, również
gdy blokada ponownego użycia jest wyłączona, aby zachować kolejność przyszłych
zmian. Nie odtwarzamy brakującej wcześniejszej historii. Hashe nie trafiają do
DTO, audytu ani klienta; kandydat jest sprawdzany przez PasswordHasher,
a nie równość hashy. Rehash podczas logowania nie przedłuża ważności.

Tworzenie, reset, przywrócenie i własna zmiana hasła zapisują datę UTC i historię
w tej samej transakcji co hasło. Nieudany zapis/audyt wycofuje całość. Starym
kontom nie dopisujemy fikcyjnych dat zmiany. Każde żądanie serwera sprawdza
ważność, również w istniejących sesjach: po terminie dostępne są tylko własny
stan, CSRF, wylogowanie i zmiana hasła. Klient przechodzi do istniejącego
ograniczonego formularza. Nie zmieniono hashowania, blokad ani długości sesji.

## Usunięcie logiczne i dwie operacje przywrócenia

**Usuń użytkownika…** wymaga potwierdzenia. Konto znika ze zwykłej listy;
zachowujemy UserId, login historyczny, datę, administratora i cały audyt.
Wszystkie role, przypisania firmowe i sesje są wyłączane atomowo. Nie można
usunąć siebie ani ostatniego aktywnego administratora. Login pozostaje
zarezerwowany: zwykłe tworzenie użytkownika nie może go ponownie użyć.

Zaznacz **Pokaż usuniętych**, wybierz konto i świadomie wybierz jedną z dwóch
odrębnych operacji:

- **To ta sama osoba — przywróć konto** zachowuje oryginalny UserId i login.
  Wybierz nowe hasło tymczasowe (generowane albo własne). Wymagana jest zmiana
  przy następnym logowaniu. Dawne role i dostęp do firm nie wracają; nadaj je
  świadomie. Historia haseł i poprzednich operacji pozostaje przy tej osobie.
- **To nowa osoba — wykorzystaj login** pozostawia starą tożsamość na zawsze
  w archiwum i tworzy nowe konto z nowym UserId. Konto archiwalne otrzymuje
  unikalną nazwę wewnętrzną, a oryginalny login pozostaje w osobnym polu.
  Nowa osoba nie otrzymuje dawnych praw, historii haseł ani sesji. Operacja
  jest atomowa; obie powiązane tożsamości są wskazane w audycie. Starego konta
  po zwolnieniu loginu nie można przywrócić operacją A.

W obu przypadkach własne hasło nie jest pokazywane ponownie. Wygenerowane
ma jednorazowe okno odczytu/kopiowania. Nie ma fizycznego usuwania kont ani
przepisywania właściciela dawnych zdarzeń. `seed-users` pomija także archiwalne
oraz historyczne loginy i nigdy nie reaktywuje konta.

## Zapamiętana tożsamość klienta

API zwraca UserId, a preferencje i wpis Credential Manager v2 są z nim związane
oprócz adresu HTTPS/loginu. Po wykryciu innego UserId dla tej samej nazwy klient
odrzuca sesję, usuwa stare poświadczenie i preferencje firmy, wyłącza autologowanie
i wymaga ponownego ręcznego logowania **przed pobraniem firm**. Nowa osoba nie
dziedziczy danych poprzednika nawet przy przypadkowo takim samym haśle.
Wpisy v1 i preferencje bez tożsamości wymagają ręcznego logowania po aktualizacji.
Nie kopiujemy sekretów do pliku preferencji. Przy tej samej osobie UserId nie
zmienia się; nowe hasło trzeba wpisać, a firmy nadal wymagają świeżego CompanyAccess.

## Wdrożenie ADR-035

Zastosowano wyłącznie addytywną migrację System
**20261024120721_PasswordLifecycleAndArchivedUsers**: nowe pola Users/PasswordPolicy,
nullable RelatedUserId w audycie i security.UserPasswordHistory. Bez zmian baz firm,
starych hashy, dat zmiany hasła i dotychczasowych ustawień złożoności. Nowe opcje
są wyłączone. Przed migracją wykonano COPY_ONLY/CHECKSUM i VERIFYONLY/CHECKSUM:
`C:\Program Files\Microsoft SQL Server\MSSQL17.SQLEXPRESS\MSSQL\Backup\Vatowiec_System_before_contractors_e915c1309ac84893826531947e88b8cf.bak`.
Log: `TestResults/adr035-backup.txt`; skrypt: `TestResults/adr035-migration.sql`.
Nie stosuj Down z danymi: usuwa historię haseł i metadane archiwizacji. API
i klient aktualizuj razem; zapis polityki wymaga jawnych nowych pól kontraktu,
aby stary klient nie mógł przypadkowo wyłączyć nieznanych mu zabezpieczeń.

Weryfikacja: standardowy Debug; testy jednostkowe, transport/Windows Credential
Manager, niedestrukcyjne SQL z rollbackiem oraz natywna Avalonia z rzeczywistym
Kestrelem/HTTPS/Identity/SQL. Scenariusze obejmują N i retencję 20, reset/obie
zmiany hasła, okres ochronny i wygaśnięcie w istniejących sesjach, archiwizację,
przywrócenie tej samej osoby i nowego UserId, brak dziedziczenia danych, CSRF,
wersje, rolę aktora, seed i awarie audytu. Natywne UI sprawdza niezależność nowych
checkboxów od złożoności, filtr archiwum i obie wyraźne operacje przywrócenia.
Raport: `TestResults/adr035-regression-debug.trx`; ślad: `TestResults/adr035-native-api.txt`.

Wynik ADR-035: standardowy Debug całego rozwiązania — **0 błędów i 0 ostrzeżeń**;
**495/495 testów**, 0 pominiętych: 479 bez SQL, 15 niedestrukcyjnych SQL/API
i natywna Avalonia z rzeczywistym API/SQL. Po rollbacku pozostały 3 zastane konta,
5 przypisań firmowych, brak zarchiwizowanych kont i brak dopisanych dat/historii
starych haseł. Nie uruchamiano resetującego runnera ani migracji baz firm.
Nie wykonano Commit ani Push.
