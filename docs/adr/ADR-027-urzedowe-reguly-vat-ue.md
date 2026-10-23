# ADR-027 — urzędowe reguły VAT UE i zachowanie historii

Data: 2026-10-23. Status: przyjęty do implementacji na zlecenie użytkownika.
Wymagania: ORG-005/007, ADR-021. Bez zmiany API, UI i architektury.

## Decyzja i granice

Porównano 28 reguł VatEuId z tabelą Vero i odsyłaczami Komisji Europejskiej.
Tylko potwierdzona zmiana formatu: BE — pierwsza cyfra 0 lub 1 (SPF Economie,
komunikat 22.09.2023). Nie dodajemy domysłów o innych znakach lub wyjątkach.
EL pozostaje prefiksem Grecji, XI Irlandii Północnej; nie zamieniamy ich na GR/GB.

Dodajemy dwie implementacje istniejącego IIdentifierChecksum: FI i HR, wyłącznie
dla VatEuId. Nie rozszerzamy zagranicznych TaxId. FI: oficjalna specyfikacja
Verohallinto v2.0 z 1.01.2023 i przykłady CSV administracji. HR: specyfikacja
OIB REGOS (instytucja publiczna) i aktualna specyfikacja Porezna HR-BR-53 wiążąca
VAT HR z poprawnym OIB. Wersje algorytmów mają osobne, stałe klucze.
Reszta to format-only-v1 / Unknown; poprawna suma także nie potwierdza rejestracji.
Nie implementujemy algorytmu na podstawie romek.info ani bibliotek firm trzecich.

TaxSchemeSeed zachowuje wszystkie 29 rekordów pierwotnych. Trzy wersje VatEuId
BE/FI/HR kończą okres 22.10.2026, nowe rekordy zaczynają 23.10.2026 i mają nową
Version oraz urzędowy SourceUrl. Są to daty stosowania wersji przez VATowca,
nie domniemane daty zmiany prawa zagranicznego. IsActive pozostaje true także
dla wersji historycznych, aby można było je wybrać dla wcześniejszej daty.
Pozostałe reguły, w tym krajowy PL TaxId, nie zmieniają wartości ani źródeł.

Aktualizacja jest jawną migracją EF Core SystemDbContext: zamknięcie okresów
i INSERT nowych wersji, bez DROP/DELETE, bez ruszania użytkowników/firm/uprawnień.
Przed wdrożeniem: kontrola lokalnej instancji i historii, backup COPY_ONLY/CHECKSUM
z VERIFYONLY. Downgrade nie usuwa historii reguł; cofnięcie decyzji wymaga kolejnej
wersji. WarningHash zawiera wybraną regułę i jej fingerprint, więc nowe zasady
wymuszają ponowne świadome potwierdzenie ostrzeżenia. AcceptWarnings/audyt bez zmian.

## Kryteria akceptacji i weryfikacja bez utraty danych

Testy wzorców 28 prefiksów, sum FI/HR, błędów, dat granicznych, braku/nakładania
reguł, SourceUrl i wersji. Dla identycznych danych zmiana reguły musi zmienić hash.
API zachowuje kontrolę uprawnień, ostrzeżenia i dotychczasowe komunikaty.
SQL: nowe i historyczne rekordy, porównanie danych pozostałych tabel System przed
i po migracji, brak zmian w bazach firm. Nie uruchamiamy pełnego resetującego
runnera DatabaseTools test — usuwa on konta i dane centralne. Testy SQL tego zadania
są osobne i niedestrukcyjne; niewykonanych dawnych testów nie liczymy jako zaliczonych.

Źródła i pełne porównanie: [VAT UE — przegląd urzędowy](../tax-rules/vat-eu-2026-10-23.md).
