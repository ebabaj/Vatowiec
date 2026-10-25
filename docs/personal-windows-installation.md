# VATowiec Personal 0.1.1-preview — instalacja Windows

Windows 10/11 **x64**, SQL Server **2025 LocalDB 17.x**. Klient i API zawierają
.NET 10; nie potrzeba SDK, IDE, Git ani terminala. ARM64 nie jest wspierany.
Preview nie zostało jeszcze sprawdzone na czystym Windows.

## Instalacja

1. Uruchom `Vatowiec-Personal-0.1.1-preview-win-x64-Setup.exe` jako zwykły użytkownik.
2. Obecny LocalDB nie będzie reinstalowany; instalacja VATowca nie wymaga UAC.
3. Przy braku LocalDB użyj przycisku pobrania instalatora Microsoftu. To wariant
   **online**: LocalDB nie znajduje się w EXE. Potwierdź pobranie, wybierz
   **Download Media → LocalDB**, otwórz `SqlLocalDB.msi` i przeczytaj warunki.
   Tylko instalacja zależności może wymagać uprawnień administratora.
4. Microsoft zgłasza błędną licencję CTP w części MSI. Jeśli ją zobaczysz,
   przerwij i wyjaśnij poprawność warunków z Microsoftem. Nie akceptujemy ich
   automatycznie. Nie wybieraj instalacji pełnego Express lub zmian istniejących instancji.
5. Wróć i kliknij Dalej. Brak gotowej zależności zatrzymuje instalację.
6. Uruchom **VATowiec Personal** z menu Start lub opcjonalnego skrótu pulpitu.
   Kreator poprosi o własny login/hasło administratora, nazwę firmy i opcjonalny
   NIP. Utworzy prywatną instancję i osobne bazy System/firmy oraz nada dostęp.
   Nie ma haseł domyślnych ani kont demonstracyjnych.
   W nowej instalacji wystarczy każde niepuste hasło. Złożoność (minimum 1,
   wszystkie klasy wyłączone), historia i ważność haseł są wyłączone; administrator
   może później włączyć je w Ustawienia instalacji → Bezpieczeństwo → Polityka haseł.
   MF/WL2 i VIES startują w **PRODUKCJA**, GUS w **TEST**. Kreator i zwykły start
   nie odpytują tych usług. Pierwsza i każda nowa baza firmy mają 149 walut,
   aktywny tylko PLN, wszystkie flagi pobierania A/B/C wyłączone.
7. Zaloguj się. Kolejne uruchomienie wraca do tej samej instalacji.

Przy zainstalowanym LocalDB instalacja i praca działają offline; zewnętrzne
usługi nadal potrzebują Internetu. API działa bez konsoli przez prywatny Named
Pipe. Nie potrzebuje portów, certyfikatów deweloperskich ani konfiguracji Network.

## Aktualizacja i backup

Przed aktualizacją wykonaj spójny backup SQL **System i wszystkich firm**, sprawdź
go oraz zabezpiecz manifest, konfigurację i `Keys` z `%LOCALAPPDATA%\Vatowiec\Personal`.
Otwarte MDF/LDF nie zastępują backupu. DPAPI wymaga starego konta Windows;
przeniesienie sekretów na inny komputer to osobna procedura. Ten preview nie ma
jeszcze kreatora backup/restore. [Zasady i ograniczenia](personal-localdb.md).

Zapisz szkice i zamknij program. Uruchom nowszy instalator na tym samym koncie.
Zachowa katalog wykrytej instalacji. Otwartemu klientowi wyśle zwykłe żądanie
zamknięcia, z ochroną szkiców i oczekiwaniem na własne API. Jeśli program się
nie zamknie, operacja zostanie przerwana; proces nie jest zabijany.

Aktualizacja z 0.1.0 zachowuje politykę, rejestry i waluty zakończonej instalacji,
także dawne wartości domyślne. Nie osłabia istniejących haseł. Niedokończony
bootstrap można wznowić bez resetu, jeśli pozostały wyłącznie potwierdzone,
niezmienione dane początkowe i nie ma kont. Indywidualne ustawienia pozostają.
Szczegóły: [ADR-041](adr/ADR-041-personal-ustawienia-poczatkowe.md).

Kopiowanie plików nie wykonuje migracji. Przy niezgodnym schemacie którejkolwiek
bazy aplikacja odmówi pracy bez zmiany danych. Wymagana instrukcja konkretnego
wydania: kontrolowane migracje obu baz po zweryfikowanym backupie oraz możliwość
odtworzenia całego kompletu. Nie resetuj danych, nie uruchamiaj ponownie kreatora
i nie migruj jednej bazy w oderwaniu od reszty. Obecne wydanie nie dodaje migracji.

## Odinstalowanie

Ustawienia Windows → Aplikacje → VATowiec Personal → Odinstaluj.
Usuwane są pliki programu, jego skróty i rejestracja. Pozostają:

- bazy, InstallationId, klucze i preferencje w `%LOCALAPPDATA%\Vatowiec\Personal`;
- poświadczenia WCM związane z instalacją i UserId;
- współdzielony LocalDB i inne instancje;
- Network, jego konfiguracja i dane.

Ponowna instalacja na tym samym koncie używa zachowanych danych. Odinstalowanie
nie jest usuwaniem danych ani przenoszeniem instalacji.

## Budowanie i próby

Na maszynie budującej wymagane .NET SDK 10, NuGet i Inno Setup >= 6.3.

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tools/release/build-personal-release.ps1
# Tylko konto bez zastanej instalacji i skrótów Personal:
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tools/release/Test-PersonalRelease.ps1
# Bez nadpisywania zainstalowanego Personal: osobne testowe AppId, świeża instalacja i upgrade:
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tools/release/Test-PersonalDefaultsRelease.ps1
```

Wyniki: `artifacts/personal-releases/0.1.1-preview/`: EXE, `SHA256SUMS.txt`,
`payload-manifest.json`, publish Desktop/Server. `-PackageOnly` pakuje istniejący
payload; po zmianie kodu wymagane pełne budowanie. Skrypt sprawdza hash i podpis
bootstrappera Microsoftu, pobierając go przy braku cache. **Nie instaluje LocalDB**.
Nie gwarantuje identycznego EXE między kompilacjami (czas/kompresor).

Licencje bibliotek w `licenses` obu części. Własna licencja projektu pozostaje
decyzją do zatwierdzenia. Instalator VATowca nie ma podpisu Authenticode, więc
SmartScreen może ostrzegać. [ADR-040](adr/ADR-040-personal-instalator-windows.md)
opisuje pochodzenie zależności, skróty i ograniczenia wariantu online.
