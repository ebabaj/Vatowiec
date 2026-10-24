VATowiec @VERSION@ - klient Windows x64
Projekt: Otwarty następca VATowca

To wydanie poglądowe kartoteki i administracji, nie gotowy program księgowy.
Klient zawiera środowisko .NET i Avalonia. Nie wymaga SDK, Visual Studio ani
lokalnego SQL Server. Wymaga dostępnego, zgodnego serwera API VATowca.

Przy logowaniu wpisz adres HTTPS API otrzymany od administratora.
localhost oznacza komputer, na którym działa klient. Na drugim stanowisku
użyj rzeczywistej nazwy DNS serwera. Certyfikat musi być zaufany i zgodny
z tą nazwą. Nie ignoruj błędów certyfikatu.

Hasło może być zapamiętane wyłącznie w Windows Credential Manager.
Niesekretne preferencje: %LOCALAPPDATA%\Vatowiec\Desktop.
Aktualizacja i odinstalowanie zachowują te dane. Przed przekazaniem konta
Windows innej osobie użyj w programie opcji „Zapomnij dane logowania”.

Licencje zależności i informacje o autorach znajdują się w katalogu licenses.
Licencja własnego kodu projektu pozostaje decyzją otwartą (ADR-002);
to lokalny preview, bez deklaracji udzielenia nowej licencji na kod projektu.
