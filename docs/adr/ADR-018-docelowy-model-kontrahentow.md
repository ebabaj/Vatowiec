# ADR-018: Docelowy model kontrahentów i niezależna historia weryfikacji

Data: 2026-10-22

Status: zatwierdzony model logiczny, uzupełniony rozstrzygnięciami użytkownika
po pierwszym zapisie ADR-018, w tym definicjami adresów i rachunków bankowych.
Szczegóły pozostawione do implementacji wskazuje §4. Dokument nie oznacza
wdrożenia modelu.

Wymagania: ORG-003, ORG-004, ORG-005, ORG-007, ORG-008, E04;
SPECYFIKACJA §3, §8, §10, §11.2. Źródło decyzji: ustalenia użytkownika
z 2026-10-22 i ich doprecyzowanie. Pierwszy zapis wskazywał pytania K-01–K-09;
§4 zachowuje ślad ich rozstrzygnięcia. Opis KsefIdWew zweryfikowano w oficjalnych
materiałach MF (§6); nie implementujemy reguł podatkowych ani integracji.

**Aktualizacja etapu:** użytkownik zakończył prace wyłącznie dokumentacyjne i zlecił implementację podstawy modelu. [ADR-019](ADR-019-implementacja-modelu-kontrahentow.md) opisuje wykonany zakres i rozstrzygnięcia techniczne. Ograniczenia dokumentacyjne poniżej zachowują kontekst powstania ADR-018; zatwierdzone decyzje biznesowe pozostają bez zmian.

## 1. Kontekst i granice pierwotnego zadania dokumentacyjnego

Wstrzymujemy implementację nowych funkcji na czas wspólnego projektowania
docelowego modelu kontrahentów. Bieżący kod, schemat SQL, migracje EF Core
i dane demonstracyjne pozostają bez zmian. Ten etap obejmuje tylko dokumentację.

W bazach projektu VATowiec znajdują się wyłącznie fikcyjne dane demonstracyjne,
które nie wymagają zachowania. Po zatwierdzeniu szczegółów modelu przy przyszłej
implementacji można usunąć i odtworzyć od podstaw wyłącznie demonstracyjne bazy
VATowca. Przed taką operacją nadal trzeba potwierdzić dokładną nazwę
i przeznaczenie bazy; zgoda nie obejmuje baz systemowych SQL Server ani baz
innych aplikacji. W tym zadaniu nie wykonujemy żadnych operacji na SQL Serverze.

**Nie projektujemy migracji istniejących danych:** nie przenosimy dawnych GUID,
nie tworzymy map starych i nowych identyfikatorów, nie zachowujemy przykładowych
kontrahentów i nie wymagamy zgodności z dawnym schematem demonstracyjnym.
Możliwość odtworzenia demonstratora nie znosi docelowych zasad ochrony danych,
backup/restore i audytu używanego systemu. Nie jest też poleceniem odtworzenia
baz w obecnym etapie.

## 2. Relacja do wcześniejszych ADR

| Decyzja | Co pozostaje | Co zmienia niniejszy ADR w modelu docelowym |
|---|---|---|
| [ADR-005](ADR-005-izolacja-firm-i-pierwszy-model.md) | Historyczny zapis wspólnej bazy, zastąpiony przez ADR-014 | Nie przywracamy wspólnej bazy |
| [ADR-014](ADR-014-database-per-company.md) | Osobna baza każdej firmy, dwa konteksty, profil tożsamości bazy, niezależny backup i lokalne transakcje | Odchodzimy od powielania CompanyId w tabelach danych jednej firmy, zwłaszcza Contractors; nie wymagamy przenoszenia identyfikatorów demonstratora |
| [ADR-015](ADR-015-identity-i-autoryzowany-wybor-bazy.md) | Uwierzytelnienie, CompanyAccess, sesje, CSRF i autoryzowany resolver bez domyślnej bazy | Nie usuwamy CompanyId z katalogu, uprawnień ani kontekstu wyboru firmy |
| [ADR-016](ADR-016-desktop-logowanie-i-kontekst-firmy.md) | Klient korzystający wyłącznie z API HTTPS, izolacja sesji i obsługa odmów | Przyszłe kontrakty kontrahentów mogą zostać dostosowane; obecnego klienta nie zmieniamy |
| [ADR-017](ADR-017-kartoteka-kontrahentow-i-audyt.md) | Idempotencja, rowversion, kontrola uprawnień i koncepcja atomowego audit.ContractorChanges | Preferujemy int IDENTITY zamiast GUID kontrahenta, rozdzielamy identyfikatory podatkowe, rozszerzamy model i wprowadzamy audytowane odstępstwa od walidacji podatkowej |

ADR-014 i ADR-017 pozostają zapisem wcześniejszych decyzji oraz wdrożonego
demonstratora. Niniejszy ADR zmienia wskazane założenia przyszłego modelu,
nie zastępuje w całości ich zasad bezpieczeństwa i nie przepisuje ich historii.

## 3. Zatwierdzone ustalenia

### 3.1. Baza firmy i identyfikatory

Architektura **database-per-company** pozostaje obowiązkowa. Kontrahenci,
ich adresy, kontakty, rachunki i obie historie audytowe należą do odpowiedniej bazy firmy.
Baza systemowa nadal przechowuje centralny katalog, użytkowników i dostęp,
bez kartotek kontrahentów i ich historii weryfikacji.

Docelowo usuwamy zbędne CompanyId z tabel zawierających wyłącznie dane jednej
firmy, w szczególności z core.Contractors. Tożsamość firmy nadal musi być
weryfikowalna w profilu bazy; centralny katalog, CompanyAccess i mechanizmy
autoryzacji zachowują CompanyId. Usunięcie kolumny z rekordu kontrahenta nie
oznacza operacji API bez kontekstu firmy ani zaufania do samego Id rekordu.
Serwer nadal uwierzytelnia użytkownika, sprawdza firmę i czynność oraz wybiera
i weryfikuje właściwą fizyczną bazę. Nie wprowadzamy domyślnej bazy.

Preferowane klucze rekordów danych kontrahentów to **int IDENTITY**, w tym
Contractors.Id nadawany przez bazę,
zamiast GUID nadawanego kontrahentowi przez klienta. Jego zakres jest lokalny
dla bazy firmy; ten sam numer może oznaczać różne rekordy w różnych firmach.
Nie zmieniamy tą decyzją typów identyfikatorów firm ani użytkowników.

Idempotencja tworzenia jest niewidocznym dla użytkownika mechanizmem operacji,
odrębnym od klucza kontrahenta. Projektujemy minimalny przebieg:

1. Klient automatycznie nadaje jeden klucz operacji dla żądania utworzenia
   i używa go ponownie przy wysłaniu tego samego żądania po utracie odpowiedzi.
   Użytkownik nie wpisuje ani nie zarządza takim kluczem.
2. Serwer przy każdej próbie sprawdza sesję, firmę i uprawnienie. Rozpoznaje
   klucz w zakresie właściwej firmy, wykonawcy i rodzaju operacji oraz porównuje
   treść żądania. Ta sama operacja z inną treścią jest konfliktem, nie drugim create.
3. Pierwsze skuteczne wykonanie utrwala kontrahenta z wygenerowanym int,
   audit.ContractorChanges oraz powiązanie klucza operacji z wynikiem w jednej
   transakcji bazy firmy. Baza musi chronić obsługę klucza przed równoległym
   wykonaniem; samo sprawdzenie w pamięci API nie wystarczy.
4. Ponowienie zatwierdzonej operacji odczytuje utrwalony wynik i zwraca ten sam
   Id, także po restarcie API lub utracie odpowiedzi. Nie tworzy kolejnego
   kontrahenta ani zdarzenia audytu. Błąd wycofujący transakcję nie zostawia
   pozornego sukcesu; ponowienie może wykonać tę samą operację.

To projekt zachowania operacji, nie nowa zatwierdzona tabela. Fizyczny zapis
powiązania i jego retencję dobierzemy przy implementacji, zachowując gwarancję
bezpiecznych ponowień. Ewentualny GUID klucza operacji nie jest głównym kluczem
kontrahenta ani mapą dawnych GUID. Rowversion, spójność relacji i świadome
rozstrzyganie konfliktów edycji pozostają wymagane.

### 3.2. Hierarchia i brak dziedziczenia

**core.Contractors** przechowuje samodzielne podmioty, grupy kontrahentów
i jednostki organizacyjne. **ParentId** wskazuje rekord nadrzędny tej samej
tabeli i służy przede wszystkim grupowaniu. Rekordy główne nie wymagają nadrzędnego.
Relacja jest lokalna dla bazy firmy; nie oznacza automatycznie zależności podatkowej.

Grupa bez NIP może skupiać niezależne podmioty, z których każdy ma własny NIP.
W przyszłym wystawianiu faktury wybranie grupy bez identyfikatora podatkowego
ma pokazać listę powiązanych podmiotów do wyboru. Nie wpisujemy w jej miejsce
automatycznie pierwszego dziecka i nie kopiujemy numeru podmiotu do grupy.

Samodzielny podmiot z odpowiednim identyfikatorem może być nabywcą. Jednostka
organizacyjna, która nie jest samodzielnym nabywcą, może być wskazana w KSeF
jako Podmiot3, zgodnie z rolą transakcyjną i kontraktem KSeF. Sam ParentId
ani sam brak TaxId nie wyznacza tej roli (§3.3.1).

**Nie ma automatycznego dziedziczenia żadnych danych.** Każdy rekord ma własną
nazwę, identyfikatory, adresy, rachunki i kontakty. Zmiana danych nadrzędnego
nie zmienia danych podrzędnego. Dotyczy to również TaxId, VatEuId i KsefIdWew.
Nie projektujemy wyjątków ani mechanizmu nadpisywania wartości odziedziczonych.

### 3.3. Trzy osobne identyfikatory w Contractors

| Pole zatwierdzone w modelu docelowym | Znaczenie |
|---|---|
| TaxId | Opcjonalny krajowy identyfikator podatkowy, tekst do 100 znaków |
| VatEuId | Opcjonalny pełny numer VAT UE z prefiksem kraju w jednym polu tekstowym, do 100 znaków |
| KsefIdWew | Opcjonalny identyfikator jednostki wewnętrznej |

TaxId i VatEuId pozostają odrębnymi polami w Contractors. **Dopuszczamy duplikaty**:
aplikacja ostrzega, ale uprawniony użytkownik może świadomie potwierdzić zapis.
Nie wprowadzamy blokującego unikalnego klucza tych identyfikatorów. Nie tworzymy
tabeli **ContractorTaxIdentifiers**. Dotychczasowe TaxIdentifier opisuje wyłącznie
wdrożony demonstrator. Szczegółowe algorytmy normalizacji i walidacji krajowych
numerów i VAT UE ustalimy przy implementacji walidatorów.

### 3.3.1. KsefIdWew — odniesienie do oficjalnej dokumentacji KSeF

KsefIdWew przechowuje pełny identyfikator jednostki wewnętrznej podatnika,
określany w FA(3) jako **IDWew**, a w API jako **InternalId**. MF opisuje go jako
wartość wygenerowaną w KSeF do rozróżniania jednostek wewnętrznych, nie odrębny
NIP podatnika. Broszura FA(3), tabela 15 na s. 30, umieszcza IDWew w
Podmiot3/DaneIdentyfikacyjne. [Źródło MF: broszura FA(3)](https://ksef.podatki.gov.pl/media/jknpcymf/broszura-informacyjna-dotyczaca-struktury-logicznej-fa-3-04032026.pdf).

Oficjalne OpenAPI KSeF opisuje format **`{nip}-{5_cyfr}`**: dziesięć cyfr NIP
podatnika, łącznik i pięć dodatkowych cyfr, łącznie 16 znaków. Przechowujemy
pełny tekst, z łącznikiem i ewentualnymi zerami początkowymi. To opis formatu
zewnętrznego, nie implementacja walidatora ani ustalenie fizycznego typu kolumny.
[Źródło MF: OpenAPI PR v2, EntityPermissionsContextIdentifier i SubunitPermissionsContextIdentifier, API 2.9.0](https://api.ksef.mf.gov.pl/docs/v2/openapi.json).

Identyfikator ten nie zastępuje TaxId/VatEuId samodzielnego nabywcy ani
wewnętrznego int Id kartoteki. Obecność NIP w IDWew nie uzasadnia automatycznego
uzupełnienia TaxId na podstawie ParentId. Dobór nabywcy i Podmiot3 musi uwzględniać
rzeczywistą rolę podmiotu oraz wymagania FA(3), a nie samą hierarchię grupowania.
Na tym etapie nie generujemy IDWew, nie wdrażamy jego walidacji, faktur ani integracji.
Źródła sprawdzono 2026-10-22; przed implementacją trzeba ponownie zweryfikować
odpowiednią wersję dokumentacji i schematu.

### 3.4. Słownik reguł i miejsce walidacji

Projektujemy **ref.TaxIdentifierSchemes** jako słownik reguł sprawdzania
identyfikatorów podatkowych, docelowo obejmujący co najmniej wszystkie państwa UE.
Słownik będzie **tabelą SQL** z możliwością wersjonowania reguł, nie wyłącznie
zestawem stałych w kodzie. Zmiana reguł nie może zmieniać znaczenia zapisanej
historii walidacji ani świadomego odstępstwa.

Walidacja podatkowa odbywa się w warstwie aplikacyjnej/API, **nie przez podatkowe
CHECK CONSTRAINT w SQL**. Aplikacja oczyszcza i normalizuje numer zgodnie
z właściwymi regułami kraju, sprawdza go i dopiero następnie proponuje zapis.
Nie opisujemy tutaj algorytmów, wzorców numerów ani sum kontrolnych. Konkretne
reguły wymagają oficjalnych źródeł, wersji, zakresu obowiązywania i przykładów
testowych przed implementacją.

Położenie słownika w topologii baz, dystrybucja wersji, powiązania i nazwy
kolumn są otwarte. Niezależnie od miejsca słownika firmowe dane zapytań
i wyniki weryfikacji pozostają w bazie firmy. Wymagania integralności SQL,
np. poprawne FK i klucze, nie są znoszone przez przeniesienie walidacji
podatkowej do aplikacji.

### 3.5. „Użytkownik ma zawsze rację”

Jest to fundamentalna filozofia VATowca: jeżeli walidacja identyfikatora nie
powiedzie się, program wyjaśnia błąd, a uprawniony użytkownik może **świadomie
zatwierdzić zapis pomimo ostrzeżenia**. Nie wolno sprowadzać tego do cichego
pominięcia walidacji ani automatycznego uznania numeru za poprawny.

Odstępstwo musi być odnotowane w audycie wraz z powiązaniem ze zmianą,
wykonawcą, czasem i kontekstem ostrzeżenia/reguły. Zapis zmiany kartoteki
i informacji o świadomym odstępstwie ma zachować zasadę atomowego audytu.
Wymagane jest uprawnienie **do akceptowania ostrzeżeń**, oprócz zwykłych praw
do firmy i edycji. Użytkownik z tym uprawnieniem może zatwierdzić nieprawidłową
wartość **bez podawania uzasadnienia**. Dotyczy to także ostrzeżenia o duplikacie
TaxId/VatEuId. Nazwę techniczną uprawnienia i kontrakt API potwierdzający dokładnie
pokazane ostrzeżenie dopracujemy przy implementacji; nie jest otwarte, czy
uzasadnienie ma być obowiązkowe — nie wymagamy go.

Ta zasada **nie pozwala ominąć** autoryzacji firmy i czynności, uwierzytelnienia,
CSRF, spójności relacji, kluczy ani kontroli współbieżności. Nie znosi też
wymogów systemów zewnętrznych: zapis numeru w kartotece pomimo ostrzeżenia
nie gwarantuje przyjęcia go przez usługę lub format zewnętrzny i nie zmienia
rzeczywistego wyniku zapytania urzędowego.

### 3.6. Dwie odrębne historie

Zachowujemy koncepcję **audit.ContractorChanges** jako historii zmian kartoteki.
Jej implementacja może zostać dostosowana do nowych kluczy, relacji i pól.
Pozostają atomowość zapisu oraz trwała identyfikacja autora, także po usunięciu
konta centralnego, bez uzależnienia historii od jego dalszego istnienia.

Osobna **audit.ExternalVerifications** będzie przechowywać niezmienne zdarzenia
sprawdzeń w rejestrach zewnętrznych, w tym na białej liście VAT i w VIES.
Poniższe pozycje są zatwierdzonym **zakresem informacji**, nie gotowymi nazwami
kolumn ani fizycznym schematem:

| Informacja | Wymaganie |
|---|---|
| Źródło | Rejestr/usługa, z której wykonano sprawdzenie |
| Rodzaj sprawdzenia | Co podlegało weryfikacji, niezależnie od samej nazwy źródła |
| Kontrahent | Identyfikator sprawdzanego rekordu w bazie firmy |
| Rachunek | Opcjonalne powiązanie z rachunkiem, jeśli uczestniczy w sprawdzeniu |
| Dane zapytania | Dane rzeczywiście użyte w zapytaniu, w tym faktycznie wysłany identyfikator i opcjonalny numer rachunku; nie tylko odsyłacz do ich późniejszego stanu w kartotece |
| Data stanu sprawdzanego | Na jaki dzień sprawdzano stan; odrębna od czasu wykonania zapytania |
| Czas wykonania | Kiedy rzeczywiście wykonano zapytanie |
| Wynik | Rezultat merytoryczny, odróżniony od braku możliwości jego uzyskania |
| Referencja urzędu | Identyfikator referencyjny zwrócony przez źródło, jeśli został udostępniony |
| Błąd | Ewentualny błąd wykonania/usługi, bez uznawania go za wynik negatywny |
| Użytkownik | Trwała identyfikacja wykonawcy, samowystarczalna w historii firmy również po usunięciu konta |

**Wynik negatywny i niedostępność usługi są różnymi zdarzeniami.** Awaria,
timeout lub brak odpowiedzi nie mogą być przedstawiane jako urzędowe
potwierdzenie negatywnego statusu. Dokładne statusy i odwzorowanie odpowiedzi
poszczególnych rejestrów ustalimy na podstawie ich kontraktów przed implementacją.

Historia sprawdzeń jest niezależna od historii zmian kontrahenta. Ponowne
sprawdzenie dopisuje kolejne zdarzenie ExternalVerifications, bez wymogu zmiany
kontrahenta, jego rowversion ani dopisywania pozornej zmiany ContractorChanges.
Późniejsza edycja kontrahenta, adresu, rachunku lub reguł nie przepisuje
wcześniejszego zdarzenia. Obie historie są niezmienne i należą do bazy odpowiedniej firmy;
wyniki zapytań nie stają się centralną kartoteką w Vatowiec_System.

### 3.7. Adresy i otwarty słownik ich rodzajów

Zatwierdzone nazwy: **core.ContractorAddresses** i **ref.AddressTypes**.
Kontrahent może mieć wiele własnych adresów; każdy adres wskazuje swój rodzaj
z otwartego słownika. Początkowe pozycje słownika to **Adres rejestrowy**
i **Adres korespondencyjny**. Użytkownik może dopisywać własne rodzaje.
Kontrahent może mieć kilka adresów tego samego rodzaju. W ramach pary
**ContractorId, AddressTypeId** najwyżej jeden adres może być preferowany
(IsDefault). Nie ograniczamy liczby wszystkich adresów tej pary do jednego.

Użytkownik potwierdził następującą definicję docelową. NULL oznacza pole
opcjonalne; pozostałe pola są wymagane. Jest to dokumentacja modelu,
nie wykonany schemat SQL.

| Kolumna | Typ SQL | Znaczenie |
|---|---|---|
| Id | int IDENTITY | Klucz główny |
| ContractorId | int | FK do core.Contractors |
| AddressTypeId | int | FK do ref.AddressTypes |
| Name | nvarchar(100) NULL | Dodatkowa nazwa adresu |
| Street | nvarchar(200) NULL | Ulica |
| BuildingNo | nvarchar(20) NULL | Numer budynku |
| ApartmentNo | nvarchar(20) NULL | Numer lokalu |
| PostalCode | nvarchar(20) NULL | Kod pocztowy |
| City | nvarchar(150) NULL | Miejscowość |
| Region | nvarchar(100) NULL | Województwo lub region |
| CountryCode | char(2) NULL | Kod kraju |
| IsDefault | bit | Adres preferowany dla danego rodzaju |
| IsActive | bit | Aktywny/nieaktywny |
| RowVersion | rowversion | Kontrola współbieżności |

Oddziały nie dziedziczą adresów, zgodnie z ogólną zasadą §3.2.
Adresy kontaktów z §3.8 nie są rekordami tej tabeli i nie są z nią wiązane.

### 3.8. Kontakty bez modelu CRM

Zatwierdzona jest jedna tabela **core.ContractorContacts**, zawierająca własne
kontakty danego kontrahenta. Nie dzielimy jej na osoby, działy i miejsca.
Nie tworzymy odrębnych tabel dla tych kategorii ani modelu CRM.

| Informacja kontaktu | Wymagalność |
|---|---|
| Name | Jedno obowiązkowe pole nazwy kontaktu |
| Stanowisko/funkcja | Opcjonalne |
| Telefon | Opcjonalny |
| E-mail | Opcjonalny |
| Pola adresu pocztowego | Opcjonalne, przechowywane w tym kontakcie |
| Uwagi | Opcjonalne |

Jeden kontakt może jednocześnie zawierać telefon, e-mail i adres. Ten sam
model obsługuje imienny kontakt do wysłania kartki, sam adres skrzynki
księgowości, kontakt „Magazyn” z telefonem i e-mailem pracownika oraz kontakt
„Magazyn części zamiennych” z adresem pocztowym. Name nazywa kontakt również
w przypadku skrzynki lub działu. Nie ma FK ani automatycznej synchronizacji
adresu kontaktu z core.ContractorAddresses. Nie ma dziedziczenia kontaktów
po ParentId.

### 3.9. Rachunki bankowe i domyślność według waluty

Zatwierdzona jest jedna tabela **core.ContractorBankAccounts**, z wieloma
rachunkami kontrahenta. Użytkownik potwierdził następującą definicję docelową.
NULL oznacza pole opcjonalne; pozostałe pola są wymagane.

| Kolumna | Typ SQL | Znaczenie |
|---|---|---|
| Id | int IDENTITY | Klucz główny |
| ContractorId | int | FK do core.Contractors |
| AccountNumber | nvarchar(100) | Numer NRB, IBAN lub inny |
| BankName | nvarchar(200) NULL | Nazwa banku |
| Bic | varchar(11) NULL | BIC/SWIFT |
| CurrencyCode | char(3) NULL | Waluta rachunku; brak waluty nie oznacza PLN |
| AccountHolderName | nvarchar(256) NULL | Nazwa właściciela rachunku |
| Name | nvarchar(100) NULL | Własna nazwa rachunku |
| IsDefault | bit | Rachunek domyślny dla waluty |
| IsActive | bit | Aktywny/nieaktywny |
| Notes | nvarchar(max) NULL | Uwagi |
| RowVersion | rowversion | Kontrola współbieżności |

Dla jednego ContractorId może istnieć **najwyżej jeden rachunek domyślny dla
każdej wartości CurrencyCode**. PLN i EUR są odrębnymi grupami, analogicznie
inne waluty. **NULL stanowi osobną grupę**, również z najwyżej jednym
domyślnym rachunkiem. Dopuszczalny jest brak rachunku domyślnego w każdej grupie.

Wybranie nowego domyślnego rachunku automatycznie zdejmuje oznaczenie
z poprzedniego rachunku tego samego kontrahenta i tej samej grupy walutowej.
Zmiana ma być atomowa, z audytem zmienionych pozycji. Baza ma zapewniać
unikalność domyślności w zakresie ContractorId i CurrencyCode, także dla NULL,
również przy równoległych zapisach; samo wyłączenie znacznika w UI nie wystarcza.
Nie tworzymy dodatkowej tabeli **ContractorDefaultBankAccounts**. Dokładne
ograniczenie SQL i obsługa wyścigu zostaną dobrane przy implementacji, bez DDL teraz.

**Nie wymuszamy unikalności numerów rachunków.** Ograniczenie domyślności
dotyczy znacznika IsDefault w grupie, a nie wartości AccountNumber.
Walidacja numerów rachunków odbywa się w Application/API. Uprawniony użytkownik
może świadomie zaakceptować ostrzeżenie bez uzasadnienia, z audytem zgodnie
z §3.5. Na tym etapie nie tworzymy osobnej kartoteki banków.

Definicje §3.7 i §3.9 obejmują dokładnie zatwierdzone kolumny. Nie dodajemy
kolejnych pól ani tabel bez uzasadnionej potrzeby i akceptacji użytkownika.

## 4. Rozstrzygnięcia wcześniejszych pytań i pozostałe doprecyzowania

Poniższa tabela aktualizuje pierwotną listę K-01–K-09 zamiast utrzymywać
rozstrzygnięte kwestie jako pytania otwarte. Wcześniejsze ADR-014–017 pozostają
bez zmian. Uzupełnienie ADR-018 dokumentuje nowe ustalenia, nie wdrożenie.

| Dawne ID | Aktualne rozstrzygnięcie |
|---|---|
| K-01 | ParentId grupuje samodzielne podmioty, grupy i jednostki; nie wyznacza automatycznie zależności podatkowej. Wybranie grupy bez identyfikatora przy fakturze otwiera wybór powiązanego podmiotu. Rola nabywcy/Podmiot3 wynika z rzeczywistej roli, nie z ParentId. |
| K-02 | Brak dziedziczenia jakichkolwiek danych. Każdy rekord ma własne identyfikatory, adresy, rachunki i kontakty. |
| K-03 | Nazwy ContractorAddresses, ContractorContacts, ContractorBankAccounts oraz AddressTypes zatwierdzone. Pełne kolumny i typy adresów oraz rachunków są w §3.7 i §3.9. Wiele adresów tego samego rodzaju, najwyżej jeden preferowany na kontrahenta i rodzaj. Domyślność rachunków oddzielnie dla każdej waluty i NULL, bez obowiązku wyboru domyślnego; numery rachunków nie muszą być unikalne. |
| K-04 | TaxId i VatEuId opcjonalne, tekst do 100 znaków; VatEuId z prefiksem w jednym polu. Duplikaty dozwolone po ostrzeżeniu i świadomym zatwierdzeniu przez uprawnionego użytkownika. Algorytmy walidatorów ustalimy przy ich implementacji. |
| K-05 | KsefIdWew oznacza pełny identyfikator jednostki wewnętrznej KSeF; format i źródła w §3.3.1. Nie jest numerem podatnika ani Id kartoteki. Brak dziedziczenia i implementacji integracji. |
| K-06 | TaxIdentifierSchemes to wersjonowana tabela SQL. Szczegóły wersji, umiejscowienia i aktualizacji słownika dopracujemy technicznie; nie jest otwarte, czy ma istnieć tabela SQL. |
| K-07 | Potrzebne uprawnienie akceptowania ostrzeżeń; zatwierdzenie nieprawidłowej wartości bez uzasadnienia, z audytem. Nazwa prawa i szczegółowy kontrakt API pozostają technicznym doprecyzowaniem. |
| K-08 | Klucze danych preferowane int IDENTITY. Idempotencja jest automatyczna i odrębna od Id kontrahenta; zatwierdzony cel i projekt przebiegu w §3.1. Nie projektujemy map starych GUID. |
| K-09 | ContractorChanges i ExternalVerifications pozostają niezależne; zakres informacji z §3.6 zachowany. Negatywny wynik jest odróżniony od braku odpowiedzi. Szczegóły adapterów i fizycznych kolumn dobierzemy przy implementacji. |

**Szczegóły pozostawione do implementacji:**

- Szczegółowe algorytmy numerów rachunków, krajowych identyfikatorów i VAT UE, dobór kraju/schematu
  oraz wersji reguł zostaną ustalone przy implementacji walidatorów, jak zlecił
  użytkownik. Miejsce tabel słownikowych w topologii baz i dystrybucja wersji
  nadal nie są określone przez samo wskazanie, że mają być tabelami SQL.
- Do implementacyjnego doprecyzowania pozostają techniczny kontrakt akceptacji
  ostrzeżeń, zapis i retencja wyniku idempotentnej operacji oraz fizyczne kolumny,
  statusy i ponowienia adapterów ExternalVerifications. Nie tworzymy z tego
  nowych tabel ani funkcji bez potrzeby.

Nie pozostawiamy jako otwartych: dziedziczenia, nazw zatwierdzonych tabel,
limitu 100 znaków, dopuszczalności duplikatów, wymogu uzasadnienia ani reguły
domyślności rachunków, w tym grupy NULL. Kolumny i typy adresów oraz rachunków,
preferencja adresu według rodzaju i brak unikalności numerów rachunków są
rozstrzygnięte. Nie rozszerzamy modelu o historię migracji demonstratora.

## 5. Kryteria przyszłej implementacji

Przed rozpoczęciem implementacji trzeba rozstrzygnąć dotyczące jej pytania
otwarte i uzyskać akceptację szczegółowego modelu. Przyszłe testy mają wykazać:

- autoryzowany wybór bazy także wtedy, gdy lokalne int powtarzają się między firmami;
- jedno utworzenie i jeden audyt przy ponowieniu tej samej operacji z int IDENTITY;
- grupowanie przez ParentId bez dziedziczenia i automatycznego przypisania roli podatkowej;
- wiele własnych adresów, także tego samego rodzaju, z najwyżej jednym preferowanym na kontrahenta i rodzaj, otwartym słownikiem rodzajów i bez dziedziczenia;
- uniwersalne kontakty bez FK do adresów kontrahenta;
- najwyżej jeden domyślny rachunek w każdej walucie, także NULL, z atomową zmianą i ochroną przy równoczesnych zapisach;
- dopuszczalność powtarzających się numerów rachunków i audytowane, świadome zatwierdzenie ostrzeżenia walidacji przez uprawnionego użytkownika;
- limit 100 znaków TaxId/VatEuId oraz audytowane potwierdzanie duplikatów i błędów przez uprawnionego użytkownika bez uzasadnienia;
- wersjonowaną normalizację i walidację, świadomy zapis mimo ostrzeżenia oraz atomowy audyt odstępstwa;
- brak możliwości obejścia praw, integralności lub rowversion przez zatwierdzenie ostrzeżenia;
- odróżnienie negatywnego wyniku rejestru od niedostępności oraz zachowanie danych faktycznie wysłanego zapytania;
- dopisanie niezmiennego sprawdzenia bez zmiany kartoteki oraz trwałość wykonawcy po usunięciu konta.

To wymagania do późniejszej weryfikacji, nie wyniki wykonanych testów.
Nie ma tu planu migracji danych demonstracyjnych, DDL ani poleceń usuwania baz.

## 6. Oficjalne źródła opisu KsefIdWew

Sprawdzono 2026-10-22, wyłącznie dokumentację publiczną, bez operacji KSeF:

- [MF: broszura FA(3), marzec 2026, tabela 15, s. 30](https://ksef.podatki.gov.pl/media/jknpcymf/broszura-informacyjna-dotyczaca-struktury-logicznej-fa-3-04032026.pdf) — znaczenie IDWew i jego miejsce w danych Podmiot3.
- [MF: OpenAPI KSeF PR v2, wersja API 2.9.0](https://api.ksef.mf.gov.pl/docs/v2/openapi.json) — rozróżnienie Nip i InternalId oraz format `{nip}-{5_cyfr}` w schematach EntityPermissionsContextIdentifier i SubunitPermissionsContextIdentifier.
