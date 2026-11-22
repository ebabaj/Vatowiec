# CLAUDE.md

Wszystkie zasady projektu, decyzje właściciela i ustalenia robocze są w
[AGENTS.md](./AGENTS.md). Ten plik jedynie je importuje dla Claude Code:

@AGENTS.md

## Zasada zapisu ustaleń

- Każde nowe ustalenie z rozmowy (zasady pracy, decyzje właściciela, zakres
  etapów, zgody na operacje) zapisuj w **AGENTS.md**, nie w CLAUDE.md.
  AGENTS.md jest wspólnym źródłem dla wszystkich agentów (Claude, Codex)
  i współautorów.
- Decyzje architektoniczne zapisuj dodatkowo jako ADR w `docs/adr/`, a ustalenia
  produktowe w `SPECYFIKACJA.md`, zgodnie z AGENTS.md §6.
- CLAUDE.md zmieniaj tylko wtedy, gdy zmienia się sam sposób importu AGENTS.md.
