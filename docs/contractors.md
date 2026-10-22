# Kompletna kartoteka kontrahentów w Avalonia

Podstawowe zakładki to **Dane główne, Adresy, Kontakty, Rachunki bankowe**;
ADR-021 dodaje piątą: **Weryfikacje**.
Korzysta wyłącznie z HTTPS API. Zachowane są logowanie, wybór firmy, uprawnienia,
rowversion, izolacja fizycznych baz i atomowy audyt.
Model biznesowy: [ADR-018](adr/ADR-018-docelowy-model-kontrahentow.md).
Technika modelu: [ADR-019](adr/ADR-019-implementacja-modelu-kontrahentow.md).
Bieżące API i formularz: [ADR-020](adr/ADR-020-kompletna-kartoteka-avalonia.md).
Rozszerzenie: [identyfikatory i zakładka Weryfikacje](identifiers-and-registries.md),
[ADR-021](adr/ADR-021-identyfikatory-i-rejestry-testowe.md).

## Uruchomienie

W katalogu projektu:

```powershell
dotnet build Vatowiec.slnx --no-restore
dotnet run --project src/Server/Api --no-build --launch-profile https
```

W drugim terminalu:

```powershell
dotnet run --project src/Desktop --no-build
```

Domyślny adres: https://localhost:7243, zmieniany na ekranie logowania lub przez
VATOWIEC_API_URL. Używaj zaufanego certyfikatu; nie wyłączaj walidacji HTTPS.
Hasła kont są wyłącznie w lokalnym user-secrets API lub środowisku.
Konfiguracja i uprawnienia: [logowanie](authentication.md).

| Konto | Firma A | Firma B |
|---|---|---|
| operator.m0 | Profil, pełna kartoteka, edycja i akceptacja ostrzeżeń | Profil i kartoteka tylko do odczytu |
| czytelnik.m0 | Wyłącznie profil | Brak dostępu |

## Dane główne i grupy

1. Zaloguj operator.m0, wybierz A i Pokaż kontrahentów.
2. Ctrl+N: wpisz np. kod TEST-GRUPA i nazwę „Grupa demonstracyjna — dane fikcyjne”,
   kraj PL, Aktywny. Ctrl+S zapisuje grupę bez identyfikatora podatkowego.
3. Utwórz drugiego kontrahenta z kodem TEST-KH. Zapisz, wyszukaj i otwórz F2
   lub dwuklikiem. Szkic nowego kontrahenta najpierw zapisuje się w danych
   głównych; adresy/kontakty/rachunki dodaje się do już zapisanej pozycji.
4. W sekcji nadrzędnego wyszukaj TEST-GRUPA po kodzie/nazwie, zaznacz wynik
   i kliknij Wybierz nadrzędnego. Zapisz dane główne. Nie wpisujesz int Id.
5. Możesz zmienić przypisanie albo kliknąć Usuń powiązanie i zapisać.
   Otwórz grupę: lista podmiotów pokazuje jej bezpośrednich członków ze stronami.
   Wybierz członka i Otwórz podmiot lub dwuklik.

Code, Name, CountryCode, IsActive i wybór nadrzędnego są edytowalne.
Kod ma do 64 znaków, nazwa do 256, kraj dwie litery ASCII; kod jest unikalny
w firmie. TaxId, VatEuId i KsefIdWew mają osobne etykiety; od ADR-021 są edytowalne
z podglądem normalizacji, regułami i świadomą akceptacją ostrzeżeń. Kontrola
formatu/sumy nie potwierdza rejestracji. Ręczne rejestry są domyślnie TEST.
Zmiana grupy nie kopiuje żadnych danych. Wskazanie siebie lub cyklu jest odrzucane.

## Adresy

W zakładce Adresy kliknij Nowy. Wybierz rodzaj (początkowo rejestrowy lub
korespondencyjny) i uzupełnij dowolne pola: nazwa adresu, ulica, budynek, lokal,
kod pocztowy, miejscowość, region i kraj. Zapisz pozycję. Można utworzyć wiele
adresów tego samego rodzaju. Lista pokazuje rodzaj, opis i status.

Preferowany dotyczy pary kontrahent–rodzaj. Zaznaczenie nowego preferowanego
automatycznie odznacza poprzedni tego samego rodzaju, atomowo z audytem obu
rekordów. Odznaczenie preferencji bez wyboru następnej jest dozwolone.
Aktywny można wyłączyć bez usuwania rekordu.

Nowy rodzaj dodasz z bocznego pola i przycisku Dodaj rodzaj. Nazwa jest wymagana,
do 100 znaków; istniejącej nazwy nie duplikujemy. Po dodaniu lista rodzajów jest
odświeżana. Operacja ma audyt oraz ochronę ponowienia po utracie odpowiedzi.

## Kontakty

W zakładce Kontakty kliknij Nowy. Wymagana jest tylko nazwa, np. „Księgowość”.
Możesz dodać sam e-mail poza nazwą, sam telefon, adres pocztowy albo dowolne
połączenie tych danych. Pozostałe pola to stanowisko/funkcja i uwagi.

Przykłady do samodzielnej próby:

- Księgowość — ksiegowosc@example.invalid, bez adresu i telefonu;
- Magazyn — Fikcyjna 2, Miasto testowe, bez telefonu i e-maila;
- Fikcyjna osoba — sama nazwa, bez pozostałych danych.

Nie wybierasz rodzaju kontaktu. Nie ma tabel osób/działów/miejsc ani CRM.
Adres kontaktu jest niezależny od kartoteki adresów. Kontakty można przeglądać,
dodawać i edytować, bez usuwania. Zatwierdzony model kontaktu nie ma IsActive.

## Rachunki bankowe

W zakładce Rachunki bankowe kliknij Nowy. Wpisz numer, opcjonalnie nazwę banku,
BIC/SWIFT, walutę, właściciela, nazwę własną i uwagi. Przykładowe, wyłącznie
fikcyjne numery do próby UI to 00000000000000000000000001–04. Nie są
przeznaczone do płatności i nie deklarujemy ich poprawności.

1. Dodaj rachunek PLN z Domyślny oraz rachunek EUR z Domyślny.
2. Dodaj dwa rachunki z pustą walutą, kolejno oznaczając je jako domyślne.
3. Otwórz pierwszy bez waluty: nie powinien już mieć znacznika. PLN i EUR
   zachowują własne oznaczenia.
4. Możesz odznaczyć domyślność bez wskazania następnego, dezaktywować rachunek
   lub edytować jego dane. Numery rachunków mogą się powtarzać.

Pusta waluta oznacza NULL, nie PLN. Jeden domyślny jest dozwolony dla każdej
waluty i osobnej grupy NULL. Zmiana wycofuje się w całości przy błędzie audytu
lub konflikcie; nie istnieje dodatkowa tabela rachunków domyślnych.

API usuwa białe znaki z numeru i zamienia litery na wielkie. Nietypowa długość
lub znaki wywołują ostrzeżenie. Spróbuj np. DEMO-?TEST. Program pokaże opis:
operator mający AcceptWarnings może kliknąć **Świadomie zapisz pomimo ostrzeżenia**.
Nie jest wymagane uzasadnienie. Potwierdzenie dotyczy dokładnie pokazanej wersji;
zmiana danych wymaga nowego potwierdzenia. Odstępstwo jest audytowane.

Ta podstawowa kontrola nie sprawdza sumy kontrolnej, kraju, białej listy ani
innego rejestru. Jej przejście ani potwierdzenie ostrzeżenia nie oznacza,
że rachunek jest prawidłowy lub został zweryfikowany urzędowo.

## Klawiatura, szkice i konflikty

Tab/Shift+Tab przechodzi po kontrolkach. Ctrl+N tworzy w aktywnej kartotece,
F2/dwuklik otwiera aktualne dane, Ctrl+S zapisuje, Esc anuluje pozycję lub zamyka
kartotekę. Przy zmianach pojawia się potwierdzenie porzucenia. Zamknięcie okna
również chroni niezapisany szkic. Nie utrwalamy szkiców, haseł, cookie ani CSRF
w plikach. Przewijaj formularz; przyciski zapisu i anulowania pozostają poza
przewijaną treścią. Nowe okno dopasowuje maksymalny rozmiar do obszaru roboczego.

Zapis danych głównych i zapis jednej pozycji szczegółu są odrębnymi operacjami.
Zapis szczegółu nie zapisuje automatycznie niezapisanych zmian głównych.
Po wygaśnięciu sesji lub odebraniu uprawnień szkic jest czyszczony dla ochrony danych.

Konflikt sprawdzisz w dwóch oknach zalogowanych do A. Otwórz ten sam adres,
kontakt albo rachunek i zmień go w obu. Zapisz pierwsze okno, następnie drugie:
pojawi się porównanie, a drugi zapis niczego nie nadpisze. Wczytaj serwer albo
jawnie zachowaj własne pola i ponownie zapisz. Nowa cudza zmiana wywoła nowy
konflikt. Nie ma force-update ani automatycznego zastępowania wersji.

Wyszukiwanie i stronicowanie listy głównej oraz szczegółów wykonuje API/SQL;
klient nie pobiera całej kartoteki. Można przeglądać i otworzyć B w trybie odczytu,
ale POST/PUT także z pominięciem interfejsu otrzymają odmowę.

## API, audyt i wdrożenie

W kontekście /api/v1/companies/{companyId}:

| Operacja | Ścieżka |
|---|---|
| Lista kontrahentów/grupy | GET /contractors z q/country/active/offset/limit oraz parentId/excludeId |
| Lista/szczegół adresu, kontaktu, rachunku | GET /contractors/{id}/{addresses,contacts,bank-accounts}[/{detailId}] |
| Dodanie/edycja szczegółu | POST kolekcji, PUT /{detailId} |
| Rodzaje adresów / dodawanie | GET/POST /address-types |

Wszystkie zapisy wymagają sesji, praw do firmy i czynności oraz świeżego CSRF.
POST szczegółu wymaga Id=0 i OperationId, PUT zgodnego int Id i RowVersion.
Nie przyjmujemy CompanyId ani ContractorId z Data; kontekst pochodzi z trasy.
Nie ma DELETE. Te same lokalne Id w innych bazach nie identyfikują tych samych danych.

ContractorChanges zawiera RecordType/RecordId, trwałego autora, czas, korelację
i stan przed/po; WarningJson opisuje świadome odstępstwo. Wszystkie zmienione
preferencje mają własny audyt. Wykorzystano istniejącą tabelę operacji z OperationKind,
bez dodatkowych tabel biznesowych. Powtórzony skuteczny POST zwraca ten sam
wynik i nie dopisuje rekordu ani audytu. Inna treść zatwierdzonej operacji daje 409.

Wdrożono jawnie 20261022161054_WarningPermission w System oraz
20261022161050_ContractorDetailAudit niezależnie w A i B. Poprzedziły je kopie
COPY_ONLY/CHECKSUM i VERIFYONLY. Nie odtwarzano baz ani tabel biznesowych.
API nie wykonuje migracji przy starcie. W etapie ADR-020 prawa operatora wynosiły
A=15, B=3; ADR-021 rozszerza A do 31 o VerifyRegistries, B nadal ma 3.
Nowe szczegóły nie zmieniają zatwierdzonych 14 kolumn adresów i 12 rachunków.

## Testy i granice

Zatrzymaj API i desktop. Pełny runner czyści **fikcyjne dane** wyłącznie w trzech
zweryfikowanych bazach, bez DROP/CREATE; po testach inicjalizuje demonstrator:

```powershell
dotnet build Vatowiec.slnx --no-restore
dotnet run --project tools/database/Vatowiec.DatabaseTools --no-build -- test
dotnet format Vatowiec.slnx --verify-no-changes --no-restore
```

Testy sprawdzają trzy kartoteki, słownik, domyślności PLN/EUR/NULL i współbieżne
zmiany preferencji, rowversion, ponowienia, błędy audytu, ostrzeżenia/uprawnienia,
CSRF, właściciela, A/B, grupy, trzy identyfikatory oraz ochronę szkicu w kliencie.
Weryfikacja 2026-10-22: **142/142 testy**, bez pominięć (67 SQL/API i 75 bez SQL).
Pełna kompilacja zakończyła się bez błędów i ostrzeżeń; kontrola
`dotnet format --verify-no-changes --no-restore` przeszła poprawnie.
Pominięte testy SQL bez runnera nie są zaliczane; raporty TRX są w TestResults.

W rzeczywistym oknie Avalonia operator.m0 utworzył w firmie A kontrahenta
**GUI-KH-BC96C2** (Id 737) i grupę **GUI-GRUPA-BC96C2**, przypisał grupę,
dodał własny rodzaj adresu, dwa adresy, dwa kontakty i pięć rachunków.
Przetestowano edycję i ponowny odczyt po zamknięciu formularza, domyślność
PLN/EUR/NULL, odznaczenie poprzedniego rachunku bez waluty i świadome
potwierdzenie ostrzeżenia dla fikcyjnego numeru. SQL potwierdził rekordy,
domyślności oraz audyt. W firmie B nie znaleziono tego kontrahenta i sprawdzono
otwieranie istniejącej kartoteki tylko do odczytu.

Trzy różne fikcyjne identyfikatory przygotowano jako dane testowe w bazie
i sprawdzono ich osobne pola tylko do odczytu w API/oknie; nie była to edycja
identyfikatorów przez formularz. Na Windows sprawdzono ekran 1920×1080 oraz
okno 940×650: widoczność zapisu, błąd przy pustej nazwie kontaktu i potwierdzenia
porzucenia szkicu przy anulowaniu oraz zamykaniu okna. Nie przeprowadzono
osobnej próby po zmianie skali DPI Windows ani na Linuksie.

Powyższe wyniki 142/142 i próba BC96C2 dokumentują wcześniejszy etap ADR-020.
Aktualne reguły, wyniki i próby rejestrów opisuje [ADR-021](identifiers-and-registries.md).
Nie wdrożono integracji KSeF, faktur, księgowania ani CRM. M0 pozostaje nieukończone.
