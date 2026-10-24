# VATowiec Personal — uruchamianie i odzyskiwanie (ADR-039)

Uruchom ikonę **VATowiec Personal (Debug)** na pulpicie. Nie uruchamiaj ręcznie API.
Klient sam sprawdzi LocalDB i konfigurację. Przy pierwszym starcie pokaże
[kreator](personal-first-run.md), a przy istniejącej instalacji — zwykłe logowanie.
Serwer działa bez widocznego terminala, przez zabezpieczony kanał Windows.
Nie wpisujesz portu i nie instalujesz certyfikatu localhost. Network pozostaje osobny.

Potrzebne są Windows, SQL Server 2025 LocalDB i odpowiednie runtime .NET 10 /
ASP.NET Core. Normalne uruchomienie używa EXE, nie polecenia SDK. Instalator,
samodzielny pakiet runtime i próba na czystym Windows pozostają kolejnym etapem;
nie deklarujemy ich wykonania na podstawie tego komputera deweloperskiego.

## Codzienna praca

1. Kliknij ikonę. Poczekaj na sprawdzenie danych i gotowość lokalnego API.
2. Zaloguj się. Dotychczasowe zapamiętywanie w Windows Credential Manager,
   autologowanie, zmiana własnego hasła i domyślna firma działają normalnie.
3. Pracuj w firmie. Dostęp nadal podlega Identity, sesji, CSRF i CompanyAccess.
4. Zamknij okno. Program najpierw chroni niezapisane zmiany/aktywną operację,
   następnie kończy własne API i LocalDB. W czasie zamykania może pokazać
   „kończenie zapisów i zamykanie…”. Nie zamykaj wtedy procesów ręcznie.

Drugie kliknięcie informuje, że Personal już działa, i nie uruchamia drugiego
serwera. Zamknij informację i wróć do istniejącego okna. Po awarii nie usuwaj
plików `.lock`: blokada jest uchwytem Windows, a nie samą obecnością pliku.

Dane pozostają w `%LOCALAPPDATA%\Vatowiec\Personal`. Zamknięcie nie kasuje kont,
baz, audytu, manifestu, kluczy ani preferencji. Instalacja jest identyfikowana
przez trwały InstallationId; zmiana technicznej nazwy kanału nie zmienia
zapamiętanej tożsamości. Hasło nadal nie jest zapisywane w JSON ani logach.

Po przejściu z prototypu ADR-038 pierwszy raz wpisz hasło ręcznie. Login,
domyślna firma i wybrane flagi zostają; stare hasło przypisane jedynie do
localhost:18443 nie jest automatycznie przejmowane. Jeśli zapamiętywanie jest
włączone, poprawne logowanie zapisze hasło pod kluczem konkretnej instalacji/UserId.

## Sytuacje awaryjne

- **Brak LocalDB:** kreator podaje potrzebną wersję; nie instaluje składnika sam.
  Zainstaluj go według [instrukcji](personal-localdb.md) i uruchom program ponownie.
- **Chwilowy błąd uruchomienia:** zachowaj pliki, zamknij program i spróbuj ponownie.
  Kreator wznawia zapisane etapy; nie resetuje danych.
- **Awaria API:** klient blokuje dalszą pracę, usuwa sesję i pokazuje komunikat
  lokalnego serwera. Uruchom program ponownie; sprawdź ostatni zapis, jeśli nie
  dostałeś potwierdzenia. Nie ponawiamy zapisu po cichu.
- **Awaria klienta:** zamknięcie jego kanału uruchamia łagodne zatrzymanie API.
  Daj mu czas na zakończenie aktywnych żądań i LocalDB (zwykle kilka sekund).
  Potem uruchom ikonę ponownie. Nie powinien pozostać osierocony serwer.
- **Brak potwierdzenia zamknięcia:** ostrzeżenie wymaga sprawdzenia ostatniej
  operacji po ponownym uruchomieniu. Controller nie kończy Network ani procesów
  wskazanych niesprawdzonym PID. Nie usuwaj danych ani blokad, by wymusić start.

## Weryfikacja deweloperska

Standardowy Debug; wyniki, obrazy i raporty poza repozytorium:
`%LOCALAPPDATA%\Vatowiec\Verification\ADR-039`.

```powershell
dotnet build Vatowiec.slnx -c Debug --no-restore
$env:VATOWIEC_REAL_UI = '1'
$env:VATOWIEC_REAL_POLICY_API = '0'
$env:VATOWIEC_PERSONAL_SETUP_TESTS = '1'
dotnet test tests/Vatowiec.Tests -c Debug --no-build --no-restore --filter 'Category!=SqlIntegration' --logger 'trx;LogFileName=personal-lifecycle-all-debug.trx' --results-directory "$env:LOCALAPPDATA\Vatowiec\Verification\ADR-039"
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tools/personal/Verify-PersonalLifecycle.ps1
```

ExecutionPolicy dotyczy wyłącznie procesu uruchamiającego lokalny skrypt testu;
nie zmienia polityki systemu ani walidacji transportu. Skrypt sprawdza prawdziwy
skrót i uruchamia jego kopię z **nowym** `Personal.WizardTest.<GUID>` jako katalogiem
danych. Steruje rzeczywistym oknem przez Windows UI Automation. Nie dotyka
normalnej instalacji Personal. Przy kolejnych startach używa tej samej fixture,
żeby sprawdzić trwałość. Nie wykonuje DROP/resetu ani SQL na SQLEXPRESS.

### Wyniki

Weryfikacja 2026-10-24: kompilacja solution Debug **0 błędów, 0 ostrzeżeń**.
Wybrany pełny zestaw bez historycznych SqlIntegration: **513/513**, bez pominięć
(`personal-lifecycle-all-debug.trx`). Po dodaniu konwersji niesekretnych preferencji
ponowiono testy logowania i magazynu: **12/12**, w tym dwie nowe regresje
preferencji i komunikatu lokalnego połączenia
(`personal-preferences-debug.trx`). Łącznie **515 różnych testów**; powtórzeń
nie doliczamy. Natywny test Avalonia również przeszedł i zapisał obraz kreatora.

`Verify-PersonalLifecycle.ps1` zakończył się samodzielnie z **17/17** kontrolami:

- rzeczywista ikona/kopia skrótu z izolowanym katalogiem, EXE Desktop i potomny
  API bez widocznej konsoli, brak nasłuchu TCP/LAN;
- pierwsza inicjalizacja w rzeczywistym oknie, login, otwarcie firmy, zapis
  kontrahenta w formularzu Avalonia i jego odczyt po ponownym uruchomieniu;
- drugi start bez konkurencyjnego API, poprawne zamknięcie własnego API i LocalDB;
- rzeczywiste zabicie tylko własnego procesu klienta, EOF i zakończenie API;
- rzeczywiste zabicie tylko własnego API, informacja i blokada klienta, ponowny
  start po obu awariach bez nieaktualnej blokady i bez resetu;
- Network zachował **ten sam PID 53636** oraz listenery 7243/7443 przez całą
  próbę; oba `/health` nadal HTTP 200, z normalną walidacją TLS.

Raport próby: `process-Personal.WizardTest.435deda2bcab44069bd7691d0ad53129.json`
w powyższym katalogu wyników. Bazy i dane tej nowej fixture pozostawiono do
inspekcji, jej LocalDB i Personal API zatrzymano. Network pozostał uruchomiony
w widocznym PowerShell. Nie uruchamiano SQL na istniejącym SQLEXPRESS.

Testy integracyjne dodatkowo potwierdziły Identity/CSRF (401 bez sesji, 400 bez
tokena), atomowy zapis i audyt, Windows Credential Manager, autologowanie i firmę
domyślną po zmianie nazwy potoku, rozdzielenie instalacji/UserId, odmowę DACL
z ograniczonego tokenu i odrzucenie fałszywego serwera o innym PID. Podczas próby
zamykania aktywny zapis API był rzeczywiście zablokowany przez transakcję SQL;
serwer zaczekał i zapisał go po zwolnieniu blokady przed zakończeniem procesu.
Brak dostępnego narzędzia LocalDB zasymulowano ograniczając PATH tylko w środowisku
próby potomka; otrzymano czytelny błąd i poprawne ponowienie z tym samym manifestem,
bez utworzenia zastępczych baz.

Nie testowano czystego komputera bez SDK, instalacji brakującego LocalDB ani
zaniku zasilania. Test dostępu używa ograniczonego tokenu Windows i fałszywego
serwera tego samego konta; nie utworzono dodatkowych kont systemu Windows.

Przed instalatorem pozostają dystrybucja runtime/LocalDB i licencje, aktualizacje
z backupem, pełne restore/przeniesienie kluczy oraz próby na czystym Windows.
Projekt transportu Personal nie wymaga już instalowania certyfikatu lokalnego.
