# IbanNet

VATowiec wykorzystuje niezmodyfikowany oficjalny pakiet NuGet **IbanNet 6.2.0**.
Autor: **Martijn Bodeman** (skwasjer). Metadane pakietu: Copyright © 2019.
Licencja: **Apache License, Version 2.0**; pełny tekst: [IbanNet/LICENSE.md](IbanNet/LICENSE.md).

- Pakiet: https://www.nuget.org/packages/IbanNet/6.2.0
- Źródło: https://github.com/skwasjer/IbanNet/tree/v6.2.0
- Commit wydania: 4c46768a0697aebaefd3afd73677ab3c0b158c3e
- Licencja pochodzi z LICENSE.md tego wydania. Pakiet deklaruje Apache-2.0
  w nuspec; w archiwum NuGet i drzewie wydania nie znaleziono dodatkowego NOTICE.

Kod biblioteki nie został skopiowany ani zmodyfikowany. Informacja dotyczy
tej zależności i nie zmienia licencji własnego kodu VATowca. Biblioteka zawiera
reguły krajowe IBAN (w tym wbudowany rejestr SWIFT r102, czerwiec 2026);
walidacja nie potwierdza istnienia rachunku, banku, właściciela ani BIC.

# libphonenumber-csharp

VATowiec wykorzystuje niezmodyfikowany pakiet NuGet **libphonenumber-csharp 9.0.42**,
port biblioteki Google libphonenumber. Autorzy według pakietu: Patrick Mézard,
Thomas Clegg, Jarrod Alexander, Google i współautorzy libphonenumber.
Licencja **Apache-2.0**: [libphonenumber-csharp/LICENSE.txt](libphonenumber-csharp/LICENSE.txt).

- Pakiet: https://www.nuget.org/packages/libphonenumber-csharp/9.0.42
- Źródło i licencja wydania:
  https://github.com/twcclegg/libphonenumber-csharp/tree/dcaee5a42ab993d26a4bac47ef1cac4f1150ade2
- Pełny tekst LICENSE pobrano z powyższego commita. W głównym katalogu wydania
  nie znaleziono dodatkowego NOTICE. Nie kopiowano ani nie modyfikowano kodu biblioteki.

Biblioteka dostarcza metadane planów numeracji, parsowanie, walidację i format E.164.
Walidacja jest lokalna; nie sprawdza aktywności numeru ani jego właściciela.
