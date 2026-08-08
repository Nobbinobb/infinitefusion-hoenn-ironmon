# Ironmon for Pokemon Infinite Fusion 2

Ironmon is a challenge mode for Pokemon Infinite Fusion 2: Hoenn with seeded,
consistent randomization, fusion-aware encounter policies, increased difficulty,
and a fast reset to starter selection.

## Project layout

- `src/` contains the canonical Ruby source.
- `docs/README.md` is the documentation index.
- `docs/ROADMAP.md` tracks milestone scope and status.
- `docs/guides/` contains installation, configuration, and development guides.
- `docs/design/` contains stable behavior and architecture specifications.
- `docs/releases/` contains versioned release notes.
- `docs/validation/` contains milestone-specific validation records.
- `dist/` contains the copy-ready game directory layout.
- `tools/Build-Distribution.ps1` synchronizes the source into `dist/` and the
  local game installation.
- `tools/Build-Milestone3.ps1` creates the cumulative ability-release ZIP and
  SHA-256 checksum in `release/`.
- `tools/Build-TrackerRelease.ps1` publishes the self-contained Windows tracker
  and creates the combined copy-ready ZIP and SHA-256 checksum in `release/`.

Milestone 3 Step 3.1 is implemented at version `0.3.3`. See
`docs/design/ABILITY_RANDOMIZATION.md` for its rules, `docs/ROADMAP.md` for
cumulative scope, and `docs/guides/INSTALLATION.md` for installation steps.

This repository is maintained independently from the official Pokemon Infinite
Fusion game repository.
