# VATowiec Personal 0.1.0-preview — rzeczywiste próby ADR-040

Data: 2026-10-24. Windows x64, istniejący LocalDB **17.0.4025.3**,
.NET SDK 10.0.402 na maszynie budującej. To próba odizolowanej instalacji na
komputerze deweloperskim, **nie test czystego Windows**. Nie ma dostępnej VM,
VBoxManage/vmrun/Get-VM ani Windows Sandbox. Nie instalowano nowego LocalDB.

## Artefakty

- EXE: `D:\Repozytorium\Vatowiec\Vatowiec\artifacts\personal-releases\0.1.0-preview\Vatowiec-Personal-0.1.0-preview-win-x64-Setup.exe`.
- Rozmiar: **90 752 469 bajtów** (wartość należy sprawdzić z aktualnym plikiem przy przebudowie).
- SHA-256: `fe8f2d7d0f8c894a9d4d34ef2d470f1b777dbab431f068f9593d08d8a1a628ef`.
- Obok: `payload-manifest.json` (679 plików, indywidualne rozmiary/SHA-256),
  `SHA256SUMS.txt`, `publish` z Desktop i osobnym `Server`.
- Payload: 243 325 648 bajtów. Oba runtime self-contained: .NET 10.0.13,
  API dodatkowo ASP.NET Core 10.0.13. Bez trimowania/AOT/single-file.
- Instalator nie jest podpisany Authenticode. Nie jest to deklaracja produkcyjnej
  gotowości ani ukończenia E15. Nie wykonywano commit/push.

## Kompilacja i testy

`dotnet build -c Debug`: sukces, **0 błędów i 0 ostrzeżeń**.
Osobne publikacje Desktop i API Release win-x64 self-contained: sukces.
Inno Setup: sukces. Nie zmieniano OutputPath/BaseOutputPath ani bin/obj.

Debug, `Category!=SqlIntegration`, `VATOWIEC_PERSONAL_SETUP_TESTS=1`:
**519 zaliczonych, 1 pominięty** (natywny test UI wymaga osobnej flagi).
Następnie ten test wykonano z `VATOWIEC_REAL_UI=1`: **1 zaliczony, 0 pominiętych**.
Raporty TRX w `tests/Vatowiec.Tests/TestResults/personal-distribution-debug.trx`
i `personal-distribution-native-ui.trx`. Nie uruchamiano destrukcyjnego runnera
ani SQLIntegration na zastanych bazach. Włączone rzeczywiste testy Personal
tworzyły wyłącznie nowe fixture LocalDB i zachowały ich dane.

Dodatkowe regresje obejmują zgodny/starszy/nowszy/rozbieżny schemat, LocalDB
odnajdywany bez PATH, prywatny pipe, odmowę dostępu, CSRF, logowanie,
tożsamość WCM, przerwaną inicjalizację, restart i awarie własnego procesu.
Detekcja została poprawiona po próbie: wersja produktu to 17, wersja pliku 2025.

## Rzeczywisty cykl instalacyjny — 43 kontrole, completed=true

Raport:
`C:\Users\admin\AppData\Local\Vatowiec\Verification\ADR-040\2b40a75f815e4db4806dc61a64b3e0c8\result.json`.
Obok logi instalatora/reinstalacji/deinstalatora i snapshoty wartości rejestru Network.
Nowy katalog danych:
`%LOCALAPPDATA%\Vatowiec\Personal.WizardTest.2b40a75f815e4db4806dc61a64b3e0c8`.
Program instalowano w `Verification\ADR-040\<GUID>\Program`, poza repozytorium.
Program testowy odinstalowano; jego bazy, manifest, klucze, preferencje i fikcyjne
poświadczenie WCM celowo pozostawiono. Żadnej zastanej instalacji Personal nie
nadpisano. Skrypt odmawia prób przy zastanych skrótach lub rejestracji Personal.

| Próba | Wynik |
|---|---|
| EXE i SHA-256, instalacja per-user, kod zakończenia 0 | OK |
| Osobna rejestracja, menu Start i pulpit, argument `--personal` | OK |
| Każdy z 679 plików porównany z publish-manifest | OK |
| API z zainstalowanego `Server`, poza repozytorium | OK |
| Rzeczywiście załadowane coreclr.dll Desktop/API z ich katalogów, bez globalnego runtime | OK |
| API Personal bez nasłuchu TCP | OK |
| Rzeczywisty kreator Avalonia: administrator, firma, dwie bazy | OK |
| Login, wybór firmy, zapis kontrahenta z formularza | OK |
| Zamknięcie klienta i jego dokładnie przypiętego API, exit 0 | OK |
| WCM, UserId, firma domyślna i wybór zapamiętywania po restarcie | OK |
| Reinstalacja przy otwartym kliencie, łagodne zamknięcie API | OK |
| Manifest, preferencje i dotychczasowe klucze zachowane bajt w bajt | OK |
| Te same pliki obu baz, pominięcie kreatora, odczyt wcześniejszego kontrahenta | OK |
| Odinstalowanie usuwa tylko program, rejestrację i skróty | OK |
| Ponowna instalacja po odinstalowaniu rozpoznaje tę samą instalację i WCM | OK |
| Odinstalowanie przy otwartym kliencie również łagodnie kończy jego API | OK |
| Dane, manifest, klucze i preferencje pozostają po obu deinstalacjach | OK |
| Rejestracja Network zachowana; PID i oba listenery Network niezmienne podczas prób | OK |
| Oba Network `/health`: HTTP 200 ze standardową walidacją TLS | OK |

Przed końcowym przebiegiem poprawiono **skrypt testowy**: snapshot rejestru ma
porównywać rzeczywiste wartości, bez zmiennych metadanych PowerShell; test wyboru
firmy musi uwzględnić automatyczne otwarcie firmy domyślnej. Wcześniejszych
niezakończonych przebiegów nie przedstawiamy jako zaliczonych.

Network pozostawiono w widocznym PowerShell (PID terminala 36536), API **PID 45872**:
`127.0.0.1:7243`, `[::1]:7243`, `10.0.0.147:7443`. Bez zmian konfiguracji,
certyfikatów, DNS, zapory, routera i istniejących baz SQLEXPRESS.
Widoczność okna konsoli serwera sprawdzono przez AttachConsole/GetConsoleWindow
i IsWindowVisible (true); samo istnienie procesu PowerShell nie było uznane za dowód.

## LocalDB — zweryfikowane pochodzenie, bez redystrybucji MSI

Oficjalny [Download Center 108836](https://www.microsoft.com/en-us/download/details.aspx?id=108836)
wskazał bootstrapper:
`https://download.microsoft.com/download/2e652cd0-da06-4fc0-836a-b75ec45035a9/SQL2025-SSEI-Expr.exe`.
SHA-256: `fa7e1fabc9a2e9c9cdab0d1512bcb30d2949133147db057ac530f510e5270680`.
Authenticode **Valid**, Microsoft Corporation, certyfikat
`AB172913A2960A224809EE8A0C371CD47A079B72`. Instalator wymaga identycznego hasha;
skrypt budujący dodatkowo ponawia weryfikację podpisu.

Wykonano wyłącznie **pobranie nośnika**, nie instalację:
`/ACTION=Download /MEDIATYPE=LocalDB /QUIET /ENU`. Wynik MSI: ProductVersion
17.0.1000.7, 68 452 352 bajty, SHA-256
`c8cf7455b79f4018c895098392bb80c2b5cba0d1e3ecfa22dd6fad7873894561`,
Authenticode Valid/Microsoft. Odczytano jego metadane i licencję bez wykonywania
MSI. Zawiera nieprawidłową licencję CTP2.0 z datą 2018. Dlatego **nie dołączono MSI**.
Microsoft [potwierdza ten problem](https://learn.microsoft.com/en-us/sql/sql-server/sql-server-2025-known-issues?view=sql-server-ver17#incorrect-license-agreement-for-localdb-installer).
Bootstrapper online umożliwia przejście przez oficjalny interfejs Microsoftu,
nie akceptuje licencji ani nie rozwiązuje za użytkownika jej sprzecznych warunków.
Przy błędnej licencji należy przerwać, dopóki Microsoft nie wyjaśni warunków.

## Niezweryfikowane / konieczne przed szerszą dystrybucją

- Czysty Windows 10/11 bez SDK, runtime, LocalDB i zastanych bibliotek natywnych.
  Własny coreclr potwierdzono, ale to nie zastępuje testu zależności natywnych.
- Gałąź instalacji brakującego LocalDB: rzeczywiste UAC, odmowa/anulowanie,
  instalacja i restart. Celowo nie odinstalowano istniejącego silnika ani nie
  instalowano go bez odrębnej zgody. Otwarta kwestia poprawnej EULA Microsoftu.
- Brak pełnego wariantu offline. Offline działa przy już obecnym LocalDB.
- Reinstalacja tej samej wersji została sprawdzona; przejście na przyszłą wersję
  z nowym schematem nie. Istnieje bezpieczna odmowa, ale osobny migrator z
  backupem/odtworzeniem pozostaje kolejnym etapem. Instalator nie migruje SQL.
- Pełne odtworzenie na innym komputerze/koncie, eksport DPAPI i upgrade po awarii
  zasilania nie były wykonywane. Nie należy traktować samych zachowanych MDF
  jako przetestowanego backupu.
- Podpisanie VATowca, test SmartScreen/antywirusa oraz zatwierdzenie własnej
  licencji projektu pozostają przed publiczną dystrybucją.

Instrukcja: [instalacja, aktualizacja, odinstalowanie](../personal-windows-installation.md).
Decyzje: [ADR-040](../adr/ADR-040-personal-instalator-windows.md).
