# Otwarty następca VATowca — specyfikacja produktu i architektury

**Wersja dokumentu:** 0.12.1 (jedyny identyfikator waluty: kod ISO 4217, ADR-024)
**Data:** 2026-10-23
**Status:** projekt założeń; nie jest potwierdzeniem zgodności podatkowej gotowego oprogramowania  
**Nazwa projektu:** robocza „Otwarty następca VATowca”; docelowa nazwa i użycie nazwy „VATowiec” wymagają odrębnej decyzji i sprawdzenia praw  
**Główna platforma:** Windows; klient i serwer projektowane także z myślą o Linux  
**Główny stos:** C# / .NET 10, Avalonia UI, ASP.NET Core, Microsoft SQL Server

> **Cel:** stworzyć bezpłatny w podstawowym użyciu, publicznie rozwijany i realnie rozszerzalny system księgowo-podatkowy dla polskich mikroprzedsiębiorców, jednoosobowych działalności gospodarczych oraz — docelowo — biur rachunkowych. Priorytetem są KPiR, ryczałt, VAT, JPK, deklaracje i KSeF, a nie pełna księgowość.

---

## 1. Podstawa i granice specyfikacji

Dokument łączy trzy rodzaje informacji:

- **[USTALONE]** — decyzje podjęte w rozmowie: księgowość uproszczona (KPiR i ryczałt), rozliczenia VAT i PIT, JPK i deklaracje, wielostanowiskowość, możliwość tworzenia dodatków bez modyfikacji programu, preferowany SQL Server, dopuszczalne bezpłatne komponenty zamknięte.
- **[REFERENCJA]** — inspiracje funkcjonalne z dokumentu *VATowiec. Instrukcja obsługi, Windows/DBF 3.84* Piotra Kowaluka (71 stron). Jest to historyczna instrukcja działania **cudzego** programu, nie dokumentacja nowego projektu ani aktualne źródło prawa podatkowego.
- **[PROPOZYCJA]** — zaprojektowane tu rozwiązania architektury, modelu danych, zakresu etapów i interfejsów. Można je zmieniać w procesie ADR (Architecture Decision Records).

Części dotyczące podatków i formatów elektronicznych trzeba dodatkowo weryfikować według **daty obowiązywania**, oficjalnych XSD/API i aktualnych przepisów. Historycznych mechanizmów VATowca (np. VAT-7 w formularzach IPS, odliczania składki zdrowotnej według dawnych zasad) **nie implementujemy jako dzisiejszych reguł**.

### 1.1. Inspiracje z dokumentacji oryginalnego VATowca

| Funkcja / zasada | Miejsce w instrukcji | Wniosek dla następcy |
|---|---|---|
| Wiele firm; wspólny silnik dokumentów, VAT, KPiR i FK | s. 3, 6, 12–14 | Wspólny dokument źródłowy oraz niezależne projekcje podatkowe |
| Kategorie księgi dokumentów i rejestrów VAT | s. 49–50 | Konfigurowalne, audytowalne reguły kwalifikacji |
| Księga dokumentów jako rdzeń ewidencji | s. 6, 12–13 | Spójne powiązania dokumentów i wpisów podatkowych |
| Rozliczenia, kompensaty, bank i kasa | s. 19–20 | Osobny, powiązany moduł rozrachunków |
| Autodekrety i wielopoziomowy plan kont | s. 39, 42 | Ogólna idea automatyzacji; FK poza początkowym zakresem |
| Raporty definiowane i formuły | s. 36–38 | Raporty i pola obliczeniowe dostępne bez zmiany kodu |
| Edytowalne kolumny, skróty klawiaturowe | s. 3, 9–10 | Interfejs przyjazny masowemu wprowadzaniu danych |
| Formularze IPS / eksport danych według definicji | s. 67–68 | Konfigurowalne mapowanie XML → formularz / PDF / obcy system |
| Historia zmian i uprawnienia | s. 10, 62 | Pełny audyt i role, bez klienckiego omijania autoryzacji |
| Opis tabel, powiązań i archiwizacji | s. 69–70 | Udokumentowany schemat i odtwarzalne kopie zapasowe |

**Szczególne wymaganie użytkownika:** w pierwotnym VATowcu było możliwe samodzielne przygotowanie XML i otwarcie na jego podstawie wypełnionego formularza ministerialnego PDF (rozwiązanie współtworzone przez użytkownika w 2016 r.). W nowym programie odpowiednik tej elastyczności musi być funkcją **pierwszoplanową**, a nie przypadkowym obejściem.

### 1.2. Czego nie kopiujemy

Nie kopiujemy oryginalnego kodu, binariów, zasobów graficznych, chronionych treści dokumentacji ani nie przenosimy automatycznie historycznej bazy DBF. Nowe moduły tworzymy samodzielnie, na podstawie zebranych wymagań użytkowych i publicznych standardów. Analiza zachowania legalnie posiadanego programu może służyć do opisania wymagań i porównania rezultatów; ewentualne szczegółowe działania dekompilacyjne wymagają odrębnej oceny prawnej. Nie zakładamy prawa do nazwy i znaków oryginalnego programu.

---

## 2. Cele i mierniki powodzenia

### 2.1. Cele produktu

1. **Prowadzenie jednej firmy bez abonamentu** i bez przymusowego przekazywania danych dostawcy aplikacji.
2. **Wielostanowiskowa praca od początku**: dwie lub więcej osób może równolegle wystawiać dokumenty i prowadzić rozliczenia, również dla firmy jednoosobowej.
3. **KPiR i ryczałt** z rzeczywistymi zapisami, korektami, rozliczeniem okresów, naliczaniem podatków i kontrolą zgodności.
4. **VAT**: ewidencje sprzedaży i zakupów, klasyfikacja transakcji, odpowiednie deklaracje/JPK, korekty, wysyłka i UPO.
5. **KSeF**: obsługa faktur ustrukturyzowanych i ich powiązania z dokumentem gospodarczym.
6. **Otwartość funkcjonalna**: raporty, reguły mapowania, dodatkowe formularze, importery, eksportery, skrypty i wtyczki przez stabilne API.
7. **Trwałość danych**: audyt, backup/restore, możliwość eksportu kompletu danych i niezależność od dostawcy usługi chmurowej.
8. **Dobra ergonomia** dla użytkowników księgujących duże serie dokumentów: szybka klawiatura, tabele, filtry i skróty.
9. **„Użytkownik ma zawsze rację”**: uprawniony użytkownik może świadomie zapisać identyfikator mimo wyjaśnionego ostrzeżenia walidacji podatkowej, z audytem odstępstwa. Nie oznacza to obejścia autoryzacji, integralności danych, konfliktu wersji ani wymogów systemów zewnętrznych. Docelowe zasady: ADR-018 i §8.3.

### 2.2. Kryterium gotowości do użytku produkcyjnego

Program **nie** jest gotowy produkcyjnie tylko dlatego, że ma działający interfejs, bazę lub generuje poprawny składniowo XML. Oznaczenie wersji jako produkcyjnej wymaga **kompletnego scenariusza dla zadeklarowanego typu firmy i okresu**: wprowadzenie danych → ewidencje → obliczenia podatków → korekty → wymagane pliki i formularze → podpis/wysyłka → status/UPO → kopia i odtworzenie danych. Muszą istnieć testy automatyczne i merytoryczne przypadków granicznych oraz weryfikacja z ekspertem podatkowym.

### 2.3. Wyłączenia z początkowego zakresu

- Pełna księgowość (dziennik, rozbudowany plan kont, bilans, rachunek zysków i strat, konsolidacja).
- Kadry, płace, Płatnik i kompletny moduł ZUS (ewidencja opłaconych składek do obliczeń podatkowych **tak**).
- Magazyny, produkcja, zamówienia i POS/fiskalizacja.
- Integracja z każdym bankiem oraz automatyczne uzgadnianie wszystkich płatności.
- Własna infrastruktura SaaS i hosting danych klientów.
- Gwarancja importu danych ze wszystkich wersji DBF VATowca.

Wyłączenie oznacza brak w pierwszym wydaniu, nie zakaz dodania później.

---

## 3. Odbiorcy, organizacje i uprawnienia

### 3.1. Role użytkowników

- **Właściciel firmy**: dostęp do rozliczeń, zatwierdzeń, uprawnień i danych własnej firmy.
- **Księgowa/księgowy**: ewidencje, kontrola, rozliczenia, przygotowanie JPK i deklaracji w zakresie nadanych ról.
- **Sprzedawca**: wystawianie dokumentów sprzedaży, dostęp ograniczony do części handlowej.
- **Osoba współpracująca**: określone moduły, np. kosztowe, zakupowe, płatności.
- **Administrator techniczny**: instalacja, backup, użytkownicy; brak domyślnego dostępu do wszystkich tajemnic podatkowych bez wyraźnego nadania.
- **Administrator biura rachunkowego** (etap kolejny): przydziały operatorów i firm.

### 3.2. Model wielofirmowości

- Instalacja serwera może obsługiwać **wiele firm** i wielu użytkowników.
- Każda firma ma własne ustawienia podatkowe, kalendarz rozliczeń, numeracje, uprawnienia oraz ewidencję.
- **[USTALONE] Database-per-company:** każda firma ma fizycznie oddzielną bazę SQL Server. Jedna baza nie przechowuje księgowości wielu firm.
- Centralna baza `Vatowiec_System` zawiera użytkowników, role, katalog firm, uprawnienia i wskazania baz. Nie zawiera dokumentów księgowych, kontrahentów ani ewidencji podatkowych.
- Jedna aplikacja ASP.NET Core obsługuje wszystkie firmy. Dopiero po uwierzytelnieniu i sprawdzeniu uprawnienia do firmy wybiera serwerowe połączenie. Bazy firm mogą znajdować się na różnych instancjach SQL Server.
- Wcześniejszy demonstrator ADR-014/017 powielał `CompanyId` w rekordach firmowych. Wdrożenie ADR-018/019 usuwa zbędne `CompanyId` z lokalnej kartoteki i jej historii. Lokalny profil nadal identyfikuje jedyną firmę w bazie; `CompanyId` pozostaje w centralnym katalogu i mechanizmach autoryzacji. Usunięcie zbędnej kolumny nie zastępuje serwerowej kontroli firmy ani weryfikacji tożsamości bazy.
- Każda baza ma niezależną historię migracji, backup i odtworzenie/przeniesienie jednej firmy wraz z załącznikami i wymaganymi kluczami, bez przenoszenia innych firm.
- Użytkownik może posiadać różne role w różnych firmach.

### 3.3. Wielostanowiskowość — wymóg bezwarunkowy

- Minimum dwa komputery w sieci lokalnej i jednoczesna praca dwóch operatorów.
- Operatorzy tej samej firmy pracują równolegle na tej samej bazie firmowej i widzą wspólny stan dokumentów po odświeżeniu/zdarzeniu. Inne firmy mają inne fizyczne bazy.
- Równoczesna edycja tego samego dokumentu nie może niezauważenie nadpisywać zmian: mechanizm `rowversion`/ETag i komunikat konfliktu.
- Numeracja dokumentów musi być odporna na kolizje oraz wycofania transakcji.
- Komunikacja klient–serwer przez HTTPS i uwierzytelnione API; **żadnych poświadczeń SQL w aplikacji użytkownika**.
- Dostęp spoza LAN przez prywatną sieć/VPN albo prawidłowo zabezpieczone publiczne API; nie wystawiamy portu SQL Server do Internetu.
- Przerwy w łączności nie mogą powodować „połowicznego zaksięgowania”. Tryb offline z kolejką synchronizacji nie należy do zakresu 1.0 — tylko wyraźna obsługa zerwanego połączenia.

### 3.4. Tworzenie firmy i cykl życia bazy

- Docelowa komenda „Utwórz firmę” automatycznie rezerwuje firmę i bazę, zakłada bazę, wykonuje migracje i inicjalizuje lokalny profil. Użytkownik aplikacji nie musi korzystać z SSMS.
- Operacja ma trwały identyfikator, klucz idempotencji, niezmienny plan docelowy, checkpointy, błąd i liczbę prób. Ponowienie tego samego żądania z inną treścią jest odrzucane; zgodne żądanie wznawia tę samą operację i bazę.
- Etapy: `Requested → DatabaseCreated → SchemaApplied → ProfileInitialized → Ready`. Błąd zachowuje ostatni ukończony etap. Tylko `Ready` dopuszcza zwykłe operacje firmowe. Blokada/lease i token konkurencyjności wykluczają dwóch wykonawców tej samej operacji.
- Po awarii weryfikuje się stan fizycznej bazy, jej właściciela/znacznik operacji, migracje i profil, zamiast ufać samemu checkpointowi. Nie wolno przejmować lub usuwać zastanej bazy o niepotwierdzonym pochodzeniu; niejednoznaczność wymaga kontrolowanego uzgodnienia stanu.
- Provisioning używa oddzielnej tożsamości z prawami tworzenia/migracji. Zwykłe konto zapisu księgowego nie ma DDL. Migracje istniejących baz nie uruchamiają się przy starcie API.
- Proces nie zakłada transakcji SQL obejmującej bazę systemową i firmową. Trwałe etapy umożliwiają naprawę przerwania między zapisami. Szczegóły i bieżący zakres: ADR-014.

### 3.5. Uwierzytelnianie i autoryzowany kontekst firmy (M0, ADR-015)

- ASP.NET Core Identity w bazie systemowej przechowuje użytkowników, hashe haseł, role i przypisania do firm. CompanyAccess określa uprawnienia ViewProfile, ViewContractors, EditContractors; rola globalna sama nie nadaje dostępu do firmy.
- API udostępnia HTTPS login/logout, token CSRF, dane własnej sesji i listę dostępnych firm. Sesja jest chroniona cookie Secure/HttpOnly/SameSite=Strict i centralnym rekordem; absolutne wygaśnięcie domyślnie 30 minut, bez sliding expiration. Logout unieważnia konkretną sesję także dla replay cookie. SecurityStamp i uprawnienia sprawdza się na serwerze przy kolejnych żądaniach.
- Firma pochodzi wyłącznie ze ścieżki operacji API. Alternatywne query companyId i X-Company-Id oraz dodatkowe pola JSON są odrzucane. Po sprawdzeniu uprawnień resolver weryfikuje konfigurację serwera, CompanyProfile.CompanyId i fizyczną historię migracji. Brak uprawnienia daje 403; brak ważnej sesji 401; niedostępna/obca baza 503. Nie ma bazy domyślnej.
- Każda operacja otrzymuje osobny CompanyDbContext. Równoczesne sesje dwóch użytkowników firmy A nie zmieniają sobie wybranej firmy. Login/logout wymaga CSRF, hasła mają politykę Identity, blokadę po błędnych próbach i limit żądań logowania.
- Konta demonstracyjne i ich hasła konfiguruje się lokalnie poza repozytorium. Migracje i seed kont wykonuje osobne narzędzie, nigdy start API. Szczegóły kontraktu, sesji, testów i ograniczeń: ADR-015 oraz docs/authentication.md.

### 3.6. Pierwszy scenariusz Avalonia (M0, ADR-016)

- Polski klient loguje przez API HTTPS, pobiera dostępne firmy, otwiera profil wybranej firmy i odczytuje kontrahentów. Zmiana firmy i wylogowanie nie wymagają restartu. Okno pokazuje aktualną firmę, użytkownika i termin sesji.
- Adres API jest konfigurowalny przez VATOWIEC_API_URL i ekran logowania. Klient zachowuje standardową walidację certyfikatów; nie akceptuje HTTP ani przekierowań. Hasło nie jest zapisywane, cookie i CSRF są tylko w pamięci. Login/logout zachowują wymagania ADR-015.
- ViewContractors jest sprawdzane przez serwer niezależnie od widoczności przycisku. Brak uprawnień, sesja 401, błąd resolvera i utrata połączenia nie powodują wyboru domyślnej firmy. Zmiana firmy i odświeżanie kartoteki usuwają stare rekordy; wygaśnięcie sesji czyści całą sesję także podczas bezczynności. Operacje UI są serializowane, opóźnione dane po terminie sesji są odrzucane.
- Etap ADR-016 obejmował odczyt; edycję dodaje §3.7. Instrukcja kont i ograniczeń wylogowania offline: docs/desktop.md. Automatyczny provisioning pozostaje kolejnym zadaniem.

### 3.7. Wdrożona podstawowa kartoteka kontrahentów (ADR-017, opis demonstratora)

- Avalonia umożliwia dodanie i edycję kodu, nazwy, opcjonalnego identyfikatora podatkowego, dwuliterowego kraju i aktywności. Dezaktywacja zachowuje rekord i historię; nie ma fizycznego usuwania z API. Skróty: Ctrl+N, F2, Ctrl+F, Ctrl+S, Esc. Formularz i szkice nie są utrwalane lokalnie.
- API wymaga ViewContractors do odczytu oraz ViewContractors + EditContractors i CSRF do POST/PUT. Dotychczasowe HTTPS, cookie i resolver obowiązują także przy zapisie. Filtry q/country/active oraz offset/limit działają w SQL; API zwraca domyślnie 50, maksymalnie 100 pozycji. Klient pokazuje strony po 50 i sprawdza istnienie następnej.
- PUT wymaga rowversion: błędny/brak daje 400, stary daje 409 i aktualne dane. Klient zachowuje własne pola, pokazuje porównanie i wymaga jawnej decyzji przed kolejnym zapisem. Nie ma force-update. Id nowej pozycji jest stałe podczas ponawiania formularza; identyczne powtórzenie POST nie tworzy drugiej pozycji ani audytu. Kolizja kodu daje 409.
- Każdy rzeczywisty zapis przez API dopisuje audit.ContractorChanges w tej samej transakcji CompanyDbContext: autor, trwała przestrzeń identyfikatorów instalacji, historyczna nazwa użytkownika, czas UTC i korelacja, typ operacji oraz wartości pięciu pól przed/po. Audyt nie ma FK do centralnego konta; usunięcie konta nie odbiera identyfikacji autora. Brak zmiany stanu nie dopisuje sztucznej historii.
- security.Installation jest pojedynczym identyfikatorem źródłowej instalacji generowanym przez migrację systemową, zachowywanym przy aktualizacjach. Centralna baza nadal nie zawiera kontrahentów ani ich audytu. Zastosowano osobne migracje systemową InstallationIdentity i firmową ContractorAudit; start API nie migruje. Historia sprzed wdrożenia nie jest odtwarzana wstecz.
- Walidacja odpowiada długościom istniejącego modelu; nie dodano weryfikacji podatkowej NIP/rejestrów (ORG-007), adresów i rachunków ani dokumentów księgowych. Instrukcja testu, migracji i granic: docs/contractors.md, ADR-017.

Weryfikacja etapu 2026-10-22: 117/117 testów (46 SQL/API, 71 bez SQL), bez pominięć; kompilacja ośmiu projektów bez błędów i ostrzeżeń oraz poprawne formatowanie. W dwóch rzeczywistych oknach Avalonia sprawdzono dodanie i edycję, wyszukiwanie, konflikt z zachowaniem pól, świadome rozstrzygnięcie i dezaktywację. SQL potwierdził pięć atomowo zapisanych zdarzeń dla fikcyjnej próby GUI-60D3A33B; próba odrzucona przez konflikt nie dopisała audytu. Migracje trzech baz wykonano jawnie po zweryfikowanych kopiach. M0 nadal wymaga cyklu życia firm i audytu operacji księgowych.

### 3.8. Zatwierdzony model kartoteki i zakres implementacji ADR-018

Zakończono etap dokumentacyjny. Zatwierdzone decyzje biznesowe określa [ADR-018](docs/adr/ADR-018-docelowy-model-kontrahentow.md), a implementację i rozstrzygnięcia techniczne [ADR-019](docs/adr/ADR-019-implementacja-modelu-kontrahentow.md). Wdrożono Contractors z int IDENTITY i ParentId, adresy, kontakty, rachunki, lokalny AddressTypes, trwałe operacje tworzenia i audyt SnapshotVersion=2. API/desktop korzystają z nowego modelu; historia §3.7 opisuje wcześniejszy etap.

W bazach projektu są wyłącznie fikcyjne dane, które nie wymagają zachowania. Przy przyszłej implementacji po zatwierdzeniu szczegółów można usunąć i odtworzyć wyłącznie demonstracyjne bazy VATowca, po sprawdzeniu ich nazwy i przeznaczenia. Nie projektujemy migracji istniejących danych, przenoszenia GUID, zachowywania przykładowych kontrahentów ani kompatybilności z dotychczasowym demonstratorem. Dotyczy to wyłącznie zweryfikowanych lokalnych Vatowiec_System oraz Vatowiec_Company_A_Dev/B_Dev; nie wolno zmieniać baz systemowych SQL ani innych aplikacji.

Zatwierdzony model: database-per-company bez zbędnego CompanyId w tabelach jednej firmy; preferowane klucze danych kontrahentów int IDENTITY; Contractors dla podmiotów, grup i jednostek, z ParentId służącym grupowaniu i bez dziedziczenia. Osobne ContractorAddresses, ContractorContacts i ContractorBankAccounts; otwarty AddressTypes. TaxId i pełne VatEuId z prefiksem są opcjonalnymi tekstami do 100 znaków, z dozwolonymi duplikatami po ostrzeżeniu i świadomej akceptacji przez uprawnionego użytkownika. KsefIdWew pozostaje odrębnym opcjonalnym identyfikatorem jednostki. Pozostają niezależna od Id idempotencja tworzenia, prawidłowe relacje, kontrola uprawnień, rowversion i audyt. Zmiana lokalnego klucza nie usuwa CompanyId z katalogu, kontekstu API ani autoryzacji. Szczegóły opisuje §8.3.

---

### 3.9. Kompletna kartoteka w Avalonia (ADR-020)

Jeden formularz ma zakładki Dane główne, Adresy, Kontakty i Rachunki bankowe. TaxId,
VatEuId i KsefIdWew są trzema odrębnymi polami tylko do odczytu, bez deklaracji
poprawności numerów. ParentId wybiera się z wyników SQL po kodzie/nazwie; można
usunąć powiązanie i przeglądać podmioty grupy. Nie ma dziedziczenia danych.

Adresy, kontakty i rachunki mają odczyt, tworzenie i edycję przez HTTPS API,
CSRF i prawa ViewContractors/EditContractors, bez DELETE. AddressTypes można
rozszerzać o własne rodzaje. Nowa preferencja adresu odznacza poprzednią dla
tego samego rodzaju; rachunek domyślny działa analogicznie dla waluty, także NULL.
Rowversion i jawne porównanie chronią konflikty. Zmiana, skutki domyślności,
audyty obu rekordów i wynik tworzenia są atomowe. RecordType/RecordId jednoznacznie
identyfikują pozycję w ContractorChanges; OperationKind rozszerza istniejącą tabelę
operacji na wszystkie kartoteki i dodawanie rodzaju adresu, bez nowej tabeli.

Podstawowe przygotowanie rachunku w Application/API usuwa białe znaki i zmienia
litery na wielkie. bank-text-v1 ostrzega o nietypowym zapisie; nie sprawdza sumy
ani kraju/rejestru. Świadome potwierdzenie wymaga osobnego AcceptWarnings=8,
zgodnego hasha pokazanej wersji i audytu WarningJson, bez uzasadnienia. To nie
potwierdza poprawności rachunku i nie obchodzi integralności ani uprawnień.

Formularze mają błędy przy polach, przewijanie i stały obszar zapisu/anulowania.
Szkic jest chroniony potwierdzeniem porzucenia. Skróty odnoszą się do aktywnej
zakładki; firmę B można otworzyć w trybie odczytu bez możliwości zapisu.
Szczegóły i wdrożenie: [ADR-020](docs/adr/ADR-020-kompletna-kartoteka-avalonia.md),
[instrukcja](docs/contractors.md). Migracje techniczne wykonano jawnie po
zweryfikowanych kopiach; nie tworzono ponownie baz ani tabel biznesowych.

Weryfikacja ADR-020 (2026-10-22): **142/142 testy** bez pominięć, w tym 67 SQL/API
i 75 bez SQL; kompilacja bez błędów/ostrzeżeń i poprawne formatowanie. W prawdziwym
oknie Avalonia sprawdzono grupę i kontrahenta, dwa adresy, dwa kontakty, pięć
rachunków, edycję/ponowny odczyt, domyślności PLN/EUR/NULL, akceptację ostrzeżenia
oraz izolację i odczyt w B. Osobno sprawdzono trzy identyfikatory tylko do odczytu,
błędy przy polach i ochronę szkicu. Szczegóły oraz zakres próby ergonomii opisuje
docs/contractors.md. M0 nadal nie jest ukończone.

### 3.10. Identyfikatory i rejestry zewnętrzne TEST (ADR-021)

Wdrożono edycję trzech opcjonalnych identyfikatorów, podgląd normalizacji,
wersjonowane reguły i duplikaty w firmie. AcceptWarnings/WarningHash z ADR-020
wiążą potwierdzenie z danymi, rowversion, wersją reguły i duplikatami. Zapis
i audyt ostrzeżenia są atomowe. NIP ma format/sumę mod 11; dla VAT UE 27 państw
i XI wdrożono formaty, ale poza PL brak sum kontrolnych jest jawnie Unknown.
IDWew ma kontrolę formatu, bez nadawania i potwierdzania istnienia w KSeF.

ref.TaxIdentifierSchemes jest wspólnym słownikiem w Vatowiec_System, bez kartotek
i wyników firm. audit.ExternalVerifications znajduje się w każdej bazie firmy;
przechowuje niezmienny snapshot wejścia, środowisko Test/Production/Mock, status
usługi i wynik, wersje, referencję oraz trwałą tożsamość wykonawcy bez centralnego FK.
Osobne VerifyRegistries=16 i CSRF chronią ręczne MF/WL2, GUS BIR1.2 i VIES.
Nie ma automatycznego pobierania przy edycji ani nadpisywania danych GUS.

Demonstrator używa wyłącznie TEST/MOCK. Serwer ogranicza adresy do oficjalnych
endpointów odpowiedniego środowiska; nie ma przekierowań ani fallbacku do produkcji.
Produkcja wymaga późniejszej świadomej konfiguracji i AllowProduction. TEST/MOCK
nie stanowią potwierdzeń produkcyjnych; historia nie jest konwertowana między
środowiskami. Timeout, awaria i brak obsługi nie oznaczają wyniku negatywnego.
Szczegóły, ograniczenia algorytmów i konfiguracja: [instrukcja](docs/identifiers-and-registries.md),
[ADR-021](docs/adr/ADR-021-identyfikatory-i-rejestry-testowe.md).

### 3.11. DataPort i konfiguracja integracji firmy (ADR-022)

Osobny adapter komercyjnego pośrednika DataPort, obok oficjalnego GUS BIR.
Ustawienia firmy mają zapis/zastąpienie/usunięcie klucza z potwierdzeniem i odczyt
wyłącznie stanu. ManageCompanyIntegrations=32 jest prawem konkretnej firmy,
nie globalną rolą; zapisy wymagają CSRF i rowversion. Operator A ma maskę 63,
operator B 3, czytelnik A 1. Sekret pozostaje zaszyfrowany w lokalnej
security.DataPortConfiguration (SingletonKey, ProtectedApiKey, RowVersion),
z trwałą ochroną DPAPI/key ring poza repozytorium i bazą. Po przeniesieniu
bez dostępnej ochrony konieczne jest ponowne wpisanie klucza.

Tylko ręczny GET po poprawnym znormalizowanym NIP, stały HTTPS dataport.pl,
X-API-Key na konkretnym żądaniu; brak ponowień, batch i tła. Wynik to
„DataPort (dane GUS)”, rzeczywisty pośrednik/cache, nie bezpośrednia odpowiedź
GUS TEST ani urzędu. Oficjalne MF/GUS/VIES nadal domyślnie Test. W istniejącym
audit.ExternalVerifications zapisujemy Source=DataPort, Production dla rzeczywistej
usługi lub Mock dla atrap, snapshot wejścia, wynik i trwałego aktora, bez sekretu.
Brak nowych kolumn kartoteki/REGON ani oddzielnej historii. Selektywny import
wyłącznie do szkicu; zwykły zapis zachowuje ostrzeżenia, prawa, rowversion i audyt.
Limit dzienny należy do konta, także kilku kluczy; reset 00:00 UTC, brak fikcyjnego
salda. Niedostępność nie jest wynikiem negatywnym. [Konfiguracja i próby](docs/dataport.md).

### 3.12. Waluty i ręczne kursy NBP (ADR-023)

Podstawa DOC-006: lokalny słownik 149 pozycji, ustawienia aktywności/pobierania
A/B/C i stronicowana kartoteka kursów w każdej bazie firmy. Domyślnie EUR/USD/GBP/CHF
z A; PLN jako waluta podstawowa bez kursu NBP. Trzy nowe tabele ref.Currencies,
core.ExchangeRates i audit.CurrencyOperations, bez powielonego CompanyId i GUID PK.
ADR-024 zastępuje numeryczny klucz waluty: ref.Currencies ma wyłącznie
niezmienne Code char(3) jako PRIMARY KEY, wielkie litery, bez Id. Kursy wskazują
CurrencyCode char(3) przez lokalny FK; ich własne bigint IDENTITY pozostaje.
Ustawienia, API i klient używają kodu. Nie mapujemy starych CurrencyId:
demonstracyjne trzy tabele walutowe są jawnie odtwarzane z seedem, bez zmian
pozostałych danych firm i konfiguracji integracji. Historyczne ADR/migracje
pozostają zapisem poprzedniego modelu.
Wartości decimal(24,12), Mid dla A/B, Bid/Ask dla C, daty publikacji i opcjonalnego
notowania, oryginalny numer tabeli i UTC pobrania. Odczyt/zarządzanie/import mają
osobne prawa firmowe 64/128/256 oraz CSRF dla mutacji; B pozostaje tylko do odczytu.

Publiczny NBP HTTPS/JSON jest pobierany wyłącznie ręcznie: najnowsze publikacje
lub zakres dzielony po 93 dni włącznie. Nie ma automatyzacji, uzupełniania dni bez
publikacji ani fikcyjnych kursów brakującej waluty. Ponowienia deduplikują dane,
a rozbieżności wymagają jawnej decyzji z wersją kursu i atomowym audytem przed/po.
Historia importu zachowuje propozycje bez ponownego HTTP. Application.IExchangeRateReader
udostępnia dokładną publikację lub ostatnią ściśle przed datą, z numerem/źródłem.
Bez faktur, reguł podatkowych i różnic kursowych. [Obsługa i struktura](docs/currencies-and-nbp.md).

## 4. Decyzje technologiczne

| Element | Decyzja / status | Uzasadnienie |
|---|---|---|
| Język i runtime | **C# / .NET 10** [PROPOZYCJA] | Wydajne tworzenie, testowanie i utrzymanie aplikacji biznesowej |
| Klient desktopowy | **Avalonia UI** [PROPOZYCJA] | Windows i Linux, dobre tabele i formularze |
| Serwer | **ASP.NET Core Web API** [PROPOZYCJA] | Centralna logika, autoryzacja, dostęp z wielu stanowisk, integracje |
| Bazy danych | **Microsoft SQL Server 2025 Express** [USTALONE co do SQL Server i database-per-company, wersja PROPOZYCJA] | Baza systemowa i niezależne, transakcyjne bazy firmowe; możliwe różne instancje |
| Rozszerzanie bazy | **SQL Server Standard** bez zmian aplikacji [PROPOZYCJA] | Skalowanie po wyczerpaniu limitów Express |
| Dostęp do danych | **EF Core** dla CRUD/migracji + jawne SQL, gdy potrzebne [PROPOZYCJA] | Zachowujemy pełną kontrolę nad zapytaniami i wydajnością |
| Testy | **xUnit**, testy integracyjne na prawdziwym SQL Server [PROPOZYCJA] | Powtarzalna kontrola obliczeń i transakcji |
| API | OpenAPI, wersjonowane endpointy + zdarzenia integracyjne | Możliwość rozbudowy programu bez modyfikacji rdzenia |
| Licencja źródeł | **MPL-2.0 do zatwierdzenia** [PROPOZYCJA] | Open source z możliwością łączenia z odrębnymi komponentami własnościowymi |

**SQL Server Express:** Microsoft dokumentuje limit 50 GB dla SQL Server 2025 Express w głównej tabeli edycji, natomiast osobna dokumentacja *SQL Server 2025 on Linux* podawała w dniu sporządzania specyfikacji 10 GB. Nie uzależniamy projektu od jednego z tych limitów bez weryfikacji konkretnej platformy, wydania i aktualnej licencji. SQL Server Express nie ma usługi SQL Server Agent — automatyzacja kopii zapasowych musi działać przez własny serwis / harmonogram OS. Rozmiar plików PDF w bazie ograniczamy poprzez przechowywanie załączników poza tabelami transakcyjnymi.

**Alternatywne DBMS:** nie implementujemy PostgreSQL w wersji 1.0, ale logika podatkowa nie może zależeć od `DbContext`, T-SQL ani typu SQL Server. Ewentualny adapter PostgreSQL pojawi się dopiero po stabilizacji modelu; nie obiecujemy pełnej przenośności zapytań od pierwszego commita.

### 4.1. Topologia

```text
Komputer A: klient Avalonia ─┐
                             ├──HTTPS──> ASP.NET Core API + silniki domenowe
Komputer B: klient Avalonia ─┘                       │
                                                   ├── Vatowiec_System (katalog i dostęp)
                                                   ├── SQL instancja 1: baza firmy A
                                                   ├── SQL instancja 1 lub 2: baza firmy B
Program zewnętrzny ─── OAuth/API key* ────────────>├── Magazyn załączników (plikowy)
                                                   └── Kolejka zadań/Outbox
* metodę uwierzytelnienia integracji określamy osobno
```

Serwer aplikacji i SQL Server mogą działać na tym samym komputerze. Klient nigdy nie otwiera bazy SQL bezpośrednio.

`SystemDbContext` i `CompanyDbContext` mają rozłączne modele oraz osobne zestawy migracji. Wybór firmy jest niezmienny w zakresie pojedynczego żądania/jednostki pracy; żadnego globalnego „aktywnego CompanyId”. Connection stringi i sekrety pozostają w infrastrukturze serwera. Baza systemowa nie staje się agregatem kopii danych firmowych. Moduły księgowe korzystają ze wspólnej transakcji bazy jednej firmy przez jawne kontrakty, zachowując granice właścicieli tabel.

### 4.2. Architektura kodu

**Modularny monolit**, nie mikrousługi. Jeden wdrażalny serwer ze ścisłymi granicami domeny:

1. `Core` — firmy, osoby, użytkownicy, role, okresy, identyfikatory, audyt.
2. `Documents` — dokument źródłowy, pozycje, załączniki, wersje, zatwierdzenia.
3. `Sales` / `Purchases` — wystawianie i zakup faktur, korekty.
4. `Tax.Vat` — ewidencja i kalkulacje VAT.
5. `Tax.Income` — KPiR, ryczałt, zaliczki, odliczenia.
6. `Tax.Reporting` — JPK, deklaracje, formularze, podpisy, wysyłki i UPO.
7. `Integrations.Ksef` — adapter KSeF 2.0.
8. `Payments` — należności, zobowiązania, powiązania płatności.
9. `Extensions` — definicje, pakiety, uprawnienia dodatków, zdarzenia, API.
10. `Infrastructure` — SQL, pliki, kryptografia, logging, hosting.

Warstwy: `Domain` (bez ORM/UI) → `Application` (przypadki użycia, transakcje) → `Infrastructure` (persystencja/transport) → `API/UI`.

---

## 5. Zasady modelowania dokumentów i księgowania

### 5.1. Jedno źródło, wiele skutków

`Document` jest źródłem zdarzenia gospodarczego. Rejestr VAT, KPiR, ryczałt, rozrachunki oraz raporty są osobnymi, kontrolowanymi zapisami/projekcjami powiązanymi z dokumentem źródłowym. Zatwierdzenie dokumentu może wygenerować wiele skutków o **różnych datach** i zasadach podatkowych.

Nie zakładamy, że:
- data wystawienia = data sprzedaży = obowiązek VAT = rozpoznanie przychodu = data zapłaty;
- kwota brutto = podstawa PIT;
- cała faktura ma jedną stawkę VAT lub jedną stawkę ryczałtu;
- faktura zapłacona jest automatycznie zaksięgowana w tym samym okresie.

### 5.2. Cykl życia dokumentu

`Draft` → `Validated` → `Posted` → `Corrected` / `CancelledWhereAllowed`.

- **Roboczy:** edytowalny, niezobowiązujący podatkowo.
- **Zweryfikowany:** przechodzi walidacje strukturalne, biznesowe i okresowe.
- **Zatwierdzony (zaksięgowany):** skutki powstają w **jednej transakcji SQL**; dokument i ewidencje są spójne.
- **Korekta:** nowy dokument / nowa wersja wpisów z powiązaniem z oryginałem i przyczyną. Nie kasujemy w ciszy historii zaksięgowanej.
- **Okres zamknięty:** zmiana wymaga świadomej procedury otwarcia/korekty i audytu uprawnień, nie zwykłego `UPDATE`.

Dla KSeF odrębnie modelujemy **status techniczny faktury** (przygotowana, wysłana, przyjęta, odrzucona, numer KSeF, UPO) i **status księgowy**. Nie wolno mieszać tych dwóch procesów.

### 5.3. Wymogi matematyczne

- Tylko `decimal` w C# i odpowiednie `decimal(p,s)` w SQL — **nigdy** `float`/`double` do wartości podatkowych.
- Osobne reguły dla ceny jednostkowej, kwoty, kursu i zaokrąglenia podatkowego; precyzje oparte na przepisach i formatach, a nie globalnej stałej „2 miejsca”.
- Sposób liczenia od netto/brutto i agregacji pozycji VAT musi być jawny i testowalny.
- Zapisujemy walutę, kwotę w walucie, kurs, źródło/datę kursu, kwotę PLN i sposób przeliczenia, gdy wymagane.
- Obliczenia mają być **deterministyczne**: te same dane, ten sam zestaw reguł i wersja schematu dają identyczny wynik.
- W procesie księgowania stosujemy idempotencję (ponowione żądanie sieciowe nie tworzy duplikatu dokumentu ani deklaracji).

### 5.4. Kategorie i reguły

- Dla rodzaju transakcji i pozycji dokumentu można zdefiniować kategorie podatkowe VAT/PIT/KPiR/ryczałt.
- Reguła ma okres obowiązywania, priorytet, autora, historię zmian, zestaw warunków i jawny wynik.
- Reguły **proponują** klasyfikację; użytkownik ma prawo świadomie skorygować wynik, jeżeli mieści się to w dopuszczalnych zasadach i ma uprawnienia.
- Każdy zapis ewidencyjny przechowuje odwołanie do źródła i zastosowanej wersji reguły.
- Silnik VAT i PIT nie może wykorzystywać arbitralnego skryptu użytkownika jako nieweryfikowanego zastępnika przepisów.

---

## 6. Wymagania funkcjonalne — identyfikatory i priorytety

Legenda: **P0** — fundament wymagany przed udostępnieniem produkcyjnym; **P1** — zakres wydania 1.0; **P2** — etap po 1.0 lub rozszerzenie. Realizacja P0/P1 może zostać podzielona na wiele iteracji.

### 6.1. Firma, użytkownicy, wspólne kartoteki

| ID | Wymaganie | Priorytet |
|---|---|---|
| ORG-001 | Rejestr wielu firm: NIP, REGON, nazwa, adresy, rachunki, identyfikatory urzędowe | P0 |
| ORG-002 | Ustawienia formy opodatkowania, statusów i okresów z historią od–do | P0 |
| ORG-003 | Wiele osób, role i uprawnienia per firma / per czynność | P0 |
| ORG-004 | Jednoczesna praca i detekcja konfliktów edycji | P0 |
| ORG-005 | Kartoteka kontrahentów, w tym osoby fizyczne, zagraniczni, wielu adresów i kont | P0 |
| ORG-006 | Uprawnienia biura rachunkowego i grupowanie obsługiwanych firm | P2 |
| ORG-007 | Weryfikacja NIP / rejestrów publicznych z datą i zapisem wyniku | P1 |
| ORG-008 | Fizycznie oddzielna baza SQL Server dla każdej firmy, centralny katalog i autoryzowany wybór bazy; różne instancje | P0 |
| ORG-009 | Automatyczne utworzenie bazy i profilu firmy z aplikacji, migracje, idempotencja i wznowienie po awarii | P0 |
| ORG-010 | Niezależny backup, samodzielne odtworzenie i przeniesienie jednej firmy przez uprawnionego użytkownika aplikacji | P0 |
| ORG-011 | Trwała identyfikacja aktora w historii księgowej mimo usunięcia jego konta z bazy systemowej | P0 |

### 6.2. Dokumenty i fakturowanie

| ID | Wymaganie | Priorytet |
|---|---|---|
| DOC-001 | Faktury sprzedaży krajowej, faktury zakupowe, dowody wewnętrzne, dokumenty bez VAT | P0 |
| DOC-002 | Faktura wielopozycyjna, różne stawki VAT i klasyfikacje podatkowe | P0 |
| DOC-003 | Faktury korygujące z połączeniem do dokumentu pierwotnego | P0 |
| DOC-004 | Wiele dat: wystawienie, zdarzenie, sprzedaż/usługa, zapłata, obowiązek VAT i PIT | P0 |
| DOC-005 | Unikalne serie numeracyjne, odporność na równoczesne wystawienie | P0 |
| DOC-006 | Zapisy w walutach obcych, wersjonowane kursy i rozliczenie kwot PLN | P1 |
| DOC-007 | Powiązane załączniki PDF/XML/obrazy z ograniczeniem dostępu | P1 |
| DOC-008 | Wydruki faktur i zestawień bez zależności od chmury | P1 |
| DOC-009 | Import/eksport CSV i XML z mapowaniem i raportem błędów | P1 |
| DOC-010 | Dokumenty seryjne, szablony cykliczne | P2 |

### 6.3. KPiR i podatek dochodowy

| ID | Wymaganie | Priorytet |
|---|---|---|
| PIT-001 | KPiR zgodna z wymogami obowiązującymi w danym okresie; ewidencja w wymaganych kolumnach | P0 |
| PIT-002 | Mapowanie dokumentów do pozycji KPiR, ręczne dowody, księgowania korygujące | P0 |
| PIT-003 | Skala podatkowa i podatek liniowy (w odpowiednich przypadkach) | P1 |
| PIT-004 | Zaliczki miesięczne/kwartalne liczone metodą zgodną z właściwymi zasadami i uwzględniające wcześniejsze okresy | P1 |
| PIT-005 | Ewidencja składek ZUS i innych odliczeń — z datą zapłaty i rokiem podatkowym | P1 |
| PIT-006 | Remanent początkowy i końcowy, różnice remanentowe | P1 |
| PIT-007 | Obliczenia okresowe i roczne, księga, podsumowania, zestawienie różnic i korekt | P1 |
| PIT-008 | Obsługa stosownych rocznych formularzy PIT (np. PIT-36/PIT-36L) przez odrębne, wersjonowane adaptery | P1* |

`PIT-008`: dokładny zakres danych i formularzy zależy od typu podatnika, rozliczenia wspólnego, dochodów spoza działalności i załączników; wymagane osobne uzgodnienie minimalnego zestawu dla 1.0. Bez tego nie ogłaszamy pełnej obsługi deklaracji rocznych.

### 6.4. Ryczałt od przychodów ewidencjonowanych

| ID | Wymaganie | Priorytet |
|---|---|---|
| RYC-001 | Ewidencja przychodów wg właściwych stawek w obrębie jednego dokumentu | P0 |
| RYC-002 | Okresy miesięczne/kwartalne i prawidłowe rozliczanie narastająco tam, gdzie wymagane | P1 |
| RYC-003 | Zapłacone składki, ulgi, odliczenia, zwroty i korekty wraz z ograniczeniami okresowymi | P1 |
| RYC-004 | Wyliczanie podatku z kontrolą rozliczeń wcześniejszych okresów i datami obowiązywania zasad | P1 |
| RYC-005 | Zestawienie roczne i obsługa PIT-28 w ustalonym zakresie | P1 |
| RYC-006 | Walidacja przychodów o mieszanych stawkach i korektach z poprzednich okresów | P1 |

**Uwaga projektowa:** nie zakodujemy na sztywno stawek ani reguł składek zdrowotnych z 2026 r. Wszystkie zasady mają wersje okresowe i testy wzorcowe. Nie wolno bez analizy kopiować przykładów ze starej instrukcji.

### 6.5. VAT, JPK i deklaracje

| ID | Wymaganie | Priorytet |
|---|---|---|
| VAT-001 | Rejestry sprzedaży i zakupów VAT, z dokumentami wielostawkowymi | P0 |
| VAT-002 | Okres podatkowy, daty obowiązku, odliczenia, oznaczenia i procedury | P0 |
| VAT-003 | Obsługa transakcji krajowych i wybranych zagranicznych, po uprzednim zdefiniowaniu zakresu | P1 |
| VAT-004 | Weryfikacja i zestawienie naliczonego/należnego VAT, kwot do zapłaty/przeniesienia/zwrotu | P1 |
| VAT-005 | `JPK_V7M(3)` / `JPK_V7K(3)` według wersji i daty, walidacja XSD i reguł merytorycznych | P1 |
| VAT-006 | Korekty JPK/deklaracji, powiązanie ze złożoną wersją i przyczyną | P1 |
| VAT-007 | `JPK_PKPIR(3)`, `JPK_EWP(4)`, `JPK_ST(1)` tam, gdzie dotyczą podatnika i okresu | P1 |
| VAT-008 | Rejestr wysyłek, identyfikatory, statusy, UPO, pobieranie i archiwizacja | P1 |
| VAT-009 | VAT-UE i korekty VAT-UEK | P1* |
| VAT-010 | Inne formularze, np. VAT-8, VAT-9M i dokumenty na żądanie | P2 |
| VAT-011 | JPK_FA / inne struktury na żądanie, jeśli właściwe | P2 |

`VAT-009` i `VAT-003`: uruchomienie wymaga zaakceptowanej listy scenariuszy (WDT/WNT, import usług, eksport itd.). **JPK_V7M/V7K jest obecnie także nośnikiem części deklaracyjnej** — nie tworzymy bez potrzeby równoległego modułu „deklaracja VAT-7” według historycznej instrukcji.

### 6.6. KSeF

| ID | Wymaganie | Priorytet |
|---|---|---|
| KSEF-001 | Adapter do aktualnego API KSeF 2.0, osobne środowiska integracyjne / demo / produkcyjne | P1 |
| KSEF-002 | Mapowanie dokumentu do aktualnej struktury FA i walidacja przed wysyłką | P1 |
| KSEF-003 | Podpis/uwierzytelnienie i uprawnienia według obsługiwanych mechanizmów KSeF | P1 |
| KSEF-004 | Obsługa statusów wysyłki, błędów, powtórzeń, numeru KSeF, UPO i identyfikatorów | P1 |
| KSEF-005 | Pobieranie faktur zakupu, deduplikacja i wprowadzanie do ewidencji | P1 |
| KSEF-006 | Archiwum surowego XML, znaczniki integralności i powiązania dokumentów | P1 |
| KSEF-007 | Obsługa scenariuszy offline/awaryjnych, o ile wynikają z przepisów i aktualnego API | P1 |

**Wydanie 1.0:** bez udanych testów integracyjnych w środowisku testowym i bez kontroli autoryzacji integracji KSeF nie może deklarować obsługi produkcyjnej KSeF.

### 6.7. Rozrachunki, bank, środki trwałe, raporty

| ID | Wymaganie | Priorytet |
|---|---|---|
| PAY-001 | Należności/zobowiązania i terminy płatności | P1 |
| PAY-002 | Wprowadzanie płatności i łączenie z dokumentami, również częściowe | P1 |
| PAY-003 | Kompensaty i raporty zaległości | P2 |
| PAY-004 | Import wyciągów (CSV, docelowo ISO 20022) i półautomatyczne kojarzenie | P2 |
| AST-001 | Kartoteka środków trwałych, zdarzenia i plan amortyzacji | P1 |
| AST-002 | Generowanie zapisów KPiR i danych JPK_ST zgodnie z okresem | P1 |
| REP-001 | Podstawowe zestawienia księgi, VAT, przychodów, sald i podatków | P1 |
| REP-002 | Raporty definiowane: pola, filtry, sortowanie, grupowanie, sumy, obliczenia | P1 |
| REP-003 | Eksport CSV/XLSX/XML/PDF z opisanymi formatami | P1 |
| REP-004 | Własne kolumny w tabelach, widoki użytkownika, skróty klawiaturowe | P1 |

---

## 7. System rozszerzeń — obowiązkowy element architektury

### 7.1. Cel

Użytkownik ma móc wykonać czynność analogiczną do historycznego rozwiązania „XML z VATowca → wypełniony ministerialny PDF”, nie uzyskując dostępu do kodu rdzenia. Rozszerzenie powinno być przenośne, eksportowalne i możliwe do udostępnienia innym użytkownikom.

### 7.2. Trzy poziomy

1. **Definicje bez kodowania**: szablony wydruków, mapowania XML/JSON/CSV, zestawienia, niestandardowe kolumny i formuły, konfiguracja skrótów/przycisków.
2. **Skrypty lub wtyczki**: walidatory, operacje w menu, importery/eksportery, integratory, automatyzacje i obróbka danych przez publiczne kontrakty SDK.
3. **Wersjonowane API zewnętrzne**: integracja z aplikacjami firm trzecich przez HTTPS; konta techniczne z ograniczonymi zakresami uprawnień.

### 7.3. Rozszerzenia deklaratywne

Minimalny pakiet `*.vatx` (nazwa robocza) może zawierać:

```text
manifest.json
mappings/*.json
reports/*.json
forms/*.json
assets/*
README.md
```

Przykładowy **projekt** manifestu (nie obowiązująca implementacja):

```json
{
  "id": "pl.example.ministerial-form-xml",
  "version": "1.0.0",
  "apiVersion": "1",
  "name": "Przykładowy eksport formularza",
  "permissions": ["documents.read", "company.read"],
  "contributions": [
    { "type": "export", "name": "Formularz XML", "definition": "mappings/form.xml.json" }
  ]
}
```

Składnia i dostępne prawa podlegają formalnemu zaprojektowaniu, walidacji i wersjonowaniu.

### 7.4. Silnik formuł i mapowań

- Deklaratywny język wyrażeń / AST; **żadnego `eval`, nieograniczonego SQL ani uruchamiania dowolnego C# z definicji użytkownika**.
- Funkcje typowane (tekst, data, liczba, słowniki, operacje arytmetyczne, warunkowe) i jawne źródła danych udostępnione w API.
- Funkcje mają uprawnienia, ograniczenia czasu/zasobów i wersje.
- Definicję można testować na danych fikcyjnych z podglądem rezultatów i raportem błędów.
- Raport/eksport zapisuje numer swojej definicji i (jeśli jest wymagane) wersję formatu prawnego.
- Dla PDF przewidujemy **eksport XML zgodnego z zewnętrznym formularzem**, ewentualnie wypełnianie technicznie dostępnych pól PDF, ale nie obiecujemy pełnej obsługi dowolnych formularzy XFA ani działania w każdej przeglądarce PDF.

### 7.5. Zdarzenia i dodatki

Przykładowe zdarzenia publikowane **po skutecznym zakończeniu transakcji**:

- `DocumentPosted`
- `DocumentCorrected`
- `PaymentRegistered`
- `TaxPeriodClosed`
- `JpkGenerated`
- `SubmissionAccepted`
- `KsefInvoiceReceived`

Dodatki otrzymują identyfikator zdarzenia, czas, firmę i dane ograniczone zgodą operatora. Zdarzenia przez `Outbox` z ponawianiem, deduplikacją i jawnymi błędami. Rozszerzenia **nie mogą** edytować bezpośrednio zapisów historycznych ani omijać walidacji rdzenia.

### 7.6. Izolacja i bezpieczeństwo dodatków

- Wtyczki wykonywalne mają własny proces/worker lub inną rzeczywistą barierę izolacji — **nie** polegamy na `AssemblyLoadContext` jako zabezpieczeniu przed złośliwym kodem.
- Definiowane uprawnienia (`read:invoices`, `write:drafts`, `export`, `network:host`, itd.) i wyraźna zgoda administratora.
- Brak domyślnych praw do całej bazy, systemu plików, kluczy KSeF i sekretów innych dodatków.
- Wtyczka nie może usuwać ani podmieniać audytu.
- Niepodpisane dodatki mogą być dopuszczalne w trybie deweloperskim, ale z komunikatem ryzyka.
- Publiczne SDK, dokumentacja, przykłady i test zgodności API dla autorów dodatków.

### 7.7. Kryterium akceptacji otwartości

Bez rekompilacji aplikacji użytkownik tworzy nową definicję formularza: wybiera zatwierdzony dokument, mapuje pola, generuje XML w swoim formacie, zapisuje go i otwiera wskazany program zewnętrzny za świadomą zgodą. Inny użytkownik instaluje tę definicję w oddzielnej instalacji i otrzymuje ten sam wynik dla tych samych danych. Rdzeń programu nie jest modyfikowany.

---

## 8. Model danych — projekt logiczny, nie gotowy DDL

Model jest rozdzielony na bazę systemową i bazy firmowe. W modelu docelowym ADR-018 nie powielamy zbędnego `CompanyId` w tabelach danych wyłącznie jednej firmy. Tożsamość bazy oraz centralny katalog i autoryzacja nadal używają `CompanyId`. Rekordy mają identyfikatory, a tam, gdzie właściwe, daty i znacznik konkurencyjności. Poniższe nazwy są robocze poza elementami wyraźnie zatwierdzonymi w §8.3; model logiczny nie jest opisem wdrożenia.

W `Vatowiec_System`: `Company` (katalog, nazwa wyświetlana, status), `CompanyDatabaseRegistration` (firma, serwer, baza, klucz serwerowego profilu połączenia), Identity `User`/`Role` i tabele towarzyszące, `CompanyAccess` oraz `UserSession`. Profil połączenia odsyła do konfiguracji/sekretów na serwerze; tabela nie zawiera haseł SQL. Identity przechowuje wyłącznie hashe haseł użytkowników. Trwały proces provisioningu i pełny audyt administracyjny pozostają kolejnymi etapami; rejestr sesji nie jest audytem księgowym.

W bazie jednej firmy: `CompanyProfile`, kontrahenci i wszystkie encje księgowe poniżej. Centralne `Company.Name` jest etykietą katalogową; źródłem nazwy prawnej i NIP/REGON jest lokalny profil. Wdrożony profil ma PK `SingletonKey` z CHECK `= 1` i unikalny `CompanyId`, a tożsamość firmy weryfikuje resolver i dodatkowo kontekst przy zapisie. Contractors i pozostałe lokalne dane nie zawierają CompanyId; relacje wskazują lokalne int Id. ADR-019 opisuje wykonanie modelu ADR-018. Nie ma relacji SQL między bazami; zarejestrowane połączenie nadal trzeba zweryfikować względem rzeczywistego profilu i wersji schematu przed dopuszczeniem operacji.

AddressTypes wdrożono w każdej bazie firmowej. ADR-021 umieszcza wspólne TaxIdentifierSchemes w bazie systemowej, bez kartotek i historii konkretnych firm.
Nie oznacza to przenoszenia firmowych adresów lub kontaktów do bazy systemowej.

| Encja | Minimalne istotne pola / relacje |
|---|---|
| `CompanyProfile` | `SingletonKey = 1`, `CompanyId`, NIP, REGON, nazwa prawna; dokładnie jeden po inicjalizacji bazy firmy |
| `TaxProfile` | typ podatnika, forma PIT, VAT, `ValidFrom`, `ValidTo`; kontekst bazy firmy |
| `core.Contractors` — wdrożona podstawa | `Id int IDENTITY`, `ParentId` grupujący podmioty/grupy/jednostki bez dziedziczenia i cykli, opcjonalne `TaxId` i `VatEuId` do 100 znaków oraz `KsefIdWew`; bez zbędnego `CompanyId`; edycja identyfikatorów z normalizacją/ostrzeżeniami ADR-021 |
| `core.ContractorAddresses` — wdrożony schemat | wiele własnych adresów kontrahenta, także tego samego rodzaju z ref.AddressTypes; najwyżej jeden preferowany na ContractorId i AddressTypeId; pełne kolumny i typy w ADR-018 §3.7 |
| `ref.AddressTypes` — wdrożony schemat | lokalny, otwarty słownik: Adres rejestrowy, Adres korespondencyjny i możliwość dodawania własnych rodzajów w zakładce Adresy (ADR-020) |
| `core.ContractorContacts` — wdrożony schemat | jeden uniwersalny model kontaktu: obowiązkowe Name, opcjonalna funkcja/telefon/e-mail/adres/uwagi, bez FK do ContractorAddresses; techniczne typy określa ADR-019 |
| `core.ContractorBankAccounts` — wdrożony schemat | wiele rachunków bez unikalności numeru, CurrencyCode char(3) NULL, IsDefault bit, najwyżej jeden domyślny w grupie ContractorId/waluta, także NULL; brak obowiązku domyślnego; pełne kolumny i typy w ADR-018 §3.9 |
| `core.ContractorCreateOperations` — wdrożone | techniczny trwały wynik tworzenia w zakresie aktora, instalacji, OperationKind i operacji; atomowy z rekordem i audytem, szczegóły ADR-019 |
| `audit.ContractorChanges` | wdrożony audyt SnapshotVersion=2 z RecordType, RecordId i WarningJson; atomowe wartości przed/po i trwały wykonawca; świadome odstępstwa rachunków i identyfikatorów ADR-021 |
| `audit.ExternalVerifications` — wdrożone | niezmienne sprawdzenia zewnętrzne w bazie firmy, niezależne od edycji, snapshot wejścia, środowisko i trwały aktor; zakres informacji w §8.3 i ADR-021 |
| `ref.TaxIdentifierSchemes` — wdrożone w System | państwo/typ/prefiks, regex, klucz algorytmu, wersja, okres, aktywność i URL źródła, bez danych kontrahentów |
| `Document` | `Id`, typ, numer/seria, kontrahent, daty, waluta, status, wersja, `SourceSystem`, `ExternalId`, `RowVersion`; kontekst bazy firmy |
| `DocumentLine` | dokument, rodzaj towar/usługa, ilość, cena, podatki, kategorie, kwoty |
| `DocumentCorrection` | dokument oryginalny / korygujący, powód i rodzaj korekty |
| `DocumentAttachment` | typ MIME, hash SHA-256, ścieżka/obiekt, uprawnienia, metadane |
| `VatEntry` | dokument/pozycja, rejestr, okres, stawka/kody, VAT należny/naliczony, wersja reguł |
| `IncomeEntry` | dokument/pozycja, okres, kategoria, kolumna KPiR lub stawka ryczałtu, wartość |
| `TaxRuleVersion` | rodzaj, okres obowiązywania, wersja, źródło prawne, konfiguracja, hash |
| `TaxCalculation` | firma, podatek, okres, źródła, wersje reguł, wynik, status, historia |
| `Payment`, `PaymentAllocation` | kwota, waluta, data, dokumenty, rozliczenia częściowe |
| `FixedAsset`, `DepreciationEntry` | wartość początkowa, historia zmian, metoda, odpis, okres |
| `JpkPackage` | rodzaj, schemat, okres, hash XML, dane wejściowe/snapshot, wynik walidacji |
| `Submission` | kanał, status, identyfikator, historia odpowiedzi, UPO/artefakt |
| `KsefExchange` | XML wejścia/wyjścia, identyfikatory, numer KSeF, statusy, UPO |
| `AuditEvent` | niezmienny `ActorId`, `ActorIssuerId`, historyczny opis aktora, firma, moment UTC, czynność, identyfikator, przed/po lub delta, korelacja; bez FK do centralnego użytkownika |
| `ExtensionPackage` | manifest, wersja, uprawnienia, publikator, podpis/hash |
| `ExtensionSettings` | firma/użytkownik, ustawienia i referencje do sekretów |
| `OutboxEvent` | typ, identyfikator zdarzenia, payload, próby, status, następne uruchomienie |

ADR-021 wdraża SQL `ref.TaxIdentifierSchemes` w bazie systemowej: wspólne wersjonowane reguły bez kartotek i historii firm. Formaty VAT UE obejmują wszystkie 27 państw i XI; lista zaimplementowanych algorytmów oraz ograniczeń jest jawna. AddressTypes, dane kontrahentów, adresy, kontakty, rachunki i obie historie pozostają w bazie firmy.

**Uwaga o projektowaniu:** kwot historycznych i złożonych deklaracji nie obliczamy ponownie po aktualizacji reguł „w miejscu”. `TaxCalculation` i `JpkPackage` powinny mieć zamrożony zestaw danych wejściowych / identyfikator obliczeń, wersję reguł i wersję schematu. Każda korekta tworzy nową rewizję z odsyłaczem do poprzedniej. Audyt i dane potrzebne do odtworzenia muszą być odporne na usunięcie z UI.

### 8.1. Kluczowe ograniczenia SQL

- Unikalność numerów wystawianych dokumentów w ramach właściwej firmy, rodzaju/serii i okresu.
- Idempotencja importów, wysyłek i tworzenia rekordów w zakresie właściwej firmy. Unikaty są lokalne dla fizycznej bazy firmy; nie wymagają powielania `CompanyId` w jej tabelach. ADR-018 opisuje niewidoczny mechanizm tworzenia Contractors z int IDENTITY: trwały klucz operacji i jej wynik zapisane atomowo z kontrahentem i audytem, bez powielenia po ponowieniu lub restarcie API.
- Docelowo najwyżej jeden preferowany adres na ContractorId i AddressTypeId; wiele adresów tej samej pary jest dozwolone. Ograniczenie dotyczy preferencji, a nie liczby adresów danego rodzaju.
- Docelowo najwyżej jeden domyślny rachunek dla ContractorId i każdej grupy CurrencyCode, w tym osobnej grupy NULL; baza chroni unikalność także przy równoczesnych zapisach. Zmiana automatycznie odznacza poprzedni domyślny rachunek tej samej grupy w jednej transakcji. Nie ma obowiązku wyboru domyślnego i nie tworzymy ContractorDefaultBankAccounts.
- Klucze obce i ochrona spójności, w tym właściwe powiązanie wszystkich relacji z tą samą firmą.
- Zakaz fizycznego usuwania zatwierdzonych dokumentów z publicznego API.
- Audyt z atomowym powiązaniem ze skutkami transakcji.
- Integralność strukturalna SQL (klucze, relacje i właściwe ograniczenia) pozostaje obowiązkowa. Walidację podatkową identyfikatorów wykonuje Application/API z wersjonowanymi regułami i audytowanym świadomym odstępstwem; nie realizujemy jej podatkowymi CHECK CONSTRAINT.
- Dokument, ewidencje, audyt księgowy i Outbox są zatwierdzane w jednej transakcji bazy firmowej, bez transakcji rozproszonych obejmujących katalog systemowy.
- Osobne migracje `SystemDbContext` i `CompanyDbContext`; każda fizyczna baza ma własną `dbo.__EFMigrationsHistory`. Wspólny zestaw migracji firmowych wdraża się osobno do każdej firmy, z kontrolą wersji obsługiwanych przez API. Historia w katalogu nie zastępuje historii fizycznej bazy.
- Migracje schematu wersjonowane, odwracalne tylko gdy bezpieczne; przed aktualizacją istniejącej bazy backup i weryfikacja zgodności. Automatyczny provisioning wykonuje migracje tylko jawnie wskazanej nowej firmy; start API nie migruje baz.

### 8.2. Załączniki

Załączniki mogą być przechowywane w katalogu kontrolowanym przez serwer lub w adapterze object storage. W SQL przechowujemy metadane, identyfikator, hash, rozmiar, ścieżkę logiczną, firmę i uprawnienia. Nie przechowujemy pełnej treści dokumentów w losowych katalogach dostępnych z klienta. Eksport firmy uwzględnia zarówno SQL, jak i załączniki.

### 8.3. Docelowy model kontrahentów — ustalenia i granice (ADR-018)

**Hierarchia i własne dane:** Contractors przechowuje samodzielne podmioty, grupy i jednostki organizacyjne. ParentId służy przede wszystkim grupowaniu i nie wyznacza automatycznie zależności podatkowej. Nie ma dziedziczenia żadnych danych: każdy rekord ma własną nazwę, identyfikatory, adresy, rachunki i kontakty. W przyszłej fakturze wybranie grupy bez identyfikatora podatkowego wyświetla powiązane podmioty do wyboru. Samodzielny podmiot z odpowiednim identyfikatorem może być nabywcą; jednostka niebędąca samodzielnym nabywcą może wystąpić jako Podmiot3 według roli i kontraktu KSeF. Faktur ani integracji teraz nie implementujemy.

**Identyfikatory:** TaxId i VatEuId są dwoma opcjonalnymi polami tekstowymi, każde do 100 znaków; VatEuId zawiera cały numer VAT UE wraz z prefiksem. Dopuszczamy duplikaty po ostrzeżeniu i świadomym zatwierdzeniu z AcceptWarnings. Nie tworzymy ContractorTaxIdentifiers. ADR-021 wdraża ref.TaxIdentifierSchemes, normalizację i kontrolę w Application/API: formaty VAT UE wszystkich 27 państw i XI oraz NIP mod 11 dla PL. Brak pozostałych algorytmów lub krajowej reguły TaxId jest jawnie Unknown, nie poprawnością. Źródła, wersje i zakresy dat są identyfikowalne.

**KsefIdWew:** odrębny, opcjonalny pełny identyfikator jednostki wewnętrznej KSeF (IDWew/InternalId), nie NIP samodzielnego podatnika ani int Id kartoteki. Oficjalny format to dziesięć cyfr NIP podatnika, łącznik i pięć dodatkowych cyfr, razem 16 znaków. Przeznaczenie, źródła MF i rozróżnienie Podmiot3 od nabywcy są w ADR-018 §3.3.1. ADR-021 kontroluje format i usuwa odstępy; nie generuje identyfikatora, nie potwierdza nadania ani zgodności z własnym TaxId jednostki i nie integruje KSeF. [Źródło formatu: oficjalne OpenAPI KSeF PR v2](https://api.ksef.mf.gov.pl/docs/v2/openapi.json).

**Adresy:** core.ContractorAddresses przechowuje wiele własnych adresów kontrahenta, również kilka tego samego rodzaju. W ramach ContractorId i AddressTypeId najwyżej jeden może mieć IsDefault. ref.AddressTypes jest otwartym słownikiem, początkowo z Adresem rejestrowym i Adresem korespondencyjnym, rozszerzanym przez użytkownika. Zatwierdzona definicja obejmuje Id int IDENTITY, ContractorId, AddressTypeId, Name, Street, BuildingNo, ApartmentNo, PostalCode, City, Region, CountryCode, IsDefault, IsActive i RowVersion. Pełne typy SQL, długości i wymagalność określa [ADR-018 §3.7](docs/adr/ADR-018-docelowy-model-kontrahentow.md#37-adresy-i-otwarty-słownik-ich-rodzajów). Oddziały nie dziedziczą adresów.

**Kontakty:** jedna core.ContractorContacts dla osób, skrzynek, działów i miejsc, bez rozdzielania ich na osobne tabele i bez CRM. Kontakt ma jedno obowiązkowe Name; opcjonalne są funkcja/stanowisko, telefon, e-mail, własne pola adresowe i uwagi. Może zawierać jednocześnie telefon, e-mail i adres. Adres kontaktu nie ma powiązania z ContractorAddresses i nie jest automatycznie synchronizowany z kartoteką adresów.

**Rachunki:** jedna core.ContractorBankAccounts obejmuje Id int IDENTITY, ContractorId, AccountNumber, BankName, Bic, CurrencyCode char(3) NULL, AccountHolderName, Name, IsDefault bit, IsActive, Notes i RowVersion. Pełne typy SQL, długości i wymagalność określa [ADR-018 §3.9](docs/adr/ADR-018-docelowy-model-kontrahentow.md#39-rachunki-bankowe-i-domyślność-według-waluty); AccountNumber to wymagany nvarchar(100), a Bic to varchar(11) NULL. Kontrahent może mieć wiele rachunków. Najwyżej jeden domyślny na każdą walutę; NULL jest osobną grupą z takim samym limitem. Brak domyślnego jest dozwolony. Wybranie nowego automatycznie odznacza poprzedni z tej samej grupy w atomowej, audytowanej operacji, a unikalność domyślności zabezpiecza baza. Numery rachunków nie muszą być unikalne. Ich walidacja odbywa się w Application/API, ze świadomą akceptacją ostrzeżenia przez uprawnionego użytkownika i audytem odstępstwa. Nie tworzymy ContractorDefaultBankAccounts ani osobnej kartoteki banków na tym etapie.

**„Użytkownik ma zawsze rację”:** po wyjaśnieniu ostrzeżenia użytkownik z prawem akceptacji ostrzeżeń może świadomie zapisać błędną wartość lub duplikat **bez podawania uzasadnienia**. Odstępstwo jest audytowane wraz ze zmianą, wykonawcą, czasem i kontekstem ostrzeżenia/reguły. Nie jest to uznanie numeru za poprawny ani obejście praw, spójności, rowversion, CSRF czy wymagań zewnętrznych. ADR-020 wdraża AcceptWarnings=8 oraz potwierdzenie związane z hashem dla podstawowej kontroli zapisu rachunku. ADR-021 rozszerza ten mechanizm na identyfikatory; kolejne krajowe algorytmy pozostają do opracowania. Nie wymagamy uzasadnienia.

**Idempotencja:** preferowane int IDENTITY nie wymaga GUID jako głównego klucza. Niewidoczny klucz tej samej operacji utworzenia i jej utrwalony wynik pozwalają po utracie odpowiedzi zwrócić ten sam int Id bez nowego rekordu ani audytu. Zapisy kartoteki, audytu i wyniku operacji są atomowe w bazie firmy, z ochroną przed równoległym wykonaniem. ADR-018 §3.1 opisuje przebieg bez narzucania dodatkowej tabeli; ADR-019 wybiera core.ContractorCreateOperations z trwałym wynikiem i bez automatycznego usuwania operacji. Przyszłą retencję trzeba zaprojektować z zachowaniem gwarancji ponowień.

**Dwie historie w bazie firmy:** zachowujemy `audit.ContractorChanges` jako atomowo zapisywaną historię edycji kartoteki, z możliwością dostosowania implementacji. Osobne `audit.ExternalVerifications` przechowuje niezmienne zdarzenia sprawdzeń, w tym białej listy VAT i VIES. Zakres informacji obejmuje źródło, rodzaj sprawdzenia, identyfikator kontrahenta, opcjonalny rachunek, dane rzeczywiście użyte w zapytaniu, datę stanu sprawdzanego, odrębny czas wykonania zapytania, wynik, referencję urzędu, ewentualny błąd i użytkownika. Dane zapytania są historycznym zapisem, a nie tylko odsyłaczem do później zmienionej kartoteki. Trwałość identyfikacji wykonawcy po usunięciu konta obowiązuje obie historie.

Wynik negatywny jest odrębny od niedostępności usługi, timeoutu lub braku odpowiedzi. Ponowne sprawdzenie dopisuje zdarzenie bez wymogu zmiany kontrahenta, jego rowversion ani pozornej edycji w ContractorChanges. Późniejsza zmiana danych nie przepisuje dawnego sprawdzenia. Fizyczny kontrakt, środowiska, statusy i odwzorowania MF/GUS/VIES opisuje ADR-021; kolejne adaptery i zmiany kontraktów wymagają osobnego wersjonowania.

**Pozostałe doprecyzowania:** dalsze krajowe algorytmy i sumy rachunków, dystrybucja kolejnych wersji reguł oraz wspólne budżety żądań dla wielu serwerów przed uruchomieniem produkcji. ADR-018 §4 rozstrzyga wcześniejsze K-01–K-09. Kolumny adresów/rachunków, preferencje i brak unikalności numerów pozostają zatwierdzone. Nie projektujemy migracji fikcyjnych danych ani map GUID. Model wdrożył ADR-019, szczegóły kartoteki ADR-020, a identyfikatory, słownik reguł i testowe rejestry z osobną historią ADR-021. Brak pełnego algorytmu nie oznacza poprawności numeru.

---

## 9. Silnik reguł podatkowych i zarządzanie zmianą prawa

### 9.1. Reguła ma zakres czasowy

Każda reguła obliczeniowa i każdy format ma:

`RuleId`, `Revision`, `ValidFrom`, `ValidTo`, zakres podmiotowy, podstawę prawną / źródło, opis, przypadki testowe i datę wprowadzenia do programu.

Ważne jest odróżnienie **daty zmiany przepisów** od **daty wydania aktualizacji programu**. Rozliczenie roku 2025 uruchomione w 2027 powinno używać właściwych zasad dla 2025 r., a nie bezrefleksyjnie bieżących stawek.

### 9.2. Schematy elektroniczne

- Pliki XSD / oficjalne schematy i wersje generatorów są wersjonowane oraz opisane.
- Dobór schematu zależy od rodzaju formularza, daty rozliczenia i reguł przejściowych; nie tylko od numeru zainstalowanej aplikacji.
- Każdy generator ma walidację syntaktyczną (XSD), merytoryczną i zestaw testów regresyjnych.
- Eksportowany XML można wyświetlić, zachować, walidować i podpisać niezależnie od automatycznej wysyłki.
- Zmiany XSD nie wymagają migracji dokumentów źródłowych, jeśli wystarcza nowy adapter eksportu.

### 9.3. Źródła aktualnych standardów — stan rozpoznania 2026-10-21

- JPK_V7M(3), JPK_V7K(3): [MF — pliki do pobrania JPK_VAT](https://www.podatki.gov.pl/podatki-firmowe/jednolity-plik-kontrolny/jpk_vat-z-deklaracja/pliki-do-pobrania) — od 1.02.2026.
- JPK_PKPIR(3), JPK_EWP(4), JPK_ST(1): [MF — JPK_PD](https://podatki.gov.pl/podatki-firmowe/jednolity-plik-kontrolny/jpk_pd/pliki-do-pobrania-jpk_pd). Obowiązek dla danego podatnika i okresu należy sprawdzić osobno.
- KSeF 2.0: [MF — wsparcie dla integratorów](https://ksef.podatki.gov.pl/ksef-na-okres-obligatoryjny/wsparcie-dla-integratorow/) — OpenAPI oraz środowiska integracyjne/demo/produkcyjne.
- e-Deklaracje, XML i UPO: [MF — informacje podstawowe](https://www.podatki.gov.pl/e-deklaracje/informacje-podstawowe) — system FK może w odpowiednim trybie generować i wysyłać XML.
- SQL Server 2025 Express: [Microsoft — edycje i limity](https://learn.microsoft.com/en-us/sql/sql-server/editions-and-components-of-sql-server-2025?view=sql-server-ver17) i [wariant Linux](https://learn.microsoft.com/en-us/sql/linux/sql-server-linux-editions-and-components-2025?view=sql-server-ver17).
- MPL-2.0: [Open Source Initiative — pełny tekst](https://opensource.org/license/mpl-2-0).

**Nie określamy** w tej specyfikacji pełnej listy stawek, ustawowych terminów i zakresu zwolnień — są to zmienne wymagania domenowe, które należy zebrać w katalogu reguł z podstawą prawną i testami.

---

## 10. UX i dostępność

1. Dwie ścieżki: **szybkie księgowanie tabelaryczne** i **pełny formularz dokumentu**.
2. Klawiatura: `Ins` nowa pozycja, `Enter` edycja/zatwierdzenie, `Esc` rezygnacja, `F3` szukanie, `F9` wydruk — z możliwością zmiany skrótów. Nie kopiujemy bezrefleksyjnie wszystkich historycznych zachowań.
3. Tabele z zapisem widoków: kolumny, szerokości, sortowanie, filtrowanie, sumy, widoki per użytkownik.
4. Operacje masowe z **podglądem zmian**, liczbą rekordów i raportem błędów.
5. Kontrolowane zamknięcie okresu: checklista niezaksięgowanych dokumentów, korekt, plików JPK i wysyłek.
6. Zrozumiałe ostrzeżenia: „co jest nie tak”, „który dokument”, „jak poprawić” zamiast technicznych błędów SQL.
7. Współpraca równoległa widoczna: osoba modyfikująca dokument, wersja, konflikt, historia.
8. Wersja polska jako podstawowa; zasoby tłumaczeń w osobnych plikach.
9. Dane poufne niewidoczne użytkownikom bez odpowiednich uprawnień — także w podpowiedziach, wyszukiwarkach i logach.

---

## 11. Bezpieczeństwo, audyt i ochrona danych

### 11.1. Bezpieczeństwo dostępu

- HTTPS/TLS dla API, nawet w LAN jako ustawienie rekomendowane.
- Logowanie użytkownika z bezpiecznym magazynem haseł (hash z solą; sprawdzone biblioteki/ASP.NET Identity) i możliwością dodatkowego uwierzytelnienia w przyszłości.
- Autoryzacja serwerowa dla wszystkich komend i zapytań, **zawsze** z kontrolą firmy i uprawnień.
- Tajne klucze KSeF/API, certyfikaty i hasła poza repozytorium i zwykłymi tabelami konfiguracji; szyfrowane w spoczynku lub przez magazyn sekretów OS.
- Minimalne uprawnienia SQL przypisane serwerowi, oddzielne role backup/migracji.
- Brak produkcyjnych NIP, PESEL, rachunków i faktur w testach publicznego CI.

### 11.2. Audyt

Rejestr: kto, kiedy, z jakiego stanowiska/kontekstu, co utworzył/zmienił/zatwierdził, źródło żądania, identyfikator korelacji, poprzednia i nowa istotna wartość. Odróżniamy log techniczny od audytu biznesowego. Retencja, zakres danych osobowych i prawo do usunięcia są projektowane z uwzględnieniem obowiązków księgowych i RODO, a nie przez proste usuwanie zapisów podatkowych.

Historia księgowa jest samowystarczalna w bazie firmy: zapisuje niezmienny `ActorId`, przestrzeń identyfikatorów źródłowej instalacji (`ActorIssuerId`) i historyczny opis pozwalający zidentyfikować wykonawcę. Identyfikatory nie są ponownie używane; zmiana nazwy ani fizyczne usunięcie centralnego konta nie zmienia zapisanej identyfikacji. Bez FK/kaskad do `User` i bez wymogu odczytu centralnej kartoteki do wyświetlenia historii. Restore lub przeniesienie firmy zachowuje identyfikację dawnych aktorów, ale nie nadaje im bieżących uprawnień. ADR-017 implementuje te zasady dla audytu kartoteki kontrahentów; audyt dokumentów księgowych pozostaje osobnym zadaniem. Same daty encji nie są audytem.

### 11.3. Backup i odtwarzanie

Zakres tej sekcji opisuje docelową ochronę używanego systemu. Obecne fikcyjne dane demonstracyjne nie wymagają zachowania: §3.8 i ADR-018 dopuszczają przyszłe odtworzenie wyłącznie demonstracyjnych baz projektu bez migracji danych. Nie oznacza to zgody na utratę przyszłych danych rzeczywistych ani zgody na operacje na bazach innych aplikacji.

- Każda baza firmowa ma własny harmonogram, historię, retencję i kopie niezależne od innych firm. Oddzielnie chronimy `Vatowiec_System`.
- Uprawniony użytkownik sam zleca backup i odtworzenie własnej firmy przez aplikację, bez SSMS i uprawnień SQL na kliencie. Oddzielny proces techniczny wykonuje operację i raportuje jej stan.
- Automatyczne pełne kopie bazy na harmonogramie, archiwizacja załączników oraz bezpieczne przechowywanie kluczy. Pakiet firmy zawiera manifest tożsamości, wersji schematu/aplikacji, integralności, załączników i wymaganych kluczy.
- Kopia poza urządzeniem źródłowym lub na zasobie z kontrolą dostępu; możliwość szyfrowania.
- Rotacja i weryfikacja integralności, **regularny test restore**.
- Dla Express harmonogram organizuje własny proces / Windows Task Scheduler / systemd timer, nie SQL Server Agent.
- Instrukcja odtwarzania po awarii: baza + załączniki + konfiguracja + sekrety + zgodna wersja programu.
- Aktualizacja aplikacji nie może automatycznie nadpisać jedynej działającej kopii danych.
- Restore najpierw odtwarza do nowej bazy roboczej, weryfikuje integralność, `CompanyId`, schemat i załączniki; przełączenie jest kontrolowane i poprzedzone kopią dotychczasowej bazy oraz zatrzymaniem zapisów danej firmy. Inne firmy mogą pracować dalej. Nie odtwarzamy centralnych kont/uprawnień z pakietu firmy.
- Przeniesienie na inną instancję zachowuje `CompanyId`, przygotowuje tożsamość techniczną i sekrety docelowego serwera, aktualizuje centralne przypisanie i unieważnia stare wybory połączenia. Klon jako nowa firma wymaga osobnej procedury; nie jest zwykłym restore.
- W tym etapie backup/restore to wymagania i plan, nie działające funkcje. ADR-011 pozostaje otwarty w zakresie RPO/RTO, harmonogramów i retencji.

### 11.4. Monitoring

Health check API/SQL/magazynu plików, diagnostyka błędów integracji, kolejka Outbox, stan ostatniej kopii, miejsca na dysku, powiadomienia administratora. Telemetria do dostawcy **wyłączona domyślnie**, jeśli w ogóle ją wprowadzimy.

---

## 12. Testy i wymagania jakościowe

### 12.1. Warstwy testów

- **Unit/domain**: obliczenia kwot, zaokrąglenia, daty, odliczenia, kursy i reguły okresowe.
- **Integration**: SQL Server, migracje, transakcje, `rowversion`, izolacja firm, autoryzacja.
- **Database-per-company**: oddzielna baza systemowa i dwie fizyczne bazy firmowe; test modelu/SQL offline nie dowodzi wykonania migracji ani izolacji rzeczywistych baz. Bieżący runner używa wyłącznie trzech jawnie dozwolonych demonstracyjnych baz, po preflight i odtworzeniu; nie tworzy *_Test ani nie dotyka Vatowiec_Dev. Po testach inicjalizuje demonstrator.
- **Contract**: OpenAPI, XSD, formaty importerów/eksporterów, manifesty wtyczek.
- **E2E**: klient → API → księgowanie → JPK → podpis/wysyłka testowa → UPO.
- **Security**: IDOR (firma A nie czyta B), eskalacja uprawnień, złe dodatki, niebezpieczne XML/XXE, wyciek sekretów.
- **Disaster recovery**: odtworzenie bazy i załączników na czystym serwerze.
- **Regression**: anonimowe lub sztuczne przypadki ustalone i zweryfikowane przez praktyka księgowości.

### 12.2. Obowiązkowe scenariusze graniczne

| ID | Scenariusz | Oczekiwanie |
|---|---|---|
| T-001 | Dwóch operatorów próbuje zatwierdzić tę samą fakturę | Jedno zatwierdzenie, brak duplikatów, jasny konflikt |
| T-002 | Dwóch operatorów wystawia faktury w tej samej serii | Brak powtórzonego numeru |
| T-003 | Zanik sieci po wysłaniu komendy `POST` | Bezpieczne ponowienie bez podwójnego księgowania |
| T-004 | Faktura z dwiema stawkami VAT i dwiema stawkami ryczałtu | Prawidłowy podział pozycji i sum |
| T-005 | Data dokumentu w jednym roku, skutek podatkowy w innym | Prawidłowy okres ewidencji |
| T-006 | Korekta faktury z okresu zamkniętego | Nowa rewizja/procedura, bez ukrytej edycji historii |
| T-007 | Płatność częściowa i nadpłata | Spójne rozrachunki |
| T-008 | Dokument walutowy i dwa różne kursy do różnych celów | Jawne źródła przeliczenia i powtarzalny wynik |
| T-009 | Zmiana wersji schematu JPK po złożeniu wcześniejszego pliku | Poprzedni XML i UPO pozostają niezmienne |
| T-010 | Korekta JPK | Powiązanie z pierwotnym plikiem, prawidłowy status |
| T-011 | Import tej samej faktury KSeF dwa razy | Brak duplikatu, zachowany identyfikator KSeF |
| T-012 | Użytkownik firmy A próbuje odczytać identyfikator dokumentu firmy B | Odmowa także przez API/raporty/wtyczki |
| T-013 | Wtyczka odmawia działania lub kończy się błędem | Nie cofa poprawnego zaksięgowania, błąd widoczny do obsługi |
| T-014 | Przeniesienie instalacji na nowy serwer | Poprawnie odtworzona baza, załączniki i połączenia |
| T-015 | Rozliczenie starego roku po aktualizacji reguł | Poprzedni zestaw zasad i powtarzalny wynik |
| T-016 | Awaria tworzenia firmy po każdym etapie i ponowienie żądania | Ta sama firma/baza, bez duplikatów, bez przejmowania obcej bazy; dostęp dopiero po Ready |
| T-017 | Odtworzenie kopii firmy A na inną instancję | Zachowane dane, załączniki i historia; firma B i centralne uprawnienia pozostają niezależne |
| T-018 | Fizyczne usunięcie konta centralnego wykonawcy księgowania | Historia firmy nadal jednoznacznie identyfikuje aktora, także po restore |
| T-019 | Katalog omyłkowo wskazuje bazę innej firmy | Odmowa przed udostępnieniem danych z powodu niezgodnej tożsamości bazy |

### 12.3. Niefunkcjonalne cele początkowe [PROPOZYCJA]

- Wsparcie **minimum 5 równocześnie aktywnych użytkowników** w scenariuszach akceptacyjnych na sprzęcie referencyjnym; architektura bez założenia „jeden użytkownik”.
- Operacje zwykłego przeglądania i zapisu bez nieuzasadnionych opóźnień; konkretne czasy i rozmiary testowej firmy określimy po pierwszych pomiarach (nie deklarujemy fikcyjnych SLA).
- Wykonalny eksport wszystkich własnych danych i dokumentów, z dokumentacją formatu.
- Budowanie klienta i serwera przez niezależnego programistę z publicznego repozytorium, bez płatnych narzędzi deweloperskich.
- Brak obowiązkowych zamkniętych bibliotek do **zbudowania** rdzenia; płatne lub zamknięte integracje dopuszczalne jako opcjonalne adaptery po analizie licencji.

---

## 13. Plan etapowania i definicja wersji

### M0 — Repozytorium i architektura

**Bieżący etap implementacyjny (2026-10-22, ADR-021):** edycja identyfikatorów, wersjonowane reguły, świadome odstępstwa i duplikaty, ręczne rejestry TEST i niezmienna historia w firmie. Pełny przebieg **180/180** (76 SQL/API, 104 bez SQL), bez pominięć; kompilacja bez błędów/ostrzeżeń i poprawne formatowanie. Przeprowadzono próbę w rzeczywistym oknie Avalonia i rzeczywiste testowe wywołania MF/WL2, GUS oraz VIES. Bazy zaktualizowano jawnie po backupach, bez odtwarzania. Wyniki, algorytmy i granice: §3.10 i docs/identifiers-and-registries.md. M0 pozostaje nieukończone.

**Poprzedni etap (2026-10-22, ADR-020):** pełna kartoteka z czterema zakładkami, wybór grupy, API szczegółów, typowany audyt i trwała idempotencja, atomowa zmiana preferencji i świadoma akceptacja ostrzeżenia rachunku. Pełny przebieg **142/142** (67 SQL/API i 75 bez SQL), bez pominięć; kompilacja bez błędów/ostrzeżeń i poprawne formatowanie. Przeprowadzono pełną próbę w prawdziwym oknie Avalonia. Bazy zaktualizowano jawnymi migracjami technicznymi bez odtwarzania tabel biznesowych. Wyniki tego etapu: §3.9 i docs/contractors.md.

**Poprzedni etap (2026-10-22, ADR-018/019):** nowe klucze i relacje, 14 kolumn adresów, 12 rachunków, 14 kontaktów, lokalny AddressTypes oraz trwała idempotencja i atomowy audyt. Pełny przebieg 128/128 testów (57 SQL/API i 71 bez SQL), bez pominięć. Sprawdzono fizyczne typy/klucze/ograniczenia, cykle, izolację, konflikty, ponowienia i klienta z prawdziwym API/SQL. W tym wcześniejszym etapie bazy demonstracyjne odtworzono bez przenoszenia dawnych GUID/danych. Wyniki poniżej i w §3.7 są historią poprzednich etapów.

**Cel:** solidna baza techniczna bez deklarowania księgowej użyteczności.

**Uzgodniony pierwszy etap techniczny (2026-10-21):** solution .NET 10,
podstawowe API, okno Avalonia, konfiguracja EF Core dla SQL Server bez łączenia
z bazą, testy xUnit i instrukcja VS Code. Architektura: ADR-013. Na tym etapie
nie implementujemy modelu firm, logowania, kontrahentów ani migracji; pozostają
one dalszą częścią M0. Ukończenie samego szkieletu nie oznacza ukończenia M0.

**Historyczny etap bazy (2026-10-21, zastąpiony przez ADR-014):** lokalny SQL Server 2025 Express,
`localhost\SQLEXPRESS`, Windows Authentication, wyłącznie `Vatowiec_Dev`.
ADR-005 rozstrzygał wspólną bazę i izolację przez CompanyId. Pierwsza migracja
obejmuje firmy i podstawową kartotekę kontrahentów (ORG-001/ORG-005 częściowo),
relacje, daty zmian i rowversion (ORG-004). Migracje są ręczne; testy SQL działają
w wycofywanych transakcjach. Brak autoryzowanego kontekstu firmy oznacza pusty
odczyt i odmowę zapisu. Logowanie, role, pełny audyt, adresy i rachunki pozostają
dalszą częścią M0 / kolejnych iteracji kartotek. Nie powstają endpointy danych
firmowych. Certyfikat lokalnego SQL można akceptować tylko w Development przy
szyfrowanym połączeniu; to odrębne ustawienie od certyfikatu HTTPS API.

**Etap przygotowania database-per-company (2026-10-21):** decyzja ADR-014 zastępuje ADR-005. Rozdzielono modele `SystemDbContext` (katalog firm i lokalizacje baz) i `CompanyDbContext` (profil oraz kontrahenci jednej firmy), jawne konfiguracje, fabryki projektowe bez połączeń i osobne migracje początkowe. SQL wygenerowano offline; w tym wcześniejszym etapie nie zmieniano SQL Servera. Runtime nie ma domyślnego połączenia do firmy; przyszły resolver dostarczy niezmienny wybór po serwerowej autoryzacji. Dotychczasowa migracja jest archiwalna i nie należy do historii nowych kontekstów.

**Etap lokalnego wdrożenia przed Identity (2026-10-21):** użytkownik uznał wszystkie bazy projektu na `localhost\SQLEXPRESS` za testowe i upoważnił do ich tworzenia, modyfikowania i usuwania po sprawdzeniu dokładnej nazwy i przeznaczenia, bez ponawiania pytań. Wyłączone są bazy systemowe SQL i innych aplikacji. Po potwierdzeniu starego schematu, migracji i braku danych usunięto `Vatowiec_Dev`. Utworzono i zmigrowano `Vatowiec_System`, `Vatowiec_Company_A_Dev` i `Vatowiec_Company_B_Dev`. Katalog zawiera dwie fikcyjne firmy i dwa wskazania baz; każda baza firmowa ma własny profil i jednego fikcyjnego kontrahenta, bez NIP/REGON. Osobne narzędzie `Vatowiec.DatabaseTools` weryfikuje lokalną instancję i jawne nazwy/znaczniki baz, wykonuje migracje oraz inicjalizację; API nie migruje przy starcie. Narzędzie pełnych testów tworzy trzy tymczasowe bazy *_Test, uruchamia cały zestaw i usuwa je po sprawdzeniu rollbacków. Zaliczone 50 testów, w tym 12 SQL, bez pominięć; kompilacja ośmiu projektów bez błędów i ostrzeżeń. Domyślne połączenie systemowe Development wskazuje lokalną bazę przez Windows Authentication; poza Development pozostaje puste. Logowanie, użytkownicy/role, autoryzowany resolver, provisioning z aplikacji, audyt aktorów i backup/restore pozostają do implementacji; M0 nie jest ukończone. Instrukcja i wyniki: docs/database-per-company.md.

**Etap Identity i resolvera (2026-10-21, ADR-015):** wdrożono użytkowników, role, przypisania i uprawnienia do firm w SystemDbContext, cookie/sesje z CSRF, login/logout i listę firm. Resolver sprawdza uprawnienie, konfigurację SQL, rzeczywisty profil i schemat bazy przed odczytem danych. Udostępniono tylko odczyt profilu/kontrahentów. Migrację IdentityAndCompanyAccess wykonano jawnie po kopii bazy systemowej; schematy firmowe pozostały bez zmian. Konta operator.m0 i czytelnik.m0 mają różne uprawnienia; hasła są w lokalnym user-secrets. Etap ten nie obejmował UI logowania Avalonia, księgowania ani automatycznego tworzenia firmy. Audyt księgowy, provisioning i backup/restore w aplikacji pozostają dalszą częścią M0. Ręczna próba: docs/authentication.md i tools/database/Test-Authentication.ps1.

Weryfikacja etapu Identity: 70/70 testów, w tym 32 SQL/API, bez pominięć; build ośmiu projektów bez błędów i ostrzeżeń. Rzeczywista próba HTTPS dwóch kont potwierdziła profile A/B dla operatora, 403 dla czytelnika w B, 400 przy manipulacji CompanyId i 401 po logout. Dane dostępowe SQL nie trafiają do klienta.

**Etap desktopowy (2026-10-21, ADR-016):** logowanie, lista dostępnych firm, otwieranie profilu, odczyt kontrahentów, zmiana firmy i wylogowanie w jednym procesie Avalonia. Weryfikacja: 96/96 testów, w tym 35 SQL/API, bez pominięć; build ośmiu projektów bez błędów i ostrzeżeń. Rzeczywista próba Windows UI Automation potwierdziła konto operatora i odrębne kartoteki A/B przez HTTPS, zmianę firmy i logout. Hasła/sesje nie są utrwalane przez klienta. M0 nadal wymaga m.in. audytu i cyklu życia firm. Instrukcja: docs/desktop.md.

- Publiczne repo, `LICENSE`, `README`, `CONTRIBUTING`, `SECURITY`, `AGENTS.md`.
- Solution .NET 10, serwer ASP.NET Core, klient Avalonia, model modułowy.
- SQL Server: osobne migracje i konfiguracja bazy systemowej oraz dwóch przykładowych baz firmowych, profile fikcyjnych firm.
- Logowanie, dwie firmy w oddzielnych bazach, role, podstawowy audyt z trwałą identyfikacją aktorów; wznawialny provisioning i niezależny backup/restore.
- Jedna komenda API, ekran klientów, test izolacji danych i równoczesnej edycji.
- CI: build, testy, skan zależności, formatowanie.

**Warunek ukończenia:** dwaj operatorzy z różnych stanowisk logują się i modyfikują różne dane, uprawnienia i konflikt edycji działają, odtworzenie kopii jest udokumentowane.

### M1 — Dokumenty i serce programu

- Firmy, kontrahenci, faktury sprzedaży i zakupu, waluty, korekty.
- Statusy, numery, załączniki, historia, import/eksport podstawowy.
- Silnik kategorii i dokumentów źródłowych; wstępna ewidencja VAT/PIT.
- **Pierwszy publiczny mechanizm definicji XML** i demonstracyjny eksport formularza.

**Warunek:** test T-001…T-006; samodzielne rozszerzenie XML bez przebudowy rdzenia.

### M2 — KPiR, ryczałt, rozliczenia okresowe

- KPiR i ryczałt, stawki/reguły okresowe, ZUS/odliczenia, remanenty.
- Zaliczki i raporty z historią obliczeń.
- Akceptowane przypadki podatkowe, kontrola okresów, procedura zamknięcia.

**Warunek:** testy merytoryczne dla zadeklarowanych form opodatkowania i lat; brak niewyjaśnionych różnic w przykładach referencyjnych.

### M3 — VAT, JPK, deklaracje

- Pełna dla określonego zakresu ewidencja VAT; JPK_V7M/V7K, JPK_PKPIR/EWP/ST stosownie do przypadku.
- Generator XML, walidator, podpis i wysyłka według wspieranych metod, status, UPO, korekty.
- Wybrane deklaracje roczne i VAT-UE według uzgodnionej listy i możliwości integracji.

**Warunek:** z powodzeniem przeprowadzona ścieżka w środowiskach testowych, kompletne wyniki XML + UPO oraz próby błędne.

### M4 — KSeF i gotowość produkcyjna 1.0

- KSeF 2.0: wystawianie, pobieranie, identyfikatory, statusy, błędy, rozliczenie.
- Pełne testy wielostanowiskowości, importu powtórnego, backupu i odtwarzania.
- Dokumentacja administratora i użytkownika, minimalne role, instalatory.
- Niezależna weryfikacja przypadków podatkowych i bezpieczeństwa danych.

**Warunek:** realna firma z jednoznacznie zdefiniowanym scenariuszem podatkowym może zgodnie z przepisami przejść pełny cykl ewidencji i wysyłek na obsługiwany rok. Jeżeli nie spełniono kryterium — pozostajemy przy `beta`, bez sztucznej deklaracji `1.0`.

### Po 1.0

Biura rachunkowe, dodatkowe podatki i typy podmiotów, banki/ISO 20022, rozbudowane środki trwałe i magazyn, publiczny katalog dodatków, alternatywne silniki SQL, web client, pełna FK jako osobny obszar.

---

## 14. Wstępny backlog dla Codexa (kolejność implementacji)

| Epic | Zadanie | Wartość / dowód ukończenia |
|---|---|---|
| E01 | Solution, moduły, zależności, CI | Kompilacja i testy na czystym środowisku |
| E02 | Local dev: SQL Server, konfiguracja, migracje | Instrukcja startu + baza testowa |
| E03 | Firmy i użytkownicy | Przypisanie ról per firma, test bezpieczeństwa |
| E04 | Kontrahenci | CRUD, filtry, NIP, wersjonowanie |
| E05 | Dokumenty i pozycje | Spójne daty, sumy, statusy i historia |
| E06 | Atomowe zatwierdzanie i concurrency | Testy T-001/T-002/T-003 |
| E07 | VAT Entry + Income Entry | Projekcje rozłączne od dokumentu |
| E08 | Kategorie + wersjonowanie reguł | Zmiana reguły nie zmienia archiwum |
| E09 | Framework eksportów XML / manifest dodatku | Działający pakiet przykładowy |
| E10 | KPiR i ewidencja ryczałtu | Tabele, raporty, ręczne korekty |
| E11 | Silniki zaliczek PIT | Testowane obliczenia wersjonowane |
| E12 | VAT okresowy i JPK_V7 | XSD + wzorcowe przypadki |
| E13 | Pozostałe wymagane JPK i deklaracje | Listy kontrolne schematów, XML, UPO |
| E14 | KSeF adapter | Test end-to-end integracyjny |
| E15 | Backup, restore, instalator, hardening | Próba odtworzenia „od zera” |

Każde zadanie ma mieć: opis domenowy, kryteria akceptacji, testy, uwagi o bezpieczeństwie i odniesienie do odpowiedniego `ID` wymagania. Nie rozdzielamy „widoku faktury” od jej integralności transakcyjnej na dwa niezależnie dopuszczane do produkcji zadania.

---

## 15. Decyzje otwarte i ryzyka

| ID | Kwestia | Propozycja / termin rozstrzygnięcia |
|---|---|---|
| ADR-001 | Nazwa i identyfikacja projektu | Przed publicznym ogłoszeniem; roboczo „Otwarty następca VATowca” |
| ADR-002 | MPL-2.0 vs inna licencja | Przed pierwszym publicznym commitem z `LICENSE`; sprawdzić użyte zależności |
| ADR-003 | SQLite/PostgreSQL jako opcja | Dopiero po stabilizacji SQL Server i kontraktów repozytorium |
| ADR-004 | SQL Server na Linux / limit Express | Przed publikacją instrukcji instalacji linuksowej; rozbieżność dokumentacji |
| [ADR-005](docs/adr/ADR-005-izolacja-firm-i-pierwszy-model.md) | Historyczna wspólna baza | Zastąpiony przez ADR-014; pozostaje zapisem zastosowanej migracji Vatowiec_Dev |
| [ADR-014](docs/adr/ADR-014-database-per-company.md) | Database-per-company, dwa konteksty i cykl życia baz | Wdrożony lokalny demonstrator: trzy bazy, dwie fikcyjne firmy, migracje i pełne testy SQL |
| [ADR-015](docs/adr/ADR-015-identity-i-autoryzowany-wybor-bazy.md) | Identity, sesje i autoryzowany wybór bazy | Wdrożone API logowania, uprawnienia do firm, resolver i sprawdzanie rzeczywistego profilu; bez UI Avalonia |
| [ADR-016](docs/adr/ADR-016-desktop-logowanie-i-kontekst-firmy.md) | Avalonia: logowanie i kontekst firmy | Wdrożony scenariusz desktopowy, odczyt kontrahentów, zmiana firmy i wylogowanie |
| [ADR-017](docs/adr/ADR-017-kartoteka-kontrahentow-i-audyt.md) | Edycja kontrahentów, filtry, rowversion i atomowy audyt | Wdrożony formularz, serwerowe filtrowanie/strony i autoryzowany zapis; jawne migracje systemowa i firmowa |
| [ADR-022](docs/adr/ADR-022-dataport-i-sekrety-firmy.md) | DataPort i ustawienia firmy | Osobny adapter pośrednika, szyfrowany lokalny klucz, kontrola uprawnień i selektywny import do szkicu; bez zmiany oficjalnych adapterów TEST |
| [ADR-023](docs/adr/ADR-023-waluty-i-reczne-kursy-nbp.md) | Waluty i ręczne kursy NBP | Lokalny słownik/ustawienia A/B/C, kursy decimal, jawne konflikty i audyt, stronicowany ekran Avalonia; bez harmonogramu i reguł faktur |
| [ADR-024](docs/adr/ADR-024-kod-iso-jako-klucz-waluty.md) | ISO 4217 jako jedyny identyfikator waluty | Code char(3) PK, CurrencyCode FK, brak numerycznego Id waluty; odtworzenie wyłącznie demonstracyjnego modułu walut i kursów |
| [ADR-021](docs/adr/ADR-021-identyfikatory-i-rejestry-testowe.md) | Identyfikatory i rejestry TEST | Wersjonowane reguły, ostrzeżenia, osobna niezmienna historia i środowiska Test/Production/Mock; ręczne oficjalne MF/WL2, GUS BIR1.2 i VIES |
| [ADR-020](docs/adr/ADR-020-kompletna-kartoteka-avalonia.md) | Kompletna kartoteka Avalonia | Cztery zakładki, grupy, CRUD szczegółów, typowany audyt, idempotencja, atomowe preferencje i osobne uprawnienie do ostrzeżeń rachunków; bez walidatorów podatkowych/integracji |
| [ADR-019](docs/adr/ADR-019-implementacja-modelu-kontrahentow.md) | Implementacja podstawy kartoteki | int IDENTITY, zabezpieczona hierarchia, lokalne szczegóły, trwała idempotencja, audyt i odtworzenie trzech demonstracyjnych baz; bez walidatorów i nowych ekranów |
| [ADR-018](docs/adr/ADR-018-docelowy-model-kontrahentow.md) | Docelowy model kontrahentów i niezależne weryfikacje | Uzupełniony o ParentId bez dziedziczenia, nazwy tabel, reguły identyfikatorów i domyślność walutową; ślad rozstrzygnięć K-01–K-09, pozostałe doprecyzowania w §4; podstawa implementacji ADR-019, bez migracji danych demonstracyjnych |
| ADR-006 | Podpisywanie XML i przechowywanie certyfikatów | Przed implementacją wysyłek MF; nie przechowywać sekretów w zwykłej bazie |
| ADR-007 | API pluginów: manifest, sandbox, granice dostępu | Przed pierwszą wykonywalną wtyczką |
| ADR-008 | Pełny wykaz typów VAT i deklaracji na 1.0 | Przed M3; uzgodnić precyzyjne scenariusze |
| ADR-009 | PIT-28/36/36L, załączniki, roczne deklaracje | Przed M3; granice zastosowania i właściwe podpisy |
| ADR-010 | Migracja starych danych z oryginalnego VATowca | Opcjonalny importer; po uzyskaniu legalnych danych testowych i dokumentacji |
| ADR-011 | Backup/restore per firma: harmonogramy, retencja, RPO/RTO | Niezależność baz ustala ADR-014; szczegóły operacyjne przed wdrożeniem |
| ADR-012 | Wtyczki zamknięte i dystrybucja bibliotek | Weryfikacja prawna każdej zależności przed dołączeniem |

### 15.1. Główne ryzyka

- **Zgodność podatkowa**: częste nowelizacje, wyjątki, ulgi, wersje formularzy. Ograniczenie: wersjonowanie reguł i testy eksperckie.
- **KSeF/JPK/API**: zmiany schematów, limity i awarie. Ograniczenie: adaptery, kolejki, idempotencja, eksport XML niezależnie od wysyłki.
- **Wielostanowiskowość**: race conditions i błędne saldo. Ograniczenie: transakcje, konflikty, testy równoczesności.
- **Dodatki użytkowników**: wyciek danych, złośliwe wykonanie. Ograniczenie: izolacja procesu, capabilities, konta integracji, audyt.
- **Zależność od SQL Server**: Microsoft jest właścicielem silnika, limity i licencje mogą się zmieniać. Ograniczenie: otwarty kod programu, export danych, separacja modelu domenowego i SQL, opcja przyszłego adaptera.
- **Rozrost zakresu**: próba implementacji całej historycznej aplikacji na raz. Ograniczenie: wyraźne M0–M4, nie implementować magazynu/FK zanim część podatkowa przejdzie testy.

---

## 16. Definicja ukończenia zadania (Definition of Done)

Zmiana może zostać zaakceptowana, jeśli:

1. Ma identyfikator wymagania lub uzgodnioną decyzję ADR.
2. Nie narusza architektury klient → API → logika domenowa → SQL.
3. Uwzględnia wielofirmowość, wielostanowiskowość i uprawnienia.
4. Dla operacji modyfikujących dane ma prawidłowe transakcje, audyt i obsługę ponowień.
5. Dla obliczeń podatkowych ma testy na konkretne okresy, zaokrąglenia i opis źródła reguły.
6. Dla eksportów elektronicznych ma walidację oficjalną strukturą i zapis wersji XSD.
7. Dla nowego interfejsu publicznego zachowuje kompatybilność lub podnosi wersję kontraktu.
8. Przechodzi testy i nie wprowadza prawdziwych danych osobowych do repozytorium.
9. Posiada dokumentację konfiguracji, wdrożenia i migracji tam, gdzie wymagana.
10. Nie wykorzystuje nielegalnie kodu ani zasobów oryginalnego VATowca lub innych projektów.

---

## 17. Podsumowanie ustaleń

**Podstawa produktu:** księgowość uproszczona + podatki + elektroniczne rozliczenia w Polsce.  
**Podstawa techniczna:** wielostanowiskowy modularny monolit na ASP.NET Core, klient Avalonia, centralna baza systemowa i fizycznie oddzielne bazy SQL Server per firma (również różne instancje), wersjonowane API.
**Wyróżnik:** użytkownik może dopisać własny formularz, eksport, raport, walidator lub integrację bez czekania na autora i bez przebudowy rdzenia.  
**Granica bezpieczeństwa:** otwarte API nie jest zgodą na niekontrolowane modyfikacje zatwierdzonych zapisów podatkowych.  
**Filozofia wydań:** odpowiedzialna wersja beta jest lepsza niż wersja „produkcyjna” bez zweryfikowanych obliczeń i wysyłek.

---

## 18. Źródła i materiały odniesienia

1. Piotr Kowaluk, *VATowiec. Instrukcja obsługi, Windows/DBF 3.84* — dokument przekazany przez inicjatora projektu, 71 stron; szczególnie s. 3, 6–14, 19, 30–31, 36–39, 42, 49–50, 62, 67–70.
2. Ministerstwo Finansów: [JPK_VAT — pliki do pobrania](https://www.podatki.gov.pl/podatki-firmowe/jednolity-plik-kontrolny/jpk_vat-z-deklaracja/pliki-do-pobrania).
3. Ministerstwo Finansów: [JPK_PD — pliki do pobrania](https://podatki.gov.pl/podatki-firmowe/jednolity-plik-kontrolny/jpk_pd/pliki-do-pobrania-jpk_pd).
4. Ministerstwo Finansów: [KSeF 2.0 — wsparcie dla integratorów](https://ksef.podatki.gov.pl/ksef-na-okres-obligatoryjny/wsparcie-dla-integratorow/).
5. Ministerstwo Finansów: [e-Deklaracje — informacje podstawowe](https://www.podatki.gov.pl/e-deklaracje/informacje-podstawowe).
6. Microsoft Learn: [SQL Server 2025 — edycje i funkcje](https://learn.microsoft.com/en-us/sql/sql-server/editions-and-components-of-sql-server-2025?view=sql-server-ver17).
7. Microsoft Learn: [SQL Server 2025 on Linux — edycje i limity](https://learn.microsoft.com/en-us/sql/linux/sql-server-linux-editions-and-components-2025?view=sql-server-ver17).
8. Open Source Initiative: [MPL-2.0](https://opensource.org/license/mpl-2-0).

**Dokument podlega przeglądowi przed każdą większą iteracją.** Opis funkcji nie jest obietnicą ich zgodności prawnej bez implementacji, weryfikacji i aktualnych testów.
