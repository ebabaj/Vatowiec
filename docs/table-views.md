# Interfejs i widoki tabel (ADR-042)

Zmiana zlecona jako „ADR-041 — przebudowa interfejsu” ma numer
[ADR-042](adr/ADR-042-interfejs-i-widoki-tabel.md), ponieważ ADR-041 już opisuje
ustawienia początkowe Personal. Obie decyzje obowiązują niezależnie.

## Korzystanie z interfejsu

Network rozpoczyna logowanie ze zwiniętym „Ustawienia połączenia” pod formularzem.
Rozwinięcie pozwala zmienić adres HTTPS; walidacja certyfikatu jest obowiązkowa.
Personal nie pokazuje tych ustawień i używa dotychczasowego prywatnego potoku.

Po zalogowaniu menu ma cztery grupy:

| Menu | Funkcje |
| --- | --- |
| KARTOTEKI | Kontrahenci |
| SŁOWNIKI | Waluty, Kursy walut |
| FIRMA | Ustawienia firmy, Zmień firmę |
| KONTO | Własne hasło, zapomnienie logowania, ustawienia instalacji (administrator), wylogowanie |

KARTOTEKI, SŁOWNIKI i FIRMA pozostają po lewej, KONTO przy prawej krawędzi
obszaru menu. Lista KONTO rozwija się w lewo od prawego końca przycisku,
w obrębie okna. Wspólny pasek zachowuje nawigację strzałkami i klawisze dostępu.

Podsekcje ustawień firmy (DataPort, waluty/NBP) i instalacji (rejestry, GUS,
użytkownicy, bezpieczeństwo) pozostają dostępne. Zmiana modułu, firmy i zamknięcie
chronią niezapisane szkice. Dolny pasek pokazuje aktualny login, firmę i lokalną
godzinę absolutnego końca sesji; znika po wylogowaniu/wygaśnięciu.

Kontrahenci wczytują się po otwarciu firmy i po powrocie do kartoteki. Domyślny
status to Aktywni; pozostałe wybory to Nieaktywni i Wszyscy. Nie ma osobnego
filtra kraju. Tekst przeszukuje wyłącznie widoczne kolumny, przed paginacją w SQL.
„NIP” jest etykietą dotychczasowego TaxId, również dla zagranicznych numerów.
Rozmiar strony 25/50/100/200 zapisuje się w System dla tej tabeli/użytkownika;
fabrycznie 50. Zmiana filtrów/rozmiaru zaczyna od pierwszej strony. Odśwież
pozwala ponowić nieudany odczyt. Starsze odpowiedzi nie przywracają danych
poprzedniej firmy/filtra, także gdy transport nie zdąży anulować żądania.

Prawy panel kontrahentów zawiera Nowy, Pokaż, Edytuj i Odśwież. Bez zaznaczenia
Pokaż/Edytuj są nieaktywne. Pokaż, F2, Enter i dwuklik pobierają aktualne dane
wyłącznie do odczytu, ze wszystkimi istniejącymi zakładkami. W podglądzie Edytuj
ponownie pobiera aktualny rekord, jeżeli użytkownik ma prawo edycji. Ctrl+N
tworzy nowy rekord. Zapisy i konflikty RowVersion działają jak dotychczas.

## Kolumny i hierarchia

Koło zębate w wierszu nagłówków kontrahentów, walut i kursów otwiera wspólny
konfigurator. Wspólny `ConfigurableTable` rezerwuje dla niego nieruchome pole
przy prawej krawędzi tabeli, niezależnie od szerokości i liczby kolumn. Tło
nagłówka wypełnia całą dostępną szerokość, także przy krótkim zestawie kolumn.
Nagłówki przewijają się razem z danymi w pozostałym obszarze, bez nachodzenia
na przycisk. Zębatka nie jest kolumną danych i nie występuje w wierszach rekordów.
Zaznacz kolumny i wybierz Zapisz mój widok. Co najmniej jedna jest wymagana.
Konfigurator pokazuje źródło bieżącego widoku.

Tytuły używają polskich nazw: „Kolumny tabeli: Kontrahenci”, „Waluty” lub
„Kursy walut”. Techniczne Contractors/Currencies/CurrencyRates pozostają
identyfikatorami API i zapisanych konfiguracji.

1. Własny widok ma pierwszeństwo; jest prywatny i wspólny dla stanowisk i firm
   tej instalacji, przypisany do UserId, a nie loginu/komputera.
2. Domyślny widok instalacji dotyczy osób bez własnych kolumn. Administrator
   zapisuje go osobnym przyciskiem „Zapisz jako domyślny dla wszystkich”.
3. Fabryczne kolumny kontrahenta: Kod, Nazwa kontrahenta, NIP, Kraj, Status, Miasto.

„Resetuj mój widok” usuwa własne kolumny i rozmiar strony tej tabeli, odsłaniając
domyślny widok instalacji (lub fabryczny). „Resetuj domyślny widok instalacji”
usuwa tylko konfigurację globalną. Administrator może mieć własny widok; zapis
globalny nie zastępuje go. Sam zapis rozmiaru strony nie tworzy własnej kopii
kolumn administratora.

Dostępne są pola głównego rekordu, nadrzędna nazwa/kod, daty i wszystkie istniejące
biznesowe pola wybranego adresu rejestrowego: nazwa, ulica, budynek, lokal, kod,
miasto, region, kraj, aktywność i preferencja. Adres rodzaju Id=1 wybieramy kolejno
według preferencji, aktywności i najmniejszego Id. Adresy innych rodzajów,
kontakty i rachunki nie trafiają do tabeli. Daty są opisane UTC, w formacie
`yyyy-MM-dd HH:mm UTC`, tak samo w prezentacji i wyszukiwaniu.

PESEL nie ma w danych ani katalogu dla osoby bez ManageContractorPesel w bieżącej
firmie. Globalny widok tego nie zmienia. Po utracie uprawnień pola są odfiltrowane;
gdy zapisany zestaw stał się całkowicie niedostępny, działa bezpieczny widok fabryczny.
Nowe kolumny nie pojawiają się automatycznie we własnym zestawie; reset pozwala
przyjąć aktualne ustawienia domyślne. Konflikt zapisu (409) wymaga ponownego otwarcia
konfiguratora i porównania stanu, bez wymuszonego nadpisania.

## Jawne wdrożenie developerskiego Network

Decyzja właściciela z 2026-10-25: wszystkie bazy VATowca na SQLEXPRESS i LocalDB
są developerskie; nie ma danych biznesowych wymagających zachowania. Wolno
usuwać/resetować/odtwarzać je i jawnie wykonywać migracje bez osobnej zgody.
Korzystaj z istniejącego środowiska; izolacja ma wynikać z potrzeby konkretnego
testu. Można zatrzymać developerskie API i po pracy przywrócić jego działanie.
Zbędne zasoby utworzone przez test sprzątaj automatycznie, zachowując raporty.
Pełne zasady: [AGENTS.md](../AGENTS.md). Nie obejmują baz/usług innych aplikacji
ani nie zmieniają Identity, audytu, uprawnień, izolacji firm i współbieżności.
Personal jest wstrzymany; nie uruchamiaj jego testów ani pozostałości instalacji.

Migracja jest jawna; zwykły start API nie migruje ani nie resetuje baz.
W istniejącym Network wykonaj następujące kroki:

1. Sprawdź lokalny SQLEXPRESS, dokładną nazwę Vatowiec_System, znacznik własności
   Vatowiec.LocalTestPurpose, trwałą tożsamość instalacji i znaną historię EF.
   Dla tej zmiany oczekiwana migracja poprzednia to
   20261024120721_PasswordLifecycleAndArchivedUsers. Sam prefiks nazwy nie wystarcza.
2. Zatrzymaj właściwe developerskie API i klientów wymagających wyłączności.
   Zakończ standardowy build Debug i kontrole wymagające wyłączności. Nie twórz
   alternatywnych katalogów kompilacji. Wcześniej zaliczonych testów nie powtarzaj
   bez istotnej zmiany albo nowej wątpliwości.
3. Przejrzyj wygenerowany offline SQL migracji 20261025121050_TableViews:
   tworzy tylko security.TableViews, audit.TableViewChanges, indeks, constraint,
   trigger niezmienności audytu i wpis historii System. Nie zmienia baz firm.
4. Wykonaj jawną migrację istniejącego System i kontrolę schematów:

   ```powershell
   dotnet run --project tools/database/Vatowiec.DatabaseTools -c Debug --no-build -- migrate-system
   ```

   Narzędzie weryfikuje lokalną instancję, własność celu i znaną historię przed
   migracją. Stosuje wszystkie oczekujące migracje, dlatego wcześniej sprawdź ich
   listę. Backup nie jest obowiązkowy dla danych demonstracyjnych; polecenie
   backup-system służy próbom backup/restore i przyszłej kontrolowanej aktualizacji
   instalacji wymagającej zachowania danych. Nie tworzymy kopii tylko po to, by
   chronić obecne dane testowe.
5. Sprawdź historię EF, obie nowe tabele i trigger, następnie uruchom aktualne API
   według AGENTS.md. Zweryfikuj HTTPS 7243, prawidłowy schemat i odczyt/zapis widoków
   przez istniejące uwierzytelnianie, CSRF i uprawnienia. Nie zmieniaj certyfikatów,
   portów, kont ani konfiguracji integracji, gdy zadanie tego nie wymaga.

Przy błędzie sprawdź cel i stan transakcji, popraw problem i jawnie dokończ
migrację albo odtwórz zweryfikowaną developerską bazę. Operacje te są już
zatwierdzone przez właściciela. Nie przenoś tego uprawnienia do zwykłego API:
bootstrap i start aplikacji nadal nie nadpisują zastanych baz.

## Testowanie i ręczna próba

Standardowy Debug. Wcześniej zaliczone zestawy pozostają dowodem dla niezmienionego
kodu; nie uruchamiaj ich ponownie wyłącznie z powodu zmiany zasad środowiska.
Nową kontrolę wykonaj po wdrożeniu na istniejącym Network, ponieważ ten cel nie
był dotychczas migrowany. Preferuj SQLEXPRESS bez dodatkowych instancji.
Historyczny TableViewSqlTests celowo sprawdzał prywatny potok, granicę migracji
i dwie tożsamości instalacji; izolacja wynikała z celu tej próby. Jego ponowne
uruchomienie wymaga odrębnego zadania Personal, a zbędne zasoby nowego przebiegu
należy później posprzątać. Nie twórz kolejnych fixture tylko dla ochrony demonstratora.
Rzeczywiste testy UI wymagają osobnego procesu (jeden globalny Avalonia lifetime).

Testy obejmują hierarchię, migrację poprzednia→nowa bez zmiany kont, równoległy
zapis z dwóch sesji, kontrolę wersji, atomowość audytu i jego niezmienność,
CSRF, izolację dwóch fizycznych firm, PESEL (dane/metadane/wyszukiwanie), ponowne
wykorzystanie loginu, restart, parametryzację i translację wszystkich kolumn do SQL.
Natywna próba sprawdza logowanie Personal/Network, menu, pasek, F2/Enter/dwuklik,
blokadę zapisu w podglądzie, przejście do edycji, konfigurator i okno 940×650.
Obraz UI i raport prywatnych celów SQL znajdują się w
`%LOCALAPPDATA%\Vatowiec\Verification\ADR-039` (istniejący katalog weryfikacji).

Ręcznie, po jawnej migracji Network:

1. Sprawdź zwinięte HTTPS w Network. Zaloguj dwie osoby z różnymi prawami;
   obejrzyj menu i dolny pasek. Próby Personal pozostają historycznym wynikiem.
2. Otwórz firmę, sprawdź automatyczną listę aktywnych, strony 25/50/100/200,
   zmianę firmy i szybkie zmiany wyszukiwania przy wolnym połączeniu.
3. W konfiguratorze pokaż Miasto/REGON, wyszukaj ich wartości, następnie ukryj
   kolumnę i sprawdź, że to pole już nie wyszukuje. Sprawdź brak PESEL bez prawa.
4. Zaznacz rekord. Pokaż/F2/Enter/dwuklik: brak zapisu, dostępne zakładki i
   szczegóły. Edytuj: zwykły zapis i istniejący konflikt RowVersion.
5. Zapisz własny i globalny widok na różnych kontach/stanowiskach. Sprawdź oba
   osobne resety, powrót do fabrycznego i trwałość po ponownym uruchomieniu.
6. Otwórz Waluty oraz Kursy walut, ich konfiguratory i operacje kontekstowe.
   Sprawdź zachowanie podsekcji ustawień bez wywoływania rejestrów/NBP w tle.

Nie wykonano prób na Linux ani osobnej zmiany DPI Windows.

## Wyniki wcześniejszej implementacji — 2026-10-25

- Solution Debug: 0 błędów, 0 ostrzeżeń.
- Zestaw ogólny: 528 zaliczonych, 136 pominiętych przez jawne bramki środowiskowe.
- Osobno: nowa próba SQL/API (1), nowe rzeczywiste UI (1), regresja rzeczywistego
  UI administracji/bezpieczeństwa (1) — wszystkie zaliczone. Ostatnia należy do
  wcześniej pominiętego zestawu: łącznie **531 różnych zaliczonych testów**, 135
  pozostałych niewykonanych. Powtórzeń nie doliczamy.
- Test SQL obejmuje prywatny potok Personal i host Network TestServer, dwa fizyczne
  cele firmowe, migrację, daty, adres rejestrowy, ochronę PESEL, dwa stanowiska,
  konflikt równoległych zapisów, audyt i ponowne wykorzystanie loginu.
- Wygenerowano offline skrypt nowej migracji System. TRX SQL i skrypt:
  `%LOCALAPPDATA%\Vatowiec\Verification\ADR-042`. Obraz UI i manifest izolowanej
  próby: istniejący katalog `Verification\ADR-039`.
- `git diff --check` i kontrola formatowania whitespace zmienionych plików C#:
  poprawne. Pełny `dotnet format --verify-no-changes --no-restore` zgłasza istniejące
  problemy formatowania/kodowania poza zakresem, m.in. AdministrationDialogs i stare
  migracje; nie wykonywano zbiorczego formatowania tych plików.

W powyższej, wcześniejszej próbie nie resetowano ani nie migrowano istniejącego
SQLEXPRESS lub normalnego Personal. Operacje SQL dotyczyły prywatnych fixture.
Wdrożenie Network po decyzji właściciela raportujemy osobno poniżej. Nie zmieniono
portów, HTTPS, certyfikatów, zapory ani transportu potoków. Bez Commit/Push/Release.

## Wdrożenie istniejącego Network po decyzji właściciela — 2026-10-25

- Usunięto sprzeczne ograniczenia developerskie z AGENTS.md: zgoda obejmuje
  wszystkie zweryfikowane bazy VATowca na SQLEXPRESS i LocalDB, przebudowę schematów,
  jawne migracje, ponowną inicjalizację i potrzebny restart developerskiego API.
  Nie wymaga ochrony fikcyjnych danych przez kolejne instancje ani obowiązkowe kopie.
- Zaktualizowano kontrolę schematu DatabaseTools: oczekuje teraz migracji
  20261025121050_TableViews oraz obu nowych tabel System. Pozostają kontrole
  lokalnego serwera, własności baz, znanej historii i obu fizycznych baz firmowych.
- Po zatrzymaniu właściwego API wykonano solution Debug: **0 błędów, 0 ostrzeżeń**.
  Następnie migrate-system zakończyło się powodzeniem na istniejącym
  localhost\SQLEXPRESS / Vatowiec_System. Nie tworzono dodatkowych instancji ani baz.
  Nie było potrzeby resetu; schematy obu firm pozostały bez migracji.
- Próba przez rzeczywisty HTTPS z normalną walidacją certyfikatu potwierdziła:
  logowanie Identity, odczyt fabrycznych kolumn, zapis własnego widoku i rozmiaru
  strony, ten sam widok UserId w drugiej firmie, odrzucenie zapisu bez CSRF (400),
  konflikt nieaktualnej wersji (409) i wyszukiwanie SQL po widocznych kolumnach.
  Tymczasowy widok z tej próby zresetowano przez API, a sesję wylogowano (204).
- Trigger niezmienności audytu jest aktywny. Liczniki istniejących kont, praw,
  konfiguracji i pozostałych tabel System są zgodne z pomiarem sprzed migracji;
  przybył wpis EF i sesje logowania. Równolegle zalogował się zwykły klient
  i zapisał własne preferencje; nie usuwano jego sesji ani ustawień.
  Osiem plików konfiguracji/kluczy Network zachowało SHA-256 z pomiaru odniesienia.
- API pozostaje uruchomione w osobnym widocznym terminalu PowerShell
  (PID terminala **55148**, rzeczywisty PID API **8964**). HTTPS localhost:7243/health
  odpowiada **200**, SQLEXPRESS działa. Nie zmieniano portów ani certyfikatów.
- Nie powtarzano wcześniejszych 531 zaliczonych testów. Nie uruchamiano testów
  Personal ani wcześniejszego skryptu sprzątania. Nowy przebieg nie utworzył baz,
  instancji SQL ani trwałych fixture do późniejszego sprzątania; zachowano raporty.

Dowody poza repozytorium, w katalogu %LOCALAPPDATA%/Vatowiec/Verification/ADR-042:
network-before-deployment.json, network-deployment-build.log,
network-deployment-migration.log, network-deployment-http.json oraz
network-after-deployment.json. Brak prób Linux i osobnej zmiany DPI pozostaje
ograniczeniem wcześniejszej weryfikacji. Bez Commit i Push.

## Korekta układu ADR-042 — 2026-10-25

Pierwsza natywna próba `WorkspaceLayoutUiTests` korzystała wyłącznie z atrapy API Network,
bez SQL, połączeń do rejestrów i uruchamiania Personal. Dla szerokości okna
900, 1050 i 1200 sprawdza wspólny nagłówek kontrahentów, walut i kursów:
szeroki zestaw kolumn, oba końce przewijania, synchronizację nagłówków/danych,
brak nakładania zębatki, kliknięcie rzeczywistym wskaźnikiem i zapis konfiguracji
z jedną kolumną. Sprawdza też prawostronne KONTO, otwieranie myszą i klawiaturą,
strzałkę FIRMA → KONTO, Escape oraz położenie rozwiniętej listy wewnątrz okna.
TRX i obrazy: `%LOCALAPPDATA%/Vatowiec/Verification/ADR-042/layout`.

### Stałe pole zębatki — ponowna korekta

Usunięto pozycjonowanie zębatki zależne od sumy szerokości kolumn. Nagłówek
korzysta ze zwykłego Grid z przewijaną częścią `*` i stałym polem przycisku 36 px.
Obszar danych ma tę samą pozostałą szerokość co przewijany nagłówek. Pełne tło
obejmuje również puste miejsce pomiędzy krótkim zestawem kolumn a zębatką.

Aktualna próba natywna sprawdza 900 i 1200 px, maksymalizację oraz powrót do
900 px. Dla kontrahentów zapisuje przez konfigurator zestawy 3 i 15 kolumn;
dla walut i kursów — 3 oraz wszystkie dostępne kolumny (6/8). Sprawdza początek,
środek i koniec przewijania, pozycję każdej pary nagłówek–komórka, równą szerokość
obu obszarów przewijania, poziomy pasek oraz klikanie zębatki. Próba używa atrapy
API bez SQL i nie uruchamia Personal. TRX, pomiary JSON i obrazy:
`%LOCALAPPDATA%/Vatowiec/Verification/ADR-042/fixed-header`.

### Polskie nazwy i wspólny wygląd

Wspólny styl Desktop obejmuje nagłówki i komórki Kontrahentów, Walut oraz Kursów
walut. Jasnoniebieskie nagłówki mają wysokość 48, Segoe UI 14 i półgruby tekst;
komórki mają odstępy 8/6 i minimalną wysokość wiersza 32. Dłuższy tekst nadal
może się zawijać. Zaznaczenie i podświetlenie wiersza są wspólne.

Ten sam wygląd otrzymały listy firm, użytkowników, wyników szukania nadrzędnego,
podmiotów grupy, szczegółów kontrahenta (adresy/kontakty/rachunki), limitów,
sposobów płatności, weryfikacji, konfliktów kursów i operacji walutowych oraz
historie administracji i polityki haseł. Pomocnicze listy zachowują istniejącą
treść i obsługę; otrzymały nagłówek sekcji. Formularze nadawania uprawnień
pozostają formularzami. Checkboxy walut i operacje zbiorcze zachowują dotychczasowe
wiązania, kontrolę uprawnień i potwierdzenie zapisu.

Wspólny katalog polskich etykiet `TableDisplayNames` działa wyłącznie w Desktop.
Nie zmieniono identyfikatorów tabel, API, baz, zapisu preferencji ani układu zębatki.
Nie dodano sortowania, przeciągania, kolejności ani ustawień szerokości kolumn.

Weryfikacja: nowy natywny `TableAppearanceUiTests` na atrapie API Network przy
900/1200 px, wszystkie trzy tytuły, style nagłówków/wierszy/zaznaczenia, zapis
i odświeżenie widoku, kliknięcie zębatki oraz checkboxy i operacje zbiorcze walut
(zaznacz widoczne/wszystkie, wyczyść, filtr, anulowanie, dezaktywacja i aktywacja).
Osobno: 7 testów nazw i operacji zbiorczych oraz natywny test administracji.
Nie powtarzano wcześniejszych 72 pomiarów pozycjonowania zębatki. Nie uruchamiano
Personal ani testów SQL. TRX i obrazy:
`%LOCALAPPDATA%/Vatowiec/Verification/ADR-042/table-appearance`.
