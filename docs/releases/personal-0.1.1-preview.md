# VATowiec Personal 0.1.1-preview — wyniki poprawki pierwszego uruchomienia

Data: 2026-10-25. Windows x64, zastany LocalDB **17.0.4025.3**, .NET SDK 10.0.402.
Nie reinstalowano ani nie aktualizowano LocalDB. Nie resetowano kont/baz i nie
wykonywano migracji zastanych baz Network lub zakończonych instalacji Personal.
Decyzja i warunki wznowienia: [ADR-041](../adr/ADR-041-personal-ustawienia-poczatkowe.md).

## Przyczyna i wartości początkowe

0.1.0 wymagał 12 znaków i czterech klas zarówno w walidacji wejścia kreatora,
jak i w polityce zapisywanej przez migrację System. Nowy bootstrap Personal
zapisuje własną politykę przed wywołaniem UserManager, w tej samej transakcji
co administrator i audyt. Wspólny walidator Identity nadal egzekwuje odczytaną
politykę z DB. Nie zmieniono seedów/konfiguracji Network.

| Nowa instalacja/firma Personal | Zapisana wartość |
|---|---|
| Złożoność haseł | Wyłączona; MinimumLength=1 |
| Wielkie/małe litery, cyfra, znak specjalny | Wszystkie false |
| Historia i okresowa zmiana haseł | Obie false; nieaktywne parametry N=5 i 90 dni |
| Puste hasło | Odrzucone w kreatorze i API |
| Waluty | 149 pozycji; tylko PLN aktywny |
| Pobieranie A/B/C | Wszystkie false; zero automatycznych kursów |
| MF i VIES | Production |
| MF WL2 | `https://wykazpodatnikowvat.mf.gov.pl/api2/` |
| GUS | Test |

Kreator pokazuje informację o produkcyjnych MF/VIES, także po ukryciu formularza
po zakończeniu konfiguracji, przed przejściem do logowania. Nie wykonuje zapytań
do rejestrów. Administrator może potem włączyć politykę lub zmienić rejestry.
Późniejszy zapis Production zachowuje normalne potwierdzenie i zabezpieczenia API.

## Kompilacja i regresja

- Standardowy `dotnet build -c Debug`: **0 błędów i 0 ostrzeżeń**.
- `dotnet test ... -c Debug --no-build --filter Category!=SqlIntegration`,
  `VATOWIEC_PERSONAL_SETUP_TESTS=1`, `VATOWIEC_REAL_UI=1`, `VATOWIEC_REAL_POLICY_API=0`:
  **524 zaliczone, 0 błędów, 0 pominiętych**.
- Raport: `tests/Vatowiec.Tests/TestResults/personal-011-all.trx`.
- Obie publikacje Release win-x64 self-contained oraz Inno Setup: sukces.
  Brak trim/AOT, niestandardowych bin/obj i zmian instalatora Network.

Nowe rzeczywiste testy potwierdziły logowanie hasłem **„a”**, odrzucenie pustego,
odczyt polityki przez API, ręczne włączenie wymagań, historii i ważności bez restartu,
aktywację EUR bez flag NBP, brak importów i kursów, odmowę niepotwierdzonego zapisu
Production oraz trwałość późniejszych własnych ustawień po restarcie.
Dotychczasowy hash nadal pozwala logować się po włączeniu bardziej wymagającej polityki.

Wznowienie sprawdzono na **faktycznej publikacji API 0.1.0**: własny nowy worker
został przerwany przed zapisem administratora, po utworzeniu starych baz i profilu.
0.1.1 wznowił tę samą instalację i przyjął proste hasło, bez odtwarzania baz.
Wstrzyknięta awaria audytu wycofała politykę, rejestry i ich historie.
Sprawdzono też kolejną nową bazę firmy na tej samej prywatnej instancji:
pełne migracje, profil, 149 walut, PLN, brak flag i kursów, pojedynczy audyt,
niezmieniona RowVersion PLN. To test wspólnej operacji provisioningowej,
nie nowego ekranu ani endpointu tworzenia następnej firmy — takich zmian nie dodawano.

Regresja obejmuje Identity, sesje, CSRF, waluty, adaptery rejestrów na atrapach,
bootstrap i natywną Avalonia. Nie wywoływano produkcyjnych rejestrów ani NBP.
Inicjalizatory nie korzystają z HTTP; dodatkowa sonda System.Net.Http w ich
kontrolowanym kontekście wykonania wykazała zero wywołań. Nie jest to pomiar
ruchu całego komputera. Starych/destrukcyjnych SQLIntegration nie uruchamiano.

## Instalacja oraz aktualizacja 0.1.0 → 0.1.1

Na komputerze zastano zainstalowany Personal użytkownika. Aby go nie nadpisać,
zbudowano warianty weryfikacyjne Inno z osobnym AppId i skrótami, z **identycznymi
rzeczywistymi payloadami** 0.1.0 i 0.1.1. Testowy AppId jest taki sam w obu wersjach.
Właściwy instalator 0.1.1 zachowuje produkcyjny AppId Personal. Nie instalowano go
na istniejącej instalacji użytkownika.

`tools/release/Test-PersonalDefaultsRelease.ps1`: **Fresh i Upgrade zakończone sukcesem**.
Raport zbiorczy:
`%LOCALAPPDATA%\Vatowiec\Verification\ADR-041\e8fe54b5497643cbb7f8e6319625a849\result.json`.

| Scenariusz | Wynik |
|---|---|
| Nowa 0.1.1, **51 kontroli** | completed=true |
| 0.1.0 → 0.1.1, **48 kontroli** | completed=true |
| Rejestracja Windows wskazuje właściwą wersję | OK |
| Pliki zainstalowane poza repo odpowiadają manifestowi payloadu | OK |
| Desktop/API używają własnych runtime; API bez TCP | OK |
| Rzeczywisty kreator, proste hasło, logowanie, firma, kontrahent | OK |
| Nowe wartości polityki, walut i rejestrów w SQL | OK |
| Edycja polityki przez UI starego API z własnym audytem | OK |
| Update otwartego klienta: łagodne zamknięcie jego API | OK |
| Własna polityka, rejestry, każda wersja waluty i liczby audytów po update | Identyczne |
| Firma, kontrahent, manifest, klucze i preferencje po update | Zachowane |
| Hasło WCM, UserId i firma domyślna po restarcie | Zachowane |
| Odinstalowanie, ponowna instalacja i ponowne odinstalowanie | Dane zachowane |
| Rejestracja/pliki i kontrolowane hashe danych istniejących Personal/Network | Bez zmian |
| Network podczas prób: ten sam PID, dwa listenery, oba /health | OK/200 |

Raporty scenariuszy:

- `%LOCALAPPDATA%\Vatowiec\Verification\ADR-040\84c237daac644ab6b3441cca296c708f\result.json`;
- `%LOCALAPPDATA%\Vatowiec\Verification\ADR-040\cdb10ab7566148c8b54f0f1d1f1700bb\result.json`.

Testowe programy odinstalowano, własne bazy i dane fixture pozostawiono.
Nie odczytywano haseł użytkownika ani nie enumerowano WCM. Testowy odczyt metadanych
SQL w PowerShell 5.1 używa wyłącznie prywatnego IPC do dokładnie sprawdzonej nowej
instancji LocalDB; stary System.Data.SqlClient nie obsługuje tam żądania TLS.
To nie zmienia używanego przez aplikację Microsoft.Data.SqlClient ani TLS API.
Pierwsza próba narzędzia kontrolnego zakończyła się na tym ograniczeniu sterownika;
nie zaliczono jej. Raporty powyżej dotyczą pełnych powtórzonych przebiegów.

## Artefakty

Katalog: `D:\Repozytorium\Vatowiec\Vatowiec\artifacts\personal-releases\0.1.1-preview\`.

- `Vatowiec-Personal-0.1.1-preview-win-x64-Setup.exe`: **90 756 387 bajtów**.
- SHA-256: `36d99ca15ca30652191cbd129c33dc261917e9c85695bf6147d872eb18a5209d`.
- `payload-manifest.json`: 679 plików, 243 341 435 bajtów payloadu, indywidualne SHA-256.
- `SHA256SUMS.txt`: skróty EXE i manifestu. Kopie raportów w katalogu wydania.
- Wersja zasobu pliku 0.1.1.0, produktu 0.1.1-preview; AppId produkcyjny Personal
  pozostaje `{C6DB8A6A-11F0-46EA-90DF-8F73D735B0B4}`.

LocalDB nadal nie jest dołączony: przy obecnym silniku instalacja/praca offline,
przy jego braku dotychczasowy wariant online. Nie zmieniono mechanizmu ani wersji
zależności. Instalator VATowca nadal nie jest podpisany Authenticode.

Network pozostawiono w widocznym PowerShell, API **PID 34972**, terminal **PID 49504**,
`127.0.0.1:7243`, `[::1]:7243` i `10.0.0.147:7443`. Oba `/health` HTTP 200 przy
standardowej walidacji TLS. Nie zmieniano certyfikatów, DNS ani zapory.

## Niezweryfikowane

- Czysty Windows bez SDK/runtime/LocalDB, instalacja zależności, UAC i problem
  licencji MSI Microsoftu opisany w ADR-040 pozostają bez nowego testu.
- Nie aktualizowano faktycznej instalacji użytkownika; testy dotyczyły izolowanych
  odpowiedników instalatora ze zmienioną tożsamością instalacji i tym samym payloadem.
- Kolejne firmy sprawdzono na poziomie wspólnego provisioningu SQL, bez nowego
  przepływu tworzenia firm w UI/API.
- Pełne backup/restore na innym komputerze i przyszła migracja zmieniająca schemat
  pozostają poza tym wydaniem. Nie dodano migracji ani automatycznych resetów.
- Nie deklarujemy gotowości produkcyjnej ani zakończenia E15. Bez commit/push.
