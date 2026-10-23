# ADR-026 — normalizacja rachunków i propozycje banku z EWIB

Data: 2026-10-23. Status: przyjęty i wdrożony na zlecenie użytkownika.
Wymagania: ORG-003/004/005/008; rozwinięcie ADR-025 i wspólnej walidacji z AGENTS.md.

## Rozwiązanie

IbanNet pozostaje w wersji 6.2.0 za IBankAccountValidator. Wspólna normalizacja
usuwa białe znaki Unicode, zmienia litery na wielkie i obsługuje jawną listę
wizualnych myślników: U+002D, U+2010–2015, U+2212, U+FE63, U+FF0D. Usuwanie
myślników dotyczy zapisów liczbowych lub przypominających IBAN (dwie litery i
dwie cyfry). Pozostałe formaty, np. LOCAL-123, zachowują myślniki, które mogą być
częścią identyfikatora. Inne znaki, np. ?, /, kropki, pozostają i nie są maskowane.
NRB zachowuje zapis bez PL, IBAN z PL. Wersja polityki zmienia się na bank-iban-v3,
co unieważnia niezapisane potwierdzenia starych ostrzeżeń; zapisany retry zachowuje
swój historyczny wynik zgodnie z ADR-025.

Tylko jawne Sprawdź numer może po poprawnym wyniku polskiego NRB/IBAN wykonać
zapytanie HTTPS do EWIB. Application definiuje niezależny port katalogu banków,
Infrastructure ma niewielki adapter HttpClient. Serwer wysyła wyłącznie osiem
cyfr numeru rozliczeniowego, nie pełny rachunek. Nie odpytuje EWIB przy zapisie,
odczycie listy ani w tle. Brak nowych tabel, cache i migracji. EWIB to publiczny
rejestr instytucji NBP; nie zmienia ustawień oficjalnych MF/GUS/VIES TEST/Mock.

Na podstawie oficjalnego Swaggera analizujemy listaWlascicieli, siedziba i jednostki,
wyłącznie numeryRozliczeniowe o numerze równym zapytaniu, a potem ich numeryBic.
Nigdy nie dobieramy BIC innej jednostki. Duplikaty tego samego BIC są scalane,
różne kody pozostają propozycjami. Kilku właścicieli lub różne nazwy oznaczają
brak jednoznaczności. Brak wyniku, błędna odpowiedź i niedostępność są rozdzielone;
nie blokują ręcznego zapisu. Parametr jest obowiązkowy i sprawdzany lokalnie,
ponieważ EWIB bez niego zwraca pełny wykaz. Stały host, brak przekierowań,
timeout 12 sekund, limit odpowiedzi 1 MiB, brak ponowień.

API zachowuje sesję, CSRF, ViewContractors, kontrolę firmy/profilu i kontrahenta.
Odpowiedź podglądu rozszerzamy o osobny wynik BankLookup; wynik walidacji numeru
pozostaje niezależny. Avalonia pokazuje znormalizowany numer, automatycznie
uzupełnia wyłącznie puste pola dla jednoznacznego wyniku. Istniejące inne wartości
wymagają jawnego użycia propozycji. Kilka BIC wymaga wyboru; bez wyboru brak zmiany.
Zmiana numeru usuwa propozycje. Dane trafiają do szkicu, a zwykły zapis API
ponownie waliduje i zapisuje je z istniejącym audytem, idempotencją i rowversion.
Nie potwierdzamy istnienia rachunku ani właściciela. Dla zagranicy brak zgadywania BIC.

## Kryteria akceptacji

Pięć przykładów użytkownika, Unicode i nieznane znaki; routing NRB/PL;
jedna nazwa/BIC, wiele BIC, brak BIC, brak banku, awaria/timeout/uszkodzona
odpowiedź; zachowanie ręcznych pól, jawny wybór i brak SQL do zapisu; uprawnienia,
izolacja i zapis z myślnikami bez podglądu. Automatyczne testy tylko z atrapami
HTTP, pełny runner SQL/API i rzeczywista Avalonia.

## Źródła i próba publicznego API

- [FAQ NBP](https://ewib.nbp.pl/faces/pages/faq.xhtml)
- [Swagger EWIB 2.0 API v1](https://ewib.nbp.pl/api/swagger.json)
- Jedno zapytanie kontrolne `zapytanie1?nrRozliczeniowy=10100000&format=json`
  2026-10-23: HTTP 200, Narodowy Bank Polski, numer 10100000 w siedzibie,
  BIC NBPLPLPWXXX dwukrotnie (etykiety BIC i BIC SEPA). Surowa publiczna odpowiedź
  tylko lokalnie w TestResults/ewib-live-response.json. Dalsze testy używają atrap;
  próba nie dotyczy żadnego rachunku klienta ani jego właściciela.

## Weryfikacja

Build: 0 błędów/ostrzeżeń. Pełny runner: 323/323 bez pominięć (103 SQL/API,
220 bez SQL). Nowe testy: 41 przypadków HTTP/normalizacji, 5 UI/ViewModel i 4 SQL/API.
Rzeczywista Avalonia z serwerem Kestrel HTTPS i atrapą HTTP EWIB: normalizacja NRB
i PL, autofill, własne wartości, jawne zastąpienie, wybór spośród kilku BIC,
brak BIC, brak zapytań dla błędnego/zagranicznego numeru, B tylko odczyt i logout.
Po podglądach każda baza ma wyłącznie swój seed rachunku, zero audytów i operacji
tworzenia; szkic anulowano. Publiczny test był osobny, dokładnie jeden.
