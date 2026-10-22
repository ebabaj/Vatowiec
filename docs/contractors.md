# Kartoteka kontrahentów — model ADR-018

Wdrożono podstawę nowego modelu: int IDENTITY, ParentId bez dziedziczenia,
osobne adresy, kontakty i rachunki oraz lokalny słownik rodzajów adresów.
Decyzje biznesowe: [ADR-018](adr/ADR-018-docelowy-model-kontrahentow.md).
Rozstrzygnięcia techniczne i zakres wdrożenia: [ADR-019](adr/ADR-019-implementacja-modelu-kontrahentow.md).

## Uruchomienie i konta

Lokalne localhost\SQLEXPRESS zawiera Vatowiec_System oraz niezależne
Vatowiec_Company_A_Dev i Vatowiec_Company_B_Dev. W obu firmach są fikcyjne
pozycje DEMO, adres, kontakt i rachunek demonstracyjny, nieprzeznaczony do płatności.
Nie odtwarzamy dawnych GUID ani danych poprzedniego demonstratora.
API nie wykonuje migracji ani seeda przy starcie.

```powershell
dotnet build Vatowiec.slnx --no-restore
dotnet run --project tools/database/Vatowiec.DatabaseTools --no-build -- verify
dotnet run --project src/Server/Api --no-build --launch-profile https
```

W drugim terminalu:

```powershell
dotnet run --project src/Desktop --no-build
```

Domyślny adres klienta to https://localhost:7243; można zmienić go na ekranie
logowania lub przez VATOWIEC_API_URL. Certyfikat musi być zaufany; nie wyłączaj
weryfikacji HTTPS. Hasła pozostają w lokalnym user-secrets API lub środowisku,
poza repozytorium. Konfiguracja: [logowanie](authentication.md).

| Konto | Firma A | Firma B |
|---|---|---|
| operator.m0 | Profil, kartoteka, dodawanie i edycja | Profil i odczyt kartoteki |
| czytelnik.m0 | Tylko profil | Brak dostępu |

## Samodzielna próba

1. Zaloguj się jako operator.m0 lokalnym hasłem, wybierz firmę A i kliknij
   Pokaż kontrahentów. Nie wpisuj rzeczywistych danych.
2. Ctrl+N otwiera formularz. Wpisz kod TEST-001, nazwę „Kontrahent fikcyjny — próba”,
   kraj PL i pozostaw Aktywny. Ctrl+S zapisuje kontrahenta, audyt i wynik
   operacji tworzenia w jednej transakcji. Id nadaje SQL Server.
3. Wyszukaj TEST-001 i naciśnij Enter. Filtry kraju i aktywności oraz strony
   po 50 pozycji wykonują zapytania SQL przez API, bez pobierania całej kartoteki.
4. Zaznacz pozycję, naciśnij F2 lub Edytuj, zmień nazwę i zapisz Ctrl+S.
   Edycja wymaga rowversion. Kod do 64 znaków i nazwa do 256 są wymagane;
   kraj to dwie litery ASCII. Kod pozostaje unikalny w obrębie fizycznej bazy.
5. Odznacz Aktywny i zapisz. Pozycja pozostaje w bazie; wyszukaj ją filtrem
   Nieaktywni. Nie ma fizycznego usuwania z API ani klienta.
6. Zmień firmę na B. Pozycja z A nie pojawia się w B; przyciski edycji są ukryte.
   Serwer niezależnie od UI odrzuca próbę zapisu bez EditContractors.
7. Wyloguj się bez zamykania aplikacji. Czytelnik widzi wyłącznie profil A,
   bez kartoteki i bez dostępu do B.

Tab/Shift+Tab przechodzi po polach, Ctrl+F wybiera wyszukiwanie, Esc anuluje.
Przed zmianą firmy lub logout zapisz albo anuluj otwarty formularz.
401 usuwa sesję i dane, 403 usuwa kontekst firmy, a utrata sieci pozostawia
szkic do ponowienia. Szkic, hasło, cookie i CSRF nie są zapisywane w plikach.

TaxId jest tylko do podglądu; podstawowy formularz i API nie edytują TaxId,
VatEuId ani KsefIdWew. Brak walidatorów nie potwierdza poprawności numerów.
Obsługę ostrzeżeń i świadomej akceptacji wdrożymy przed udostępnieniem edycji
identyfikatorów. Nie ma jeszcze ekranów adresów, kontaktów ani rachunków.

## Konflikt dwóch użytkowników lub dwóch okien

Otwórz dwa okna Avalonia i zaloguj oba jako operator.m0 do A. W obu otwórz
ten sam kontrahent do edycji. W pierwszym zmień nazwę i zapisz. W drugim
wprowadź inną nazwę i spróbuj zapisać: pojawi się konflikt z porównaniem.
Druga próba nie nadpisuje pierwszej i nie tworzy audytu.

Wybierz jawnie wczytanie wersji serwera albo zachowanie własnych pól. W drugim
przypadku sprawdź porównanie i ponownie naciśnij Zapisz. Kolejna cudza zmiana
ponownie wywoła konflikt; nie ma force-update. Zapis bez zmiany nie tworzy historii.

## Zmiana kontraktu API

CompanyId pozostaje GUID w ścieżce /api/v1/companies/{companyId}/contractors.
Id kontrahenta w GET/PUT jest teraz dodatnim int, lokalnym dla bazy.
POST wymaga Id=0 i OperationId, np.:

```json
{
  "id": 0,
  "code": "TEST-API",
  "name": "Fikcyjna pozycja API",
  "countryCode": "PL",
  "isActive": true,
  "operationId": "d9555013-724a-469c-a6ab-d23d0b197c54"
}
```

Klucz operacji generuj raz dla konkretnej operacji tworzenia; przykład nie jest
stałym kluczem do wszystkich żądań. POST/PUT wymagają aktualnego cookie i CSRF,
a serwer przy każdej próbie sprawdza ViewContractors i EditContractors.

Pierwszy POST zwraca 201 z int Id i RowVersion. Ponów identyczny POST z tym samym
OperationId: otrzymasz 200 z pierwotnym DTO i Id, bez nowego rekordu/audytu.
Mechanizm jest trwały także po restarcie API. Inna treść zatwierdzonej operacji
daje 409 operation_payload_mismatch. Po utracie odpowiedzi klient zachowuje
OperationId; nie tworzy nowego klucza automatycznie.

PUT /{intId} wymaga tego samego Id w JSON, RowVersion ostatniego odczytu,
bez OperationId. Może przekazać ParentId; UI zachowuje istniejący ParentId.
Nieznany rodzic, wskazanie siebie lub cykl daje odmowę. Nie ma dziedziczenia.
Odpowiedź zawiera TaxId zamiast TaxIdentifier oraz VatEuId, KsefIdWew i ParentId.
Numery nie są polami zapisu — wysłanie ich w JSON powoduje 400. Wdrożenie
API i desktopu musi być wspólne; nie utrzymujemy zgodności starego demonstratora.

## Schemat i weryfikacja

W każdej firmie istnieją core.CompanyProfile, core.Contractors,
core.ContractorAddresses (14 kolumn), core.ContractorBankAccounts (12),
core.ContractorContacts (14, w tym RowVersion), core.ContractorCreateOperations,
ref.AddressTypes, audit.ContractorChanges oraz dbo.__EFMigrationsHistory.
Migracja firmowa: 20261022143310_CompanyModelV2. System zachowuje trzy migracje
katalogu/Identity/Installation i 13 tabel, bez kontrahentów.

Audyt zachowuje autora i historyczną nazwę po usunięciu konta centralnego.
Nie ma kaskad ani FK do użytkownika. Zmiana oraz audyt i wynik tworzenia są
atomowe pomimo kilku SaveChanges wewnątrz jednej jawnej transakcji.

Pełny przebieg odtwarza tylko trzy dozwolone bazy demonstracyjne. Zatrzymaj
API i desktop; bieżące fikcyjne rekordy i sesje zostaną usunięte:

```powershell
dotnet build Vatowiec.slnx --no-restore
dotnet run --project tools/database/Vatowiec.DatabaseTools --no-build -- test
```

Runner sprawdza instancję, dokładne nazwy, znacznik Vatowiec.LocalTestPurpose,
znany schemat/historię i oznaczenia fikcyjnych danych przed DROP. Niepotwierdzony
cel blokuje odtworzenie. Po testach inicjalizuje ponownie firmy i konta z lokalnej
konfiguracji. Nie tworzy baz *_Test ani nie dotyka baz innych aplikacji.

Wynik pełnego przebiegu: **128/128**, bez pominięć, w tym **57 SQL/API** i **71 bez SQL**.
Potwierdzono fizyczne kolumny/typy/klucze/FK, cykle (także równoczesne próby),
izolację A/B, int Id, rowversion, trwałe ponowienia, zakres aktora/firmy,
rollback audytu oraz indeks domyślności PLN/EUR/NULL. Test klienta z prawdziwym
API/SQL potwierdza dodanie, edycję, wyszukiwanie, zmianę firmy i logout.
Wyniki TRX znajdują się lokalnie w TestResults. Domyślne testy bez runnera
pomijają SQL; pominięcie nie jest zaliczeniem. Próba rzeczywistego okna Avalonia
jest sprawdzana osobno i nie wchodzi do licznika xUnit.

Próba rzeczywistego okna Windows UI Automation z 2026-10-22 przeszła: login,
utworzenie i edycja, wyszukiwanie, TaxId tylko do odczytu, izolacja B i logout.
SQL potwierdził fikcyjny GUI-V2-87D0EC24 z int Id=139 w A, dwa zdarzenia
Create/Update z SnapshotVersion=2 i wykonawcą operator.m0 oraz brak rekordu w B.
Id jest wynikiem tej próby, nie stałą wymaganą przez aplikację. Dodatkowa próba
HTTPS verify-users potwierdziła oba konta, CSRF, logout i odmowy 403 czytelnika.

Nie implementowano TaxIdentifierSchemes, ExternalVerifications, walidacji
podatkowej, integracji rejestrów/KSeF, dokumentów ani księgowania. M0 nie jest
jeszcze ukończone. Historyczne wyniki wcześniejszego modelu opisuje ADR-017.
