# Klient Windows 0.1.0-preview

Wydanie obejmuje wyłącznie Avalonia, dla Windows 10/11 **x64**. API i SQL
pozostają na serwerze. Klient zawiera .NET 10, więc stanowisko nie wymaga SDK,
Visual Studio ani repozytorium. To preview istniejących kartotek i administracji,
nie ukończony produkt księgowy ani deklaracja gotowości produkcyjnej.
Decyzja: [ADR-036](adr/ADR-036-instalator-klienta-windows.md).

## Budowanie

Wymagane na komputerze budującym: Windows x64, Git, .NET 10 SDK, dostęp do NuGet
oraz Inno Setup **6.3 lub nowszy**. Inno nie jest potrzebny użytkownikowi klienta.
Zaufane źródło: [JRSoftware](https://jrsoftware.org/isinfo.php), ewentualnie
pakiet winget (sprawdza hash instalatora z manifestu):

```powershell
winget install --id JRSoftware.InnoSetup --exact --source winget --scope user
```

Skrypt nie instaluje narzędzi automatycznie. Przy braku kompilatora podaje tę
instrukcję; można wskazać jego pełną ścieżkę przez `-IsccPath`.

Z katalogu repozytorium:

```powershell
git status --short
dotnet build Vatowiec.slnx -c Debug
$env:VATOWIEC_REAL_UI = '1'
$env:VATOWIEC_REAL_POLICY_API = '0'
dotnet test tests/Vatowiec.Tests -c Debug --no-build --filter 'Category!=SqlIntegration'
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tools/release/build-release.ps1
```

Przed buildem Debug zatrzymaj blokującą go instancję API, bez obchodzenia blokad
innym katalogiem. Po testach uruchom API Debug zgodnie z AGENTS.md. Nie wykonuj
resetującego runnera SQL ani migracji przy budowaniu klienta.

Skrypt wydania buduje **tylko Desktop i SharedContracts** w standardowym Release,
publikuje `win-x64 --self-contained true`, bez trimowania, AOT i single-file.
Nie zmienia `OutputPath`/`BaseOutputPath`. Sprawdza zależności, brak plików
serwera/sekretów/baz/testów/źródeł, usuwa PDB (także natywne z NuGet), kopiuje
licencje, kompiluje `.iss` i zapisuje hash. Tryb `-PublishOnly` przygotowuje
sam katalog klienta; **nie oznacza zbudowania instalatora**.

Wyniki, ignorowane przez Git:

```text
artifacts/releases/0.1.0-preview/
  Vatowiec-0.1.0-preview-win-x64-Setup.exe
  SHA256SUMS.txt
  payload-manifest.json
  publish/
```

Manifest zawiera wersję SDK, commit bazowy, informację o niezatwierdzonych
zmianach oraz ścieżki, rozmiary i SHA-256 plików klienta. Nie zawiera konfiguracji
lokalnej ani pełnych ścieżek użytkownika. Czysty Git jest sprawdzany przed
wydaniem; skrypt ostrzega przy zmianach, dopuszczając wymagany lokalny preview
bez commita. Odtworzenie takiego wydania wymaga także tych zmian roboczych.
Powtarzalny proces nie oznacza identycznego bajtowo EXE z innej wersji SDK/Inno.

Dla kolejnego wydania zmień metadane Desktop i uruchom skrypt z
`-Version '0.1.1-preview'`; wersję instalatora i plików skrypt przekazuje jawnie.
**Nie zmieniaj AppId w `.iss`**, bo odpowiada za ciągłość aktualizacji.
Nie twórz tagu/GitHub Release w ramach lokalnego budowania.

## Instalacja i pierwsze uruchomienie

1. Przenieś EXE i `SHA256SUMS.txt` na stanowisko. Porównaj wynik
   `Get-FileHash .\Vatowiec-0.1.0-preview-win-x64-Setup.exe -Algorithm SHA256`
   z sumą przekazaną zaufanym kanałem.
2. Uruchom instalator jako zwykły użytkownik. Domyślny katalog to
   `%LOCALAPPDATA%\Programs\VATowiec`. Nie jest wymagane podniesienie uprawnień.
3. Instalator tworzy skrót **VATowiec** w menu Start. Skrót na pulpicie jest
   opcjonalny. Windows pokazuje aplikację na liście zainstalowanych programów.
4. Podaj rzeczywisty adres HTTPS API otrzymany od administratora. Przykład
   `https://vatowiec.example.local:7243` jest fikcyjny. `localhost` na drugim
   stanowisku oznacza to stanowisko, a nie serwer. Wymagania serwera opisuje
   [osobna instrukcja LAN/VPN](server-lan-vpn.md).
5. Zaloguj się istniejącym kontem i wybierz udostępnioną firmę. Zapamiętanie
   hasła i firmy domyślnej działa jak dotychczas: [instrukcja](desktop.md).

To lokalny instalator **bez podpisu Authenticode**. Suma SHA-256 pozwala porównać
plik, ale nie zastępuje podpisu wydawcy. Nie opisujemy go jako podpisanego ani
nie zalecamy wyłączania zabezpieczeń Windows.

Brak połączenia, przekroczenie czasu i problem TLS mają osobne komunikaty.
Przy błędzie certyfikatu administrator sprawdza nazwę DNS, ważność, łańcuch
zaufania i zegar. Klient nie ma opcji ignorowania TLS, nie przyjmuje HTTP,
adresu z hasłem, ścieżką lub query ani przekierowania logowania.

## Aktualizacja i odinstalowanie

Zapisz edycje i zamknij klienta przed aktualizacją. Uruchom instalator nowej
wersji na tym samym koncie Windows. Stały AppId zachowuje miejsce instalacji;
Inno korzysta z Restart Manager do wykrycia blokujących procesów i proponuje
ich zamknięcie. Nie wymusza restartu klienta po cichu. Jeśli klient nie pozwala
zamknąć edycji, anuluj instalację, zapisz pracę i ponów.

Odinstalowanie: **Ustawienia Windows → Aplikacje → VATowiec → Odinstaluj**.
Usuwane są pliki instalacji i skróty. Preferencje w
`%LOCALAPPDATA%\Vatowiec\Desktop` oraz Windows Credential Manager pozostają
zarówno po aktualizacji, jak i odinstalowaniu. `.iss` nie wykonuje ich kasowania.
Aby świadomie usunąć hasło, użyj **Zapomnij dane logowania** przed odinstalowaniem.
Instalator nie kopiuje preferencji ani poświadczeń między kontami Windows.

## Licencje

Projekt używa nazwy wydawcy **Otwarty następca VATowca**, zgodnej z README.
Nie dodano fikcyjnej firmy ani cudzej grafiki. Licencja własnego kodu pozostaje
otwartą decyzją ADR-002; ten lokalny preview nie rozstrzyga jej ani nie jest
publicznym wydaniem kodu na MPL-2.0.

W instalacji `licenses/CLIENT-DEPENDENCIES.md` podaje rzeczywiste wersje,
autorów, licencje i repozytoria pakietów. Pełne teksty SkiaSharp/HarfBuzzSharp,
ANGLE, .NET i ich third-party notices są kopiowane z przywróconych pakietów
NuGet. Brak wymaganej licencji zatrzymuje skrypt. Teksty MIT zapisane w repo:

- Avalonia: [licence.md, commit z nuspec 12.1.4](https://github.com/AvaloniaUI/Avalonia/blob/2d79ba55fae1b1e71c69f93a668080c9029e6a31/licence.md).
- MicroCom: [LICENSE, commit z nuspec 0.11.6](https://github.com/kekekeks/MicroCom/blob/76785efcafd91b5902fd19dd11145f6dd655b7b4/LICENSE).
- Tmds.DBus: [COPYING, commit z nuspec 0.94.1](https://github.com/tmds/Tmds.DBus/blob/b4a7fed0b878f74cb54f7cca84d2889af4e596ba/COPYING).

Zachowano też wcześniejszą informację IbanNet (biblioteka serwera; jej DLL nie
jest częścią klienta). Nie skopiowano nupkg, kodu źródłowego ani narzędzi SDK.

Wyniki konkretnych prób i ograniczenia: [weryfikacja 0.1.0-preview](releases/0.1.0-preview.md).

## Powtórzenie prób okienkowych

Na interaktywnym pulpicie Windows, przy działającym lokalnym API Debug:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tools/release/Test-Release.ps1
# Pełny cykl tylko na koncie bez dotychczasowej instalacji i skrótów VATowca:
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tools/release/Test-Release.ps1 -RunInstaller
```

Skrypt sprawdza manifest, start EXE poza repozytorium, rzeczywiste HTTPS API
(celowo nieistniejący login, bez zmiany istniejących kont), edycję adresu,
odrzucenie HTTP oraz niezaufanego certyfikatu odizolowanego lokalnego serwera
testowego. Certyfikat nie jest instalowany w magazynie zaufania. Wyniki trafiają
do `TestResults`. Drugi wariant wykonuje cichą instalację, reinstalację,
uruchomienia i odinstalowanie. Odmawia dotknięcia zastanej instalacji/skrótu.
Porównuje hash istniejących preferencji (bez wypisywania treści), tworzy i usuwa
tylko unikalne fikcyjne poświadczenie testowe. Nie odczytuje cudzych haseł ani
nie enumeruje Credential Manager. Zalecane jest oddzielne testowe konto Windows.
