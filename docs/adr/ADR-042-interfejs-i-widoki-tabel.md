# ADR-042: interfejs aplikacji i wspólne widoki tabel

Data: 2026-10-25. Wymagania REP-004, ORG-003/008/011, model kontrahentów
ADR-018/020/030/031, sesje ADR-015/032/035, transport ADR-039.

Zlecenie użytkownika nazywało tę zmianę ADR-041. Numer jest już zajęty przez
[jawne ustawienia początkowe Personal](ADR-041-personal-ustawienia-poczatkowe.md).
Zachowujemy tamten dokument i jego odwołania; niniejsza decyzja otrzymuje kolejny
numer ADR-042. Nie zastępuje ani nie zmienia domyślnych ustawień Personal.

## Decyzja i kryteria odbioru

Wspólny klient Personal/Network otrzymuje menu KARTOTEKI / SŁOWNIKI / FIRMA /
KONTO, kontekstowe operacje po prawej i stale widoczny po zalogowaniu pasek
użytkownika, firmy oraz absolutnego końca sesji. Ustawienia HTTPS na logowaniu
Network są zwinięte; Personal nie udostępnia adresu ani konfiguracji połączenia.
Transport, walidacja certyfikatów, Identity, hasła i sesje pozostają dotychczasowe.

Kryteria: rzeczywisty podgląd bez zapisów i blokowania rekordu, jawne Edytuj,
automatyczne strony 25/50/100/200 (fabrycznie 50), aktywni jako filtr początkowy,
anulowanie i odrzucanie starych odpowiedzi, wyszukiwanie SQL po widocznych polach,
trwała hierarchia widoków, ochrona PESEL także w metadanych, wersjonowane zapisy
i atomowy audyt ustawień globalnych. Migracja dotyczy wyłącznie nowych tabel System.

## Kontrakt tabel i paneli operacji

`TableCatalog` w SharedContracts definiuje stabilne identyfikatory Contractors,
Currencies, CurrencyRates oraz kolumn: identyfikator, etykieta, typ, kolejność,
widoczność fabryczna, prawo, format i możliwość ukrycia/wyszukiwania.
Identyfikator jest kontraktem wersji; nie należy zastępować go etykietą ani
zmieniać jego znaczenia. `TableCells` jest wspólnym prezenterem nagłówków i
komórek w Avalonia, bez odwołań do SQL. `WorkspaceOperation` + `ICommand`
definiują operacje kontekstowe, ich dostępność i stabilny identyfikator automatyzacji.
Nowy moduł dodaje definicję tabeli, jawne mapowania projekcji/wyszukiwania oraz
operacje, wykorzystując ten sam konfigurator i magazyn. Nie dodajemy frameworka.

Warstwa Desktop rozdziela identyfikatory tabel od polskich nazw przez
`TableDisplayNames`: Contractors → Kontrahenci, Currencies → Waluty,
CurrencyRates → Kursy walut. Są to wyłącznie etykiety konfiguratora, bez zmiany
kontraktu API i zapisanych preferencji.

Wspólny [TableStyles.axaml](../../src/Desktop/TableStyles.axaml) ustanawia wygląd
kartoteki kontrahentów dla tabel i pozostałych list rekordów: pełne jasnoniebieskie
tło nagłówka, wysokość 48, Segoe UI 14, półgrube nagłówki, marginesy komórek 8/6,
minimalny wiersz 32 oraz wspólne zaznaczenie. Dłuższe dane nadal mogą się zawijać
i powiększać wiersz. Listy pomocnicze zachowują dotychczasową projekcję rekordów,
z nagłówkiem sekcji; limity kupieckie zachowują trzy wyrównane pola.
Formularz uprawnień nie staje się tabelą. Obecny układ stałego pola zębatki
w ConfigurableTable i synchronizacja przewijania pozostają bez zmian.

## Przechowywanie i bezpieczeństwo

System zawiera `security.TableViews`: klucz bigint, InstallationId, OwnerId,
TableId, opcjonalne ColumnsJson/PageSize i SQL rowversion. Unikalny indeks obejmuje
instalację, właściciela i tabelę, z porównaniem identyfikatora tabeli BIN2.
OwnerId pusty oznacza domyślny widok instalacji; pozostałe wartości są UserId
Identity. Instalacja pochodzi z trwałego `security.Installation`, użytkownik
z uwierzytelnionej sesji; klient nie podaje żadnego z tych identyfikatorów.
Brak FK do konta zachowuje odrębność zarchiwizowanej tożsamości i jej ustawień.
Ponowne użycie loginu nie przenosi widoków na nowy UserId.

Pierwszeństwo: własne ColumnsJson → domyślne instalacji → katalog fabryczny.
Rozmiar strony jest osobną preferencją użytkownika w rekordzie tej tabeli:
zapis samego PageSize nie utrwala kopii kolumn administratora. Reset własnego
widoku usuwa ten rekord, łącznie z rozmiarem strony; reset instalacji nie dotyka
własnych widoków. Brak rekordu ma wersję null i wymaga null przy pierwszym zapisie.
Nieaktualna wersja istniejącego rekordu daje 409, również przy resecie.

API operuje na `/api/v1/companies/{companyId}/table-views/{tableId}`:
GET odczytuje katalog dostępnych pól i wynik hierarchii, PUT `mine` lub
`installation` zapisuje/resetuje wybrany zakres. Nie ma odczytu ustawień innych
użytkowników. Firma służy świeżej kontroli praw kolumn; ustawienie pozostaje
wspólne pomiędzy firmami i stanowiskami instalacji. Każda mutacja wymaga CSRF
i aktualnej sesji. Zapis instalacji wymaga świeżej roli InstallationAdministrator
oraz prawa do danej tabeli, bez automatycznego CompanyAccess. Sprawdzenia i zapis
odbywają się pod wspólną blokadą administracyjną System, wewnątrz transakcji.

Globalne kolumny i `audit.TableViewChanges` zapisują się atomowo. Audyt zawiera
instalację, tabelę, trwałego aktora/nazwę, UTC i identyfikatory kolumn przed/po.
Nie zawiera wartości danych tabeli. Kontekst i trigger SQL zabraniają UPDATE/DELETE
audytu. Prywatne zmiany preferencji nie tworzą globalnego audytu.

Zapis odrzuca puste/nieznane/powtórzone/niedozwolone identyfikatory. Odczyt
przecina utrwalony zestaw z bieżącym katalogiem/uprawnieniami. Nieznane pola
przyszłej/starej wersji nie trafiają do metadanych; nowa opcjonalna kolumna nie
pojawia się samoczynnie we własnym widoku. Jeżeli wszystkie zapisane pola są
niedostępne, wybieramy dozwolone pola fabryczne. Zapis globalny nie omija PESEL.

## Odczyt i podgląd kontrahenta

POST `contractors/search` otrzymuje `VisibleColumns`, sprawdza allowlistę i
prawa. Wyrażenia LINQ są budowane z jawnych mapowań, nigdy z fragmentów SQL
przekazanych przez klienta. Wartość wyszukiwania jest przechwyconą zmienną,
parametryzowaną przez EF, także dla PESEL. Nie włączamy logowania parametrów
ani treści żądań. Dotychczasowy kontrakt bez VisibleColumns pozostaje zgodny
ze starszym klientem; nowy klient zawsze wysyła jawny zestaw.

Projekcja bierze jedynie adres rodzaju Id=1, istniejący zasiany „Adres rejestrowy”.
Przy wielu adresach tego rodzaju wybiera preferowany, następnie aktywny,
następnie najmniejsze Id. Nigdy nie wybiera adresu korespondencyjnego ani adresu
rodzica. Puste powiązanie daje puste komórki. Filtrowanie poprzedza stabilne
ORDER BY Code,Id i OFFSET/FETCH; nie ma N+1 ani pobierania całej kartoteki.
Daty głównego rekordu są prezentowane/wyszukiwane jako `yyyy-MM-dd HH:mm UTC`.

Pokaż używa obecnych GET i AsNoTracking. Nie tworzy transakcji rezerwującej
rekord, nie zmienia czasu/wersji/audytu. Osobny stan VM blokuje wszystkie ścieżki
zapisu danych głównych, szczegółów, finansów i importu propozycji. Pola formularzy
są tylko do odczytu; zakładki i szczegóły pozostają dostępne. W tym trybie
wyłączone są też nowe weryfikacje zapisujące historię, dostępny jest jej odczyt.
Edytuj ponownie pobiera aktualny rekord; PUT zachowuje rowversion i istniejące
rozstrzyganie konfliktów. F2/Enter/dwuklik otwierają podgląd, Ctrl+N nowy rekord.

## Wdrożenie i granice

Migracja EF `20261025121050_TableViews` tworzy tylko dwie tabele, indeks i trigger.
Nie zmienia kont, haseł, uprawnień, konfiguracji integracji ani baz firm.
Na mocy decyzji właściciela z 2026-10-25 wdrażamy ją jawnie na istniejącym
developerskim Network bez tworzenia dodatkowej instancji SQL; backup danych
demonstracyjnych nie jest warunkiem zgody. Wszystkie bazy VATowca są obecnie
developerskie i mogą być resetowane/odtwarzane według AGENTS.md. Personal jest
wstrzymany; jego istniejących pozostałości nie uruchamiamy w tym zadaniu.
Personal zachowuje odmowę
startu przy niezgodnej historii, bez automatycznej aktualizacji. Network może
korzystać z fabrycznego widoku podczas kontrolowanego wdrożenia, lecz zapis
preferencji wymaga migracji System. Procedura jawnego wdrożenia i testy:
[interfejs i widoki tabel](../table-views.md).

Nie implementujemy szerokości, zmiany kolejności, przeciągania kolumn, pesymistycznych
blokad, faktur ani instalatora. Nie deklarujemy ukończenia M0 ani gotowości produkcyjnej.
