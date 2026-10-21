# ADR-016: Klient Avalonia — logowanie, wybór firmy i odczyt kartoteki

Data: 2026-10-21  
Status: przyjęty w zakresie zleconego scenariusza M0.  
Wymagania: ORG-003, ORG-005 (odczyt), ORG-008, UX §10, M0/E04.  
Uzupełnia ADR-013, ADR-014 i ADR-015. Nie zmienia schematu baz ani zasad API.

## Decyzja

Desktop zależy wyłącznie od SharedContracts. Adapter VatowiecApiClient obsługuje
istniejące endpointy przez HttpClient. SystemDbContext, CompanyDbContext,
Identity i resolver pozostają na serwerze; klient nigdy nie otrzymuje połączeń SQL.
DTO w SharedContracts opisują odpowiedzi istniejącego kontraktu JSON.

Wprowadzamy MainViewModel z INotifyPropertyChanged oraz trzy stany widoku:
logowanie, wybór firmy, praca w firmie. Proste zdarzenia okna wywołują operacje
modelu; nie dodajemy frameworka MVVM. Hasło nie jest właściwością modelu ani
konfiguracji: kontrolka czyści je przed oczekiwaniem na login. Transport używa
go wyłącznie w treści żądania HTTPS. Brak zapisu haseł, cookie i tokenów do plików.

Adres API pochodzi z VATOWIEC_API_URL (domyślnie https://localhost:7243) i można
zmienić go na ekranie logowania. Akceptowany jest wyłącznie adres HTTPS origin,
bez userinfo, query, fragmentu i ścieżki. HttpClientHandler zachowuje standardową
walidację certyfikatów, ma własny CookieContainer w pamięci i nie śledzi
przekierowań. Nowe logowanie tworzy nowy transport i magazyn cookie.

Login i logout poprzedza pobranie tokenu CSRF z cookie. Logout otrzymuje nowy
token dla aktualnej tożsamości. Nie zmieniamy HTTPS/HttpOnly/SameSite na serwerze,
nie dodajemy wyjątków CSRF, CORS ani alternatywnego uwierzytelniania desktopu.

## Firma i błędy

Po login klient pobiera aktualną listę firm i uprawnień. Otwierając firmę,
odczytuje jej profil przez API; resolver serwera weryfikuje bazę. Klient dodatkowo
odrzuca odpowiedzi profilu/kontrahentów z innym CompanyId. CompanyId występuje
wyłącznie w ścieżce operacji; nie ma globalnego przełączania bazy na serwerze.

Zmiana firmy usuwa profil i listę kontrahentów przed pobraniem listy firm.
Odświeżenie kartoteki najpierw usuwa stare rekordy, także w razie błędu sieci.
Bez ViewContractors przycisk jest ukryty, model odmawia wywołania adaptera,
a API niezależnie kontroluje uprawnienie przy każdym żądaniu.

Operacje interfejsu są serializowane przez stan zajętości. Użytkownik nie może
zmienić firmy w trakcie pobierania danych. Po zakończeniu żądania ponownie
sprawdzamy termin sesji, aby opóźniona odpowiedź nie przywróciła wygasłych danych.
Timer interfejsu sprawdza absolutną datę sesji również podczas bezczynności.
401 usuwa całą sesję i dane; 403 usuwa kontekst firmy i wraca do wyboru;
503 i problemy sieci/certyfikatu mają polski komunikat oraz możliwość ponowienia.
Serwer sprawdza unieważnienie sesji przy następnym żądaniu; klient nie odpytuje
serwera w tle o zmiany uprawnień.

Udany logout unieważnia sesję serwerową zgodnie z ADR-015. Przy niedostępnym API
klient usuwa lokalną sesję i wyraźnie informuje, że nie potwierdził unieważnienia
na serwerze. Zamknięcie procesu usuwa cookie z pamięci; sesja serwerowa wygasa
w swoim terminie. Zamknięcie okna nie zastępuje potwierdzonego logout.

## Weryfikacja i granice

Testy bez UI sprawdzają transport, CSRF, błędy i przejścia modelu, w tym
opóźnioną odpowiedź, uprawnienia i czyszczenie danych. Testy z API i dedykowanymi
bazami SQL używają tego samego adaptera klienta i istniejącego serwera:
odczyt danych A/B, odmowy czytelnika, wygaśnięcie i serwerowe unieważnienie logout.
Osobno uruchamiamy rzeczywistą aplikację Windows i sprawdzamy kontrolki przez UI Automation.

Nie dodajemy edycji kontrahentów, księgowości, provisioningu firm ani zapamiętywania
logowania. Dotychczasowe migracje pozostają bez zmian, API nie migruje przy starcie.
To pierwszy scenariusz użytkownika, nie ukończenie całego M0.

Instrukcja: [desktop](../desktop.md).
Wiązania i INotifyPropertyChanged: [dokumentacja Avalonia](https://docs.avaloniaui.net/docs/data-binding/introduction-to-data-binding).
