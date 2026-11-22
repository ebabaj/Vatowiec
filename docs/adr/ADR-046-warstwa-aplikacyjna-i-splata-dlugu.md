# ADR-046: miejsce logiki biznesowej i spłata długu technicznego po M0

Data: 2026-11-22. **Status: PRZYJĘTY — akceptacja właściciela 2026-11-22.**
Elementy porządkowe (pkt 2–5 „Wdrożone”) nie zmieniają zachowania programu i zostały
wdrożone 2026-11-22 na zlecenie właściciela („spłata długu technicznego z analizy”).
Reguła z pkt 1 obowiązuje dla nowego kodu, w szczególności modułów M1.

## Kontekst

Analiza kodu z 2026-11-21 wskazała dług, który urośnie wraz z M1 (dokumenty, KPiR, VAT):

1. Logika biznesowa (walidacja, idempotencja, audyt, transakcje) jest w statycznych
   metodach endpointów w projekcie API, w folderze o mylącej nazwie `Security`.
   `Core.Application` zawiera niemal wyłącznie reguły identyfikatorów.
2. Klient Avalonia ma ponad 130 obsług zdarzeń `async void`; nieoczekiwany wyjątek
   w którejkolwiek zamyka program razem z niezapisanym szkicem.
3. `CompanyDatabaseResolver` przy każdym żądaniu API firmy odpytuje historię migracji
   bazy firmy (`GetAppliedMigrationsAsync`).
4. Brak CI — rozwiązane w M0 ([ci.yml](../../.github/workflows/ci.yml)).
5. Gęsty styl kodu (np. `Program.cs` z obsługą błędów w wierszu).

## Decyzja

### 1. Gdzie żyje logika (reguła dla nowego kodu)

- **Endpoint** (`src/Server/Api/Endpoints`) odpowiada wyłącznie za HTTP: trasę, wiązanie
  parametrów, CSRF/politykę autoryzacji, wybór bazy firmy przez `CompanyDatabaseResolver`
  i zamianę wyniku usługi na odpowiedź (`EndpointResults.Error` — stały kształt
  `{ code, message }`).
- **Reguły domenowe** (wyliczenia, niezmienniki, statusy dokumentu) — `Domain` modułu,
  bez zależności od EF Core, HTTP, Avalonia i T-SQL (AGENTS §2).
- **Przypadki użycia** (zatwierdzenie dokumentu, księgowanie, numeracja, idempotencja,
  audyt i Outbox w jednej transakcji bazy firmy) — usługa aplikacyjna modułu z jawnym
  wejściem/wyjściem, testowalna bez HTTP. Do czasu wydzielenia abstrakcji repozytoriów
  usługa korzystająca bezpośrednio z `CompanyDbContext` leży w `Infrastructure` modułu
  (wzorzec istniejących `UserAdministrationService`, `CompanyOperationRequests`,
  `CompanyBackupService`, `ExchangeRateService`, `IdentifierChecks`).
  Czyste reguły bez EF Core — w `Application`.
- Błędy biznesowe usługi to typowany wyjątek albo wynik z kodem; endpoint nie przekazuje
  surowych wyjątków SQL (AGENTS §6.7).
- **Istniejące endpointy** kartoteki, walut i rejestrów migrujemy przyrostowo — przy
  zmianie danego obszaru, bez osobnego „wielkiego przepisania”, które bez korzyści
  funkcjonalnej ryzykuje regresje w przetestowanych zapisach (rowversion, idempotencja,
  audyt, PESEL). Moduł dokumentów M1 powstaje od początku według tej reguły.

### 2. Porządek w API (wdrożone)

- Endpointy przeniesione z `src/Server/Api/Security` do `src/Server/Api/Endpoints`
  (przestrzeń nazw `Vatowiec.Api.Endpoints`), z zachowaniem historii git. W `Security`
  zostały wyłącznie `ApiSecurity` (uwierzytelnianie, sesje, CSRF, limity) oraz
  `InstallationAdministration` (polityka administratora instalacji).
- `IdentifierChecks` (kontrola identyfikatorów z ostrzeżeniami i `WarningHash`)
  przeniesione do `Vatowiec.Core.Infrastructure.Contractors` — pierwsza usługa kartoteki
  poza projektem API.
- Zduplikowane pomocniki `Error(...)` (9 plików) i odczyt identyfikatora użytkownika (12 plików)
  zastąpione wspólnym `EndpointResults` (`Error`, `HttpContext.UserId()`).
- `Program.cs`: rejestracja usług aplikacyjnych (`AddApplicationServices`), obsługa
  błędów (`UseApiErrorHandling`) i blokada sesji z obowiązkową zmianą hasła
  (`UsePasswordChangeGate`) wydzielone do `Hosting/ApiPipeline.cs`. Kody i treści
  odpowiedzi bez zmian.

### 3. Obsługa zdarzeń klienta (wdrożone)

- Jedynym `async void` w oknie głównym i widoku użytkowników jest `UiActions.Run`.
  Obsługi zdarzeń wywołują `Run(...)`; akcja startuje synchronicznie, więc
  `e.Handled` ustawione przed pierwszym `await` działa jak dotąd.
- Nieoczekiwany wyjątek trafia do `MainViewModel.ReportUnexpectedError`: komunikat
  w oknie zamiast zamknięcia programu. Oczekiwane błędy API nadal obsługuje model
  widoku (`Run` w `MainViewModel`). Treść wyjątku nie jest pokazywana ani logowana
  (może zawierać dane z formularza) — do śladu diagnostycznego trafia tylko typ.
- Siatka bezpieczeństwa: `Dispatcher.UIThread.UnhandledException` w `MainWindow`
  obsługuje wyjątki spoza `UiActions` tak samo.
- Okno Personal (`PersonalSetupWindow`) pozostaje bez zmian — prace Personal są
  wstrzymane (AGENTS, 2026-10-25).
- Podział `MainViewModel` na modele widoków poszczególnych ekranów **nie** jest częścią
  tej zmiany. Nowe ekrany M1 dostają własne modele widoku zamiast kolejnych plików
  `partial` `MainViewModel`.

### 4. Weryfikacja schematu bazy firmy (wdrożone)

- `CompanySchemaVerificationCache` (singleton API) pamięta przez **30 s** wyłącznie
  **udaną** weryfikację historii migracji dla trójki serwer/baza/`CompanyId`.
  Niezgodność nie jest zapamiętywana i usuwa wpis.
- Przy każdym żądaniu nadal sprawdzane są: `CompanyAccess` i wymagane prawa, aktywność
  firmy, rejestracja bazy i profil połączenia, polityka połączenia oraz **profil firmy
  w bazie** (zgodność `CompanyId`). Pamięć nie omija żadnej kontroli bezpieczeństwa.
- Ryzyko: jeżeli ktoś jawnie zmieni historię migracji bazy firmy przy działającym API,
  niezgodność zostanie wykryta najpóźniej po 30 s. Nowa wersja API (nowe migracje)
  oznacza restart procesu i pustą pamięć. Odtworzenie kopii (ADR-044 krok 3) migruje
  bazę roboczą przed przełączeniem nazwy, więc schemat po przełączeniu jest zgodny,
  a profil firmy jest sprawdzany przy każdym żądaniu.

### 5. Styl kodu

Nowy i zmieniany kod: jedna instrukcja w wierszu, wiersze czytelne bez przewijania.
Nie przeformatowujemy masowo istniejących plików (szum w historii git); porządkujemy je
przy zmianie, jak `Program.cs` w tym ADR.

## Konsekwencje

- Brak zmian kontraktu HTTP, schematu baz i migracji.
- Testy: 2 nowe testy jednostkowe pamięci weryfikacji schematu; pełny przebieg SQL
  (`DatabaseTools test`) i natywny test okna potwierdzają brak regresji.
- Do zrobienia przy kolejnych zmianach: przeniesienie logiki zapisu kartoteki
  (`ContractorEndpoints`, `ContractorDetailEndpoints`, `ContractorFinanceEndpoints`),
  walut i rejestrów do usług modułu; osobne modele widoku dla ekranów klienta.
