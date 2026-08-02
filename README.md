# Ironmon for Pokemon Infinite Fusion 2

Ironmon is a challenge mode for Pokemon Infinite Fusion 2: Hoenn with seeded,
consistent randomization, fusion-aware encounter policies, increased difficulty,
and a fast reset to starter selection.

## Project layout

- `src/` contains the canonical Ruby source.
- `docs/` contains the roadmap, design notes, and validation records.
- `dist/` contains the copy-ready game directory layout.
- `tools/Build-Distribution.ps1` synchronizes the source into `dist/` and the
  local game installation.

The current implementation covers Roadmap Steps 1.1 through 1.5 plus the
implementation portion of Step 1.6. See `docs/ROADMAP.md` for status and
acceptance criteria.

This repository is maintained independently from the official Pokemon Infinite
Fusion game repository.
