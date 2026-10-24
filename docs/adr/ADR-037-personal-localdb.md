# ADR-037: Personal jako profil wspólnego VATowca z LocalDB

Data: 2026-10-24. Status: przyjęty kierunek i sprawdzony prototyp techniczny;
instalator Personal pozostaje następnym etapem. Wymagania ORG-003/004/008/010/011,
T-012/T-014, dystrybucja E15. Uzupełnia ADR-014/015/016/032/035/036.

Etap 2 uzupełnia [ADR-038](ADR-038-personal-pierwsze-uruchomienie.md): normalny
kreator Avalonia, prywatny kanał inicjalizacji, checkpointy i zamknięcie
bootstrapu po pierwszym administratorze. Zweryfikowano nowe bazy, wznowienie,
restart, logowanie i graceful shutdown. Poniższe granice prototypu opisują etap 1;
aktualne działanie i wyniki: [pierwsze uruchomienie](../personal-first-run.md).

Etap 3: [ADR-039](ADR-039-personal-cykl-zycia-api.md) wprowadza Named Pipes zamiast
lokalnego HTTPS, pojedynczą instancję klienta i pełny nadzór cyklu życia API.
Personal nie wymaga już certyfikatu .NET ani konfiguracji portu.

## Kontekst i decyzja

Personal: jeden komputer Windows, Avalonia, lokalne API ASP.NET Core i prywatna
instancja SQL Server 2025 Express LocalDB należąca do użytkownika Windows.
Network: dotychczasowe API i pełna instancja SQL Server Express. To **jeden kod**
Domain/Application/Infrastructure/API/Desktop, te same DTO, formularze,
migracje i SQL Server provider EF Core. Nie wprowadzamy SQLite, kopii modelu,
alternatywnego logowania ani bezpośredniego SQL z klienta.

Database-per-company obowiązuje również w Personal: jedna fizyczna baza System,
osobne fizyczne bazy firm, centralne Identity/CompanyAccess, autoryzowany resolver,
niezmienne CompanyId i trwałe ActorId/ActorIssuerId. Konto Windows ogranicza
dostęp do LocalDB i plików; nie zastępuje uprawnień aplikacji ani jej audytu.

## Granice prototypu

Jawny profil uruchomieniowy/środowisko `PersonalPrototype` korzysta z
`%LOCALAPPDATA%\Vatowiec\PersonalPrototype\host.json`. Nie wczytuje konfiguracji
Development ani jej user-secrets. Default `https` Network pozostaje pierwszy
i zachowuje dotychczasowe ustawienia. Personal ma tylko HTTPS localhost:17443,
AllowedHosts=localhost, odrębne nazwy cookies oraz key ring sesji chroniony DPAPI
użytkownika. HTTPS nadal sprawdza certyfikat normalnie; prototyp korzysta
z istniejącego, już zaufanego certyfikatu localhost, bez jego modyfikowania.

Polityka SQL wymaga osobnego jawnego trybu i dopuszcza tylko prywatną instancję
`(localdb)\VatowiecPersonal_Prototype` i trzy konkretne nazwy baz prototypu.
Nie dopuszcza domyślnego MSSQLLocalDB, SQLEXPRESS, innych serwerów, SQL loginów,
AttachDbFilename ani wyłączenia opcji Encrypt. Wyjątek TrustServerCertificate
jest ograniczony do tego lokalnego IPC i nie zmienia polityki Network ani TLS
klienta. Potwierdzono transport named pipe. Ochrona IPC opiera się na Windows
i prywatnej instancji; nie deklarujemy na tej podstawie szyfrowania transportu
SQL równoważnego zdalnemu TLS.

Oddzielna komenda narzędzia administracyjnego `personal-prototype` jest
rozpatrywana **przed** historycznym `EnsureLocalServer`. Nigdy nie uruchamia
resetującego runnera. Tworzy tylko nowy katalog i instancję 17.x; zajęta nazwa
lub katalog kończą operację. Przed operacjami bazowymi weryfikuje LocalDB 17,
DB_NAME i własny znacznik, używa stałej allowlisty. Nie ma resetu, DROP ani
automatycznego usuwania danych. Stosuje istniejące migracje wyłącznie do nowych
baz. Bazy i kopie zostają do inspekcji, a własne API/LocalDB są zatrzymywane.

## Wynik i konsekwencje

Rzeczywisty eksperyment zakończył się powodzeniem: pełne migracje, Identity,
CSRF, uprawnienia, CRUD kontrahenta, konflikt rowversion, idempotencja, izolacja
firm, audyt i jego trigger, restart procesu z zachowaniem danych i sesji,
backup/VERIFYONLY. [Wyniki i granice](../personal-localdb.md).

Gotowość techniczna LocalDB **nie oznacza gotowego wydania instalacyjnego**.
Potrzebne są kontroler procesu API (pojedynczość, gotowość, bezpieczne wygaszanie,
zabezpieczenie przed osieroceniem), bootstrap pierwszego administratora i firmy,
certyfikat HTTPS bez SDK, instalacja/składniki/licencje LocalDB, aktualizacje
z backupem oraz przetestowane restore i przenoszenie kluczy. Prototyp testowy
zamyka własny proces API przez Kill; nie uznajemy tego za docelowy graceful shutdown.

Migracja Personal → nowa instalacja Network ma przenosić bazy SQL, a nie
odtwarzać rekordy czy zmieniać ich właścicieli. Zachowujemy identyfikatory,
historię migracji i audyt. Zmieniają się kontrolowane lokalizacje baz i profile
połączeń. DPAPI jest przypisane do konta/komputera: eksport/przechronienie
sekretów lub ich ponowne wpisanie wymaga osobnej procedury. Automatyczne scalanie
z istniejącą instalacją i automatyczne przenoszenie haseł/sesji klienta nie
wchodzą w ten etap. Nie dotykamy istniejącego środowiska SQLEXPRESS.
