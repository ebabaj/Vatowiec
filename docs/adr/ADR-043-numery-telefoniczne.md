# ADR-043: wspólna obsługa numerów telefonicznych

Data: 2026-11-02. Wymaganie: KON-TEL-001. Zakres: VATowiec Network.

## Kontekst

Jedynym edytowalnym polem telefonu w obecnym modelu biznesowym jest
`ContractorContact.Phone`: domena, `DetailValues`, formularz kontaktu Avalonia
i endpointy POST/PUT kontaktów. SQL już przechowuje `nvarchar(100) NULL`.
Identity ma dziedziczone `PhoneNumber`, ale bieżące formularze i endpointy
administracji użytkowników go nie udostępniają. Nie dodajemy nowego pola ani API.

## Decyzja

Używamy oficjalnego pakietu NuGet libphonenumber-csharp 9.0.42, Apache-2.0,
z metadanymi Google libphonenumber. Wersja jest przypięta w centralnym props;
licencja i informacja o autorach trafiają do standardowych wyników budowania.
Aktualizacja metadanych wymaga aktualizacji pakietu i ponownego uruchomienia testów.

`PhoneNumberService` w SharedContracts jest jedynym miejscem reguł parsowania,
walidacji, normalizacji i prezentacji. Ten projekt zawiera już wspólne walidatory
formularzy używane przez API i Desktop. Serwis jest bezstanowy i nie wymaga
zależności od HTTP, Avalonia, SQL ani Domain. Nie tworzymy drugiej implementacji
serwerowej ani ręcznych planów numeracji.

- Domyślny region wejścia: PL, niezależnie od kraju adresu kontaktu.
- Poprawny nowy lub zmieniony numer: E.164 w żądaniu desktopu i w zapisie API.
  API ponownie waliduje wejście, niezależnie od klienta.
- Numer regionu PL: krajowa część znacząca w UI, bez separatorów. Pozostałe
  regiony: pełne E.164, również w formularzu otwartym z API i na liście kontaktów.
  Format biblioteki zachowuje włoskie zero po +39.
- Puste pole: dozwolone, nowy pusty zapis staje się NULL.
- Wynik rozróżnia Empty, Valid, Invalid i Ambiguous. Metadane rozstrzygają
  długość i strukturę. Nieznany kod kraju, numer lokalny bez pełnego kontekstu,
  kilka numerów lub numer wewnętrzny wymagają jednoznacznego wejścia.
- Dopuszczamy cyfry ASCII, początkowy + i zwykłe separatory prezentacyjne.
  Nie tłumaczymy liter na cyfry ani nie łączymy kilku numerów. Błędny tekst
  pozostaje w formularzu i otrzymuje komunikat po polsku. Nie ma automatycznego
  obejścia walidacji przez AcceptWarning.

## Historia, współbieżność i zapis

Nie wykonujemy migracji ani zapisu przy odczycie. `DetailEditor` pamięta wartość
SQL i formatuje tylko jej prezentację. Niezmieniony numer wraca w tej samej
postaci, nawet gdy historyczna wartość zawiera spacje, jest błędna lub pusta.
Zmiana samej notatki nie przepisuje historycznego telefonu.

Serwer nie ufa klientowi w sprawie zachowania historii: pod blokadą istniejącej
transakcji pobiera aktualną encję, sprawdza rowversion i dopiero porównuje telefon
z faktyczną wartością SQL i jej prezentacją. Zmieniony telefon musi przejść
normalną walidację. Zachowujemy uprawnienia, CSRF, idempotencję POST i atomowy
audyt. Odczyt → prezentacja → PUT bez zmian nie powoduje nowej wersji ani audytu.

## Rozstrzygnięcie przykładu niemieckiego

`+49501234567` z tabeli zlecenia jest odrzucany strukturalnie przez
`IsValidNumber` metadanych 9.0.42. Użytkownik potwierdził zachowanie ścisłej
walidacji metadanych i poprawienie przykładu. Zachowano tę walidację;
test obejmuje jego odrzucenie, a poprawnym niemieckim przykładem jest
`+4930123456`. Historyczna wartość `+49501234567` pozostaje widoczna z +49
i nie jest automatycznie zmieniana. Dopuszczenie nowych numerów wyłącznie
możliwych długościowo wymaga osobnej decyzji: osłabiłoby wymaganą kontrolę
struktury. Nie wprowadzono wyjątku dla jednego numeru ani planu niemieckiego.

## Konsekwencje

Brak zmiany modelu i schematu SQL. Masowa normalizacja historii, jeśli będzie
potrzebna, wymaga osobnego podglądu, raportu błędów/niejednoznaczności i jawnej
decyzji użytkownika. Samo dodanie wspólnego serwisu takiej migracji nie uruchamia.
Walidacja jest lokalna: nie sprawdzamy operatora, aktywności ani właściciela.
Nie tworzono instalatora, paczki Release ani publikacji.
