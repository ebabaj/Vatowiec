# Archiwalna historia Vatowiec_Dev

Oryginalna migracja `20261021092204_InitialCompaniesAndContractors`, jej designer
i snapshot zostały zachowane bez zmiany treści. Dotyczą wyłącznie historycznego
CoreDbContext i zastosowanego schematu Vatowiec_Dev.

Pliki są wyłączone z kompilacji w projekcie Infrastructure. Nie są częścią
historii SystemDbContext ani CompanyDbContext i nie służą do aktualizacji
którejkolwiek z nowych baz. Nie przywracaj ich do kompilacji w celu migracji
Vatowiec_Dev. Po zgodzie użytkownika stara, pusta baza testowa została usunięta
podczas lokalnego wdrożenia database-per-company; nie uruchamiaj historycznego Down.

Aktualna decyzja: ADR-014. Przeniesienie danych wymaga oddzielnej procedury,
backupu, porównania wyników i jawnego zatwierdzenia wdrożenia.
