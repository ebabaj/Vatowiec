# ADR-022 — DataPort i szyfrowana konfiguracja firmy

Status: przyjęty i zaimplementowany na zlecenie użytkownika, 2026-10-22.
Wymagania: ORG-007, bezpieczeństwo §8/11 SPECYFIKACJA.md oraz zlecenie DataPort.
Rozszerzenie ADR-014–021; nie zastępuje oficjalnego GUS BIR1.2.

## Rozwiązanie i kryteria akceptacji

Jedna lokalna tabela security.DataPortConfiguration w każdej bazie firmy:
SingletonKey=1, opcjonalny ProtectedApiKey i RowVersion. Nie dodajemy CompanyId
ani identyfikatorów GUID. Pozostawienie wiersza po usunięciu klucza umożliwia
wykrywanie starych wersji konfiguracji. Bez zmian w Contractors i bez nowego REGON.

ViewProfile udostępnia wyłącznie stan konfiguracji. Osobne firmowe
ManageCompanyIntegrations=32 oznacza prawo administratora do zapisu/zmiany/usunięcia;
globalna rola go nie zastępuje. Zapisy wymagają CSRF i zgodnej rowversion,
usunięcie osobnego potwierdzenia. Operator A otrzymuje to prawo; B i czytelnik nie.

ASP.NET Data Protection szyfruje klucz z purpose zależnym od CompanyId. Osobny,
trwały key ring jest poza repozytorium i bazą, na Windows chroniony DPAPI tożsamości
serwera. Nie zmieniamy ochrony sesji Identity. Po przeniesieniu do innej tożsamości/
maszyny wymagane jest ponowne wpisanie klucza, jeśli nie odtworzono dostępnej
konfiguracji ochrony. Brak możliwości odszyfrowania blokuje zapytanie, bez fallbacku.
API nie zwraca sekretu ani jego zaszyfrowanej postaci. Desktop ma tylko puste
maskowane pole nowego klucza i czyści je po użyciu/opuszczeniu ustawień.

Osobny adapter GET https://dataport.pl/api/v1/company/{nip}?format=full,
X-API-Key na konkretnym żądaniu, Accept application/json. Tylko HTTPS, stała
allowlista, brak przekierowań, wspólnego nagłówka sekretu i automatycznych ponowień.
NIP przechodzi obecną normalizację i kontrolę PL przed połączeniem. Nie inicjujemy
zapytania przy otwieraniu ani zapisywaniu kontrahenta.

DataPort to rzeczywisty komercyjny pośrednik, Environment=Production, Source=DataPort,
nie TEST GUS ani bezpośrednia odpowiedź urzędu. To jawny wyjątek nowego zlecenia
od wcześniejszych ograniczeń TEST dla oficjalnych MF/GUS/VIES, które pozostają
bez zmian. W testach HTTP stosujemy Mock i oznaczamy tak zapisy testowej historii.
Bez rzeczywistego klucza nie wykonujemy płatnych/limitowanych prób sieciowych.

Wykorzystujemy audit.ExternalVerifications bez nowych kolumn: uporządkowane pola
wyniku w istniejącym DataJson, w tym opcjonalna data zakończenia, bez całego JSON
i bez sekretu. Nie dodajemy oddzielnego audytu weryfikacji. UI pokazuje pośrednika
i możliwość cache, porównuje dane i przenosi tylko wybrane pola do szkicu. Zapis
szkicu korzysta z obecnego API, ostrzeżeń, rowversion i atomowego audytu.

400, 401/INVALID_API_KEY, 404, 429/RATE_LIMIT_EXCEEDED, 500, timeout i wadliwy JSON
są odrębnymi stanami; tylko 404 oznacza brak podmiotu. Nie przekazujemy surowych
komunikatów dostawcy, które mogłyby ujawnić sekret. Limit należy do konta, także
przy kilku kluczach; reset 00:00 UTC. Nie wyliczamy fikcyjnego salda per firma.

Akceptacja: build, format, pełne SQL/API i desktop, zapis/rotacja/usunięcie klucza,
szyfrowanie i trwałość ochrony, odmowy praw/CSRF, izolacja i brak ujawnień,
mapowanie odpowiedzi/błędów, historia oraz świadomy import i audyt. Jawne migracje
po preflight i backupach, bez odtwarzania baz. Bez Commit/Push.

Źródła: [dokumentacja](https://dataport.pl/api),
[kontrakt REST](https://dataport.pl/api/reference), sprawdzone 2026-10-22.

## Wykonanie

Dodano dokładnie jedną trzykolumnową tabelę na bazę firmy i rozszerzono CHECK
CompanyAccess. Migracje wykonano jawnie, po preflight oraz zweryfikowanych kopiach
wszystkich trzech baz. Użytkownik potwierdził demonstracyjne przeznaczenie także
rekordów bez fikcyjnych oznaczeń; runner odtworzył demonstrator, bez DROP/CREATE.
Konta operator.m0/czytelnik.m0 nadal pochodzą z lokalnych sekretów.

210/210 testów, w tym 83 SQL/API i 127 bez SQL, bez pominięć. Sprawdzono także
równoczesny pierwszy zapis konfiguracji i różne klucze w dwóch równoległych
sprawdzeniach firm. Próby HTTP wyłącznie na atrapach, rzeczywiste okno Avalonia
sprawdzono bez wywoływania DataPort. [Wyniki i instrukcja](../dataport.md).
