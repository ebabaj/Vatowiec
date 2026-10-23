# ADR-024 — kod ISO 4217 jako jedyny identyfikator waluty

Status: wdrożony i zweryfikowany w demonstratorze, 2026-10-23.
Dotyczy DOC-006 i zmienia część ADR-023 dotyczącą klucza waluty.

`ref.Currencies.Code char(3)` jest jedynym PK waluty, zapisywanym wielkimi
literami. Nie ma Id ani innego zastępczego identyfikatora. Kod jest niezmienny;
API zmienia wyłącznie ustawienia waluty wskazanej kodem w ścieżce.
EF używa Code jako klucza i nie generuje jego wartości. Klucz ma porównanie
binarne, dzięki czemu małe litery nie są alternatywnym zapisem identyfikatora.
EF i trigger SQL blokują zmianę kodu istniejącej waluty.

`core.ExchangeRates.CurrencyCode char(3)` wskazuje Code przez lokalny FK.
Unikalność kursu obejmuje CurrencyCode/Source/TableType/EffectiveDate.
Id kursu pozostaje bigint IDENTITY. Flagi pobierania pozostają właściwościami
wiersza słownika. Kontrakty, serwisy i Avalonia używają kodu zamiast liczby.
Istniejące CurrencyCode rachunków bankowych już posługują się kodem, nie Id;
nie zmieniamy przy okazji zatwierdzonych zasad ich edycji i ostrzeżeń.

Jednorazowa migracja demonstratora usuwa i odtwarza wyłącznie trzy tabele
modułu walutowego: ExchangeRates, CurrencyOperations i Currencies wraz z ich
ograniczeniami oraz triggerami. Inicjalizuje 149 kodów i domyślne EUR/USD/GBP/CHF
z A. Nie przenosi danych ani nie mapuje CurrencyId. Reset historii jest jawny:
stare demonstracyjne snapshoty miały także numery walut i klucze ponowień.
Nowe operacje ponownie tworzą niezmienny, atomowy audyt.

Historyczne migracje i ADR-023 pozostają zapisem poprzedniego etapu. Nowe firmy
wykonują pełny łańcuch, kończący się modelem Code. Migracja nie dotyka innych
tabel, centralnych kont, konfiguracji integracji ani sekretów DataPort.
Przed wykonaniem wymagane są kontrola lokalnej instancji, dokładnych nazw,
znaczników, schematów i zweryfikowane kopie. Wycofanie do błędnego modelu z
numerycznym identyfikatorem nie jest obsługiwane przez Down.

Kryteria akceptacji: brak Id w walucie/CurrencyId w bieżącym modelu i API,
fizyczny PK/FK char(3), odrzucanie zmiany kodu i małych liter, migracja także
od pustego schematu, zachowane CAS/CSRF/prawa/izolacja, deduplikacja i audyt,
działający filtr klienta. Zapisujemy liczniki przed/po. Stare kursy są usuwane;
ewentualne porównanie ich wartości po ponownym pobraniu z NBP jest weryfikacją,
nie migracją danych. Początkowe ograniczenie czyszczenia pozostałych danych
zostało następnie jawnie odwołane przez użytkownika dla pełnego testu fixture.
Migracja DDL pozostaje ograniczona do modułu walutowego. Nie rozszerzamy zakresu
na inne aplikacje ani SQL systemowy.

Wdrożenie: migracja 20261023041721_CurrencyIsoCodePrimaryKey, wyłącznie A/B
po preflight i COPY_ONLY/CHECKSUM/VERIFYONLY. Skróty wszystkich danych poza
modułem pozostały identyczne, także DataPort. 150/150 testów bez SQL i build
bez ostrzeżeń/błędów. Ręczne SQL/API oraz okno Avalonia potwierdziły kody,
integralność, prawa, filtry, zapis ustawień i deduplikację. Przed resetem A miała
413 kursów; po ponownym pobraniu 415, w tym wszystkie dawne kombinacje i wartości
oraz dwie dodatkowe publikacje ze źródła. [Wyniki](../currencies-and-nbp.md).

Końcowy pełny zestaw: 244/244, 94 SQL/API i 150 bez SQL, bez pominięć.
Po autoryzowanym czyszczeniu fixture przywrócono fikcyjne kartoteki i konta,
zachowano szyfrogram DataPort i ponownie pobrano 415 kursów w A (B pozostaje
pusta). Obejrzano aktualne okno po pełnym teście: zapis EUR i filtr C działają.
