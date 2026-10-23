# Stan pracy — urzędowe reguły VAT UE (ADR-027)

2026-10-23. UKOŃCZONO. Bez Commit/Push. Bazowy HEAD bcd34a2.
Po wznowieniu przeczytaj ten plik i git status. Nie implementuj ponownie.

## Wykonano

- Porównano 28 reguł VatEuId z Vero, KE i materiałami administracji krajowych.
- Potwierdzona korekta formatu BE: pierwsza cyfra 0 lub 1. Nowe sumy tylko
  FI (wagi/mod11 Verohallinto) i HR (MOD11,10 REGOS/Porezna), za istniejącym
  IIdentifierChecksum + DI. Pozostałe 25 prefiksów format-only / Unknown.
- PL NIP, EL/XI zachowane; krajowe TaxId poza Polską nadal Unknown.
- TaxSchemeSeed zachowuje 29 wcześniejszych rekordów; BE/FI/HR zamknięte
  22.10.2026, trzy nowe wersje 2026-10-23.1 od 23.10.2026, urzędowe SourceUrl.
- Migrację 20261023080602_OfficialVatEuRules wygenerowano, przejrzano i jawnie
  WYKONANO w Vatowiec_System (8 migracji, 32 reguły). Bez DELETE/DROP.
  Down odmawia kasowania historii; odwrócenie decyzji przez nową wersję.
- Kontrola localhost\SQLEXPRESS major17 Express, Windows Auth i markerów
  wszystkich trzech baz. Backup System COPY_ONLY/CHECKSUM i VERIFYONLY:
  C:\Program Files\Microsoft SQL Server\MSSQL17.SQLEXPRESS\MSSQL\Backup\Vatowiec_System_before_contractors_d12d8c6ff8be4586a4fa36fcc960bf2c.bak
- Porównanie liczności i SHA256 danych: 40 innych tabel trzech baz bez zmian
  po migracji. Po SQL/API 39 tabel nadal identycznych; tylko UserSessions
  dopisane przez prawdziwe logowania i unieważnione przez logout.
  Bez usuwania kont, ról, uprawnień, danych firmowych, konfiguracji/kluczy.
- Zachowano API, WarningHash, AcceptWarnings, rowversion, idempotencję i audyt;
  brak zmian Avalonia, bibliotek i innych integracji.
- API zatrzymane na build i ponownie uruchomione: HTTPS localhost:7243,
  narzędzie exec session70177. Zwykła walidacja certyfikatu, csrf GET200.

## Wyniki

- Build całego Vatowiec.slnx: 0 błędów/ostrzeżeń (vat-eu-build.log).
- 285/285 wykonanych testów, bez pominięć: 282 offline i 3 nowe niedestrukcyjne
  SQL/API. TestResults/vat-eu-preserved.trx i vat-eu-preserved.log.
- Stare 103 SQL/API z resetującymi fixture WYŁĄCZONE, NIE RAPORTUJ ICH JAKO PASSED.
- 62 nowe offline: FI/HR wzorce/błędy, 28 prefiksów, daty/wersje, Unknown,
  brak/nakładanie reguł, bezpieczny skrypt migracji. Nowe SQL: fizyczne reguły,
  historia, WarningHash, rzeczywiste logowanie operator/czytelnik, CSRF,
  zakaz edycji B, stary hash422, brak zmian kartotek/audytów.
- Formatowanie zmienionych plików poprawne (vat-eu-format-changed.log).
  Pełny format NIE PRZECHODZI przez wcześniejsze WHITESPACE w niezmienianych
  ContractorDetailEndpoints.cs, EwibTests.cs, EwibDesktopTests.cs (vat-eu-format.log).
- git diff --check poprawny. GUI Avalonia nie było ponownie testowane ręcznie;
  istniejące testy klienta weszły do offline. Brak zapytań do rejestrów zewnętrznych.
- Początkowe 2 błędy oczekiwań FI oraz założenie o prawie edycji B poprawione.
  CSV Vero ma ilustracyjne błędne numery: 23456786 (oczekiwane 0), 34567892
  (reszta 1); zachowano je jako negatywne testy, nie zmieniano algorytmu.

## Zmienione pliki

- AGENTS.md, SPECYFIKACJA.md, README.md, docs/CODEX_PROGRESS.md,
  docs/identifiers-and-registries.md, nowy docs/adr/ADR-027-urzedowe-reguly-vat-ue.md,
  nowy docs/tax-rules/vat-eu-2026-10-23.md (pełna tabela, źródła i raport).
- Application/TaxIdentifiers.cs i nowy ForeignVatChecksums.cs; API/Program.cs.
- System migracja OfficialVatEuRules, designer i SystemDbContextModelSnapshot.
- Nowe OfficialVatRulesTests.cs, OfficialVatRulesSqlTests.cs; zaktualizowane
  CompanyModelTests.cs, RegistryAdapterTests.cs, SqlServerPersistenceTests.cs.
- DatabaseTools/LocalDatabases.cs: ostatnia migracja i kontrola znanej historii.

## Co pozostało

Brak implementacji do dokończenia w zakresie zadania. Użytkownik otrzymuje raport.
Dalsze kraje wymagają odrębnego urzędowego algorytmu i wzorców. Historyczne błędy
formatowania poza zakresem pozostawiono. Nie wykonano Commit ani Push.

KRYTYCZNE DLA KOLEJNYCH TESTÓW: aktualne wymaganie zabrania kasowania tabel i danych
System. Nie uruchamiaj DatabaseTools test/prepare-tests/initialize ani dawnych fixture.
Odtworzenie testów: build; ustaw procesowo VATOWIEC_PRESERVED_VAT_SQL=1;
dotnet test Vatowiec.slnx --no-build --filter '(Category!=SqlIntegration)|FullyQualifiedName~OfficialVatRulesSqlTests'
Usuń flagę po testach. Hasła tylko user-secrets vatowiec-api-development / środowisko.
