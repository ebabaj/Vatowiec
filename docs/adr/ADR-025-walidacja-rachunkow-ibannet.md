# ADR-025 — walidacja rachunków przez IbanNet

Data: 2026-10-23. Status: przyjęty i wdrożony na zlecenie użytkownika.
Wymagania: ORG-003/004/005/008, kartoteka kontrahentów; rozwinięcie ADR-018/020.

## Decyzja

Zastępujemy uproszczoną regułę `bank-text-v1` z ADR-020 adapterem oficjalnego
pakietu IbanNet 6.2.0 w Infrastructure. Wersja jest ustalona centralnie
w Directory.Packages.props. Application definiuje IBankAccountValidator i wynik
niezależny od biblioteki. Domain, Avalonia i kontrakty nie zależą od IbanNet.
Nie zmieniamy schematu SQL ani innych integracji.

Usuwamy separatory białych znaków, stosujemy wielkie litery. Dokładnie 26 cyfr
traktujemy jako polski NRB: tylko kopia do walidacji otrzymuje PL. Zapis zachowuje
26 cyfr. Zapis IBAN rozpoznajemy po dwóch literach kraju i przynajmniej jednej
cyfrze w dwuznakowym polu kontrolnym; rozpoznany krótki prefiks (np. PL) także
przekazujemy do walidacji. Sam początek DE w DEMO-LOCAL nie oznacza IBAN.
Tak rozpoznany numer podlega
regułom kraju, struktury i sumy kontrolnej biblioteki. Pozostałe formaty są
oznaczone jako spoza IBAN i niezweryfikowane. Nie ustalamy kraju z waluty ani
z danych kontrahenta. Błędny lub nieobjęty standardem numer wymaga ostrzeżenia,
a nie bezwarunkowej blokady. Ograniczenia długości kolumn i uprawnienia pozostają.
Poprawny wynik nie potwierdza istnienia rachunku, właściciela, banku ani BIC.

Zapis ponownie waliduje po stronie serwera. WarningHash wiąże wynik, wersję
reguł, binaria biblioteki (wersja informacyjna i MVID), dane, firmę, wykonawcę,
klucz operacji i rowversion. Zmiana reguł wymusza nowe świadome potwierdzenie.
AcceptWarnings nadal jest wymagane; zaakceptowane odstępstwo i wersja reguł
trafiają atomowo do istniejącego audytu. Skuteczne ponowienie już zapisanego POST
nadal zwraca jego trwały wynik, bez nowego rekordu i audytu. To nie jest nowy zapis.

API zwraca wynik również z odczytem rachunku. POST bank-accounts/validate
sprawdza firmę, kontrahenta, ViewContractors i CSRF; nie zapisuje danych.
Avalonia pokazuje wynik serwera obok numeru; zmiana numeru usuwa stary wynik
i potwierdzenie. Istniejące konflikty, izolacja i idempotencja pozostają.

## Aktualizacje i dystrybucja

Dependabot sprawdza co tydzień tylko IbanNet, najwyżej jeden PR. Brak
automatycznego zatwierdzania/scalania. Aktualizacja pakietu i reguł krajowych
wymaga testów i publikacji nowej wersji VATowca; brak pobierania reguł w runtime.
Licencja Apache-2.0 i informacja o autorze są kopiowane do build/publish programów
przez Directory.Build.targets. Sprawdzono nuspec, archiwum i oficjalny tag 6.2.0:
brak dodatkowego NOTICE. Nie zmieniamy licencji własnego kodu.

## Kryteria akceptacji

NRB/PL, DE/GB/FR, checksum, długość, kraj, znaki, non-IBAN i normalizacja;
SQL/API: potwierdzenie, zmiana reguł/danych, uprawnienia, audyt, retry, konflikty
i firmy A/B. Build, pełny runner oraz rzeczywista zakładka Avalonia. Wyników
formatu nie utożsamiamy z urzędową weryfikacją MF.

Źródła: [NuGet 6.2.0](https://www.nuget.org/packages/IbanNet/6.2.0),
[wydanie i licencja](https://github.com/skwasjer/IbanNet/tree/v6.2.0),
[Dependabot](https://docs.github.com/en/code-security/reference/supply-chain-security/dependabot-options-reference).
