# Stan pracy Codex — IbanNet (ADR-025)

2026-10-23. ZADANIE UKOŃCZONE. Bez Commit/Push — użytkownik wykona je sam.
Przed wznowieniem przeczytaj ten plik i git status; nie zaczynaj od nowa.
Poprzedni moduł walut jest opublikowany: 7bb07a7fbd93f930363fd6dff5e49575f0a88a7f,
DataPort: fc7f0f310c297663984893bb80df10218756a730. Bieżąca zgoda NIE obejmuje publikacji.

## Wykonano

- IbanNet 6.2.0 w centralnym Directory.Packages.props i Infrastructure csproj.
- Application: IBankAccountValidator, typowany wynik i normalizacja. Infrastructure:
  adapter IbanNet; NRB sprawdzany z PL na kopii, zapis NRB bez PL, IBAN według kraju,
  konkretne polskie błędy i non-IBAN jako niezweryfikowany. Brak zapytań zewnętrznych.
- API: wynik przy odczycie/zapisie rachunku; POST bank-accounts/validate z ViewContractors,
  CSRF, profilem i sprawdzeniem kontrahenta. WarningHash obejmuje reguły/binary MVID,
  wynik/dane, firmę, autora, operację i rowversion. Istniejące transakcje, uprawnienie
  AcceptWarnings, audyt i idempotencja zachowane. Brak nowych tabel/migracji.
- Avalonia: przycisk Sprawdź numer i wynik obok pola; wynik również na liście.
  Zmiana numeru usuwa wynik i potwierdzenie. Test ViewModel z rzeczywistym API gotowy.
- Dependabot tylko IbanNet co tydzień, brak automatycznego scalania.
- licenses/IbanNet/LICENSE.md pobrano z oficjalnego tagu v6.2.0, Apache-2.0;
  THIRD-PARTY-NOTICES.md: autor Martijn Bodeman, wersja, commit i zasady dystrybucji.
  Sprawdzono nuspec, nupkg i upstream tree: brak dodatkowego NOTICE.
- Directory.Build.targets dołącza oba pliki prawne do build/publish programów.
  Publish API i Desktop wykonany; hash licencji jest identyczny w obu dystrybucjach.
- ADR-025, historyczna adnotacja ADR-020, SPEC, AGENTS, README i docs/contractors.md zaktualizowane.
- Dodano 24 testy walidatora i 5 testów SQL/API (w tym ViewModel+HTTP).
  Stary fixture zerowego NRB zastąpiono poprawnym testowym NRB; fixture testu
  odmowy MF dla DE miał błędną sumę, zmieniono tylko numer na wzorcowy DE.
  Logika MF i innych integracji pozostała bez zmian.

## Testy i bieżący proces

- Restore i build całego rozwiązania: OK, 0 błędów/ostrzeżeń.
- Testy BankAccountValidatorTests: 24/24 w końcowym pełnym przebiegu.
- Pierwszy pełny runner: 266/267, jedyny błąd to stary fixture DE opisany wyżej;
  poprawiono go. Nie traktuj pierwszego przebiegu jako sukcesu.
- Końcowy pełny runner: 273/273 bez pominięć, 99 SQL/API + 174 bez SQL.
  TRX TestResults/database-per-company-b5110f291dec42bb89ddf41fc0e874fc.trx.
  Logi TestResults/iban-full.log i iban-build.log. Po pierwszym 266/267 poprawiono
  fixture DE; kolejne przebiegi były zielone. Próba UI ujawniła mylące mapowanie
  cyfr kontrolnych 00/01/99 w IbanNet; poprawiono komunikaty i dodano regresje.
  Dodano też rozróżnienie DEMO-... od prefiksu niemieckiego IBAN i krótkiego PL.
- Runner kontroluje dokładne nazwy/instancję/znaczniki trzech baz, robi backupy
  COPY_ONLY/CHECKSUM/VERIFYONLY przy konfiguracji DataPort, zachowuje szyfrogram
  w pamięci i przywraca go po testach. Nie odczytuje sekretu.
- Użytkownik wcześniej jawnie pozwolił kasować wszystkie dane demonstratora
  (odwołał ograniczenie walutowe). Nie trzeba ponawiać pytania o runner.
- Zatrzymano lokalny API PID 14120 przed build/test. Desktop nie działał.

## Zakończenie i ewentualne wznowienie

Nie pozostały obowiązkowe kroki implementacji. Brak Commit/Push. Nie uruchamiaj
ponownie zakończonych testów bez nowej zmiany. Przed dalszą pracą sprawdź git status.

- UIA rzeczywistej Avalonia: operator.m0/A, NRB ze spacjami/zapis/ponowny odczyt
  bez PL/edycja, PL/DE/GB/FR preview, błąd sumy i świadomy zapis, non-IBAN i zapis,
  czyszczenie wyniku, B readonly i izolacja, wylogowanie — OK.
- Zrzut TestResults/iban-ui-warning.png. Odczyt SQL po UI: A 4 rachunki (1 seed+3 UI),
  4 audyty rachunków, 2 zaakceptowane ostrzeżenia, 1 NRB bez PL; B 1 seed, 0 audytów.
- Helper UIA wybiera tylko widoczne/wirtualizowane pozycje. Brak NewDetail w B
  wynika z prawidłowego ukrycia, nie błędu aplikacji. Sprawdzono brak/dostępność.
- API HTTPS i Desktop pozostawiono uruchomione, Desktop wylogowany. PID w
  TestResults/iban-processes.json; zawsze sprawdź rzeczywisty ExecutablePath przed
  ewentualnym zatrzymaniem. Nie ma haseł w logach/pliku.
- Finalny publish API i Desktop ma licencję i notices. SHA256 LICENSE:
  3D60FEFD7B68DB4A2517BF551B0944A2770AC92CF12CC087AFE7C0BBD7656FEE.
- Końcowa dokumentacja/zakres dołączone do raportu. Bazy mają te same historie
  migracji co przed zadaniem, nie tworzono tabel. Runner wyczyścił dane demo
  zgodnie z istniejącą zgodą; szyfrogram DataPort przywrócony. Brak zapytań MF/GUS/
  VIES/DataPort/NBP z nowego walidatora lub próby UI.

## Pliki

Directory.Packages.props, Directory.Build.targets, .github/dependabot.yml;
Application/BankAccountPreparation.cs; Infrastructure/IbanNetBankAccountValidator.cs,
DependencyInjection.cs i csproj; API Security/ContractorDetailEndpoints.cs,
ContractorDetailMapping.cs; SharedContracts/ContractorDetailContracts.cs;
Desktop/MainWindow.axaml/.cs, Services/VatowiecApiClient.cs,
ViewModels/MainViewModel.Details.cs; tests/BankAccountValidatorTests.cs,
BankAccountIntegrationTests.cs, AuthenticationIntegrationTests.cs,
ContractorDetailsIntegrationTests.cs, IdentifierRegistryIntegrationTests.cs;
licenses/*; docs/adr/ADR-025*, ADR-020*, SPECYFIKACJA.md, AGENTS.md,
docs/CODEX_PROGRESS.md, docs/contractors.md, README.md. Zawsze porównaj aktualne git status.

## Znane ograniczenia

Format NRB rozpoznajemy jako dokładnie 26 cyfr ASCII. Inne czysto lokalne formaty
są non-IBAN i wymagają ostrzeżenia, nie są bezwarunkowo blokowane. Biblioteka nie
potwierdza banku, BIC, właściciela ani istnienia rachunku. Upgrade wymaga wydania
aplikacji/testów; nie ma runtime update. Podgląd nie zapisuje audytu, gdyż nie
zmienia kartoteki; świadomy zapis zawsze zapisuje istniejący audyt atomowo.