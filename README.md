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
- `tools/Build-Milestone1.ps1` creates the copy-ready Milestone 1 ZIP and
  SHA-256 checksum in `release/`.

Milestone 1 is complete at version `0.1.0`. See `docs/ROADMAP.md` for its
implemented scope and validation record, and `docs/INSTALLATION.md` for
installation steps.

This repository is maintained independently from the official Pokemon Infinite
Fusion game repository.
