# ADR-040: instalator VATowiec Personal dla Windows

Data: 2026-10-24. Status: wdrożenie preview, ograniczenia w raporcie prób.
Wymagania: dystrybucja E15, ORG-003/004/008/009/011, T-012/T-016.
Rozszerza ADR-036–039, bez zmian modelu danych i uwierzytelniania.

Aktualizacja 0.1.1-preview: [ADR-041](ADR-041-personal-ustawienia-poczatkowe.md)
określa domyślne ustawienia nowych instalacji Personal i bezpieczne wznowienie
bootstrapu 0.1.0; zakończone instalacje zachowują własną konfigurację.

## Dystrybucja

Osobny Inno Setup: AppId `{C6DB8A6A-11F0-46EA-90DF-8F73D735B0B4}`,
domyślny katalog `%LOCALAPPDATA%\Programs\VATowiec Personal`, skróty
`VATowiec Personal` z argumentem `--personal`. Instalacja bieżącego użytkownika
bez podnoszenia uprawnień. Network zachowuje swoje AppId, katalog i skróty.
Wyłącznie Windows 10/11 x64; LocalDB 2025 nie wspiera Windows ARM64.

Desktop i API publikowane osobno, Release win-x64 self-contained .NET 10,
bez trim/AOT/single-file. API znajduje się w `Server`. Release nie szuka API
w repozytorium/Debug. Nie potrzeba SDK, IDE, terminala ani certyfikatu .NET.
Transport pozostaje prywatnym Named Pipe z kontrolą SID/PID ADR-039. Desktop
nie otwiera SQL; konfigurację API tworzy prywatny worker bez ustawień Network.

Skrypt `tools/release/build-personal-release.ps1` publikuje do jednego payloadu
w artifacts, weryfikuje pliki, licencje, manifest i SHA-256, kompiluje osobny
`Vatowiec-Personal.iss`. Bez alternatywnych bin/obj. Binariów nie śledzimy w Git.
Debug pozostaje konfiguracją testów. Licencje brakujące w NuGet przypięto
do wersji, commitów i skrótów w `licenses/Personal/sources.json`.

## LocalDB — świadomy wariant online

Sprawdzamy HKLM64 `SQL Server Local DB\Installed Versions\17.0` i pliki DLL/EXE
SQL 2025. Worker sprawdza **ProductVersion 17** i rzeczywiste `SqlLocalDB versions`.
FileVersion SQL 2025 wynosi `2025.170.*`, dlatego FileMajorPart nie jest wersją
silnika. Narzędzie odnajdujemy przez rejestr, bez zależności od PATH. Detekcja
nie tworzy i nie zmienia instancji.

Microsoft [opisuje możliwość dołączania LocalDB](https://www.microsoft.com/en-us/download/details.aspx?id=108836).
Jednak pakiet faktycznie pobrany przez oficjalny Express, `SqlLocalDB.msi`
17.0.1000.7, zawiera licencję CTP2 z datą 2018 i zakazem publikacji. Nie zmieniamy
tych warunków ani nie akceptujemy ich za użytkownika. Microsoft
[potwierdza błąd licencji i opisuje obejście przez Express](https://learn.microsoft.com/en-us/sql/sql-server/sql-server-2025-known-issues?view=sql-server-ver17#incorrect-license-agreement-for-localdb-installer).

**MSI nie jest dołączony; to nie jest pełne wydanie offline.** Przy obecnym
LocalDB instalacja i praca nie wymagają sieci. Przy jego braku osobna strona
prosi o zgodę, pobiera oficjalny bootstrapper Express z przypiętym SHA-256
i uruchamia jego interfejs. Użytkownik wybiera Download Media → LocalDB,
otwiera MSI i rozpatruje warunki Microsoftu. Tylko zależność może wymagać UAC.
Nie uruchamiamy instalacji pełnego Express ani automatycznej akceptacji EULA.
Przy błędnych warunkach użytkownik ma przerwać; nie deklarujemy rozwiązania
problemu licencyjnego Microsoftu przez samo pobranie inną ścieżką.

Źródłem bootstrappera jest Download Center 108836. URL i hash przypięto w .iss
i `Get-LocalDb2025Bootstrapper.ps1`. Przed budowaniem sprawdzany jest SHA-256
i ważny Authenticode Microsoft Corporation; instalator wymaga identycznego
hasha pobrania. Zmiana pliku powoduje odmowę i wymaga przeglądu nowej wersji.
Brak mirrorów i wyjątków TLS. Brak gotowego LocalDB zatrzymuje kopiowanie
programu, również w trybie cichym, bez mylącego komunikatu gotowości.

## Aktualizacje i zachowanie danych

Dane są poza `{app}`: manifest z InstallationId, SqlData, klucze DPAPI,
preferencje w `%LOCALAPPDATA%\Vatowiec\Personal`; hasła w WCM. Paczka nie zawiera
kont demonstracyjnych, baz, backupów, sekretów, PDB, appsettings ani certyfikatów.
Odinstalowanie zachowuje wszystkie te dane i współdzielony LocalDB.

Instalator identyfikuje okna po pełnej ścieżce obrazu równej instalowanemu
Desktop.exe, zachowuje uchwyt procesu, wysyła WM_CLOSE i czeka maksymalnie 65 s.
Istniejąca ochrona szkiców i oczekiwanie na własne API pozostają aktywne.
Brak zamknięcia zatrzymuje operację. Nie ma kill po nazwie/PID ani force-close
Restart Managera. Network i Personal Debug z innego katalogu nie są zamykane.

Zwykły start porównuje pełne historie migracji System i firmy z programem;
odrzuca starszy, nowszy i rozbieżny schemat bez automatycznego Migrate/seed/reset.
To wydanie nie dodaje migracji ani migratora istniejących danych. Wersja
wymagająca aktualizacji schematu musi dostarczyć osobną procedurę ze spójnym,
zweryfikowanym backupem wszystkich baz, manifestu i kluczy, kontrolą celu,
jawną migracją i próbą odtworzenia kompletu. Do tego czasu działa bezpieczna
odmowa; nie wolno jej omijać przez ponowny kreator lub częściowe migracje.
DPAPI wymaga starego konta Windows; kopia plików na nowe konto nie przenosi sekretów.

## Weryfikacja

`Test-PersonalRelease.ps1` odmawia dotknięcia zastanej instalacji/skrótu Personal.
Tworzy nowy katalog programu i nowy Personal.WizardTest.GUID. Sprawdza instalator,
UIA Avalonia, API spoza repo, kreator, login, kontrahenta, restart, reinstalację
przy otwartym programie, WCM oraz zachowanie baz, manifestu, kluczy i preferencji
po odinstalowaniu. Fixture pozostają. [Raport prób](../releases/personal-0.1.0-preview.md)
podaje granice; bez czystej maszyny Windows nie deklarujemy ukończenia E15.
