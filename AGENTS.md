# AGENTS.md — instrukcje dla Codexa i współautorów

Projekt: **Otwarty następca VATowca** (nazwa robocza).  
Główne źródło wymagań: [`SPECYFIKACJA.md`](./SPECYFIKACJA.md).  
Status: implementacja modelu kontrahentów ADR-018, 2026-10-22; szczegóły techniczne ADR-019.

**Aktualny etap obejmuje implementację zatwierdzonego modelu ADR-018.**
Użytkownik zakończył etap dokumentacyjny. Można odtwarzać wyłącznie lokalne
Vatowiec_System, Vatowiec_Company_A_Dev i Vatowiec_Company_B_Dev po kontroli
instancji, dokładnych nazw, znaczników i fikcyjnego przeznaczenia danych.
Nie migruj dawnych GUID ani danych demonstracyjnych. Nie modyfikuj innych baz.
[ADR-019](docs/adr/ADR-019-implementacja-modelu-kontrahentow.md) opisuje rozwiązania
techniczne; [ADR-018](docs/adr/ADR-018-docelowy-model-kontrahentow.md) pozostaje
źródłem decyzji biznesowych. Poza zakresem są walidatory podatkowe, VIES/biała
lista/KSeF, fizyczne TaxIdentifierSchemes i ExternalVerifications oraz nowe
ekrany adresów/kontaktów/rachunków. Nie wykonuj Commit ani Push.

## 1. Co budujemy

Wielostanowiskową aplikację księgowo-podatkową dla polskich mikrofirm: **KPiR, ryczałt, VAT, JPK, deklaracje, KSeF**. Aplikacja ma być **otwarta na rozszerzenia** bez przebudowy rdzenia. Pełna księgowość, magazyn i kadry/płace nie należą do początkowego zakresu.

## 2. Stos i architektura

- C# / .NET 10; **Avalonia UI** (klient); **ASP.NET Core** (API/serwer); **SQL Server** (domyślnie Express); **xUnit**.
- **Modularny monolit**: `Domain`, `Application`, `Infrastructure`, `Api`, `Desktop` i wyraźne moduły biznesowe.
- **Klient nie komunikuje się bezpośrednio z SQL Server**. Dane, autoryzacja i księgowanie należą do API.
- **Database-per-company jest obowiązkowe**: `Vatowiec_System` przechowuje katalog firm, użytkowników, role, uprawnienia i lokalizacje baz; każda firma ma fizycznie oddzielną bazę SQL Server, także na innej instancji.
- `SystemDbContext` nie może mapować kontrahentów, dokumentów ani ewidencji księgowych. `CompanyDbContext` mapuje dane dokładnie jednej firmy; oba konteksty mają osobne migracje i snapshoty.
- Wybór bazy następuje wyłącznie na serwerze po uwierzytelnieniu i autoryzacji firmy. Brak wyboru lub niezgodność `CompanyId` oznacza odmowę; nie ma domyślnej bazy firmowej. Kontekst i wybór połączenia nie mogą być współdzielone między żądaniami.
- Uwierzytelnianie i sesje API określa ADR-015: Identity w bazie systemowej, cookie HTTPS/HttpOnly, CSRF dla login/logout, absolutna ważność i serwerowe unieważnienie sesji. Rola globalna nie zastępuje CompanyAccess. Jedynym źródłem firmy jest ścieżka API; resolver weryfikuje rzeczywisty profil i historię migracji przed odczytem danych.
- Hasła kont demonstracyjnych pochodzą wyłącznie z lokalnego user-secrets lub środowiska. Nie loguj haseł, tokenów, cookie ani connection stringów; nie dodawaj publicznego endpointu obchodzącego uwierzytelnianie na potrzeby demonstratora.
- Klient Avalonia stosuje ADR-016: HTTPS ze standardową walidacją certyfikatów, cookie i CSRF tylko w pamięci, nowy token CSRF przed logout. Nie utrwalaj hasła ani sesji; po zmianie firmy, odmowie lub wygaśnięciu usuń poprzednie dane. Ukrycie przycisku nie zastępuje serwerowej kontroli ViewContractors. Adres API konfiguruje VATOWIEC_API_URL lub ekran logowania.
- Kartoteka stosuje ADR-017: POST/PUT wymagają CSRF, ViewContractors i EditContractors; PUT wymaga rowversion. Nie dodawaj fizycznego usuwania, force-update ani automatycznego rozstrzygania konfliktu. Zapis kontrahenta i audit.ContractorChanges jest atomowy w bazie firmy. Audyt zachowuje ActorId, historyczną nazwę i ActorIssuerId z trwałego security.Installation, bez FK do centralnego konta. Listę filtrujemy i stronicujemy po stronie SQL, nie pobieramy całej kartoteki do klienta.
- ADR-017 opisuje wcześniejszy demonstrator; wdrożenie ADR-018 opisuje ADR-019. Brak zbędnego CompanyId w danych jednej bazy firmy, klucze kartoteki int IDENTITY, trwały wynik POST w core.ContractorCreateOperations. CompanyId w katalogu, uprawnieniach i wyborze firmy pozostaje. ParentId w Contractors służy grupowaniu podmiotów, grup i jednostek, nie określa zależności podatkowej. Żadne dane nie są dziedziczone: każdy rekord ma własne identyfikatory, adresy, rachunki i kontakty. Wybranie grupy bez identyfikatora przy przyszłej fakturze ma wyświetlać wybór powiązanego podmiotu; nie implementuj faktur teraz.
- Podstawowy API i UI nie edytują TaxId/VatEuId/KsefIdWew do czasu wdrożenia kontroli ostrzeżeń; nieznane pola zapisu są odrzucane. TaxId i VatEuId są odrębnymi opcjonalnymi tekstami do 100 znaków; VatEuId zawiera prefiks. Duplikaty są dozwolone po ostrzeżeniu i świadomym zatwierdzeniu. Nie twórz ContractorTaxIdentifiers. Opcjonalne KsefIdWew przechowuje pełny IDWew jednostki KSeF, odrębny od NIP podatnika i int Id kartoteki; format i oficjalne źródła są w ADR-018. Nie implementuj jeszcze walidacji ani integracji KSeF.
- Zatwierdzone nazwy: core.ContractorAddresses, otwarty ref.AddressTypes (Adres rejestrowy, Adres korespondencyjny i rodzaje użytkownika), core.ContractorContacts, core.ContractorBankAccounts. Pełne kolumny, typy SQL i wymagalność adresów oraz rachunków określa ADR-018 §3.7 i §3.9; nie dodawaj pól ani tabel bez uzasadnionej potrzeby i akceptacji użytkownika. Wiele adresów tego samego rodzaju jest dozwolone, ale najwyżej jeden preferowany na ContractorId i AddressTypeId; oddziały nie dziedziczą adresów. Kontakt ma obowiązkowe Name, pozostałe dane opcjonalne; może łączyć telefon, e-mail i własny adres bez FK do ContractorAddresses. Nie dziel kontaktów na osoby/działy/miejsca i nie buduj CRM.
- ContractorBankAccounts ma CurrencyCode char(3) NULL i IsDefault bit. Najwyżej jeden domyślny rachunek na ContractorId i grupę waluty, także oddzielną grupę NULL; brak domyślnego jest dozwolony. Zmiana domyślnego atomowo odznacza poprzedni w tej samej grupie, z ochroną unikalności w bazie. Nie wymuszaj unikalności AccountNumber; numery waliduje Application/API z możliwością świadomej, audytowanej akceptacji ostrzeżenia przez uprawnionego użytkownika. Nie twórz ContractorDefaultBankAccounts ani osobnej kartoteki banków na tym etapie.
- Docelowa walidacja podatkowa jest wersjonowana przez tabelę SQL ref.TaxIdentifierSchemes i wykonywana w Application/API, bez podatkowych CHECK w SQL. „Użytkownik ma zawsze rację”: użytkownik mający prawo akceptowania ostrzeżeń może świadomie zapisać nieprawidłową wartość lub duplikat bez uzasadnienia, z audytem odstępstwa. Nie pozwala to omijać praw, integralności, rowversion ani wymogów zewnętrznych. Konkretne algorytmy walidacji ustalimy przy implementacji walidatorów.
- Tworzenie kontrahenta ma niewidoczną dla użytkownika idempotencję operacji, niezależną od int Id: trwały wynik powiązany z kluczem żądania, chroniony przed równoległym wykonaniem i zapisany atomowo z kartoteką oraz audytem. Utrata odpowiedzi i ponowienie nie mogą utworzyć drugiego rekordu. Przebieg opisuje ADR-018; nie wymaga GUID jako klucza głównego, map starych GUID ani migracji fikcyjnych danych.
- Zachowaj rozdzielenie audit.ContractorChanges (zmiany kartoteki) i docelowego audit.ExternalVerifications (niezmienne sprawdzenia z danymi rzeczywiście użytego zapytania). Obie historie należą do bazy firmy. Nowe sprawdzenie nie wymaga zmiany kontrahenta; niedostępność usługi nie jest negatywnym wynikiem weryfikacji.
- Logika domenowa nie może zależeć od Avalonia, HTTP, EF Core ani T-SQL.
- Nie dodawaj mikrousług, event sourcingu ani nowego frameworka tylko ze względu na modę. Uzasadnij zmiany architektoniczne w ADR.

## 3. Nienaruszalne zasady danych

1. Każda operacja dotyczy uprawnionej firmy (`CompanyId`). **Sprawdzenie uprawnienia następuje na serwerze**.
2. Zatwierdzony dokument jest audytowalny. Nie wolno ukradkiem edytować lub fizycznie usuwać księgowych zapisów historycznych.
3. Zatwierdzenie dokumentu i jego skutków ewidencyjnych jest **atomowe** — jedna transakcja.
4. Równoczesne zapisy muszą wykrywać konflikty (`rowversion`/ETag) i zapobiegać podwójnej numeracji.
5. Żądania zewnętrzne, importy i zadania kolejkowane muszą być idempotentne.
6. Kwoty pieniężne i stawki liczymy na `decimal`, **nie** `float`/`double`.
7. Reguły podatkowe i schematy JPK mają wersje oraz okres obowiązywania. Nie zastępuj reguł historycznych bieżącymi.
8. Nie umieszczaj sekretów ani autentycznych danych podatników w repozytorium, testach lub logach.
9. Historia księgowa zawiera trwały identyfikator aktora (z przestrzenią identyfikatorów źródłowej instalacji) i historyczny opis użytkownika w bazie firmy. Usunięcie konta centralnego nie może usuwać ani anonimizować tej identyfikacji; nie stosuj FK ani kaskad do centralnej kartoteki użytkowników. Identyfikatorów aktorów nie używa się ponownie.
10. Dokument, skutki ewidencyjne, audyt księgowy i Outbox zapisuje się atomowo w bazie firmy. Nie uzależniaj księgowania od transakcji obejmującej bazę systemową lub inną instancję SQL.
11. Każda firma ma niezależny backup/restore bazy, załączników i wymaganych kluczy. Odtworzenie firmy nie odtwarza centralnych kont ani uprawnień.
12. Docelowe utworzenie firmy w aplikacji automatycznie tworzy bazę, wykonuje migracje i inicjalizuje profil. Osobna tożsamość techniczna obsługuje provisioning; zwykłe API nie otrzymuje uprawnień CREATE DATABASE/DDL. Proces ma trwały klucz idempotencji, checkpointy i bezpieczne wznowienie, bez usuwania zastanej bazy.

## 4. Podatki i integracje

- **Nie zgaduj** stawek, dat, pól XML, interpretacji przepisów ani formatu API MF.
- Każda reguła podatkowa wymaga wskazania okresu, źródła, przykładów wzorcowych i testów.
- Zewnętrzne XSD/OpenAPI są wersjonowane. Testuj XML względem odpowiedniej oficjalnej wersji.
- Oddziel wyliczenie JPK/deklaracji od podpisu, wysyłki i pobierania UPO.
- Rozróżniaj status dokumentu księgowego, faktury w KSeF i operacji wysyłki.
- Nie uznawaj „XSD valid” za wystarczający dowód poprawności podatkowej.

## 5. Rozszerzenia

- Raporty, XML/JSON/CSV, formularze i mapowania powinny działać przez **deklaratywny, wersjonowany kontrakt**.
- Wtyczki nie mogą otrzymać dostępu do całego SQL Server ani do sekretów innych dodatków.
- Publiczne komendy i API posiadają autoryzację, walidację, audyt i wersjonowanie.
- Kod dodatków uruchamiaj w izolowanym procesie lub podobnie skutecznej izolacji; `AssemblyLoadContext` sam w sobie nie jest sandboxem bezpieczeństwa.
- Minimalny demonstrator: eksport XML dla formularza użytkownika **bez zmiany kodu rdzenia**.

## 6. Zasady każdej zmiany

Przed kodowaniem:

1. Przeczytaj odpowiadające zadaniu fragmenty `SPECYFIKACJA.md`.
2. Wskaż ID wymagania (np. `DOC-001`) i sprawdź, czy nie ma kwestii otwartej `ADR-*`.
3. Napisz krótką propozycję rozwiązania i kryteria akceptacji, gdy zmiana wpływa na księgowanie, prawo podatkowe lub model danych.

Podczas kodowania:

4. Twórz możliwie małe, kompilowalne zmiany.
5. Pisz testy jednostkowe, integracyjne i testy konfliktów/uprawnień odpowiednio do zmiany.
6. Wprowadzając migrację SQL, opisz sposób wdrożenia i ryzyko niezgodności danych.
   Generowanie migracji i SQL offline jest odrębne od ich wykonania. Nie uruchamiaj migracji wszystkich baz przy starcie API. Provisioning wykonuje je jawnie dla nowo tworzonej firmy; aktualizacje istniejących firm są kontrolowane osobno.
7. W API zwracaj konkretne błędy biznesowe zamiast surowych wyjątków SQL.

Po kodowaniu:

8. Uruchom build i odpowiednie testy; opisz dokładnie co zostało sprawdzone oraz co nie było możliwe do sprawdzenia.
9. Uaktualnij specyfikację, jeśli powstało nowe ustalenie, oraz dopisz ADR dla decyzji architektonicznej.
10. Nie ogłaszaj gotowości produkcyjnej bez zgodności z definicją wersji z `SPECYFIKACJA.md`.

## 7. Proponowany szkielet repozytorium (do zatwierdzenia)

```text
/
  SPECYFIKACJA.md
  AGENTS.md
  README.md
  LICENSE
  docs/
    adr/
    tax-rules/
    integrations/
    user-guide/
  src/
    Server/
    Desktop/
    Modules/
      Core/
      Documents/
      Vat/
      IncomeTax/
      Reporting/
      Ksef/
      Payments/
      Extensions/
    SharedContracts/
  tests/
    Unit/
    Integration/
    Contract/
    EndToEnd/
  examples/
    extensions/
  tools/
```

Nie twórz wszystkich katalogów pustymi szablonami bez potrzeby; szkielet jest mapą docelową.

## 8. Pierwsze zadanie dla Codexa

Poniżej zapis wcześniejszych etapów i zgody na lokalne operacje testowe.
Aktualny zakres implementacyjny i lista trzech dozwolonych baz z początku pliku mają pierwszeństwo.
Po zatwierdzeniu szczegółów nowego modelu przyszła implementacja może odtworzyć
wyłącznie demonstracyjne bazy VATowca: fikcyjne dane nie wymagają zachowania,
nie projektujemy ich migracji, map GUID ani zachowania przykładowych kontrahentów.
Nie rozszerza to zgody na bazy systemowe SQL ani bazy innych aplikacji.

> Przygotuj propozycję i minimalny szkielet repozytorium według M0 w `SPECYFIKACJA.md`: solution .NET 10, ASP.NET Core Web API, Avalonia Desktop, projekt Domain/Application/Infrastructure, integracja z SQL Server, testy xUnit i instrukcja uruchomienia. Zacznij od dokumentu ADR opisującego podział projektów i granice modułów. Nie implementuj podatków, KSeF ani schematów JPK bez osobnych wymagań i testów. Zapewnij możliwość zalogowania dwóch użytkowników oraz podstawowy model dwóch firm z izolacją danych.

Aktualny etap po zaakceptowaniu ADR-014: wdrożenie lokalne i testy SQL. Użytkownik potwierdził, że wszystkie bazy projektu są testowe, i zezwolił na ich tworzenie, modyfikowanie i usuwanie na `localhost\SQLEXPRESS`, w tym usunięcie starej `Vatowiec_Dev`, bez ponawiania pytań. Przed usunięciem należy potwierdzić dokładną nazwę i przeznaczenie bazy; nie wystarcza sam prefiks nazwy. Nie wolno zmieniać baz systemowych SQL ani baz innych aplikacji. Narzędzie administracyjne korzysta z jawnej listy nazw i znacznika własności (dla starej Vatowiec_Dev: schemat i znana migracja). Aktualny runner SQL jawnie odtwarza trzy dozwolone bazy demonstracyjne, wykonuje testy i ponownie inicjalizuje konta oraz dane fikcyjne; nie tworzy baz *_Test. Przed testami zatrzymaj API i desktop. Pominiętych testów nie raportujemy jako zaliczonych. Zgoda nie obejmuje baz produkcyjnych ani zdalnych instancji i nie oznacza migracji przy starcie API.

## 9. Licencje i cudze materiały

Oprogramowanie źródłowe planujemy publikować na licencji **MPL-2.0**, decyzja pozostaje do zatwierdzenia. Dopuszcza się opcjonalne bezpłatne komponenty zamknięte tylko wtedy, gdy ich licencje zezwalają na przyjęty sposób użycia/dystrybucji. **Nie kopiuj** kodu, ikon, tekstów ani innych chronionych zasobów oryginalnego VATowca. Historyczna instrukcja jest wyłącznie materiałem do opisu funkcji i potrzeb użytkownika.
