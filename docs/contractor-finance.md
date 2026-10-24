# Warunki finansowe kontrahenta — ADR-031

Zakładka **Finansowe** zawiera dwa niezależne widoki:

- **Sprzedaż** — warunki naszej sprzedaży klientowi.
- **Zakup** — warunki naszych zakupów od dostawcy.

Ten sam kontrahent może mieć oba zestawy. Nie dziedziczą się z kontrahenta
nadrzędnego ani między kierunkami. Są wartościami domyślnymi do przyszłych
dokumentów. Nie ma jeszcze faktur, naliczania sald, wykorzystania limitów,
blokowania sprzedaży ani automatycznego stosowania rabatów/terminów.

## Samodzielne sprawdzenie

1. Uruchom API i Avalonia według [instrukcji kartoteki](contractors.md).
   Zaloguj operator.m0, wybierz firmę A, pokaż kartotekę i otwórz zapisanego
   kontrahenta (F2 lub dwuklik). Dla nowej pozycji najpierw zapisz dane główne.
2. Przejdź do **Finansowe** i wybierz **Sprzedaż**. Wpisz np. termin `14`,
   Przelew, EUR, rabat `2,5` i fikcyjne uwagi. Wybierz **Zapisz warunki**.
3. Przełącz na **Zakup**. Powinien mieć własne puste ustawienia. Zapisz np.
   termin `30`, Gotówkę, PLN i rabat `0`. Powrót do Sprzedaży pokaże jej wartości.
4. W **Nowy limit** wybierz PLN i wpisz np. `10000`, pozostaw Aktywny, zapisz.
   Dodaj osobny EUR `2500`. Możesz również dodać EUR w Zakupie — to inny limit.
   Drugi aktywny limit EUR w tym samym kierunku i kontrahencie zostanie odrzucony.
5. Zaznacz limit i wybierz **Edytuj limit / dezaktywuj**, odznacz Aktywny i zapisz.
   Historia pozostaje; można dodać nowy aktywny limit tej samej waluty. Bezpośrednie
   przełączenie na aktywny, gdy istnieje inny, zostanie odrzucone bez nadpisania.
6. Rozwiń **Słownik sposobów płatności**. Nowy sposób wymaga nazwy; kod techniczny
   jest generowany raz i pozostaje stały. Edycja pozwala zmienić nazwę/aktywność.
   Zmiana nazwy jest widoczna w wyborze warunków; nie zmienia stabilnego kodu.
   Nie ma przycisku fizycznego usuwania. Nieaktywny sposób pozostaje czytelny
   w dawnych ustawieniach, ale nie może być nowym wyborem.
7. Otwórz firmę B kontem operator.m0. Finansowe można przeglądać, ale zapis,
   edycja limitów i słownika są wyłączone. API również odmawia zapisu.

Puste dni, waluta, sposób, rabat lub uwagi oznaczają **brak indywidualnego
ustawienia**. Pusta waluta nie podstawia PLN. Dni `0` i rabat `0` są jawnymi
ustawieniami, odrębnymi od pustego pola. Można wyczyścić wszystkie warunki
zapisanej konfiguracji; rekord i audyt pozostają, bez fizycznego usuwania.

**Domyślna waluta** oferuje „Brak indywidualnej waluty” i aktywne waluty firmy.
Nowy limit pozwala wybierać tylko aktywne waluty. Zapisane warunki i limity
w walucie później dezaktywowanej zachowują kod i kwoty; obok waluty pojawia się
„nieaktywna”. Podczas edycji można zachować ten kod, zmieniając inne pola,
albo wybrać aktywną walutę. Nieaktywna pozycja nie jest nowym wyborem.
Przy aktywnych PLN/EUR i nieaktywnej USD nowy wybór obejmuje tylko PLN/EUR,
a istniejąca USD pozostaje widoczna. Odświeżanie słowników nie tworzy zmian
w szkicu; rzeczywista zmiana nadal wymaga zapisu lub świadomego porzucenia.

Termin: liczba całkowita 0–3650. Rabat: decimal 0–100%, do 4 miejsc po przecinku.
Limit: nieujemny decimal, do 16 cyfr całkowitych i 8 po przecinku. Można wpisać
przecinek lub kropkę dziesiętną, bez separatorów tysięcy. Nadmiar cyfr po przecinku
jest błędem, nie jest cicho zaokrąglany. Uwagi mają maksymalnie 2000 znaków.
To ograniczenia integralności; AcceptWarnings nie pozwala ich ominąć.

## Niezależne szkice, anulowanie i konflikty

Warunki, edycja limitu i edycja sposobu płatności mają osobne przyciski zapisu
oraz anulowania. Zapis jednego nie zapisuje pozostałych szkiców. **Dane główne
nie są wysyłane przy zapisie finansów**. Można np. zmienić nazwę kontrahenta
w danych głównych, przejść do Finansowe, zapisać warunki i nadal mieć niezapisaną
nazwę. Zamknięcie formularza zapyta o jej porzucenie.

Przełączenie kierunku/zakładki i odświeżenie finansów pytają o porzucenie
niezapisanych szkiców finansowych. **Wróć do edycji** je zachowuje. Anulowanie
konkretnego szkicu wymaga potwierdzenia, jeśli był zmieniany. **Odśwież finanse**
po potwierdzeniu odczytuje aktualne ustawienia i słowniki; nie odświeża szkicu
nazwy ani identyfikatorów kontrahenta.

W Finansowe Ctrl+S zapisuje tylko warunki, Ctrl+N otwiera nowy limit, F2 otwiera
zaznaczony limit, Esc zamyka kartotekę z kontrolą szkiców. Limit i słownik mają
własne przyciski zapisu; skrót nigdy nie zapisuje danych głównych z tej zakładki.

Konflikt sprawdzisz w dwóch uruchomionych klientach: otwórz te same warunki
lub ten sam limit/sposób płatności, zapisz zmianę w pierwszym, potem w drugim.
Drugi zapis otrzyma 409; panel pokazuje Twoje pola i wersję serwera. Wybierz
**Wczytaj wersję serwera** lub **Zachowaj moje pola**, porównaj i dopiero ponownie
zapisz. Nie ma force-update ani automatycznego zastępowania. Równoczesne pierwsze
utworzenie warunków tej samej pary również nie tworzy dwóch konfiguracji.

## Model i API

Każda baza firmy ma trzy nowe tabele, bez CompanyId i bez relacji do innej bazy:

| Tabela | Struktura |
|---|---|
| core.ContractorTradeTerms | Id int IDENTITY; ContractorId FK; Direction varchar(8); PaymentDays int NULL; PaymentMethodCode varchar(32) NULL FK; CurrencyCode char(3) NULL FK; DiscountPercent decimal(7,4) NULL; Notes nvarchar(2000) NULL; RowVersion; CreatedAtUtc/UpdatedAtUtc |
| ref.PaymentMethods | Id int IDENTITY; Code varchar(32) unikalny i niezmienny; Name nvarchar(100); IsActive; RowVersion; CreatedAtUtc/UpdatedAtUtc |
| core.ContractorCreditLimits | Id int IDENTITY; ContractorId FK; Direction varchar(8); CurrencyCode char(3) NOT NULL FK; Amount decimal(24,8); IsActive; RowVersion; CreatedAtUtc/UpdatedAtUtc |

Unikalna para ContractorId/Direction dla warunków. Filtrowany indeks limitów
na ContractorId/Direction/CurrencyCode WHERE IsActive=1; nieaktywnych może być
wiele. FK walut wskazują istniejące **ref.Currencies.Code**, z tą samą kolacją
Latin1_General_100_BIN2. Id kursów i słownik walut pozostają bez zmian.
PaymentMethods.Id służy istniejącemu audytowi RecordId; FK warunków wskazuje
unikalny Code. Nie ma FK do użytkownika centralnego ani nowych tabel audytu.

Początkowe kody: TRANSFER/Przelew, CASH/Gotówka, CARD/Karta,
OFFSET/Kompensata, OTHER/Inny. Użytkownik edytuje nazwę; API odrzuca zmianę kodu.
Nowy kod ma do 32 znaków ASCII (wielkie litery, cyfry, _ i -, początek literą).

| Trasa w /api/v1/companies/{companyId} | Działanie |
|---|---|
| GET /financial-lookups | Waluty i sposoby płatności; odczyt ViewContractors bez wymagania uprawnień do modułu kursów |
| GET /contractors/{id}/trade-terms/{Sale\|Purchase} | Właściwe warunki; przy braku Id=0, RowVersion=null i puste wartości |
| PUT /contractors/{id}/trade-terms/{Sale\|Purchase} | Pierwszy zapis z OperationId, kolejny z RowVersion |
| GET /contractors/{id}/credit-limits?direction=Sale&offset=0&limit=50 | Stronicowana lista danego kierunku, także nieaktywne; maksymalnie 100 |
| POST /contractors/{id}/credit-limits | Utworzenie limitu z OperationId |
| PUT /contractors/{id}/credit-limits/{limitId} | Edycja/aktywacja/dezaktywacja z RowVersion |
| POST /payment-methods | Nowa pozycja słownika z OperationId |
| PUT /payment-methods/{code} | Edycja nazwy/aktywności z RowVersion; kod niezmienny |

Odczyt wymaga firmowego ViewContractors, zapis dodatkowo EditContractors i CSRF.
Nie dodano nowych praw ani nie zmieniono przypisań. Błędne waluty i sposoby są
odrzucane przez API oraz chronione FK; nieaktywne można zachować w istniejącym
odwołaniu, ale nie wybierać jako nowej wartości. Wszystkie operacje wymagają
sesji, uprawnienia do firmy i prawidłowego profilu oraz historii migracji bazy.

Zapis i **audit.ContractorChanges** są atomowe. RecordType rozróżnia
ContractorTradeTerms, ContractorCreditLimit i PaymentMethod; RecordId wskazuje
lokalne int Id. Historia ma wartości przed/po, czas, trwałego aktora/issuer
oraz historyczną nazwę, SnapshotVersion=3. Nie kopiuje danych głównych ani PESEL.
Tworzenie utrwala wynik w ContractorCreateOperations w tej samej transakcji;
ponowienie tego samego skutecznego żądania nie dodaje rekordu ani audytu.
Inna treść z tym samym kluczem daje 409. Nie usuwamy danych fizycznie.

## Wdrożenie bez resetów

Jawnie wykonano **20261023182240_ContractorFinance** osobno w lokalnych
Vatowiec_Company_A_Dev i Vatowiec_Company_B_Dev. Firmy mają po 9 migracji,
System nadal 11, bez DDL w bazie systemowej. Up tworzy tylko trzy tabele,
ich ograniczenia/indeksy i pięć początkowych sposobów; nie zmienia kartotek,
walut, integracji, konfiguracji ani kluczy. Przy inicjalizacji nowej firmy
migracja tworzy tę samą strukturę. Nie ma migracji przy starcie API.

Sprawdzono lokalny SQL Server 17 Express/SQLEXPRESS, maszynę, Windows Authentication,
dokładne nazwy baz, znaczniki i znaną historię. Kopie COPY_ONLY/CHECKSUM przeszły
RESTORE VERIFYONLY. W katalogu SQL Backup:

- Vatowiec_Company_A_Dev_before_contractor_finance_ac0acb4d204f480fafe4d4ba14f0f183.bak
- Vatowiec_Company_B_Dev_before_contractor_finance_b65ac532ee69412db19ad21b1d7c0a25.bak

Pierwsza próba DDL wykryła kolację inną niż w kluczu Currencies.Code i została
wycofana bez zmiany danych. Poprawiono jeszcze niezastosowaną migrację oraz
mapowania/snapshot i dodano kontrolę kolacji do testu. Ponowione wdrożenie przeszło.
Nie zmieniano istniejących pól walut ani banków.

**42 istniejące tabele** miały identyczne liczności/SHA256 po migracji, w tym
szyfrogramy GUS i DataPort. Po testach **41 nadal identycznych**; test logowania
oraz próba GUI dopisały zwykłe, następnie unieważnione UserSessions. Nowe testy
finansów wycofują wszystkie swoje dane, audyt i konta; nie wyłączają triggerów.
Liczniki IDENTITY mogą mieć luki po rollback. Kontrahenci nadal A=4, B=1.
Po weryfikacji w każdej firmie jest 5 początkowych sposobów i brak indywidualnych
warunków/limitów — nie pozostawiono testowych ustawień użytkownikom.

Down usuwałby tabele finansów, więc nie jest bezpiecznym rollbackiem po rozpoczęciu
ich użytkowania. Ewentualne cofnięcie wymaga osobnej decyzji i zachowania danych
oraz audytu. Nie uruchamiaj destrukcyjnego runnera DatabaseTools test/initialize/
prepare-tests na aktualnych bazach.

## Wyniki pierwszego wdrożenia

**383/383**, 0 pominiętych: 374 bez SQL i 9 niedestrukcyjnych SQL/API. Build całego
solution: **0 błędów i ostrzeżeń**; formatowanie zmienionych C# i diff-check poprawne.
Nie uruchomiono **103 starszych testów SQL/API z resetującymi fixture**; nie są
zaliczone w tym przebiegu.

Nowe testy obejmują zakresy i dokładność decimal, puste ustawienia bez PLN,
fizyczne FK/checks, kolację i indeksy, rolę/prawa/CSRF, izolację A/B, niezależność
Sale/Purchase, limity PLN/EUR, konflikty rowversion, dezaktywację, edycję nazwy
przy stabilnym kodzie, odwołanie do dezaktywowanego sposobu, replay bez drugiego
audytu i wymuszoną awarię audytu z wycofaniem wcześniejszej zmiany.
Dwa rzeczywiste połączenia SQL próbują wstawić aktywny limit tego samego klucza;
drugi insert czeka na zakończenie pierwszej transakcji. Obie transakcje są
wycofywane, bez utrwalania fixture. Konflikt wersji API sprawdzono jako dwa zapisy
oparte na tej samej odczytanej wersji.

Rzeczywiste okno Avalonia: zakładka, widok Zakupu, brak automatycznego PLN,
walidacja dni, ochrona przełączania/anulowania szkiców warunków/limitu/sposobu,
zachowanie niezapisanej nazwy danych głównych, read-only firmy B, logout.
Próba GUI **nie zapisała danych**. Realne zapisy/edycje API/SQL wykonano w testach
z rollback, a rozdzielenie zapisów klienta dodatkowo w testach modelu widoku.
Nie wykonywano zapytań produkcyjnych ani zmian integracji GUS/MF/VIES/DataPort/banków.

```powershell
dotnet build Vatowiec.slnx --no-restore
$env:VATOWIEC_PRESERVED_VAT_SQL = '1'
dotnet test Vatowiec.slnx --no-build --filter '(Category!=SqlIntegration)|FullyQualifiedName~ContractorFinanceSqlTests|FullyQualifiedName~StandardIdentifierSqlTests|FullyQualifiedName~OfficialVatRulesSqlTests|FullyQualifiedName~GusConfigurationSqlTests|FullyQualifiedName~RegistrySettingsSqlTests' --logger 'trx;LogFileName=finance-preserved.trx' --results-directory TestResults
Remove-Item Env:VATOWIEC_PRESERVED_VAT_SQL
```

Raporty w ignorowanym TestResults: finance-build.log, finance-format.log,
finance-preserved.log/.trx, finance-ui.log, finance-backup.log,
finance-migration.sql, finance-{before,after,final}-data.json.
Decyzje i precyzja modelu: [ADR-031](adr/ADR-031-warunki-finansowe-kontrahenta.md).

## Naprawa formularza Finansowe — 2026-10-23

Zgłoszona regresja dotyczyła wyłącznie prezentacji i inicjalizacji szkicu.
Odczyt SQL oraz rzeczywisty endpoint zwracały dla ContractorId=4132:
Sale = 30/TRANSFER/PLN, Purchase = 45/TRANSFER/PLN. Odpowiedź DTO i klient
Get<TradeTermsResponse> przenosiły kody bez zmiany. Nie zmieniono API, modelu,
kontraktów ani schematu; nie wykonano migracji ani resetów.

Przyczyny i naprawa:

- Gettery list wyboru tworzyły nowe tablice i nowe puste pozycje przy każdym
  powiadomieniu ViewModelu. Dwukierunkowy SelectedItem przenosił przejściowy
  wybór pustej pozycji do szkicu, zamiast odtwarzać zapisany kod. To również
  powodowało różnicę wobec snapshotu i fałszywe ostrzeżenie o zmianach.
  Listy są teraz stabilne, wybór odszukiwany po Code, a publikacja słowników
  i danych nie stanowi edycji. Przejściowe null oraz wybory podczas ładowania
  są ignorowane. Prezentacja jest jednokierunkowa; tylko interaktywna zmiana
  kontrolki przekazuje kod do szkicu. Obsługiwane są mysz i klawiatura.
- Po odrzuceniu zmiany kierunku przez ochronę szkicu dwukierunkowy binding
  mógł pozostawić inny widoczny kierunek niż w ViewModelu. Kontrolka jest teraz
  jawnie synchronizowana z zaakceptowanym kierunkiem, także po „Wróć do edycji”.
  Snapshot i RowVersion pochodzą z odpowiedzi konkretnego Sale/Purchase.
- Nieznany kod nie jest pustą wartością: pojawia się z opisem „kod niedostępny
  w słowniku” i ostrzeżeniem, oryginalny kod pozostaje w szkicu. Zapis warunków
  jest wstrzymany do odświeżenia słownika lub świadomego wyboru innej pozycji.
  Użytkownik nadal może świadomie wybrać pozycję „Brak indywidualnego…”.
- Tylko zakładka Finansowe ma nowy układ: maksymalnie 860 px, dwie kolumny
  termin/sposób oraz waluta/rabat, pola liczbowe 140 px, osobne uwagi i tabela
  limitów (waluta, kwota, status). Przyciski zapisu/anulowania są przy warunkach;
  odświeżanie jest pomocnicze. Edytor limitu także ma zwarty układ. Pozostałe
  zakładki i integracje pozostały bez zmian w ramach tej naprawy.

Dodano 7 regresji w ContractorFinanceBindingTests: dokładne dane 4132 w atrapie,
obie wersje RowVersion, stabilność list, opóźnione słowniki, przejściowe callbacki,
ponowne otwarcie, zapis i odczyt, brak automatycznych zapisów, nieznane kody,
ochrona prawdziwych zmian, niezależność danych głównych, read-only i limity.
**390/390** wykonanych testów: 381 bez SQL + 9 niedestrukcyjnych SQL/API.
0 niepowodzeń i 0 pominiętych w wybranym zestawie. 103 dawne testy resetujące
pozostały wyłączone i nie są zaliczone. Build: 0 błędów, 0 ostrzeżeń;
formatowanie zmienionych C# i diff-check poprawne. Filtr powyżej pozostaje właściwy.

Rzeczywiste GUI sprawdzono przez Windows UI Automation i ogląd zrzutu okna:

1. 4132 odczytano bez zapisu: poprawne trzy wartości obu kierunków, przełączanie,
   odświeżanie i ponowne otwarcie bez pytania o porzucenie. SHA256 wszystkich
   kolumn jego ContractorTradeTerms, także RowVersion, nie zmieniło się.
2. Osobny fikcyjny kontrahent **FIN-UI-1023212310, Id=4173**: rzeczywisty zapis
   Sale30/TRANSFER/PLN i Purchase45/TRANSFER/PLN oraz ponowne otwarcie. Dodatkowo
   zmiana Sale na Gotówka/EUR, zapis i odczyt, potem przywrócenie TRANSFER/PLN.
3. Rzeczywista zmiana Purchase na 46 uruchomiła ochronę; „Wróć do edycji”
   zachowało 46 i widoczny Zakup, porzucenie przywróciło zapisane 45 i pozwoliło
   przejść na Sprzedaż. Zapis finansów zachował szkic nazwy danych głównych,
   a po jego odrzuceniu oryginalna nazwa nadal była w bazie.
4. W GUI utworzono i edytowano limity: Sale EUR 2600 oraz PLN 10000,
   Purchase PLN 5000. Przełączanie zachowało właściwe listy. Firma B była
   tylko do odczytu, bez możliwości zapisu warunków, wyboru lub edycji limitów.
5. Pomiar rzeczywistych kontrolek: sekcja 860 px, pola liczbowe 140 px,
   poprawne dwa wiersze. Przy oknie 920×650 szerokość sekcji wynosiła 824 px,
   działało przewijanie pionowe bez wychodzenia poza obszar formularza.
   Tab: termin → sposób → waluta → rabat → uwagi → Zapisz → Anuluj.

Fikcyjny kontrahent 4173 został po próbie oznaczony jako nieaktywny; jego warunki,
limity i audyt pozostają do wglądu. Nie usuwano danych. Kontrahent 4132 nie był
celem żadnej próby zapisu. Nie uruchamiano zapytań do rejestrów zewnętrznych.

Logi lokalne: TestResults/finance-repair-{build,preserved,format}.log,
finance-repair-preserved.trx, finance-repair-ui*.log, finance-repair-data-final.log
oraz finance-repair-ui.png. Nie zawierają haseł ani sesji użytkownika.

Szybka regresja ręczna: otwórz zapisane warunki obu kierunków, przełącz je bez
edycji, zamknij i otwórz ponownie. Następnie na fikcyjnym kontrahencie zmień
jedno pole, sprawdź obie decyzje ochrony szkicu, zapisz i otwórz ponownie.
Porównaj również kod waluty i sposób, a nie tylko termin w dniach.
