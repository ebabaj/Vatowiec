# ADR-013: Szkielet modularnego monolitu

Data: 2026-10-21  
Status: zaakceptowany w zakresie architektury przez użytkownika.  
Powiązania: SPECYFIKACJA.md §4.2, M0, E01 i przygotowanie E02; AGENTS.md §8.

## Kontekst i zakres

Program ma obsługiwać wiele firm i stanowisk oraz umożliwiać rozwój modułów bez
uzależnienia domeny od interfejsu, HTTP lub SQL Server. Pierwsza część M0 obejmuje
wyłącznie kompilowalny szkielet, uruchamialne API i okno desktopowe, konfigurację
EF Core oraz testy. Nie wprowadza modelu danych, migracji, logowania ani księgowości.

## Decyzja

Wybieramy C# i .NET 10, ASP.NET Core Web API, Avalonia UI, EF Core z providerem
Microsoft SQL Server oraz xUnit. Serwer będzie pojedynczym wdrażanym modularnym
monolitem. Desktop będzie komunikował się z nim przez HTTPS i publiczne kontrakty.

Projekty i dozwolone bezpośrednie referencje:

| Projekt | Referencje do projektów |
|---|---|
| Core.Domain | brak |
| Core.Application | Core.Domain |
| Core.Infrastructure | Core.Application |
| SharedContracts | brak |
| Api | Core.Application, Core.Infrastructure, SharedContracts |
| Desktop | SharedContracts |
| Tests | Api, Core.Infrastructure |

Referencja API do infrastruktury służy konfiguracji kontenera DI. Domain nie zna
EF Core, HTTP ani Avalonia. Application będzie definiować przypadki użycia i
interfejsy implementowane przez Infrastructure. SharedContracts zawiera wyłącznie
publiczne DTO, nigdy encje EF ani DbContext. Desktop nie ma referencji do serwera,
infrastruktury i pakietów dostępu do SQL. Domenę i warstwę Application pozostawiamy
bez sztucznych klas demonstracyjnych do czasu pierwszego rzeczywistego wymagania.

Core obejmie firmy, użytkowników, uprawnienia, wspólne kartoteki i audyt.
Documents będzie właścicielem dokumentów źródłowych; VAT i IncomeTax ich skutków
podatkowych; Reporting eksportów i deklaracji; Ksef komunikacji z systemem
zewnętrznym; Payments rozrachunków; Extensions kontraktów i definicji dodatków.
Nowe moduły będą miały własne granice Domain/Application/Infrastructure i
komunikować się przez jawne kontrakty, bez dostępu do cudzych tabel lub DbContext.
Nie tworzymy ich pustych katalogów ani projektów w tym etapie.

API udostępnia tylko wersjonowaną informację techniczną `/api/v1/status` i
`/health` sprawdzający działanie procesu. Health check nie sprawdza bazy.
OpenAPI jest dostępne w środowisku Development. Brak endpointów danych firmowych.

CoreDbContext jest pusty, rejestrowany jako scoped i skonfigurowany dla SQL Server.
Brak connection stringa jest dozwolony w szkielecie. Inicjalizacja API i testy nie
wykonują zapytań, nie tworzą bazy ani nie uruchamiają migracji. Nie ma domyślnego
adresu SQL. Przyszła konfiguracja pochodzi z sekretów deweloperskich lub zmiennej
ConnectionStrings__Vatowiec, nie z repozytorium ani klienta.

SDK i wersje pakietów są przypięte. Proste okno Avalonia używa XAML i motywu Fluent;
MVVM zostanie wprowadzone wraz ze stanem i operacjami użytkownika, bez dodatkowego
frameworka na potrzeby statycznego ekranu.

## Konsekwencje i dalsze decyzje

Można budować i uruchamiać aplikacje bez działającego SQL Server. Testy sprawdzają
host API oraz rejestrację providera bez otwierania połączenia. Nie dowodzą działania
transakcji, uprawnień ani izolacji firm — to kolejne części M0.

Przed modelem danych i pierwszymi migracjami rozstrzygniemy ADR-005 (izolacja firm)
oraz opiszemy model i kryteria akceptacji. Każdy przyszły odczyt i zapis firmowy
będzie wymagał serwerowej autoryzacji CompanyId. Nie traktujemy filtra UI jako
zabezpieczenia. Przed publicznym wydaniem pozostają decyzje ADR-001 (nazwa) i
ADR-002 (licencja). Ten ADR nie zatwierdza MPL-2.0 ani nie rozstrzyga pozostałych ADR.

## Kryteria akceptacji tego etapu

- Solution buduje się na .NET 10; testy xUnit przechodzą bez SQL Server.
- API zwraca status HTTP 200 i dokument OpenAPI w Development.
- Aplikacja Avalonia otwiera polskie okno startowe bez dostępu do sieci i bazy.
- Rejestracja EF Core wybiera SQL Server i nie otwiera połączenia.
- README opisuje start w VS Code, wymagane narzędzia i ograniczenia etapu.

## Dokumentacja techniczna

- [Avalonia: rozpoczęcie pracy](https://docs.avaloniaui.net/docs/get-started/)
- [EF Core: konfiguracja DbContext](https://learn.microsoft.com/ef/core/dbcontext-configuration/)
- [EF Core: provider SQL Server](https://learn.microsoft.com/ef/core/providers/sql-server/)
