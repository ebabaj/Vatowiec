# Polityka bezpieczeństwa

## Wspierane wersje

Projekt jest na etapie M0 i nie ma wydania produkcyjnego. Wydania `preview`
służą wyłącznie do prób i nie powinny przechowywać rzeczywistych danych
księgowych ani danych osobowych. Poprawki bezpieczeństwa trafiają do gałęzi
`main` i do kolejnego wydania.

| Wersja | Wsparcie |
|---|---|
| `main` | tak |
| wydania `*-preview` | tylko przez kolejne wydanie |

## Zgłaszanie podatności

**Nie zgłaszaj podatności w publicznych zgłoszeniach (Issues), dyskusjach ani
pull requestach.**

Użyj prywatnego zgłoszenia w GitHub: zakładka **Security** repozytorium →
**Report a vulnerability**. W zgłoszeniu podaj:

- dotknięty komponent (API, klient Avalonia, narzędzia bazodanowe, instalator),
- wersję lub commit,
- kroki odtworzenia i spodziewany skutek,
- informację, czy podatność jest już publicznie znana.

Nie dołączaj prawdziwych danych podatników, haseł, kluczy API ani
connection stringów. Używaj danych fikcyjnych.

Potwierdzimy otrzymanie zgłoszenia i uzgodnimy termin publikacji poprawki.
Prosimy o nieujawnianie szczegółów przed udostępnieniem poprawki.

## Zakres szczególnie istotny

- izolacja firm (database-per-company, wybór bazy wyłącznie po serwerowej autoryzacji),
- uwierzytelnianie, sesje, CSRF i uprawnienia (`CompanyAccess`, rola administratora instalacji),
- ochrona sekretów (klucz GUS, klucz DataPort, Data Protection/DPAPI, Windows Credential Manager),
- integralność i niezmienność audytu oraz historii księgowej,
- kontrola wersji (`rowversion`) i idempotencja zapisów,
- transport HTTPS edycji Network i lokalny kanał Named Pipe edycji Personal.

## Zasady projektu

Zasady bezpieczeństwa obowiązujące przy zmianach kodu opisują
[AGENTS.md](AGENTS.md) i [SPECYFIKACJA.md](SPECYFIKACJA.md) §11.
