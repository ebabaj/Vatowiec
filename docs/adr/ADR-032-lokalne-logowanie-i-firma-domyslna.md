# ADR-032: Opcjonalne zapamiętywanie logowania i firmy domyślnej na Windows

Data: 2026-10-24  
Status: przyjęty na jawne zlecenie użytkownika.  
Wymagania: ORG-003, ORG-008, UX §10, bezpieczeństwo §11.1.  
Uzupełnia ADR-015/016; zastępuje wyłącznie dotychczasowy zakaz opcjonalnego
zapamiętywania hasła w kliencie. Bez zmian uwierzytelniania serwera i SQL.

Aktualizacja [ADR-035](ADR-035-historia-hasel-i-archiwizacja-tozsamosci.md):
preferencje zawierają niezmienny UserId zwrócony przez API. Credential Manager
używa przestrzeni v2 z dodatkowym UserId. Opisana poniżej v1 jest historyczna:
nie używamy jej do logowania, a dawne preferencje bez UserId tracą autostart
i firmę domyślną oraz wymagają ręcznego logowania. Niezgodność UserId po loginie
unieważnia nową sesję klienta i usuwa dawne poświadczenie/preferencje, zanim
pobierzemy listę firm. Przywrócenie tej samej osoby zachowuje UserId, a nowa
osoba wykorzystująca ten sam login zawsze otrzymuje inną tożsamość.

## Decyzja

Hasło może być zapisane dopiero po udanym Login() i wyłącznie przez
ILoginCredentialStore w Windows Credential Manager. Adapter korzysta z
CredWriteW/CredReadW/CredDeleteW, CRED_TYPE_GENERIC i CRED_PERSIST_LOCAL_MACHINE:
wpis dotyczy bieżącego użytkownika Windows na tym komputerze, bez roamingu.
Nie ma zapisu hasła do JSON/XML/rejestru/appsettings/user-secrets ani zastępczego
magazynu w razie awarii. Obie opcje — zapamiętanie i autologowanie — są domyślnie
wyłączone. Interfejs zaleca je wyłącznie na zaufanym koncie Windows.

Nazwa wpisu ma przestrzeń Vatowiec:DesktopLogin:v1 i SHA-256 jednoznacznej pary
kanoniczny HTTPS origin + login znormalizowany do wielkich liter. Origin zawiera
port; alias innego hosta/portu jest innym celem. Stosujemy istniejącą walidację
adresu; nie dopuszczamy userinfo, ścieżek, query, fragmentów, HTTP i przekierowań.
Odczyt weryfikuje również nazwę celu i użytkownika wpisu. Bufory natywne hasła
są zerowane przed zwolnieniem. Istniejący transport wymaga krótkotrwałego string;
nie przechowujemy go w polu/właściwości ViewModelu, diagnostyce ani audycie.

ILoginPreferencesStore przechowuje tylko wersję preferencji, ostatni profil,
adres API, login, flagi i opcjonalny CompanyId GUID. Plik jest poza repozytorium:
%LOCALAPPDATA%\Vatowiec\Desktop\login-preferences.json, z atomową podmianą pliku.
Profile są osobne dla każdej pary API/login. VATOWIEC_API_URL ma pierwszeństwo
przed zapamiętanym adresem; zmiana celu nie przenosi do niego hasła poprzedniego
profilu. Zmiana pól formularza nie wykonuje żadnego żądania w tle.

## Przebieg i bezpieczeństwo

Jednorazowa inicjalizacja okna odczytuje preferencje. Bez autologowania formularz
pozostaje ręczny. Sprawdzenie obecności wpisu nie tworzy stringa hasła; maska
„••••••••” jest wyłącznie PlaceholderText pustego pola, obok statusu „Hasło
zapisane na tym komputerze”. Nie trafia do Text ani do API. Sam checkbox nie
oznacza istnienia hasła. Stan jest odświeżany przy zmianie API/loginu, usunięciu
poświadczenia i aktywacji okna. Zmiana celu czyści też wpisane hasło.
Jawny przycisk logowania może użyć rzeczywistego poświadczenia przy pustym polu,
niezależnie od checkboxa. Nowe wpisane hasło ma pierwszeństwo; przy zaznaczonym
zapamiętywaniu zastępuje poświadczenie dopiero po udanym logowaniu.
Autologowanie odczytuje poświadczenie dokładnego celu i wywołuje istniejące
Login(): nowe cookie w pamięci i standardowe /csrf oraz /login, bez odtwarzania
sesji z dysku. Po każdym udanym logowaniu, także ręcznym, wspólna ścieżka pobiera
świeżą listę firm. Jeśli ustawiony GUID nadal jest na liście, używa wspólnej
logiki OpenCompany() i serwer ponownie sprawdza uprawnienia. Checkbox autologowania
steruje wyłącznie próbą podczas startu. Odczyt listy i otwarcie firmy pozostają
w jednej granicy zajętości istniejącego Run, bez równoległego otwierania.
W przeciwnym razie pokazuje wybór firmy. Nigdy nie wybiera pierwszej pozycji.
Ręczne otwarcie firmy nie zmienia domyślnej. Zmień firmę działa jak wcześniej.

Błąd logowania/magazynu pokazuje ręczny formularz. Nie ma automatycznych ponowień,
timerów logowania ani ponownego logowania po wygaśnięciu sesji. Stan zajętości
serializuje start, przycisk login i otwieranie firmy. Brak/odmowa dostępu do
domyślnej firmy nie uruchamia wyboru innej ani nie nadaje żadnych praw.

Wylogowanie wyłącza autostart profilu także przy awarii API lub wygaśnięciu sesji,
zachowując możliwość ręcznego użycia poświadczenia. Nadal próbuje unieważnić sesję
serwerową i zawsze czyści lokalny kontekst. Jeśli nie można zapisać wyłączenia,
usuwa poświadczenie, żeby uniemożliwić automatyczne wysłanie go przy następnym
starcie; jeśli oba magazyny odmawiają, jawnie informuje o niepowodzeniu.
Zapomnij dane logowania wyłącza autostart i usuwa wyłącznie wskazany wpis
Credential Manager, zachowując konta, prawa i preferencję firmy domyślnej.

## Weryfikacja

Testy z atrapami obu magazynów: sukces/błędy, per-serwer/login, dawna firma
domyślna, świeże prawa i odczyt B, brak arbitralnego wyboru, jednorazowy start,
CSRF/HTTPS przez istniejący transport, logout/expiry/forget i awarie magazynów.
Osobny test Windows zapisuje/odczytuje/usuwa wyłącznie unikalne fikcyjne
poświadczenie. Test pliku sprawdza dozwolone niesekretne pola i uszkodzone dane.
Rzeczywiste okno jest sprawdzane z atrapą API i odrębnym profilem testowym,
z restartem procesu, bez odczytu istniejących haseł i bez SQL.

Kontrakt Microsoft: [CREDENTIALW i zakres trwałości](https://learn.microsoft.com/en-us/windows/win32/api/wincred/ns-wincred-credentialw),
[CredReadW](https://learn.microsoft.com/en-us/windows/win32/api/wincred/nf-wincred-credreadw),
[CredWriteW](https://learn.microsoft.com/en-us/windows/win32/api/wincred/nf-wincred-credwritew),
[CredDeleteW](https://learn.microsoft.com/en-us/windows/win32/api/wincred/nf-wincred-creddeletew).
