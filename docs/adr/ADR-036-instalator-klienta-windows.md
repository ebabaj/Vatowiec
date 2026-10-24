# ADR-036: pierwszy instalator klienta Windows

Data: 2026-10-24. Status: przyjęte dla lokalnego 0.1.0-preview.
Zakres wymagań: klient desktopowy, ORG-003/008, instalatory E15; bez uznania
całego E15/M0 za ukończone. ADR-015/016/032/035 zachowują moc.

## Decyzja

Dystrybuujemy tylko Avalonia i SharedContracts, przez per-user Inno Setup ze
stałym AppId. Publikacja Release win-x64 zawiera własny runtime .NET, bez
trimowania, Native AOT i single-file. Release jest jawnym wyjątkiem dla wydania;
codzienna kompilacja i lokalny serwer pozostają Debug. Nie zmieniamy modelu danych.

Instalacja trafia do LocalAppData/Programs, preferencje pozostają w dotychczasowym
LocalAppData/Vatowiec/Desktop, hasła wyłącznie w Credential Manager i z UserId.
Aktualizacja/odinstalowanie nie zarządzają tymi magazynami. Restart Manager
wykrywa aplikację blokującą aktualizację. Nie dodajemy nowego mechanizmu logowania,
automatycznej aktualizacji ani uprawnień administratora Windows.

API, SQL, migracje, sekrety, certyfikaty prywatne i konfiguracja serwera nie są
częścią klienta. LAN/VPN wymaga odrębnego wdrożenia TLS/DNS/listenera/zapory;
nie wprowadzamy tych zmian przy pakowaniu. Klient zachowuje standardową
walidację certyfikatów. Błąd TLS i niedostępność API mają czytelne komunikaty.

## Kryteria akceptacji i ograniczenia

Wymagane: self-contained publish, kontrola zawartości i licencji, rzeczywisty
EXE instalatora, uruchomienie EXE bez dotnet run, instalacja, skróty, połączenie
HTTPS, zmiana adresu, reinstalacja bez utraty preferencji i odinstalowanie.
Raport odróżnia próby na tej maszynie od nieprzeprowadzonej próby na drugim PC.
Manifest wskazuje commit i stan roboczy; SHA-256 identyfikuje instalator.
Brak podpisu wydawcy i otwarta licencja własnego kodu (ADR-002) są jawne.
Nie wykonujemy commitów, tagów ani publikacji GitHub Release.

Instrukcje: [wydanie klienta](../windows-release.md), [serwer LAN/VPN](../server-lan-vpn.md).
