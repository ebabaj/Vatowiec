# ADR-028 — centralny klucz GUS i świadomy wybór produkcji

Data: 2026-10-23. Status: przyjęty i wdrożony na zlecenie użytkownika.
Wymagania ORG-003/007, rozszerzenie ADR-015/021/022.

Jedna instalacja używa jednego produkcyjnego klucza BIR1.2. Singleton
security.GusConfiguration w System przechowuje wyłącznie szyfrogram i rowversion.
Osobny purpose i key ring GUS (Data Protection + DPAPI bieżącego konta Windows)
są powiązane z security.Installation.Id. Ochrona DataPort pozostaje bez zmian.
Brak/utrata klucza wymaga ponownego wpisania, bez fallbacku do Test lub DataPort.

Ustawienia instalacji są dostępne po logowaniu, przed wyborem firmy. Rola
InstallationAdministrator jest centralna i sprawdzana w bazie przy każdym
żądaniu ustawień, także gdy role w cookie są nieaktualne. Nie zastępuje praw
firmowych i nie jest nadawana automatycznie administratorowi firmy.
Pierwszą rolę nadaje administrator serwera przez jawne polecenie lokalnego
narzędzia, wskazując istniejące konto. Zapis/usunięcie wymaga CSRF i rowversion,
usunięcie jawnego potwierdzenia. Odczyt zwraca tylko stan i wersję.

Adaptery i API pozostają obecne. GUS odczytuje klucz centralnie tylko dla
Production; Test korzysta wyłącznie z publicznego klucza testowego, Mock bez sieci.
Domyślne ustawienia repozytorium nadal Test i AllowProduction=false. Produkcja
wymaga lokalnej konfiguracji środowisk i wszystkich zgodnych adresów HTTPS.
Wspólna bramka GUS obejmuje firmy i cały przebieg login–wyszukanie–logout.
Historia sprawdzenia pozostaje w bazie wybranej, autoryzowanej firmy.

Kryteria: brak sekretów w odpowiedziach/logach, konflikt ustawień 409, odmowy
zwykłych kont, brak wpływu konfiguracji GUS na MF/VIES/DataPort, bezpieczne
błędy usług i jawne środowisko. HTTP testujemy tylko atrapami. Migracja wyłącznie
System, po COPY_ONLY/CHECKSUM/VERIFYONLY. Bez resetów i kasowania danych.

Wdrożono jawną migrację 20261023094021_CentralGusConfiguration (System: 9,
firmy: nadal po 7 migracji). Up tworzy jedną tabelę; brak zmian firmowych i DataPort.
Rola pierwszego administratora nie została automatycznie nadana. Build bez
błędów/ostrzeżeń, 321/321 wykonanych testów (317 offline, 4 niedestrukcyjne SQL/API).
Testy nowych ustawień, kont i historii wycofują własne transakcje. Nie wykonano
produkcyjnych zapytań ani ręcznej próby nowego okna Avalonia.
[Instrukcja produkcji, źródła i pełny raport](../production-registries.md).
