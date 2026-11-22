# ADR-045: nazwy baz firm z nazwy firmy oraz usuwanie firmy

Data: 2026-11-22. **Status: PRZYJĘTY — decyzje właściciela z 2026-11-22.**
Zmienia [ADR-044](ADR-044-provisioning-i-backup-firmy.md) w części „nazwę bazy wyznacza
serwer z identyfikatora firmy”. Pozostałe zasady ADR-044 (osobna usługa VATowiec
Operations, etapy, dzierżawa, znaczniki własności, brak przejmowania obcych baz) obowiązują.
Wymagania: ORG-010, SPECYFIKACJA §3.4 i §11.3.

## Kontekst

Krok 1 ADR-044 nadawał bazom nazwy `Vatowiec_Company_{CompanyId}`. Właściciel chce czytelnych
nazw (np. `Vatowiec_Tenjin`) oraz możliwości usunięcia firmy z instalacji — scenariusz biura
rachunkowego, które po zakończeniu umowy i przekazaniu klientowi dokumentów i kopii usuwa
wszystkie dane klienta. Nazwa firmy musi być wielokrotnie używalna (np. „DOMAGAŁA” mąż,
potem żona, potem sp. z o.o. — kolejno, nie jednocześnie).

## Decyzje właściciela

### Nazwa bazy

1. Prefiks `Vatowiec_` jest stały i taki sam dla bazy systemowej (`Vatowiec_System`) i baz
   firm. Ewentualna konfiguracja prefiksu przy instalacji — poza obecnym zakresem.
2. Program pyta tylko o nazwę firmy. Nazwę bazy wyznacza serwer automatycznie:
   polskie i inne znaki diakrytyczne → litery łacińskie (ł → l), spacje i pozostałe znaki
   spoza `A–Z a–z 0–9` → `_`, bez powtórzonych i skrajnych `_`, najwyżej 100 znaków po
   prefiksie. Przykład: „DOMAGAŁA sp. z o.o.” → `Vatowiec_DOMAGALA_sp_z_o_o`.
3. Gdy nazwa bazy jest zajęta (rejestracja w katalogu, niezakończone zlecenie albo istniejąca
   baza na serwerze; porównanie bez rozróżniania wielkości liter), program informuje
   **„Nazwa zajęta…”** i nie tworzy firmy; administrator podaje inną nazwę. Bez automatycznych
   sufiksów.
4. Nazwy zastrzeżone (np. `Vatowiec_System`, `Vatowiec_Dev`, bazy demonstracyjne i testowe,
   prototypy Personal) są zawsze „zajęte”.
5. Nazwa bazy powstaje przy tworzeniu firmy i nie zmienia się przy późniejszej zmianie nazwy
   firmy. Bezpieczeństwo nadal zapewniają: ścisły wzorzec nazwy, wyznaczanie po stronie
   serwera i znaczniki własności bazy.
6. Istniejąca baza firmy Tenjin otrzymuje jednorazowo nazwę `Vatowiec_Tenjin`
   (operacja na bazie developerskiej po sprawdzeniu znaczników).

### Usuwanie firmy (do wdrożenia po ADR-044 krok 2)

1. Usuwa administrator instalacji w Ustawieniach instalacji. Wykonuje usługa VATowiec
   Operations (DROP wymaga jej tożsamości).
2. Kreator pyta o **kopię archiwalną**, domyślnie TAK. Kopia trafia do osobnego katalogu
   archiwum na serwerze, nie podlega rotacji i nie jest pobierana przez aplikację.
   Baza jest usuwana dopiero po udanej i zweryfikowanej kopii.
3. Potwierdzenie: jedno zwykłe pytanie „Czy na pewno chcesz usunąć firmę?”. Gdy kopia
   archiwalna nie będzie wykonana — dodatkowe ostrzeżenie o nieodwracalności.
4. Usunięcie czyści **wszystko**: bazę SQL, wpis w katalogu, prawa użytkowników
   (`CompanyAccess`), historię operacji technicznych tej firmy i wpisy audytu administracji
   dotyczące tej firmy. Wyjątek od niezmienności tych historii jest kontrolowany: wykonuje go
   wyłącznie usługa operacji w ramach zlecenia usunięcia.
5. Zostaje wyłącznie rejestr usuniętych firm: `CompanyId`, nazwa firmy, NIP i REGON (jeśli
   były w profilu), data usunięcia i kto usunął. `CompanyId` jest zablokowany — nie może
   zostać użyty ponownie poza **przywróceniem tej samej firmy z kopii archiwalnej**.
6. Nazwa firmy i nazwa bazy są po usunięciu ponownie dostępne.
7. Administrator instalacji może przywrócić usuniętą firmę z kopii archiwalnej pod tym samym
   `CompanyId`, z weryfikacją kopii. Przywracający dostaje pełne prawa (jak twórca); inni
   użytkownicy muszą otrzymać prawa ponownie. Jeżeli nazwa bazy jest w tym czasie zajęta,
   program informuje „Nazwa zajęta…”.

## Kolejność wdrożenia

1. Nazwy baz z nazwy firmy i zmiana nazwy bazy Tenjin — **wdrożone 2026-11-22**.
2. ADR-044 krok 2: kopia z rotacją i harmonogramem — **wdrożone 2026-11-22**.
3. Usuwanie firmy z kopią archiwalną i rejestrem usuniętych firm — **wdrożone 2026-11-22**
   ([instrukcja](../company-deletion.md)). Rozstrzygnięcia techniczne: firma jest
   dezaktywowana od początku usuwania; anulowanie możliwe do chwili usunięcia bazy;
   usuwane są też pliki zwykłych kopii firmy; archiwum w `{BackupRoot}\_archive\`;
   wyjątek od niezmienności historii przez `SESSION_CONTEXT` tylko w transakcji
   czyszczenia i tylko dla wierszy usuwanej firmy.
   Uzupełnienie decyzji właściciela (2026-11-22): przy usuwaniu z kopią archiwalną
   usuwana historia z System jest zapisywana do `….history.json` obok archiwum; firma
   jest dezaktywowana już przy przyjęciu zlecenia. Usunięcie **bez** archiwum startuje
   najwcześniej po 10 minutach (czas na anulowanie) — celowo nieopisane w instrukcji
   użytkownika ani w interfejsie (`CompanyOperationRequests.DeleteWithoutArchiveGrace`).
4. ADR-044 krok 3: odtworzenie istniejącej firmy oraz przywrócenie usuniętej.

## Konsekwencje i ryzyko

- Obowiązek przechowywania dokumentacji podatkowej spoczywa na podatniku. Usunięcie bez
  kopii archiwalnej jest nieodwracalne; program ostrzega o tym w potwierdzeniu.
- Czyszczenie audytu przy usunięciu firmy jest świadomym wyjątkiem od niezmienności historii
  (AGENTS.md §3). Dotyczy wyłącznie danych usuwanej firmy i jest wykonywane jednym,
  autoryzowanym zleceniem.
- Bazy utworzone wcześniej z nazwą `Vatowiec_Company_{guid}` pozostają poprawne (wzorzec je obejmuje).
