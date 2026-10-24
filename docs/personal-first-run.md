# VATowiec Personal — kreator pierwszego uruchomienia

Stan implementacji: etap 2, zweryfikowany 2026-10-24 na rzeczywistym LocalDB
17.0.4025.3 i w natywnym oknie Avalonia, standardowy Debug.
Decyzja: [ADR-038](adr/ADR-038-personal-pierwsze-uruchomienie.md).

Aktualizacja ADR-039: kreator korzysta obecnie z controllera procesu i lokalnego
Named Pipe, bez portu i certyfikatu .NET. [Aktualna instrukcja uruchomienia,
zamykania i odzyskiwania](personal-running.md). Poniższe wyniki etapu 2 opisują
historyczny test HTTPS, zastąpiony nowym transportem bez zmian modelu danych.

## Uruchomienie i obsługa

Edycję Personal wybiera skrót do `Vatowiec.Desktop.exe` z argumentem `--personal`.
Nie wymaga to wpisywania poleceń przez użytkownika. W źródłach klient odszukuje
standardowe API Debug; docelowo instalator dostarczy serwer w podkatalogu `Server`.
Skrót Network nie otrzymuje tego argumentu. Nie zmieniamy domyślnego profilu API.
Na komputerze deweloperskim przygotowano skrót „VATowiec Personal (Debug)”
na pulpicie. Otwiera normalny kreator, bez terminala i bez fikcyjnych kont.

1. Kreator sprawdza obecność SQL Server 2025 LocalDB (17.x). Brak składnika
   pozostawia formularz zablokowany i wyjaśnia potrzebę instalacji; niczego nie
   pobiera i nie instaluje sam. Po instalacji można ponowić sprawdzenie.
2. Użytkownik wpisuje własny login, hasło i jego powtórzenie oraz nazwę firmy.
   NIP jest opcjonalny; podany wymaga 10 cyfr i poprawnej sumy kontrolnej.
   Nie potwierdzamy nadania numeru ani nie odpytujemy rejestrów.
3. „Przygotuj instalację” pokazuje kolejne etapy: prywatną instancję LocalDB,
   bazę systemową, fizycznie osobną bazę firmy oraz administratora i uprawnienia.
   Początkowa polityka wymaga minimum 12 znaków i wszystkich czterech klas;
   zapis ostatecznie waliduje wspólny serwerowy UserManager. Politykę można
   później zmienić w istniejących ustawieniach instalacji.
4. „Przejdź do logowania” uruchamia normalne API Personal i dotychczasowy ekran
   logowania. Należy zalogować się własnym loginem/hasłem; kreator nie zapisuje
   poświadczenia w Credential Manager ani nie włącza autologowania.
5. Przy kolejnym uruchomieniu klient weryfikuje zapisany stan i przechodzi
   od razu do logowania. Nie tworzy dodatkowej bazy, firmy ani konta.

Hasło jest czyszczone z obu pól przy wysłaniu. Nie trafia do ustawień ani logów.
Administrator otrzymuje rolę instalacyjną i osobne wszystkie prawa do pierwszej
firmy, w tym PESEL; informacja jest pokazana przed zatwierdzeniem. Firma i konto
nie zawierają danych demonstracyjnych. Słowniki i nastawy startowe pochodzą
z normalnych migracji, bez seed-users.

## Dane, przerwanie i ponowne uruchomienie

Dane znajdują się w `%LOCALAPPDATA%\Vatowiec\Personal`:
`installation.json` to niesekretna konfiguracja i checkpointy;
`SqlData` zawiera nowe MDF/LDF, a `Keys` odrębne klucze integracji i sesji.
Nie przenoś plików otwartej bazy i nie usuwaj manifestu, aby „zacząć od nowa”.
To nie jest procedura resetowania. Brak konfiguracji przy istniejących danych
powoduje odmowę utworzenia zastępczej instalacji.

Po przerwaniu przed zatwierdzeniem konta uruchom program ponownie. Kreator
zachowa identyfikatory, bazy i zapisane dane firmy, poprosi ponownie o hasło
i wykona brakujące etapy. Zapisana nazwa/NIP nie są automatycznie zastępowane.
Po zatwierdzeniu administratora inicjalizacja jest trwale zamknięta, nawet gdy
aplikacja nie zdążyła pokazać sukcesu. Kolejne uruchomienie odczyta potwierdzenie
SQL i przejdzie do zwykłego logowania.

Nieznana baza, brak znacznika własności, obce pliki albo niezgodna konfiguracja
powodują odmowę. Kreator nie przejmuje i nie usuwa istniejących danych.
Drugi klient tej samej instalacji otrzymuje informację o już uruchomionym
Personal. Zamknięcie klienta zatrzymuje jego własny serwer; Network pozostaje
oddzielnym procesem. LocalDB i dane mogą pozostać na dysku po zamknięciu.

## Połączenie lokalne i przygotowanie komputera

Aktualne Personal używa Windows Named Pipes z DACL bieżącego użytkownika,
odmową Network SID i kontrolą tożsamości procesu po stronie klienta. Nie
nasłuchuje na TCP/LAN i nie potrzebuje certyfikatu. Gotowość jest potwierdzana
przez własny proces i `/health` w tym kanale. Certyfikat Let's Encrypt,
standardowa walidacja TLS oraz porty Network 7243/7443 pozostają nietknięte.

Brak LocalDB: [oficjalna procedura i ograniczenia](personal-localdb.md).
Instalator i dostarczenie runtime/LocalDB nadal wymagają oddzielnego etapu.
Nie deklarujemy gotowego instalacyjnego Personal 0.1.0-preview.

## Weryfikacja

Testy `PersonalSetupTests` i `PersonalSetupUiVerification` tworzą tylko nowe,
jednoznacznie nazwane katalogi `Personal.WizardTest.<GUID>`, instancje i bazy.
Nie wykonują DROP, resetu ani operacji na SQLEXPRESS. Pozostawiają dane do
inspekcji i zatrzymują tylko własne testowe instancje. Próby przerwania obejmują
stan przed transakcją konta oraz po COMMIT przed publikacją konfiguracji.

Uruchomienie weryfikacji dla współautorów (po zwolnieniu standardowego Debug):

```powershell
dotnet build Vatowiec.slnx -c Debug --no-restore
$env:VATOWIEC_REAL_UI = '1'
$env:VATOWIEC_REAL_POLICY_API = '0'
$env:VATOWIEC_PERSONAL_SETUP_TESTS = '1'
dotnet test tests/Vatowiec.Tests -c Debug --no-build --no-restore --filter 'Category!=SqlIntegration' --logger 'trx;LogFileName=personal-wizard-debug.trx' --results-directory TestResults
```

Nigdy nie zastępuj tego historycznym `DatabaseTools test`.

### Wyniki z 2026-10-24

- Kompilacja całej solution Debug: **0 błędów, 0 ostrzeżeń**.
- **506/506** testów bez historycznych SqlIntegration i bez trzech osobnych
  przypadków RealLocalDb; obejmuje natywne Avalonia, wcześniejsze moduły oraz
  osiem nowych przypadków walidacji/wersji/izolacji profilu.
- Dwa osobne przypadki RealLocalDb przeszły wraz z ośmioma testami powyżej:
  raport `TestResults/personal-wizard-integration-debug.trx` ma **10/10**.
- Dodatkowa próba rzeczywistego nagłego zakończenia własnego workera podczas
  tworzenia firmy i wznowienia przeszła; raport
  `personal-wizard-process-interruption-debug.trx` ma **2/2** (drugi przypadek
  ponawia kontrolę zamknięcia polityki SQL wobec Network).
- Po dodaniu kontroli graceful shutdown ponowiono test natywnego Avalonia:
  `personal-wizard-native-debug.trx` **1/1**, oba własne serwery zakończyły pracę
  poprawnie, bez awaryjnego Kill. Obraz formularza: `TestResults/personal-wizard.png`.
- Łącznie **509 różnych zaliczonych testów**; powtórzeń nie doliczamy.
  Żaden test z tych wybranych przebiegów nie został pominięty. Historycznych
  testów SqlIntegration na istniejącym SQLEXPRESS celowo nie uruchamiano.

Sprawdzono rzeczywiste pełne migracje nowych baz, utworzenie dokładnie jednego
konta i firmy, rolę instalacyjną, osobny CompanyAccess i cztery wpisy atomowego
audytu; logowanie przez istniejące HTTPS/Identity/CSRF i odczyt profilu firmy;
restart z tymi samymi identyfikatorami, odmowę ponownej inicjalizacji i odmowę
drugiego klienta; niepoprawne hasło i NIP; wznowienie przed zapisem konta,
po COMMIT przed publikacją manifestu oraz po faktycznym przerwaniu procesu.
W Avalonia sprawdzono wyczyszczenie haseł, komunikaty błędów, przejście do
logowania, wybór firmy, ponowne uruchomienie i pominięcie formularza inicjalizacji.

Połączenia testów są konstrukcyjnie ograniczone do ich nowych prywatnych
instancji i dokładnych nazw baz. Polityka odrzuca również próbę użycia
`localhost\SQLEXPRESS/Vatowiec_System` jako celu Personal. Nie wykonywano
operacji SQL na istniejących bazach Network ani na MSSQLLocalDB/prototypie.
Testowe dane i instancje pozostawiono do inspekcji; własne instancje zatrzymano.

Nie zweryfikowano czystego komputera bez SDK/LocalDB, rzeczywistej instalacji
brakującego składnika, zaniku zasilania i awarii dysku. Brak LocalDB jest
obsługiwany komunikatem i blokadą formularza, ale nie odinstalowywano istniejącego
składnika, aby wymusić tę sytuację. Certyfikat/instalator pozostają kolejnym etapem.
