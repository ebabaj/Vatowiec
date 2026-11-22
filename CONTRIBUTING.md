# Współtworzenie projektu

Dziękujemy za zainteresowanie projektem **Otwarty następca VATowca** (nazwa
robocza). Projekt jest na etapie M0 i nie jest jeszcze programem do prowadzenia
księgowości.

## Najpierw przeczytaj

- [SPECYFIKACJA.md](SPECYFIKACJA.md) — wymagania, identyfikatory (np. `ORG-010`),
  etapy M0–M4 i definicja ukończenia zadania (§16).
- [AGENTS.md](AGENTS.md) — obowiązujące zasady architektury, danych i środowiska
  developerskiego. Dotyczą one ludzi i agentów AI tak samo.
- [docs/adr/](docs/adr/) — decyzje architektoniczne. Zmiana architektury wymaga
  nowego ADR z kolejnym numerem.

## Zasady zmian

1. Każda zmiana odwołuje się do identyfikatora wymagania lub do ADR.
2. Zmiany dotyczące księgowania, prawa podatkowego lub modelu danych zaczynają się
   od krótkiej propozycji z kryteriami akceptacji, zanim powstanie kod.
3. Klient nie łączy się z SQL Server; dane, autoryzacja i księgowanie należą do API.
   Każda firma ma osobną bazę (database-per-company).
4. Kwoty liczymy na `decimal`. Reguły podatkowe mają wersję, okres obowiązywania,
   źródło i testy. Nie zgadujemy stawek, dat ani pól XML.
5. Migracje są jawne; start API nie migruje ani nie resetuje baz. Migracja SQL
   wymaga opisu wdrożenia i ryzyka niezgodności danych.
6. Nie dodajemy sekretów, prawdziwych danych podatników ani danych osobowych do
   repozytorium, testów ani logów.
7. Nie kopiujemy kodu, ikon, tekstów ani innych chronionych zasobów oryginalnego
   VATowca. Nowa zależność wymaga sprawdzenia licencji i wpisu w `licenses/`.

## Środowisko

Wymagany jest .NET SDK zgodny z [global.json](global.json). Pełne instrukcje
uruchomienia API, klienta Avalonia i lokalnego SQL Server są w [README.md](README.md).
Budujemy wyłącznie standardową konfigurację Debug, bez własnych `OutputPath`.

```powershell
dotnet restore Vatowiec.slnx
dotnet build Vatowiec.slnx -c Debug --no-restore
dotnet test Vatowiec.slnx -c Debug --no-build --filter "FullyQualifiedName!~Personal"
dotnet format Vatowiec.slnx --verify-no-changes --no-restore
```

Pliki tekstowe mają kodowanie UTF-8 bez BOM i końce linii LF
([.editorconfig](.editorconfig), [.gitattributes](.gitattributes)).

## Testy

Testy bez SQL uruchamiają się zawsze. Pozostałe są jawnie włączane zmiennymi
środowiskowymi i bez nich są **pomijane**. Pominięty test nie jest zaliczony.

| Zmienna | Zakres |
|---|---|
| `VATOWIEC_DATABASE_PER_COMPANY_SQL=1` i `VATOWIEC_DEMO_RESET_AUTHORIZED=1` | Testy niszczące na tymczasowych bazach `*_Test`; ustawia je (wraz z pozostałymi flagami SQL) runner `DatabaseTools test`, który tworzy i usuwa te bazy |
| `VATOWIEC_PRESERVED_VAT_SQL=1` | Niedestrukcyjne testy SQL z wycofywanymi transakcjami |
| `VATOWIEC_PROVISIONING_SQL=1` | Tworzenie firmy i kopie: tworzy i usuwa własne bazy i podkatalogi kopii (`VATOWIEC_BACKUP_ROOT`, domyślnie `C:\ProgramData\Vatowiec\Backups`), zmiany w System wycofuje |
| `VATOWIEC_REAL_UI=1` | Natywne testy okna Avalonia (Windows) |
| `VATOWIEC_PERSONAL_SETUP_TESTS=1` | Testy LocalDB edycji Personal (prace wstrzymane) |

Pełny przebieg SQL opisuje [docs/database-per-company.md](docs/database-per-company.md#testy).

## Ciągła integracja

[CI](.github/workflows/ci.yml) na każdym pushu do `main` i każdym pull requeście
buduje Debug, uruchamia testy bez SQL (bez wstrzymanych testów Personal),
sprawdza formatowanie i skanuje zależności pod kątem znanych podatności.
Zmiana musi przejść CI.

## Commity i pull requesty

- Tytuły i opisy commitów piszemy po polsku.
- Tytuł zaczyna się od numeru ADR w formacie trzycyfrowym, np.
  `ADR-042: zapamiętywanie kolejności i szerokości kolumn`. Nie wymyślamy numerów;
  zmiana bez ADR ma polski opis bez numeru.
- Opis pull requesta podaje wymaganie/ADR, wykonane testy oraz to, czego nie
  udało się sprawdzić.

## Bezpieczeństwo

Podatności zgłaszaj wyłącznie prywatnie, zgodnie z [SECURITY.md](SECURITY.md),
a nie w publicznych zgłoszeniach.

## Licencja

Wnosząc zmianę, zgadzasz się na jej udostępnienie na licencji [MIT](LICENSE).
