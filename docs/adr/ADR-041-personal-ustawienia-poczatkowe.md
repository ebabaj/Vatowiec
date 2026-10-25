# ADR-041: jawne ustawienia początkowe VATowiec Personal

Data: 2026-10-25. Wymagania ORG-003/004/007/008/009/011, DOC-006, T-012/T-016, E15.
Decyzja na wyraźne zlecenie użytkownika; uzupełnia ADR-023/024/029/034/035/037–040.

## Przyczyna i decyzja

Kreator 0.1.0 sprawdzał hasło przez `new PasswordPolicy()`: minimum 12 i cztery
klasy znaków. Te same ustawienia zapisywała migracja System, a następnie odczytywał
je dynamiczny walidator Identity. Były to dwa miejsca stosujące złożoność; zmiana
samego tekstu w kreatorze lub neutralnych PasswordOptions Identity nie wystarczała.

W **nowej instalacji Personal** zapisujemy jawnie: IsEnabled=false, MinimumLength=1,
RequireUppercase/Lowercase/Digit/SpecialCharacter=false, PreventPasswordReuse=false,
EnforcePasswordExpiration=false. Nieaktywne wartości pomocnicze pozostają N=5
i 90 dni. Każde niepuste hasło przechodzi politykę; także znak pojedynczy lub spacja.
Puste hasło odrzucają kreator i serwer. Pozostałe ograniczenia wejścia normalnego
logowania, hasher, Identity, lockout, sesje, CSRF, historia/audyt i prawa są zachowane.
Administrator nadal może sam włączyć każdą regułę. Nie oceniamy istniejącego hasha
według nowej polityki podczas logowania.

MF, MF WL2 i VIES zaczynają w Production na istniejących oficjalnych endpointach
z `RegistrySettings.OfficialUrl` i przechodzą tę samą allowlistę. GUS pozostaje Test.
Jest to osobna, jawna decyzja bootstrapu Personal. Późniejsze ręczne zapisy
Production nadal wymagają roli, CSRF, RowVersion i ConfirmProduction. Kreator
informuje o rzeczywistych usługach, lecz nie wykonuje zapytań do nich ani do NBP.

Każda nowa baza firmy Personal zachowuje 149 kodów, aktywny tylko PLN,
DownloadA/B/C=false dla wszystkich. Istniejące migracje nadal najpierw tworzą
pełny słownik z dotychczasowym seedem; osobny inicjalizator provisioningowy stosuje
profil Personal. Nie zmieniamy HasData, migracji, snapshotów ani domyślnych
wartości Domain współdzielonych z Network.

## Transakcje, wznowienie i zachowanie konfiguracji

`PersonalBootstrapDefaults.InitializeSystem` jest wywoływany tylko przez prywatny
bootstrap. Weryfikuje dokładny cel LocalDB tej instalacji, SQL znacznik własności,
brak kont i zamknięcia inicjalizacji. Pod blokadą administracji, w **tej samej
transakcji co pierwszy administrator, rola, CompanyAccess i seal** zapisuje politykę,
MF/VIES i istniejące niezmienne historie PasswordPolicyChanges/RegistryConfigurationChanges.
Identity czyta zapisane, jeszcze niezatwierdzone nastawy z tego samego scoped DbContext.
Nie ma dodatkowego sztywnego walidatora. Awaria audytu lub zapisu konta wycofuje całość.

Rozpoczęty bootstrap 0.1.0 można wznowić bez resetu. Systemowe wartości zastępujemy
tylko wtedy, gdy są dokładnie niezmienionym seedem i nie mają historii zmian.
Indywidualna polityka lub konfiguracja rejestru pozostaje. Jeżeli jej wymagania
odrzucają nowe hasło, obowiązuje istniejący dynamiczny walidator.

`InitializeCompany` jest wspólną operacją dla nowej bazy firmy, niezależną od
numeru firmy i jej nazwy. Weryfikuje prywatną instancję, wersję 17, dokładną nazwę
pierwszej bazy albo nazwę zawierającą CompanyId następnej, własność i profil.
Transakcja firmy ze sp_getapplock zapisuje profil, ustawienia walut,
`audit.CurrencyOperations` oraz extended property `Vatowiec.Personal.CompanyDefaults`.
Audyt wskazuje technicznego aktora inicjalizacji w przestrzeni tożsamości instalacji;
nie podszywa się pod konto, którego jeszcze nie utworzono.

Przy wznowieniu firmy 0.1.0 wymagany jest nietknięty słownik migracyjny, brak
kontrahentów, kursów i historii operacji walutowych. Zastane indywidualne dane
powodują czytelną odmowę, bez nadpisania. SQL checkpoint zapobiega powtórnemu
stosowaniu profilu po przerwie między firmą a zapisem administratora. Ponowienie
nie zmienia wersji PLN ani nie wyłącza walut aktywowanych później przez użytkownika.
Nie tworzymy transakcji rozproszonej System/firma: zachowujemy checkpointy ADR-038.

Po autorytatywnym zamknięciu bootstrapu w SQL nie uruchamiamy tych metod przy
starcie ani przy aktualizacji. Stare zakończone instalacje, także zachowujące
wcześniejsze domyślne wartości, pozostają bez zmian. Nie ma nowej migracji ani
masowego DML na istniejących bazach.

## Dystrybucja i weryfikacja

Nowa wersja to **0.1.1-preview**. Ten sam produkcyjny AppId Personal, nowy katalog
artifacts, osobna publikacja Desktop/API self-contained i manifest/SHA-256.
Wersja zasobu instalatora wynika z parametru, README z wersji publikacji. Instalator
Network, LocalDB, Named Pipes, porty, certyfikaty i zapora bez zmian.

Testy rzeczywiste obejmują prosty login przez API, edycję nastaw, restart,
przerwany worker z faktycznej publikacji 0.1.0, rollback audytu i kolejną nową bazę
firmy na tej samej instancji. Nie dodajemy tu nowego ekranu ani endpointu tworzenia
kolejnych firm; ich provisioning musi używać wspólnego inicjalizatora nowych baz.
Test następnej firmy dotyczy jej SQL/profilu/walut, nie nowego przepływu UI.
Rejestry testowane są na atrapach; bootstrap nie ma adaptera HTTP.

Na komputerze istnieje instalacja Personal użytkownika. Próby instalacji używają
oddzielnego AppId i skrótów, identycznego między testową 0.1.0 i 0.1.1, z niezmienionymi
rzeczywistymi payloadami obu wersji. Produkcyjnej instalacji nie nadpisujemy.
To ograniczenie należy wskazać w raporcie, zamiast twierdzić, że aktualizowano konto
użytkownika. [Wyniki](../releases/personal-0.1.1-preview.md).
