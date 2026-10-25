# Audyt zgodności VATowiec Network z Linux x64

Data audytu: 2026-10-25. Raport diagnostyczny; bez zmian implementacyjnych.

**Obecny klient VATowiec Network kompiluje się dla `linux-x64`. Przygotowanie działającej wersji linuksowej jest wykonalne bez przebudowy architektury, ale jej działania nie potwierdzono na Linuksie.** Najważniejsze ograniczenia dotyczą zapamiętywania haseł, dystrybucji i braku testów na docelowym systemie.

Przeczytano [AGENTS.md](../AGENTS.md), odpowiednie fragmenty [specyfikacji](../SPECYFIKACJA.md), kod oraz zależności klienta. Analiza dotyczy wyłącznie Network: API i SQL Server pozostają na Windows.

| Komponent | Zgodność z Linux | Wymagane zmiany | Trudność |
|---|---|---|---|
| .NET 10 i C# | Tak na poziomie kompilacji | Zapewnienie .NET 10 i jego zależności na docelowej dystrybucji | Niska |
| Avalonia Desktop | Obsługa Linux dostępna; działanie aplikacji nieprzetestowane | Weryfikacja grafiki, skalowania, czcionek i obsługi okien | Niska–średnia |
| HTTP i HTTPS Network | Brak znalezionej zależności od Windows | Poprawny adres serwera i zaufanie do certyfikatu na Linux | Niska |
| Identity, cookies, CSRF | Mechanizm niezależny od systemu klienta | Nie wykryto potrzeby zmian protokołu | Niska |
| Ręczne logowanie | Kod przewiduje pracę bez dostępnego magazynu haseł | Test całego przebiegu na Linux | Niska |
| Preferencje użytkownika | Kod wieloplatformowy | Sprawdzenie katalogu użytkownika, uprawnień i zapisu | Niska |
| Windows Credential Manager | Niedostępny | Wyłączenie odpowiednich funkcji albo nowy adapter magazynu | Średnia |
| Zapamiętywanie hasła i autologowanie | Obecna implementacja nie zapewnia ich na Linux | Adapter linuksowy oraz dostosowanie komunikatów i opcji | Średnia |
| DPAPI dla GUS/DataPort | Pozostaje na serwerze Windows | Brak zmian potrzebnych z powodu systemu klienta | Brak w tym zakresie |
| NuGet i biblioteki natywne | Publikacja dobrała składniki Linux | Sprawdzenie zależności systemowych podczas uruchamiania | Niska–średnia |
| Ścieżki, konfiguracja, zasoby | Nie znaleziono blokady w ścieżce Network | Test systemu plików i wyglądu interfejsu | Niska |
| Instalacja, aktualizacja, uruchamianie | Obecne narzędzia wydania dotyczą Windows | Osobna procedura dystrybucji, uruchamiania i aktualizacji Linux | Średnia |

## 1. Czy obecny kod kompiluje się dla linux-x64?

**Tak.** Wykonano jedną publikację [projektu Desktop](../src/Desktop/Vatowiec.Desktop.csproj) przy użyciu SDK **10.0.402**, z parametrami Debug, `linux-x64`, `--self-contained false`, bez AOT i przycinania.

Publikacja zakończyła się kodem **0**, bez zgłoszonych błędów i ostrzeżeń. Sprawdzono:

- cel `.NETCoreApp,Version=v10.0/linux-x64`;
- plik wykonywalny w formacie **ELF x86-64**;
- obecność linuksowych bibliotek natywnych SkiaSharp i HarfBuzz;
- wymaganie środowiska **Microsoft.NETCore.App 10.0** na komputerze docelowym.

Wszystkie katalogi wynikowe i pośrednie próby znajdowały się poza repozytorium. **Nie uruchamiano programu na Linuksie.**

## 2. Które konkretne fragmenty zależą od Windows?

Najistotniejszy dla Network jest [WindowsLoginCredentialStore](../src/Desktop/Services/WindowsLoginCredentialStore.cs). Korzysta z `advapi32.dll`: `CredReadW`, `CredWriteW`, `CredDeleteW` i `CredFree`. Metoda `CheckWindows` w tej klasie jawnie odrzuca pozostałe systemy.

[Konstruktor MainWindow](../src/Desktop/MainWindow.axaml.cs) wybiera ten magazyn bez sprawdzania platformy. Samo utworzenie obiektu nie wywołuje jeszcze funkcji Windows, więc nie jest to automatyczna blokada startu.

Pozostałe znalezione zależności trzeba rozdzielić według miejsca wykonania:

| Kod | Zależność | Znaczenie dla klienta Network |
|---|---|---|
| [PersonalPipeClient](../src/Desktop/Services/PersonalPipeClient.cs) | Windows Named Pipes, identyfikacja procesu przez `kernel32.dll` | Dotyczy Personal |
| [PersonalBackend](../src/Desktop/Services/PersonalBackend.cs) | Windows i uruchamianie API jako EXE | Dotyczy Personal |
| [GusKeyProtector](../src/Modules/Core/Infrastructure/Registries/GusKeyProtection.cs) i [DataPortKeyProtector](../src/Modules/Core/Infrastructure/Registries/DataPortKeyProtector.cs) | Windows DPAPI | Działają na pozostającym na Windows serwerze |
| [build-release.ps1](../tools/release/build-release.ps1) i [Vatowiec.iss](../tools/release/Vatowiec.iss) | Publikacja `win-x64`, Inno Setup, skróty Windows | Obecna procedura wydania nie przygotowuje wersji Linux |

Kod Personal jest kompilowany razem z aplikacją, ale [wybór trybu w App](../src/Desktop/App.axaml.cs) uruchamia go dopiero po przekazaniu `--personal`. Jego obecność **nie zablokowała publikacji Network**.

## 3. Jakie funkcje prawdopodobnie nie będą działały?

Bez zmian nie będą działały zapisywanie i odczytywanie haseł przez Windows Credential Manager, a więc również autologowanie wymagające tego magazynu. Obsługa „Zapomnij dane logowania” może zgłaszać jego niedostępność.

[Logika preferencji i zapamiętywania logowania](../src/Desktop/ViewModels/MainViewModel.LoginPreferences.cs) przechwytuje błędy magazynu i przewiduje kontynuowanie pracy z ręcznym logowaniem. Oznacza to, że brak Credential Managera nie powinien sam w sobie uniemożliwiać korzystania z firm, lecz wymaga potwierdzenia na Linux. Mogą też pojawiać się nieadekwatne komunikaty o Windows.

Uruchomienie interfejsu zależy dodatkowo od środowiska graficznego i bibliotek systemowych. W opublikowanych zależnościach występuje backend **Avalonia.X11**; dla sesji Wayland należy sprawdzić dostępność i działanie XWayland. Nie potwierdzono natywnej pracy Wayland.

## 4. Czy istnieją zamienniki wieloplatformowe?

Tak. Istniejący [ILoginCredentialStore](../src/Desktop/Services/LoginPreferences.cs) umożliwia dodanie adaptera bez zmiany protokołu logowania.

Na Linux można rozważyć Secret Service przez D-Bus, np. GNOME Keyring, oraz obsługę środowiska KDE. Trzeba uwzględnić brak usługi, zablokowany magazyn i odmowę dostępu. Wieloplatformowe rozwiązanie zwykle oznacza wspólny interfejs i adaptery dla poszczególnych systemów.

Najtańszy wariant pierwszej wersji to **ręczne logowanie i niedostępne opcje zapamiętywania hasła**. Dodanie magazynu linuksowego wymagałoby również przyszłego rozszerzenia zasad [ADR-032](./adr/ADR-032-lokalne-logowanie-i-firma-domyslna.md), które obecnie dopuszczają wyłącznie Windows Credential Manager. W tym audycie niczego nie zmieniano.

## 5. Czy Avalonia wymaga zmian w interfejsie?

**Nie znaleziono potrzeby przepisywania ekranów.** [Program](../src/Desktop/Program.cs) używa `UsePlatformDetect()`, a [główne okno](../src/Desktop/MainWindow.axaml) i [motyw aplikacji](../src/Desktop/App.axaml) korzystają ze standardowych mechanizmów Avalonia.

Potrzebne byłyby dostosowania dostępności opcji logowania i komunikatów zależnych od Windows. Dopiero próby GUI pokażą, czy konieczne są korekty rozmiarów, czcionek, fokusu lub skalowania.

[FileLoginPreferencesStore](../src/Desktop/Services/LoginPreferences.cs) używa `LocalApplicationData` i `Path.Combine`, bez zakodowanego dysku Windows. Na Linux powinien korzystać z katalogu danych użytkownika, zwykle pod `$XDG_DATA_HOME` albo `~/.local/share`. Zapis obejmuje niesekretne preferencje, nie hasła ani cookies.

## 6. Czy można korzystać z dotychczasowego API Network bez zmian?

**Tak na poziomie kodu i protokołu.** [VatowiecApiClient](../src/Desktop/Services/VatowiecApiClient.cs) korzysta z `HttpClient`, cookies w pamięci, standardowej walidacji TLS i tokenów CSRF. Nie używa Windows Authentication ani bezpośredniego połączenia z SQL. Identity i sprawdzanie uprawnień pozostają po stronie serwera.

Warunkiem jest osiągalny adres HTTPS i certyfikat zaufany na Linux. Zaufanie zapisane w Windows nie przenosi się automatycznie. Domyślne `localhost` na komputerze linuksowym wskazywałoby ten komputer, więc klient musi otrzymać rzeczywisty adres serwera.

Jeśli API obsługuje już inne stanowiska Network, nie widać potrzeby zmiany API z powodu Linux. Jeśli działa wyłącznie na loopback, pozostaje zwykłe przygotowanie dostępu sieciowego opisane w [instrukcji LAN/VPN](./server-lan-vpn.md). Nie zmieniano tej konfiguracji.

## 7. Ile pracy wymagałaby wersja linuksowa?

Szacunek dla jednej osoby znającej projekt, jednej wybranej dystrybucji x64 i dostępnego środowiska testowego:

| Wariant | Orientacyjny nakład |
|---|---:|
| Potwierdzenie działania podstawowego Network z ręcznym logowaniem | 1–3 osobodni |
| Używalne preview z prostą dystrybucją, instrukcją i regresją podstawowych ekranów | 3–6 osobodni łącznie |
| Zapamiętywanie haseł i autologowanie przez magazyn Linux | dodatkowo 3–6 osobodni |
| Doprecyzowanie dystrybucji, aktualizacji i testy na kilku środowiskach | dodatkowo 3–7 osobodni |

Pełniejsze wydanie to orientacyjnie **2–4 osobotygodnie**. Największa niepewność dotyczy środowisk graficznych, magazynów poświadczeń i utrzymania dystrybucji.

## 8. Jakie testy pozostają nieprzeprowadzone?

Nie wykonano na Linux:

- startu aplikacji i ładowania bibliotek natywnych;
- testów X11/XWayland, grafiki, DPI, czcionek i polskich znaków;
- rzeczywistego HTTPS, zaufania certyfikatów, loginu, CSRF, logoutu i wygaśnięcia sesji;
- wyboru firm, odmów dostępu, zapisów i konfliktów wersji;
- zapisu preferencji, restartu klienta i zachowania przy niedostępnym magazynie haseł;
- testów schowka, otwierania przeglądarki, instalacji i aktualizacji.

Zależności publikacji obejmują Avalonia **12.1.4**, SkiaSharp **3.119.4**, HarfBuzzSharp **8.3.1.3** i Tmds.DBus.Protocol **0.94.1**. Dobranie ich podczas publikacji potwierdza dostępność składników Linux, ale nie zastępuje testu z bibliotekami konkretnej dystrybucji.

## Ocena wykonalności i stan po audycie

**Ocena wykonalności: dobra.** Podstawowy Network z ręcznym logowaniem wygląda na zadanie rzędu kilku dni. Pełne dopracowanie poświadczeń i dystrybucji wymaga wyraźnie większego nakładu. Nie ma obecnie dowodu, że potrzebna byłaby przebudowa klienta lub zmiana API.

Podczas audytu nie zmieniono źródeł, projektów, konfiguracji, dokumentacji ani baz; nie zatrzymywano ani nie uruchamiano API. Końcowy `git status` audytu był czysty. Niniejszy dokument jest późniejszym zapisem raportu na polecenie użytkownika; przy jego zapisywaniu nie powtarzano analizy ani kompilacji.

Automatyczna kontrola wykonania odrzuciła usunięcie artefaktów komunikatem „blocked by policy”. W chwili zakończenia audytu pozostały wyłącznie w utworzonym na potrzeby próby katalogu tymczasowym `C:\Users\admin\AppData\Local\Temp\vscode-agent-codex-sandbox-YOAyqk\Vatowiec-LinuxAudit-9e038c914c4e4a3dbbc13f237c23ad02`, poza repozytorium.
