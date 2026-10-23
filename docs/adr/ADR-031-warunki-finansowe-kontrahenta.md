# ADR-031 — standardowe warunki finansowe kontrahenta

Data 2026-10-23. Status: wdrożone i przetestowane na zlecenie użytkownika.
Wymagania ORG-003/012 i podstawa przyszłego PAY-001; bez dokumentów i rozrachunków.
Rozszerzenie ADR-018/020/024, bez zmiany historii wcześniejszych decyzji.

W bazie firmy dodajemy ContractorTradeTerms: Id int IDENTITY, ContractorId,
Direction varchar(8) Sale/Purchase, PaymentDays int NULL (0–3650),
PaymentMethodCode varchar(32) NULL, CurrencyCode char(3) NULL,
DiscountPercent decimal(7,4) NULL (0–100), Notes nvarchar(2000) NULL, RowVersion
i standardowe znaczniki czasu. Unikalna para ContractorId/Direction. Brak wartości
to brak indywidualnego ustawienia, również dla waluty; nie podstawiamy PLN.

ref.PaymentMethods: Id int IDENTITY dla istniejącego audytu RecordId, Code
varchar(32) unikalny/stabilny (FK z warunków), Name nvarchar(100) edytowalne,
IsActive, RowVersion i znaczniki czasu. Początkowe TRANSFER/Przelew, CASH/Gotówka,
CARD/Karta, OFFSET/Kompensata, OTHER/Inny. Bez fizycznego usuwania przez API;
FK blokuje usunięcie użytej pozycji. Dezaktywacja zachowuje wcześniejsze wybory.

ContractorCreditLimits: Id int IDENTITY, ContractorId, Direction, CurrencyCode
char(3) NOT NULL, Amount decimal(24,8) >=0, IsActive, RowVersion i znaczniki czasu.
Filtrowany indeks unikalny chroni jedną aktywną pozycję na kontrahenta/kierunek/
walutę. Nieaktywnych może być wiele. Brak automatycznego wyłączania innego limitu.
Obie waluty FK do istniejącego ref.Currencies.Code, bez nowego słownika.
Kolacja pól CurrencyCode to istniejące Latin1_General_100_BIN2, identycznie
jak klucz słownika i pozostałe powiązania walutowe.
Skala rabatu 4 i kwoty 8 zapobiega utracie dokładności; nadmiar cyfr odrzucamy
zamiast zaokrąglać po cichu. Nowy wybór nieaktywnych walut/metod jest zabroniony;
już zapisane odwołanie można zachować przy edycji innych pól.

Odczyt ViewContractors, zapis EditContractors razem z ViewContractors i CSRF.
Rowversion i ręczne porównanie konfliktu; brak force-update i fizycznego DELETE.
Zapis plus ContractorChanges atomowy w bazie firmy, trwały aktor/issuer/nazwa,
RecordType/RecordId; bez snapshotu danych głównych lub PESEL. Tworzenie korzysta
z ContractorCreateOperations i blokady SQL, z trwałym replay wyniku. Kod płatności
jest niezmienny, a Direction istniejącego limitu nie jest przenoszony do innej grupy.

Avalonia dodaje zakładkę Finansowe z widokami Sprzedaż/Zakup. Osobne szkice i
przyciski zapisu/anulowania warunków, limitu i słownika. Przełączanie widoku,
zakładki i opuszczenie formularza chronią niezapisane finanse. Szkic danych
głównych pozostaje niezależny i nie jest wysyłany przy zapisie finansów.
Słownik płatności zarządzany w tym samym oknie, według podejścia otwartego słownika.

Migracja addytywna tylko w firmach po backupie i kontroli instancji/znaczników.
Bez resetów. Testy integralności, praw/CSRF, wersji i równoczesnych zapisów,
izolacji kierunków/firm/walut, audytu i niezależnych szkiców.
Nie implementujemy faktur, sald, wykorzystania limitu ani stosowania ustawień
do dokumentów. Integracje, banki, centralne uprawnienia i architektura pozostają.

## Wykonanie

Migracja 20261023182240_ContractorFinance zastosowana jawnie w A i B po kopiach
COPY_ONLY/CHECKSUM i RESTORE VERIFYONLY. System nadal 11 migracji, firmy po 9.
Dodano tylko trzy tabele, ich indeksy/ograniczenia i pięć sposobów płatności
w każdej firmie. Nie zmieniono wcześniejszych danych ani przypisań praw.
42 wcześniejsze tabele zgodne liczności/SHA256 po migracji; po testach 41,
z wyjątkiem zwykłych, następnie unieważnionych UserSessions.

Pierwsza próba DDL wykazała brak zgodnej kolacji FK waluty i została wycofana
bez nowych tabel/zmiany danych. Poprawiono wtedy jeszcze niezastosowaną migrację,
mapowania i snapshot, dodano test zgodności kolacji i ponowiono bez resetów.

Build: 0 błędów i ostrzeżeń. Testy: 383/383, 374 bez SQL i 9 niedestrukcyjnych
SQL/API. Obejmują fizyczne FK/checks, rowversion, idempotencję, atomowość przy
błędzie audytu, kierunki/waluty/firmy, dwie konkurencyjne transakcje SQL oraz
niezależność szkiców. 103 stare testy resetujące nieuruchomione i niezaliczone.
Rzeczywiste GUI: zakładka, brak domyślnego PLN, walidacja, ochrona szkiców,
oddzielny szkic danych głównych, słownik i read-only firmy B. Próba GUI bez
zapisu danych; realny zapis API/SQL w wycofywanych transakcjach testowych.
[Instrukcja i szczegóły](../contractor-finance.md).
