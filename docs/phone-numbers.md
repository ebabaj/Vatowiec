# Obsługa telefonów — wynik prac z 02.11.2026

Wymaganie: KON-TEL-001. Decyzje: [ADR-043](adr/ADR-043-numery-telefoniczne.md).

Nowy lub zmieniony telefon zapisuje się w SQL jako E.164. Formularz i lista
kontaktów pokazują polski numer bez +48; pozostałe numery pozostają z kodem kraju.
Nie ma separatorów w wynikowym formacie. Puste pole pozostaje opcjonalne.
Historia nie jest automatycznie przepisywana, także przy edycji samej notatki.

## Zmienione pliki

- `src/SharedContracts/PhoneNumberService.cs`: wspólna obsługa przez
  libphonenumber-csharp 9.0.42, walidacja, normalizacja i prezentacja.
- `src/SharedContracts/ContractorDetailContracts.cs`: walidacja kontaktu
  po obu stronach API oraz prezentacja telefonu na liście.
- `src/SharedContracts/Vatowiec.SharedContracts.csproj`, `Directory.Packages.props`:
  zależność i przypięta wersja.
- `src/Desktop/ViewModels/MainViewModel.Details.cs`: wczytanie formatu UI,
  walidacja szkicu i zapis E.164 z ochroną niezmienionego telefonu.
- `src/Server/Api/Security/ContractorDetailEndpoints.cs`: obowiązkowa kontrola
  API, porównanie historii z autorytatywną encją SQL po sprawdzeniu rowversion.
- `src/Server/Api/Security/ContractorDetailMapping.cs`: zachowanie dokładnej
  historycznej wartości telefonu, gdy nie została zmieniona.
- `tests/Vatowiec.Tests/PhoneNumberTests.cs`: 37 przypadków jednostkowych.
- `tests/Vatowiec.Tests/PhoneNumberSqlTests.cs`: rzeczywisty test SQL/API
  z wycofaniem całej transakcji.
- `tests/Vatowiec.Tests/ContractorDetailsIntegrationTests.cs`: zastąpienie
  dawnego nienumerycznego telefonu testowego poprawnym numerem.
- `Directory.Build.targets`, `licenses/THIRD-PARTY-NOTICES.md`,
  `licenses/libphonenumber-csharp/LICENSE.txt`: licencja Apache-2.0 i autorzy.
- `SPECYFIKACJA.md`, `docs/adr/ADR-043-numery-telefoniczne.md` i niniejszy raport.

## Sprawdzenia

Standardowy `dotnet build Vatowiec.slnx -c Debug`: 0 błędów, 0 ostrzeżeń.
Nie tworzono instalatora, publikacji ani paczki Release.

Łącznie **544 różne testy zakończone powodzeniem, 0 błędów**:

- 37 testów jednostkowych telefonów: przykłady PL/00/+48, stacjonarne,
  różne długości zagraniczne, Czechy, Niemcy, włoskie zero, numer +1 z 48
  w dalszej części, puste pole, długość, znaki i niejednoznaczność,
  idempotentna normalizacja i odtworzenie formularza z wartości SQL.
- 1 rzeczywisty test SQL/API: POST/GET/PUT, odczyt surowej wartości SQL,
  niezmieniony numer/rowversion/audyt, idempotencja POST, błędy pola Phone,
  zmiana historycznych uwag bez zmiany telefonu, konflikt, uprawnienia i CSRF.
  Test wykorzystał istniejące zweryfikowane bazy VATowca oraz rollback,
  bez resetu, migracji ani trwałego zapisu danych testowych.
- 2 istniejące testy `ContractorFinanceSqlTests` jako kontrola regresji
  obsługi API, transakcji i audytu.
- 504 inne testy regresji spoza SQL i Personal. 6 dodatkowych testów
  natywnego UI/izolowanego środowiska zostało pominiętych przez ich atrybuty;
  nie są liczone jako zaliczone. Testów Personal nie uruchamiano.

Wyniki VSTest w `tests/Vatowiec.Tests/TestResults`:
`phones.trx`, `phones-and-api-regression.trx`, `phone-regression.trx`.
Pierwszy raport obejmuje wcześniejszy zestaw 36 testów jednostkowych i test SQL;
drugi zawiera końcowe 37 jednostkowych oraz 2 regresji SQL. Wyniki wspólne
nie są podwójnie liczone w powyższym podsumowaniu.

## Schemat i rozstrzygnięcie przykładu

Nie zmieniono schematu ani migracji. `Phone` nadal ma `nvarchar(100) NULL`,
czyli wystarcza na pełne E.164. Nie uruchamiano zbiorczej normalizacji historii.

Użytkownik potwierdził zachowanie ścisłej walidacji i korektę przykładu
`+49501234567`: metadane uznają go za niepoprawny strukturalnie. Jest odrzucany
jako nowy numer, a test potwierdza odrzucenie; poprawny przykład niemiecki to
`+4930123456`. Istniejący historyczny wpis pozostaje widoczny z pełnym +49
i nie jest automatycznie zmieniany. Nie zastosowano ręcznego wyjątku dla numeru.

Walidacja używa wyłącznie lokalnych metadanych pakietu. Nie sprawdzano
aktywności u operatora, nie wykonywano wywołań AI ani zapytań rejestrowych.

## API po zakończeniu

Aktualną standardową kompilację Debug uruchomiono profilem `https`.
Sprawdzono `https://localhost:7243/health`: HTTP 200, `Healthy`, ze standardową
walidacją certyfikatu. `/api/v1/status` zwraca `Vatowiec` / `M0-skeleton`.
Proces nasłuchujący na 7243: PID **40884**, `src/Server/Api/bin/Debug/net10.0/Vatowiec.Api.exe`.
Pozostawiono go uruchomionego. Proces uruchomiono w tle; nie tworzono drugiego API.
