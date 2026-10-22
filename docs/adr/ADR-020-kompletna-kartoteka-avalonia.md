# ADR-020: Kompletna kartoteka kontrahenta w jednym formularzu Avalonia

Data: 2026-10-22
Status: przyjęty dla bieżącego zakresu M0.
Wymagania: ORG-003/004/005/008, E04, SPECYFIKACJA §8, §10–11.
Uzupełnia ADR-018/019; zasady biznesowe ADR-018 pozostają obowiązkowe.
Źródło doprecyzowania: pełne polecenie użytkownika z 2026-10-22.

## Cel i kryteria

Zastępujemy stary formularz jednym ekranem: Dane główne, Adresy, Kontakty,
Rachunki bankowe. Wszystkie dane zapisujemy przez istniejące HTTPS API.
Prace obejmują cały zakres, testy SQL/API, formatowanie i rzeczywistą próbę
okna Windows. Nie dodajemy faktur, CRM, integracji rejestrów/KSeF ani
TaxIdentifierSchemes i ExternalVerifications. Trzy identyfikatory są osobnymi
polami tylko do odczytu, bez deklarowania poprawności numerów.

ParentId wybiera się z wyników wyszukiwania kodu/nazwy, bez wpisywania int.
Można usunąć powiązanie i otworzyć członka grupy. Nie ma dziedziczenia ani
automatycznego rozstrzygania roli podatkowej. Istniejący trigger i kontrola
rowversion nadal chronią hierarchię. Lista kontrahentów zachowuje filtry i strony.

## API, transakcje i typ wpisu audytu

Dodajemy odczyt, POST i PUT w trasach kontrahenta addresses, contacts,
bank-accounts, GET/POST address-types firmy oraz filtry parentId/excludeId
istniejącej listy kontrahentów. Szczegóły są stronicowane, maksymalnie 100
rekordów na stronę. GET pojedynczej pozycji sprawdza też jej ContractorId;
sam lokalny Id nie pozwala przenieść pozycji do obcego kontrahenta.

DTO DetailResponse zawiera Id, CompanyId z kontekstu, ContractorId, Kind,
Data oraz RowVersion. DetailWriteRequest zawiera Id, Data, RowVersion,
OperationId oraz potwierdzenie ostrzeżenia. Data jest jawnym kontraktem pól
szczegółów; mapowanie zapisuje tylko opisowe pola istniejącej encji,
bez przypisywania Id, FK kontrahenta i rowversion z danych użytkownika.
Rodzaj adresu ma osobny DTO; nie można zmieniać ani usuwać istniejących rodzajów.

Wszystkie zapisy wymagają EditContractors + ViewContractors i CSRF. Odczyt
wymaga ViewContractors. Resolver zawsze sprawdza aktualny dostęp, profil i
historię migracji. Konta, sesje i hasła pozostają w bazie systemowej, a wszystkie
kartoteki, wyniki operacji i audyty w bazie odpowiedniej firmy.

Rozszerzamy istniejące audit.ContractorChanges o RecordType, RecordId,
WarningJson; ContractorId staje się opcjonalny wyłącznie dla wpisów słownika.
Nowe wpisy jednoznacznie identyfikują Contractor, ContractorAddress,
ContractorContact, ContractorBankAccount, AddressType lub wewnętrzną grupę
BankAccountGroup. Trwałe ActorId/ActorIssuerId/ActorName, UTC, korelacja,
niezmienność i wartości przed/po pozostają zachowane. RecordId dawnych zdarzeń
kontrahenta uzupełnia się z istniejącego ContractorId jako nową metadaną;
nie konwertujemy dawnych GUID ani nie przenosimy historycznych danych testowych.

Preferencję adresu użytkownik ustawia tak samo jak domyślność rachunku.
Transakcyjna blokada SQL ContractorDetails serializuje zapisy szczegółów
w jednej bazie, także między procesami API. Odznaczenie poprzedniej pozycji
w grupie następuje przed zapisem nowej, w tej samej transakcji. Audyt obejmuje
oba zmienione rekordy. Dotyczy to też grupy rachunków z walutą NULL i zmiany
rodzaju adresu/waluty przy edycji. Unikalne filtrowane indeksy nadal są ostatnią
barierą SQL. Konflikt wersji nie nadpisuje rekordu; użytkownik porównuje dane
i jawnie wczytuje serwer albo zachowuje własne pola przed nowym zapisem.

Nie dodajemy DELETE. Adresy i rachunki można dezaktywować; kontakty nie mają
IsActive w zatwierdzonym modelu, więc pozostają edytowalne bez usuwania.

## Idempotencja bez nowej tabeli

Wykorzystujemy core.ContractorCreateOperations. Dodajemy OperationKind do PK
(ActorIssuerId, ActorId, OperationKind, OperationId). ContractorId jest nullable
dla dodawania rodzaju adresu. Pozostałe wyniki wskazują kontrahenta.
Hash żądania obejmuje rodzaj operacji, właściciela i znormalizowane wartości.
Ta sama operacja z innym właścicielem/treścią daje konflikt. Każdy formularz
POST zachowuje jeden klucz przy błędzie sieci; ponowienie zatwierdzonego
żądania zwraca pierwotny wynik bez dodatkowej pozycji ani audytu.
Transakcja obejmuje rekord, wszystkie skutki domyślności, audyt i wynik operacji.

## Podstawowa kontrola numeru rachunku i ostrzeżenia

Application/API usuwa białe znaki z AccountNumber i zamienia litery na wielkie.
Brak numeru i przekroczenie długości kolumny są błędami strukturalnymi.
Podstawowa reguła bank-text-v1 ostrzega o długości poza 8–34 znakami lub znakach
innych niż litery/cyfry ASCII. Jest to wyłącznie sygnał nietypowego zapisu:
nie sprawdzamy sumy kontrolnej, poprawności dla kraju ani żadnego rejestru.
Przejście tej kontroli nie oznacza prawidłowego rachunku.

422 bank_warning zwraca opis i hash pokazanej wersji żądania/reguły.
Świadome ponowienie wymaga AcceptWarning=true, tego samego WarningHash oraz
osobnego CompanyPermission.AcceptWarnings=8. Edycja danych lub rowversion
unieważnia wcześniejsze potwierdzenie. Nie wymagamy uzasadnienia. Audyt
zapisuje wersję reguły, opis, potwierdzenie i hash wraz z danymi faktycznie
zapisanymi. Potwierdzenie nie obchodzi CSRF, uprawnień, FK, długości kolumn
ani współbieżności. Operator demonstracyjny ma w A prawa 15, a w B nadal 3.

## Ergonomia i granice szkicu

Pola główne i szczegóły są w jednym ekranie. Listy szczegółów mają strony,
formularz ma przewijanie, a zapis/anulowanie pozostają poza przewijaną treścią.
W trybie odczytu można otworzyć całą kartotekę, ale pola zapisu są zablokowane.
Ctrl+N/F2/Ctrl+S/Esc odnoszą się do aktywnej zakładki. Podwójne kliknięcie
pozycji otwiera jej aktualny stan z API. Walidacja pokazuje błędy przy polach.
Porzucenie zmienionego formularza, przełączenie zakładki ze szkicem szczegółu
i zamknięcie okna wymagają potwierdzenia. Sesja i dane są nadal czyszczone
po wygaśnięciu lub odebraniu dostępu; potwierdzenie szkicu nie omija zabezpieczeń.

Nowego kontrahenta najpierw zapisuje się w danych głównych; szczegóły dodaje
się po otwarciu zapisanej pozycji. Zapis jednej pozycji szczegółu jest oddzielną
operacją od ewentualnego niezapisanego szkicu danych głównych. Nie deklarujemy
jednego COMMIT dla całego wielozakładkowego formularza.

## Wdrożenie i testy

Nie odtwarzamy baz ani tabel biznesowych. Migracja systemowa WarningPermission
rozszerza CHECK uprawnień, a firmowa ContractorDetailAudit zmienia wyłącznie
techniczne tabele operacji i audytu. Poprzedza je COPY_ONLY/CHECKSUM i
RESTORE VERIFYONLY dla trzech rozpoznanych lokalnych baz. API nie migruje przy
starcie. Down usuwa metadane i może utracić informację o rodzaju/ostrzeżeniu;
nie jest bezpiecznym wycofaniem używanego systemu. Ewentualny rollback wymaga
kopii oraz zgodnej wersji serwera/klienta.

Archiwalny snapshot firmy ma rozszerzenie .cs.archive. EF CLI wyszukiwał
plik o tej samej nazwie w archiwum mimo wyłączenia z kompilacji; inne rozszerzenie
zapobiega nadpisywaniu archiwum i pozostawieniu nieaktualnego snapshotu aktywnego.
Treść archiwalnego modelu pozostaje historyczna.

Runner test czyści wyłącznie fikcyjne fixture DML w trzech zweryfikowanych
istniejących bazach, nie wykonuje DROP/CREATE. Wymaga wcześniej jawnie wykonanych
migracji i zatrzymanego API/desktopu. Po testach inicjalizuje demonstrator oraz
konta. Nie dodajemy baz *_Test. Weryfikacja obejmuje wszystkie trzy kartoteki,
słownik, domyślności, konflikty i jawne rozstrzyganie, idempotencję, błędy audytu,
CSRF/uprawnienia, izolację oraz rzeczywisty scenariusz w oknie Avalonia.
Wyniki i instrukcja: [kartoteka](../contractors.md).
