# Stan pracy Codex — EWIB i normalizacja (ADR-026)

2026-10-23. ZADANIE UKOŃCZONE. Bez Commit/Push — użytkownik wykona je sam.
Przed wznowieniem przeczytaj ten plik i git status. Nie powtarzaj ukończonej pracy.
Bazowy HEAD f3e26b7 (wcześniejszy IbanNet i zasada wspólnego walidatora).

## Wykonano

- Zrealizowano wymagania załącznika 1802d74d-6d47-42bb-90bb-834b5b5ef704/Pasted text.txt.
- Wspólna BankAccountPreparation.Normalize: whitespace Unicode, wielkie litery,
  myślniki U002D, U2010–2015, U2212, UFE63, UFF0D w zapisach liczbowych/IBAN.
  Pozostałe znaki zostają; LOCAL-123 zachowuje myślnik. NRB zapis bez PL, IBAN z PL.
- IbanNet nadal6.2.0; polityka bank-iban-v3. Brak nowej walidacji/checksum.
  BankAccountCheck.PolishClearingNumber tylko po poprawnej walidacji polskiego numeru.
- Application IBankDirectory; Infrastructure EwibBankDirectory, HttpClient HTTPS,
  stały host/parametr8cyfr, brak redirects/cookies/retry, timeout12s, limit1MiB.
  Parser według oficjalnego Swaggera: tylko BIC dokładnego numeru rozliczeniowego
  w siedzibie/jednostkach instytucji. Deduplikacja tylko identycznych BIC.
  Found/NotFound/Ambiguous/Unavailable/InvalidResponse; brak wyniku nie blokuje wpisu.
- API /bank-accounts/validate zwraca opcjonalny BankLookup. Zachowane prawa, CSRF,
  profile, właściciel wpisu i izolacja; zapisy/GET nie odpytują EWIB, tylko jawny
  podgląd poprawnego NRB/PL. Wysyłamy 8cyfr, nie pełny rachunek. Brak tabel/migracji.
- Avalonia pokazuje znormalizowany numer, wypełnia jednoznaczne puste pola,
  nie nadpisuje własnych wartości, daje jawne propozycje/wybór BIC. Wszystko do szkicu.
  Zmiana numeru usuwa propozycje; spóźniona odpowiedź nie zmienia innego szkicu.
- Normalizacja i walidacja zawsze na serwerze przy zapisie. Zachowane WarningHash,
  AcceptWarnings, rowversion, idempotencja i atomowy audyt. Inne integracje bez zmian.
- Zaktualizowano SPEC, AGENTS (zachowano stałą zasadę wspólnej walidacji), README,
  docs/contractors.md; dodano ADR026. Nie zmieniono historycznych ADR025 i wcześniejszych.

## Wyniki

- Build całego Vatowiec.slnx: 0 błędów/ostrzeżeń (TestResults/ewib-build.log).
- Pełny runner: 323/323 bez pominięć, 103 SQL/API i 220 bez SQL.
  TRX TestResults/database-per-company-e841dfc32cfd4f56b0e61273d10334aa.trx.
  Nowe testy: 41 HTTP/normalizacja, 5 Desktop, 4 SQL/API (50łącznie).
  Wcześniejsze osobne offline 46/46. Log TestResults/ewib-full.log.
- Runner początkowo zatrzymał się przed czyszczeniem na ręcznych TaxId w demo.
  Kontrola lokalnego lenovo/SQLEXPRESS, profilu A, znaczników i znanego schematu OK.
  Użytkownik wcześniej jawnie potwierdził wszystkie dane demo i pełny reset;
  użyto procesowo VATOWIEC_CONFIRMED_DEMO_DATA=1, bez ponownego pytania.
  Zachowano wszystkie kontrole nazw/instancji/znaczników/schematu/historii;
  wykonano inspect, trzy COPY_ONLY/CHECKSUM/VERIFYONLY backupy, runner i reseed.
  Istniejący szyfrogram DataPort zachowany/przywrócony bez odczytu sekretu.
- JEDNA rzeczywista próba publiczna: EWIB zapytanie1 nrRozliczeniowy10100000,
  HTTP200; Narodowy Bank Polski, siedziba10100000, dwa te same NBPLPLPWXXX
  (BIC i BIC SEPA). Potwierdzono FAQ i Swagger. Pełna odpowiedź tylko lokalnie
  TestResults/ewib-live-response.json; nie drukuj logo base64. Brak pełnego rachunku.
- Wszystkie testy automatyczne HTTP mają atrapy, także istniejące SQL/API.
- Rzeczywista Avalonia z HTTPS API/Kestrel i atrapą HTTP EWIB:
  normalizacja NRB/PL w polu, autofill, własne pola, jawne zastąpienie, wybór
  drugiego z wieluBIC, brakBIC, błędna suma/zagranica bez wywołania, B readonly,
  anulowanie szkicu i logout — OK. Nie wykonano kolejnej publicznej próby.
  Zrzut TestResults/ewib-ui-proposals.png. Lokalny harness tylko w ignorowanym
  TestResults/EwibUiHost; nie dodano mock-mode ani obejścia auth do produkcyjnegoAPI.
  Odczyt SQL podczas podglądu: A/B po1seed, 0audytów, 0operacji tworzenia.
  Szkic anulowano, przykładowego39... nie zapisano w demonstratorze.
- Testowy host7443 zamknięty (zweryfikowany EwibUiHost.exe PID39388).
  Przywrócono zwykłe API HTTPS7243, execsession11999. Desktop PID43684 jest
  wylogowany z adresem7243. Przed zatrzymaniem zawsze sprawdź bieżącą tożsamość procesu.

## Pliki zmienione

Application/BankAccountPreparation.cs, nowy IBankDirectory.cs;
Infrastructure/IbanNetBankAccountValidator.cs, DependencyInjection.cs,
nowy Registries/EwibBankDirectory.cs; API/Security/ContractorDetailEndpoints.cs;
SharedContracts/ContractorDetailContracts.cs; Desktop/MainWindow.axaml/.cs,
ViewModels/MainViewModel.Details.cs i nowy MainViewModel.BankLookup.cs;
tests/AuthenticationIntegrationTests.cs, DesktopTests.cs, BankAccountValidatorTests.cs,
ContractorDetailsIntegrationTests.cs i nowe EwibTests.cs, EwibDesktopTests.cs,
EwibIntegrationTests.cs; docs/adr/ADR026, SPECYFIKACJA.md, AGENTS.md, README.md,
docs/contractors.md i docs/CODEX_PROGRESS.md. Porównaj aktualny git status.

## Ograniczenia / następne kroki

Nie ma zaległych kroków implementacji. Nie aktualizowano NuGet, SQL ani licencji.
Nie potwierdzamy istnienia rachunku/właściciela. Zagraniczne BIC pozostają ręczne.
EWIB400 według Swaggera to brak wyniku LUB błędny parametr, więc komunikat
Unavailable, nie pewny NotFound. Podgląd nie ma trwałej historii — nie zmienia danych;
świadomy zwykły zapis zawiera bank/BIC w istniejącym audycie. Nie dodano tabel.
Przy dalszych zmianach używaj wspólnego walidatora i testów; nie rób ponownie
liveEWIB bez potrzeby. W razie nowego zlecenia sprawdź stan repo, zachowaj noCommit/Push.