# Kartoteka kontrahentów — dodawanie, edycja, wyszukiwanie i konflikty

**Aktualizacja projektowa 2026-10-22:** wdrożenie nowych funkcji wstrzymano.
[ADR-018](adr/ADR-018-docelowy-model-kontrahentow.md) opisuje docelowy model
oraz pozostałe szczegóły implementacyjne: preferowany int IDENTITY zamiast GUID,
usunięcie zbędnego CompanyId z Contractors, grupowanie ParentId bez dziedziczenia,
własne adresy, uniwersalne kontakty i rachunki. Zatwierdzone są ContractorAddresses,
ContractorContacts, ContractorBankAccounts oraz otwarty AddressTypes. TaxId i VatEuId
mają do 100 znaków, duplikaty i błędy można świadomie zatwierdzać z odpowiednim prawem,
bez uzasadnienia, z audytem. Domyślne rachunki są osobne według waluty, także NULL.
Pozostają KsefIdWew, wersjonowana walidacja w tabeli SQL oraz
osobna historia ExternalVerifications. Te zmiany nie są zaimplementowane.
Poniższa instrukcja opisuje dotychczasowy demonstrator. Jego fikcyjne rekordy
i identyfikatory nie wymagają przenoszenia ani zachowania; po zatwierdzeniu
szczegółów modelu demonstracyjne bazy projektu mogą zostać odtworzone od podstaw.
W obecnym zadaniu nie zmieniamy danych ani schematu.

Etap z 2026-10-22: podstawowa kartoteka pięciu pól istniejącego modelu,
bez faktur i księgowania. Decyzja: [ADR-017](adr/ADR-017-kartoteka-kontrahentow-i-audyt.md).
Instrukcję kont i haseł zawiera [logowanie](authentication.md).

## Uruchomienie i schemat baz

Na lokalnym `localhost\SQLEXPRESS` wykonano jawnie migracje:

- Vatowiec_System: InstallationIdentity — pojedynczy trwały identyfikator instalacji.
- Vatowiec_Company_A_Dev i Vatowiec_Company_B_Dev: ContractorAudit — tabela
  audit.ContractorChanges i indeks listy kontrahentów, osobno w każdej bazie.

Przed wdrożeniem sprawdzono nazwy, znaczniki własności, profile i historie baz.
Wykonano trzy kopie COPY_ONLY/CHECKSUM oraz RESTORE VERIFYONLY z CHECKSUM.
Pliki są w katalogu Backup lokalnego SQL Server, poza repozytorium. Nie odtwarzano
ani nie usuwano baz demonstracyjnych; istniejące dane i konta zachowano.
API nie wykonuje migracji przy starcie. Na kolejnej lokalnej kopii demonstratora
po przeglądzie SQL użyj osobno:

```powershell
dotnet run --project tools/database/Vatowiec.DatabaseTools --no-build -- migrate-contractors
```

Narzędzie aktualizuje tylko trzy konkretne, oznaczone lokalne bazy demonstracyjne,
po kopii każdej z nich. Nie jest narzędziem migracji baz produkcyjnych.
Usunięcie migracji Down usuwa historię lub identyfikator instalacji; nie stosuj
go jako wycofania używanego systemu. Szczegóły ryzyka i wznowienia: ADR-017.

Uruchom API i desktop w dwóch terminalach:

```powershell
dotnet build Vatowiec.slnx --no-restore
dotnet run --project src/Server/Api --no-build --launch-profile https
```

```powershell
dotnet run --project src/Desktop --no-build
```

Adres klienta: domyślnie `https://localhost:7243`, zmieniany w login albo przez
VATOWIEC_API_URL. Certyfikat HTTPS musi być zaufany. Nie wyłączaj jego sprawdzania.

## Dodawanie i walidacja

1. Zaloguj się jako **operator.m0** lokalnym hasłem i wybierz **firmę A**.
   Operator ma prawo edycji w A; w B ma wyłącznie odczyt.
2. Kliknij **Nowy (Ctrl+N)**. Wpisz np. kod `TEST-001`, nazwę
   `Przykład — dane fikcyjne`, opcjonalny identyfikator `DEMO-TAX-001`, kraj `PL`.
   Pozostaw zaznaczone **Aktywny**. Używaj wyłącznie danych fikcyjnych.
3. **Zapisz (Ctrl+S)** zapisuje pozycję i audyt. Wyszukaj ją po kodzie lub odśwież listę.
4. Puste kod/nazwa, zbyt długie wartości i kraj inny niż dwie litery dają komunikat.
   Kod jest unikalny w firmie: powtórzenie, także z inną wielkością liter po
   normalizacji, nie nadpisuje istniejącej pozycji. Kod i kraj są zapisywane wielkimi literami.

Kod ma do 64 znaków, nazwa 256, identyfikator podatkowy 32. Ten etap nie sprawdza
sumy kontrolnej NIP ani rejestrów urzędowych. Identyfikator może być pusty.
Tab/Shift+Tab przechodzi po polach; Esc lub **Anuluj zmiany** odrzuca formularz.
Przy otwartym formularzu najpierw zapisz lub anuluj, aby zmienić firmę albo wylogować.

## Edycja i status aktywności

Zaznacz kontrahenta i naciśnij **F2** lub **Edytuj**; działa też dwuklik.
Zmień nazwę, identyfikator, kraj albo kod i zapisz Ctrl+S. Formularz otwiera
aktualną wersję z API, a zapis wymaga jej rowversion.

Odznaczenie **Aktywny** i zapis zachowuje kontrahenta oraz historię. Nie ma
przycisku ani endpointu fizycznego usunięcia. Pozycję można ponownie aktywować
przez edycję. W filtrze **Wszyscy** zobaczysz oba statusy; **Aktywni** ukryje
nieaktywną pozycję, a **Nieaktywni** pozwoli ją znaleźć.

## Wyszukiwanie i strony

Ctrl+F ustawia fokus w polu wyszukiwania. Wpisz fragment kodu, nazwy lub
identyfikatora i naciśnij Enter albo **Szukaj**. Możesz jednocześnie ograniczyć
kraj i status aktywności. Wyczyszczenie kryteriów i ponowne wyszukanie przywraca
widok bez tych ograniczeń. Znaki `%` i `_` są tekstem, nie poleceniami SQL.

Filtry wykonuje serwer w bazie wybranej firmy; klient nie pobiera całej kartoteki.
Strona pokazuje do 50 pozycji. **Następna** jest dostępna po wykryciu kolejnej
strony; **Poprzednia** wraca. Zmiana kryteriów zaczyna od pierwszej strony.
Równoległe dodawanie/zmiany mogą przesunąć granice stron — odśwież wtedy wyniki.

## Konflikt równoczesnych zmian

1. Uruchom **dwie instancje desktopu**, zaloguj w obu operator.m0 i wybierz A.
   Sesje są niezależne. Można również użyć dwóch kont mających EditContractors w A;
   konta czytelnika nie trzeba w tym celu zmieniać.
2. W obu oknach wyszukaj tę samą pozycję i **otwórz edycję przed zapisaniem
   jakiejkolwiek zmiany**.
3. W pierwszym oknie zmień nazwę na `Fikcyjna nazwa — pierwsza osoba` i zapisz.
4. W drugim wpisz inną nazwę i zapisz. Pojawi się porównanie pięciu pól Twojej
   wersji i aktualnej wersji serwera. Zapis jest zablokowany; niczego nie nadpisano.
5. Wybierz jedną z decyzji:
   - **Wczytaj wersję z serwera (odrzuć moje zmiany)** — formularz przyjmuje
     aktualne dane, odrzucając poprzednio wpisane pola.
   - **Zachowaj moje dane do ponownego zapisu** — zachowuje wpisane pola i przyjmuje
     porównaną wersję jako podstawę. Sprawdź formularz, następnie **ponownie naciśnij
     Zapisz**. Samo kliknięcie decyzji nie wykonuje zapisu.

Jeśli ktoś w międzyczasie znów zapisze pozycję, pojawi się kolejny konflikt.
Nie ma automatycznego nadpisywania ani opcji zapisu pomijającej rowversion.
Test integracyjny sprawdza też dwa odrębne konta oraz rzeczywiście równoległe żądania.

## Uprawnienia i błędy

W firmie B operator.m0 może wyszukiwać i czytać, ale nie widzi **Nowy/Edytuj**.
Czytelnik.m0 nadal widzi tylko profil A, bez kartoteki. Bezpośrednie POST/PUT
bez ViewContractors i EditContractors daje 403, także z ważnym cookie i CSRF.
Próba zmiany CompanyId poza ścieżką, innego Id w JSON i brak CSRF są odrzucane.

Utrata sieci podczas zapisu zachowuje formularz i jego Id. Ponów po odzyskaniu
połączenia. Jeśli serwer zdążył utworzyć pozycję, ponowienie identycznego POST
nie tworzy drugiej pozycji ani audytu. Ponowienie starego PUT może dać konflikt:
porównaj aktualny stan. Wygaśnięcie sesji lub odmowa dostępu usuwa dane i formularz;
nie zapisujemy szkiców ani sesji na dysku. Timeout połączenia wynosi 20 sekund.

## Audyt i przeprowadzona weryfikacja

W audit.ContractorChanges bazy firmy są zapisane: autor (trwałe ActorId,
ActorIssuerId i nazwa historyczna), czas UTC, korelacja, operacja i JSON wartości
przed/po. Zmiana i audyt są w jednej transakcji. Nieudany zapis ani konflikt
nie dopisują historii sukcesu. Dla dawnych danych nie dopisujemy wymyślonej
historii wstecz. Usunięcie centralnego konta nie usuwa ani nie anonimizuje autora.
W tym etapie nie dodano ekranu przeglądania historii.

Do kontrolnego odczytu lokalnej bazy możesz użyć:

```sql
SELECT a.ActorName, a.ActorId, a.ActorIssuerId, a.OccurredAtUtc,
       a.Operation, a.BeforeJson, a.AfterJson
FROM audit.ContractorChanges a
JOIN core.Contractors c ON c.CompanyId = a.CompanyId AND c.Id = a.ContractorId
WHERE c.Code = N'TEST-001'
ORDER BY a.CreatedAtUtc, a.Id;
```

Zapytanie jest administracyjną kontrolą testowej bazy, nie połączeniem klienta
Avalonia z SQL. Runtime docelowego wdrożenia powinien mieć audyt tylko do
SELECT/INSERT; lokalne konto Windows programisty ma szersze uprawnienia.

Próba Windows UI Automation w dwóch rzeczywistych procesach Avalonia potwierdziła
create, edit, wyszukiwanie, konflikt z zachowaniem pól, świadomy ponowny zapis
i dezaktywację. W A pozostała **nieaktywna pozycja GUI-60D3A33B** z pięcioma
wpisami audytu (Create i cztery Update), bez wpisu dla odrzuconej próby konfliktu.
Sprawdzono to odczytem SQL; B nie była zmieniana poza migracją schematu.

Pełny zestaw testów i kompilację uruchamia się tak:

```powershell
dotnet build Vatowiec.slnx --no-restore
dotnet run --project tools/database/Vatowiec.DatabaseTools --no-build -- test
dotnet format Vatowiec.slnx --verify-no-changes --no-restore
```

Runner tworzy trzy dedykowane *_Test, migruje je jawnie, uruchamia SQL/API
i testy bez SQL, sprawdza sprzątnięcie danych i usuwa fixture. Nie zmienia danych
demonstracyjnych. Wyniki TRX są w TestResults. Pominięcie SQL przy zwykłym
dotnet test nie jest zaliczeniem testów integracyjnych.

Wynik końcowy 2026-10-22: **117/117** zaliczonych, **46 SQL/API** i **71 bez SQL**,
bez pominięć. Dodano 21 przypadków względem etapu odczytu (11 SQL/API i 10 bez SQL).
Build ośmiu projektów: 0 błędów i 0 ostrzeżeń. Kontrola dotnet format przeszła.
Raport: TestResults/database-per-company-b5f11c8ce500499aa9389799d7e3aacd.trx.
