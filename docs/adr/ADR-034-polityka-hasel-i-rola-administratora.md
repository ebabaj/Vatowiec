# ADR-034: Polityka haseł instalacji i zarządzanie administratorem

Data: 2026-10-24. Status: przyjęty w zakresie jawnego zlecenia użytkownika.
Wymagania ORG-003/004/008, T-012; uzupełnienie ADR-015/033.

Jedna konfiguracja security.PasswordPolicy w System, początkowo włączona,
minimum 12 i cztery klasy znaków. Wyłączenie znosi długość i złożoność, ale
nigdy nie dopuszcza pustego hasła. Nie zmienia hashowania, blokad i sesji.
IPasswordValidator zastępuje domyślny walidator Identity i czyta świeżą konfigurację
przez scoped provider bez cache procesu. UserManager egzekwuje ją jednakowo przy
CreateAsync, ResetPasswordAsync i ChangePasswordAsync. Generator korzysta z tego
samego źródła; domyślnie 16 znaków z kryptograficznego generatora. Weryfikacja
istniejącego hasła nadal porównuje hash, bez oceny jego zgodności z nową polityką.

Zapis polityki wymaga roli z DB, CSRF, RowVersion i osobnego potwierdzenia
wyłączenia. Ustawienia i niezmienny audit.PasswordPolicyChanges zapisujemy atomowo,
bez sekretów. Zmiana nie aktualizuje kont, stempli ani sesji. Migracja jest
addytywna, tylko System, po backupie z CHECKSUM i VERIFYONLY. Bez resetów.

Edytor udostępnia wyłącznie istniejącą rolę administratora instalacji. Oddzielny
zapis i potwierdzenie, stempel użytkownika, audyt UserAdministrationChanges,
SecurityStamp i unieważnienie sesji są w jednej transakcji. Wspólny sp_getapplock
serializuje zmiany roli, blokady i inne zapisy administracyjne. Świeża weryfikacja
aktora następuje po uzyskaniu blokady. Zabrania się odebrania roli sobie oraz
ostatniemu aktywnemu administratorowi; nadanie wymaga aktywnego konta docelowego.
Rola nie dodaje CompanyAccess. Nie dodajemy edytora własnych ról.

Akceptacja: niezależne klasy, długość, wyłączenie z zachowaniem niepustego hasła,
działanie bez restartu w każdej ścieżce UserManager, zachowanie dawnych haseł,
potwierdzenia/wersje/uprawnienia/audyt, grant/revoke i natychmiastowa odmowa starej
sesji, ochrona administratora przy dwóch równoległych operacjach.
Wyłącznie standardowy Debug, bez nowych katalogów wyjściowych. SQL/API na
wycofywanych fixture, bez resetowania kont i baz.
