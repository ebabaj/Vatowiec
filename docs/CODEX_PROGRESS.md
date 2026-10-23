# Stan prac — ustawienia GUS/MF/VIES w instalacji (ADR-029)

2026-10-23. **Zadanie zakończone. Bez Commit i Push.**
Zachowano wcześniejsze niezacommitowane prace ADR-028 (39 plików zastanych przy
rozpoczęciu zadania). Nie zaczynaj ponownie implementacji ani migracji.

## Wykonano

- Przeczytano AGENTS/SPEC, ORG-003/007; dodano ADR-029 rozszerzający ADR-028.
- Publiczne tryby/URL obu środowisk GUS/MF/VIES (także oba WL2 MF) w centralnym
  security.RegistryConfigurations; nowe instalacje i aktualizacja inicjują 3 TEST.
- audit.RegistryConfigurationChanges: niezmienny audyt przed/po, czas, trwały
  ActorId/ActorIssuerId/historyczna nazwa, bez FK do konta. Konfiguracja i audyt
  zapisują się atomowo. Operacje klucza GUS audytują tylko metadane, bez sekretu.
- SystemDbContext, osobne mapowania i migracja
  20261023120832_InstallationRegistrySettings. Jawnie WYKONANA po backupie.
  Dodaje tylko dwie tabele, indeks, trigger i 3 wiersze. Down odmawia kasowania
  audytu. W System 10 migracji, w obu firmach nadal 7. Bez resetów baz/kartotek.
- RegistrySettings scoped z jednego SELECT i prywatnego niezmiennego snapshotu.
  Nowe żądania widzą zapis bez restartu; trwająca operacja zachowuje środowisko
  i endpoint, również cała sesja GUS. Normalny serwer nie używa publicznych
  Registries:* z user-secrets ani flagi AllowProduction. Nie usunięto starych
  ustawień; jawne IConfiguration nadal służy testom i Test-only RegistryProbe.
- API GET /api/v1/installation/integrations/registries i PUT /{GUS|MF|VIES}:
  istniejąca centralna rola sprawdzana w DB, CSRF, rowversion, applock,
  potwierdzenie każdego zapisu Production, walidacja URL wszystkich środowisk.
  Oficjalny host/ścieżka/HTTPS, bez przekierowań/userinfo/query/fragment;
  końcowy slash opcjonalny, zapis kanoniczny. Brak fallbacku.
- Avalonia: istniejące Ustawienia instalacji → Integracje zewnętrzne, wybór
  usługi, TEST/PRODUKCJA, edycja obu zestawów URL, WL2 MF, potwierdzenie,
  zapis/odświeżenie, dotychczasowy panel klucza tylko przy GUS.
- GUS sekret, purpose/ring i DPAPI/Data Protection bez zmian; DataPort,
  konfiguracja i klucze firm pozostały bez zmian.
- Dokumentacja opisuje obsługę przez UI; brak obowiązku publicznych ustawień
  przez dotnet user-secrets, brak wymogu restartu. Historycznego ADR-028 nie
  nadpisano, ADR-029 opisuje zmianę sposobu konfiguracji.

## Weryfikacja i bezpieczeństwo danych

- Ostatnia kompilacja całego solution: **0 błędów, 0 ostrzeżeń**.
- Ostatni przebieg **335/335, 0 pominiętych**: 330 bez SQL + 5 niedestrukcyjnych
  SQL/API. Filtrowanie Category!=SqlIntegration oraz OfficialVatRulesSqlTests,
  GusConfigurationSqlTests i RegistrySettingsSqlTests; VATOWIEC_PRESERVED_VAT_SQL=1.
- **103 stare SQL/API z resetującymi fixture NIE uruchomiono i nie zaliczono.**
  NIE uruchamiaj DatabaseTools test/prepare-tests/initialize w tym zadaniu.
- Sprawdzono role/CSRF/rowversion/potwierdzenia, niepoprawne URL, routing obu
  środowisk na atrapach HTTP, nowe scope po zapisie i stałe ustawienia trwającej
  sesji GUS, niezmienny audyt i ochronę sekretu. Testy nowej konfiguracji pracują
  w zewnętrznej transakcji SQL i wycofują swoje zmiany. Nie uruchamiano żadnych
  produkcyjnych zapytań do rejestrów ani Test probe.
- Przed migracją zweryfikowano instancję/machine, lokalny SQL17 Express,
  Windows Authentication, dokładne 3 nazwy, znaczniki i historię migracji.
- Backup System COPY_ONLY/CHECKSUM + RESTORE VERIFYONLY:
  C:\Program Files\Microsoft SQL Server\MSSQL17.SQLEXPRESS\MSSQL\Backup\Vatowiec_System_before_contractors_1c3908d8b2254f45b78cea6d6276c0d8.bak
- SHA256/liczności: 42 stare tabele identyczne bezpośrednio po migracji;
  po testach i GUI 41 identycznych (wyłączono UserSessions: zwykłe logowania
  dodały następnie unieważnione sesje). W szczególności GUS i DataPort identyczne.
  Lokalny plik user-secrets identyczny SHA256; nie modyfikowano jego zawartości.
- Rzeczywiste GUI Avalonia przez HTTPS, operator.m0 z już istniejącą rolą:
  logowanie, pola GUS/MF/VIES i WL2, odrzucenie URL HTTP, odmowa zapisu PRODUKCJI
  bez potwierdzenia, zapis MF Production, odświeżenie bez restartu API, powrót
  MF do Test, wybór innych usług, wylogowanie. Nie dotykano pola klucza i nie
  klikano zapytań. W audycie pozostały **2 rzeczywiste zdarzenia MF operatora**
  Test→Production i Production→Test. **Wszystkie 3 usługi są w Test.**
  Nie nadawano ani nie odbierano użytkownikom ról.
- Formatowano zmienione C# (dotnet format --include); git diff --check poprawny.
  Nie poprawiano wcześniejszych problemów formatowania innych modułów EWIB.

## Pliki bieżącego zadania

Dokumenty: AGENTS.md, README.md, SPECYFIKACJA.md, docs/CODEX_PROGRESS.md,
docs/identifiers-and-registries.md, docs/production-registries.md,
docs/adr/ADR-029-ustawienia-rejestrow-w-instalacji.md.

Nowe pliki implementacji:
- src/Modules/Core/Domain/RegistryConfiguration.cs
- src/Modules/Core/Infrastructure/Persistence/SystemConfigurations/RegistryConfigurationMapping.cs
- src/Modules/Core/Infrastructure/Persistence/Migrations/System/20261023120832_InstallationRegistrySettings.cs i .Designer.cs
- src/Server/Api/Security/RegistryConfigurationEndpoints.cs
- src/SharedContracts/RegistryConfigurationContracts.cs
- src/Desktop/ViewModels/RegistryConfigurationEditor.cs
- tests/Vatowiec.Tests/RegistrySettingsTests.cs, RegistrySettingsDesktopTests.cs, RegistrySettingsSqlTests.cs

Dostosowane istniejące (część to zastane, jeszcze niezatwierdzone pliki ADR-028):
- Infrastructure: Persistence/SystemDbContext.cs, SystemDbContextModelSnapshot.cs,
  Registries/RegistryAdapters.cs.
- API: Program.cs, Security/GusConfigurationEndpoints.cs.
- Desktop: MainWindow.axaml i .axaml.cs, Services/VatowiecApiClient.cs,
  ViewModels/MainViewModel.Gus.cs.
- Testy: CompanyModelTests.cs, SqlServerPersistenceTests.cs,
  AuthenticationIntegrationTests.cs, GusConfigurationSqlTests.cs,
  OfficialVatRulesSqlTests.cs; stare fixture otrzymały jawne Mock ustawienia,
  nie były uruchamiane. FakeApi jest partial w plikach testów desktopu.
- Narzędzie: tools/database/Vatowiec.DatabaseTools/LocalDatabases.cs (lista
  tabel i wersji), RegistryProbe.cs (jawne ustawienia Test niezależne od DB).

Wszystkie pozostałe zmiany ADR-028 były obecne przed rozpoczęciem tego zadania
(np. klucz GUS, role, kontrakt sesji i endpointy). Nie wycofuj ich ani nie traktuj
jako obcych plików do wyczyszczenia. Dokładna bieżąca lista: git status --short.

## Dowody lokalne i dalsze kroki

TestResults/registry-settings-build.log, registry-settings-format.log,
registry-settings-preserved.log i .trx, registry-settings-migration.sql,
registry-settings-before-data.json, registry-settings-after-data.json,
registry-settings-final-data.json, registry-settings-local-settings.sha256,
registry-settings-ui.log. Raporty nie są częścią repozytorium.

Implementacja ukończona. Pozostaje ocena użytkownika, opcjonalne świadome
produkcyjne sprawdzenie z własnym kluczem GUS oraz samodzielny Commit/Push.
Nie ogłaszamy gotowości całego VATowca do produkcyjnej księgowości.
Przy wznowieniu najpierw przeczytaj ten plik, sprawdź git status i procesy.
Nie resetuj danych, nie usuwaj lokalnych sekretów ani konfiguracji.
