# Logowanie i kartoteka kontrahentów w Avalonia

Klient korzysta wyłącznie z API HTTPS. Katalog firm i uprawnienia są w
Vatowiec_System, a kontrahenci w osobnych Vatowiec_Company_A_Dev/B_Dev.
Pierwszy etap odczytu opisuje [ADR-016](adr/ADR-016-desktop-logowanie-i-kontekst-firmy.md).
**Aktualizacja 2026-10-22:** dodawanie, edycję, wyszukiwanie, stronicowanie,
dezaktywację i konflikt opisuje [kartoteka kontrahentów](contractors.md), ADR-017/018/019/020.
Cztery zakładki obejmują grupy, adresy, kontakty i rachunki. ADR-021 dodaje edycję
identyfikatorów z normalizacją/ostrzeżeniami oraz piątą zakładkę Weryfikacje
(MF/WL2, GUS, VIES, domyślnie TEST): [instrukcja](identifiers-and-registries.md).
Szczegóły i historię można otwierać także w B w trybie odczytu. Id kontrahenta jest
int; lokalna kontrola nie potwierdza rejestracji, a TEST/MOCK nie potwierdzają produkcji.
Operator edytuje w A, a w B nadal tylko czyta.

## Uruchomienie

Z głównego katalogu projektu:

```powershell
dotnet build Vatowiec.slnx --no-restore
dotnet dev-certs https --check --trust
dotnet run --project src/Server/Api --no-build --launch-profile https
```

Jeśli brakuje zaufania do certyfikatu, wykonaj lokalnie `dotnet dev-certs https --trust`
i zaakceptuj systemowy komunikat. Nie wyłączaj walidacji certyfikatów.
Lokalny SQL Express i trzy demonstracyjne bazy muszą być dostępne.
Start API nie wykonuje migracji ani seeda.

W drugim terminalu:

```powershell
dotnet run --project src/Desktop --no-build
```

Domyślny adres to `https://localhost:7243`. Można zmienić go w oknie logowania
albo przed uruchomieniem ustawić `$env:VATOWIEC_API_URL = 'https://localhost:7243'`.
Adres nie może zawierać ścieżki, loginu, hasła ani parametrów. Zmiana dotyczy
następnego logowania; nie zapisuje się w pliku.

## Operator: firma A i firma B

1. Wpisz login **operator.m0** i lokalnie skonfigurowane hasło. Kliknij **Zaloguj się**
   lub naciśnij Enter w polu hasła.
2. Zaznacz **Firma demonstracyjna A — dane fikcyjne** i kliknij **Otwórz firmę**.
   Możesz też otworzyć firmę dwuklikiem na jej pozycji. Pojedyncze kliknięcie tylko
   zaznacza; dwuklik otwiera klikniętą firmę, z taką samą kontrolą dostępu jak przycisk.
3. Okno pokazuje firmę, użytkownika i termin sesji. Kliknij **Pokaż kontrahentów / odśwież**.
   Zobaczysz kod DEMO, nazwę fikcyjnego kontrahenta A i kraj PL.
4. Kliknij **Zmień firmę**, wybierz **Firma demonstracyjna B — dane fikcyjne**,
   następnie **Otwórz firmę** i **Pokaż kontrahentów / odśwież**. Nazwa kontrahenta
   pochodzi teraz z B; dane A zostają usunięte z widoku przy zmianie firmy.
5. **Wyloguj** wraca do ekranu logowania i unieważnia sesję na serwerze.

Hasło operatora jest w istniejącym lokalnym user-secrets API pod kluczem
`DemoUsers:Operator:Password`; klient nie czyta tego magazynu automatycznie.
Aby poznać istniejącą wartość, możesz wyświetlić ją we własnym lokalnym terminalu
przez `dotnet user-secrets list --project src/Server/Api`. Wynik zawiera sekrety:
nie wklejaj go do czatu, zgłoszeń ani logów. Konfigurację własnych haseł i jawny
`seed-users` opisuje [instrukcja kont](authentication.md#dwa-konta-demonstracyjne).
Hasło nie jest publikowane w repozytorium ani w tej instrukcji.

## Czytelnik i odmowa dostępu

Po wylogowaniu zaloguj się jako **czytelnik.m0**, z osobnym hasłem z lokalnego
`DemoUsers:Reader:Password`. Lista zawiera tylko A. Po otwarciu A zobaczysz
komunikat **Brak uprawnień do kartoteki kontrahentów**; przycisk pobierania jest
ukryty. B nie jest dostępna. API niezależnie od UI zwraca 403 przy bezpośrednim
żądaniu kartoteki A lub danych B. Test automatyczny tych odmów:

```powershell
dotnet run --project tools/database/Vatowiec.DatabaseTools --no-build -- verify-users
```

Można uruchomić dwie instancje desktopu jednocześnie. Każda ma osobny magazyn
cookie w pamięci, więc użytkownicy i wybory firm nie zmieniają sobie sesji.

## Brak połączenia i sesja

- Błąd adresu HTTP jest odrzucany przed wysłaniem danych logowania. Błąd sieci,
  TLS lub limit 20 sekund daje komunikat; po usunięciu przyczyny ponów operację.
- Jeśli pobranie listy firm po login się nie udało, użyj **Odśwież listę**.
- Odmowa 403 usuwa dane firmy i wraca do wyboru. Nie ma domyślnej bazy ani
  automatycznej zmiany firmy po błędzie.
- Sesja ma absolutny termin, domyślnie 30 minut. Po jego upływie także bezczynny
  klient czyści dane. 401 z API również przenosi do logowania. Hasło trzeba wpisać ponownie.
- Przy odświeżaniu kartoteki stare rekordy są czyszczone przed pobraniem nowych,
  więc błąd nie pozostawia danych wyglądających na aktualne.
- **Wyloguj** przy braku sieci usuwa lokalne cookie i dane, ale pokazuje ostrzeżenie,
  że serwer nie potwierdził unieważnienia. Sesja na serwerze zachowuje swój termin.
  Zamknięcie programu usuwa lokalną sesję; dla potwierdzonego logout użyj przycisku
  przy działającym połączeniu.

## Weryfikacja etapu

Historyczny build etapu ADR-016: bez błędów i ostrzeżeń. Zestaw tego etapu: **96/96** testów,
bez pominięć, w tym **35 SQL/API** i **61 bez SQL**. Zawiera 26 nowych przypadków:
23 transportu/modelu desktopu oraz 3 integracyjne z API/SQL.

```powershell
dotnet build Vatowiec.slnx --no-restore
dotnet run --project tools/database/Vatowiec.DatabaseTools --no-build -- test
dotnet format Vatowiec.slnx --verify-no-changes --no-restore
```

Aktualny runner czyści fikcyjne fixture DML wyłącznie w trzech istniejących bazach demonstracyjnych, bez DROP/CREATE; zatrzymaj wcześniej API i desktop. Po testach inicjalizuje demonstrator. Wynik TRX zapisuje w
TestResults. Zwykłe `dotnet test --no-build` bez włączenia SQL nie zalicza testów SQL;
raportuje je jako pominięte.

Rzeczywista próba Windows UI Automation potwierdziła logowanie operatora przez
HTTPS, dwie firmy, odczyt odrębnych nazw kontrahentów A/B, zmianę firmy i logout
w tym samym procesie Avalonia. Drugie konto czytelnik.m0 widziało wyłącznie A,
bez przycisku pobierania i danych kontrahentów. Nie jest to sprawdzenie aplikacji na Linux/macOS
ani pełny audyt dostępności UI.

## Ustawienia firmy i DataPort (ADR-022)

Kartoteka ma przycisk Ustawienia firmy, sekcję Integracje zewnętrzne → DataPort
z maskowanym nowym kluczem, stanem i potwierdzanym usunięciem. Operator A może
zarządzać kluczem; B i czytelnik widzą tylko stan. Zakładka Weryfikacje ma
osobny przycisk DataPort obok zachowanego oficjalnego GUS. Dane pośrednika
mogą pochodzić z cache, nie są bezpośrednim potwierdzeniem GUS TEST.
[Instrukcja i granice prób](dataport.md).

## Waluty i kursy NBP (ADR-023)

W głównej nawigacji jest **Słowniki → Waluty i kursy NBP**. Ten sam ekran
ustawień otwiera przycisk **Ustawienia firmy → Waluty i kursy NBP**.
Operator A może zaznaczać aktywne waluty i typy A/B/C, zapisywać ustawienia
oraz ręcznie pobierać najnowsze publikacje lub okres. B i czytelnik mają odczyt.
PLN nie wymaga kursu. Otwarcie ekranu i zapis ustawień nie odpytują NBP.
Lista kursów jest filtrowana/stronicowana przez SQL; daty i liczby są polskie.
Konflikt innej wartości wymaga jawnej decyzji z kontrolą wersji i audytem.
Ctrl+S zapisuje ustawienie, Esc wraca; niezapisany szkic wymaga decyzji.
[Pełna instrukcja, struktura i wyniki](currencies-and-nbp.md).
