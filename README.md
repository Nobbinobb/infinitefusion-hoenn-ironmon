# Ironmon for Pokemon Infinite Fusion 2

Ironmon is a challenge mode for Pokemon Infinite Fusion 2: Hoenn with seeded,
consistent randomization, fusion-aware encounter policies, increased difficulty,
and a fast reset to starter selection.

## Project layout

- `src/` contains the canonical Ruby source.
- `docs/` contains the roadmap, design notes, and validation records.
- `docs/PIVOT_SYSTEM.md` specifies the automated one-Pokemon pivot and
  deterministic fusion-gamble rules.
- `docs/POKEMON_DATA_RANDOMIZATION.md` specifies Milestone 3 determinism,
  fusion data ownership, and fusion evolution behavior.
- `docs/ABILITY_RANDOMIZATION.md` specifies the agreed Step 3.1 slot, ability
  pool, fusion inheritance, evolution, and inspection rules.
- `docs/DEVELOPMENT.md` explains the local debug-mode helper and ability
  inspector access.
- `dist/` contains the copy-ready game directory layout.
- `tools/Build-Distribution.ps1` synchronizes the source into `dist/` and the
  local game installation.
- `tools/Build-Milestone3.ps1` creates the cumulative ability-release ZIP and
  SHA-256 checksum in `release/`.

Milestone 3 Step 3.1 is implemented at version `0.3.2`. See
`docs/ABILITY_RANDOMIZATION.md` for its rules, `docs/ROADMAP.md` for cumulative
scope, and `docs/INSTALLATION.md` for installation steps.

This repository is maintained independently from the official Pokemon Infinite
Fusion game repository.
