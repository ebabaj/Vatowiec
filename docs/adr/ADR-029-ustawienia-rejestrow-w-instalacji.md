# ADR-029 — publiczna konfiguracja rejestrów w bazie instalacji

Data: 2026-10-23. Status: wdrożone i przetestowane na zlecenie użytkownika.
Wymagania ORG-003/007. Rozszerza ADR-028, zastępując ręczne konfigurowanie
publicznych adresów i trybów rejestrów przez user-secrets.

W System dodajemy security.RegistryConfigurations (trzy rekordy GUS/MF/VIES:
tryb, URL Test/Production, osobne URL WL2 dla MF, rowversion) oraz niezmienne
audit.RegistryConfigurationChanges (wartości przed/po, czas i trwały aktor z
identyfikatorem instalacji, bez FK do konta). Zapis konfiguracji i audytu atomowy.
Nowa instalacja i pierwszy zapis nowego słownika po aktualizacji: wszystkie Test.
Nie importujemy starego Production bez świadomego zatwierdzenia w UI. Istniejące
pliki ustawień i sekrety pozostają; normalny serwer ignoruje ich publiczne nastawy
Registries na rzecz DB. Klucz GUS i jego ochrona z ADR-028 bez zmian.

Ustawienia instalacji w Avalonia zawierają wybór GUS/MF/VIES, TEST/PRODUKCJA,
edycję obu zestawów adresów i jawne potwierdzenie każdego zapisu Production.
API wymaga istniejącej roli InstallationAdministrator, CSRF, rowversion.
Wszystkie URL sprawdza serwer: HTTPS i dokładny oficjalny host/ścieżka, bez
userinfo/query/fragment/portów innych niż 443. Tylko końcowy slash jest opcjonalny.
Zwykły użytkownik nie zmienia nastaw, centralna rola nie nadaje praw firmy.

RegistrySettings pozostaje kontraktem istniejących adapterów. Normalna rejestracja
DI tworzy scoped, niezmienny snapshot trzech wierszy z jednego zapytania SQL.
Nie ma cache procesu ani restartu po zapisie. Tryb, endpoint i środowisko historii
tej samej operacji pochodzą z tego samego snapshotu, także podczas zmiany nastaw
przez drugiego administratora. Błąd jednej usługi nie wprowadza fallbacku do innej.
Mock pozostaje dostępnym sposobem jawnego wstrzyknięcia ustawień w testach/probe;
UI udostępnia wyłącznie Test/Production. Nie uruchamiamy produkcyjnych zapytań.

Migracja wyłącznie System po zweryfikowanym backupie, bez resetów i zmian DataPort.
Kryteria: atomowy i niezmienny audyt, konflikty/uprawnienia/CSRF, walidacja URL,
nowe żądanie widzi zmianę, trwająca sesja GUS zachowuje pierwotny host i tryb;
HTTP testujemy wyłącznie na atrapach, testy SQL wycofują własne transakcje.

Jawnie wdrożono migrację 20261023120832_InstallationRegistrySettings po backupie
COPY_ONLY/CHECKSUM i RESTORE VERIFYONLY, bez resetów. System ma 10 migracji,
firmy po 7. Build bez błędów i ostrzeżeń; 335/335 testów (330 bez SQL,
5 niedestrukcyjnych SQL/API). 103 stare testy resetujące wyłączono z przebiegu.
Wyniki oraz instrukcja: [ustawienia rejestrów](../production-registries.md).
