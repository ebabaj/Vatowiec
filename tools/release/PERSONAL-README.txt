VATowiec Personal @VERSION@ - Windows 10/11 x64

Wydanie zawiera klienta i lokalne API wraz z .NET 10. Nie wymaga SDK,
Visual Studio, terminala ani certyfikatu HTTPS. API używa prywatnego Named Pipe.

Wymagany SQL Server 2025 LocalDB 17.x NIE jest dołączony. Gdy już jest
zainstalowany, instalacja i praca są możliwe offline. Gdy go brakuje,
potrzebny jest Internet i osobna zgoda na instalację zależności Microsoftu.
Instalator pobiera przypięty skrótem SHA-256, podpisany bootstrapper Microsoftu.
Microsoft zgłasza błędną licencję CTP w niektórych MSI LocalDB 2025.
Nie akceptujemy jej automatycznie ani nie redystrybuujemy tego MSI.
W instalatorze Microsoftu wybierz Download Media -> LocalDB, następnie otwórz
pobrany MSI. Jeżeli warunki są nieprawidłowe, przerwij i wyjaśnij to z Microsoftem.

Po instalacji uruchom VATowiec Personal. Kreator sprawdzi LocalDB, przygotuje
nowe osobne bazy, poprosi o własny login/hasło administratora i nazwę firmy.
Nie ma domyślnych haseł ani kont demonstracyjnych.
W nowej instalacji wystarczy niepuste hasło. Złożoność, historia i ważność haseł
są początkowo wyłączone; administrator może je włączyć w ustawieniach.
MF (także WL2) i VIES rozpoczynają w PRODUKCJA, GUS w TEST. Zapytania wyłącznie
na żądanie użytkownika. W każdej nowej firmie aktywny tylko PLN, wszystkie flagi
pobierania A/B/C wyłączone. Istniejące ustawienia zakończonej instalacji pozostają.

Pliki programu: %LOCALAPPDATA%\Programs\VATowiec Personal
Dane: %LOCALAPPDATA%\Vatowiec\Personal (bazy, installation.json, Keys,
preferencje); hasła zapamiętane wyłącznie w Windows Credential Manager.
Nie kopiuj samego MDF jako kopii zapasowej działającej bazy.

Aktualizacja: wykonaj spójny backup SQL obu baz oraz bezpieczną kopię manifestu
i kluczy DPAPI w kontekście tego samego konta Windows. Zapisz szkice i zamknij
program. Uruchom nowy instalator. Instalator nie migruje baz. Przy niezgodnym
schemacie program odmawia pracy bez zmiany danych. Wymagana osobna procedura
aktualizacji baz z backupem, zgodna z instrukcją konkretnego wydania.
Ta wersja nie wprowadza nowych migracji ani automatycznej aktualizacji schematu.

Odinstalowanie usuwa wyłącznie pliki programu i jego skróty. Zachowuje bazy,
manifest, klucze, preferencje i poświadczenia; nie usuwa współdzielonego LocalDB.
Ponowna instalacja na tym samym koncie Windows korzysta z dotychczasowych danych.
Network i jego dane nie są aktualizowane przez instalator Personal.

Preview: przed użyciem na czystym Windows wymagany jest test instalacji LocalDB
i zależności natywnych na osobnej maszynie. Instalator VATowca nie ma jeszcze
podpisu Authenticode, więc Windows może pokazać ostrzeżenie SmartScreen.
