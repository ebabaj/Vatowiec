# ADR-035: Historia i ważność haseł oraz archiwizacja tożsamości

Data: 2026-10-24. Status: przyjęty w zakresie zlecenia użytkownika.
Wymagania: ORG-003/004/008, T-012; rozszerzenie ADR-015/032/033/034.

Historia i ważność są niezależne od złożoności, domyślnie wyłączone. Zachowujemy
do 20 zastąpionych hashy Identity na UserId, bez odtwarzania brakującej historii.
Włączenie blokady sprawdza aktualny hash oraz N ostatnich (1–20), przez wspólny
PasswordHasher. UserManager zapisuje hasło, datę UTC i historię w tej samej
transakcji; ponowne hashowanie podczas logowania nie jest zmianą hasła.
Nastawy pomocnicze domyślnie N=5 i ważność=90 dni (zakres 1–3650).

Ważność liczymy od późniejszej z dat: skutecznej zmiany hasła i włączenia
ważności. Nie wpisujemy fikcyjnych dat zmiany starym kontom. Każde żądanie
sesji sprawdza bieżącą politykę i przechodzi do istniejącego ograniczonego trybu
zmiany hasła, także po upływie terminu w trwającej sesji.

Archiwizacja nie usuwa konta ani audytu. Zachowuje historyczny login, czas
i administratora; odbiera role/przypisania, unieważnia sesje. Wszystkie operacje
administracyjne używają istniejącej blokady SQL, świeżej roli aktora, CSRF,
stempla współbieżności i atomowego audytu. Chronimy siebie i ostatniego administratora.

A przywraca tę samą osobę z tym samym UserId, nowym hasłem tymczasowym i bez praw.
B pozostawia stare konto w archiwum, zmienia jego nazwę techniczną, a login
przekazuje nowemu UserId bez dawnych praw, historii haseł i sesji. Dwa zdarzenia
audytu wskazują powiązane UserId, bez zmiany właściciela wcześniejszej historii.
Po B starego konta nie przywracamy przez A. Zwykłe tworzenie nigdy nie zwalnia
loginu. Seed nie reaktywuje archiwum ani nie przejmuje historycznych loginów.

API zwraca niezmienny UserId. ADR-032 wiąże z nim preferencje i nazwę wpisu
Credential Manager v2. Dawne wpisy bez tożsamości wymagają ręcznego logowania.
Zmiana UserId po uwierzytelnieniu odrzuca sesję i poprzednie preferencje, usuwa
stare poświadczenie, wyłącza autostart i wymaga nowego logowania przed firmą.

Migracja wyłącznie addytywna System po COPY_ONLY/CHECKSUM/VERIFYONLY. Bez zmian
baz firmowych, resetów kont i automatycznych migracji przy starcie. Testy SQL
z rollback/savepoint oraz natywna Avalonia; standardowy Debug, bez Commit/Push.
