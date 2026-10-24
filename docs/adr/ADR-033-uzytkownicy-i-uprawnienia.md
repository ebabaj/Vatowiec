# ADR-033: Użytkownicy i uprawnienia instalacji

Data: 2026-10-24. Status: przyjęty w zakresie zlecenia użytkownika.
Wymagania: ORG-003/004/008, T-012, SPECYFIKACJA §3 i §11.1. Uzupełnia ADR-015/032.

Rozbudowujemy istniejący UserManager, sesje i CompanyAccess w System. Dodajemy
IsBlocked i MustChangePassword (domyślnie false dla istniejących kont) oraz
audit.UserAdministrationChanges bez sekretów, hashy, tokenów i stempli Identity.
Brak dostępu oznacza brak CompanyAccess. Katalog praw wynika z CompanyPermission;
serwer waliduje zależności, UI prezentuje polskie nazwy i wyjaśnienia.

InstallationAdministrator jest sprawdzany na nowo przy zapisie, wewnątrz
transakcji. Wspólna blokada transakcyjna administracji serializuje operacje;
ConcurrencyStamp użytkownika i RowVersion istniejących przypisań wykrywają
nieaktualne formularze. Zapis praw, SecurityStamp, unieważnienie sesji i audyt
są atomowe. Odebranie praw nie czeka na termin cookie. Audyt chroni kontekst EF
i trigger SQL. Nie udostępniamy edytora ról globalnych; pierwszy/dalszy administrator
nadal przez istniejące lokalne polecenie. Blokada własna i ostatniego aktywnego
administratora jest zabroniona. Rola instalacji nie daje praw do danych firm.

Hasło tymczasowe jest losowane kryptograficznie i walidowane polityką Identity.
Domyślnie ma 16 znaków z alfabetu bez I/i, L/l, O/o, 0 i 1, z wymaganymi klasami
w losowych pozycjach. Surowsze wymagania długości/unikalności Identity mają
pierwszeństwo. Wyświetlamy grupy po 4 znaki wyłącznie w prezentacji; do schowka
i logowania trafia hasło bez separatorów grupowania. Administrator może zamiast
generowania podać własne hasło, bez przycinania lub normalizacji, sprawdzane przez
te same CreateAsync/ResetPasswordAsync. Oba tryby zachowują MustChangePassword.
Odpowiedź jest jednorazowa, bez możliwości ponownego odczytu. Avalonia przekazuje
ją bezpośrednio do maskowanego okna z Pokaż/Ukryj i jawnym kopiowaniem, bez pola
hasła w VM. Po Ukryj kontrolka ponownie zawiera wyłącznie maskę. Własne hasło
jest wpisywane w krótkotrwałym oknie, czyszczonym po zamknięciu; DTO redagują
ToString. Nie zapisujemy treści ani wyboru hasła w preferencjach lub audycie.
Niepewna odpowiedź wymaga ponownego resetu, nigdy odtworzenia sekretu z audytu.
Po loginie hasłem tymczasowym istniejąca sesja pozwala tylko na CSRF/me/logout
i zmianę własnego hasła. Nie zapisujemy hasła tymczasowego w Credential Manager.
Zmiana własnego hasła wymaga bieżącego hasła, aktualizuje Identity i audyt oraz
unieważnia sesje. Klient wraca do logowania; dotychczasowe lokalne poświadczenie
nie jest zmieniane automatycznie. Standardowe HTTPS, CSRF i cookie pozostają.

seed-users tworzy wyłącznie brakujące konta razem z początkowymi przypisaniami.
Istniejące konta pomija w całości. Nie dodajemy polecenia resetu do zwykłego seeda.
Migracja addytywna wyłącznie System, jawnie po COPY_ONLY/CHECKSUM/VERIFYONLY;
żadnej zmiany baz firm, resetu kont ani migracji przy starcie API.

Akceptacja: tworzenie/reset/blokada, obowiązkowa zmiana własna, izolacja A/B,
walidacja zależności, odmowa administracji i starej sesji, ochrona administratora,
konflikt bez częściowego zapisu, niezmienny audyt i ponowny seed zachowujący stan.
Weryfikacja Debug, atrapy Avalonia i SQL/API wycofywane transakcją/savepointem.
