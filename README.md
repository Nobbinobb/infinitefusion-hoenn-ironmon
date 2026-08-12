# Ironmon for Pokemon Infinite Fusion 2

Ironmon is a challenge mode for Pokemon Infinite Fusion 2: Hoenn with seeded,
consistent randomization, fusion-aware encounter policies, increased difficulty,
and a fast reset to starter selection.

## Project layout

- `src/` contains the canonical Ruby source.
- `docs/IRONMON_MECHANICS.html` is the complete public mechanics manual.
- `docs/README.md` is the compact documentation index.
- `docs/ROADMAP.md` tracks milestone scope and status.
- `docs/guides/` contains installation, configuration, and development guides.
- `docs/releases/` contains versioned release notes.
- `dist/` contains the copy-ready game directory layout.
- `tools/Build-Distribution.ps1` synchronizes the source into `dist/` and the
  local game installation.
- `tools/Build-Milestone3.ps1` retains the historical cumulative ability-release
  build.
- `tools/Build-TrackerRelease.ps1` publishes the self-contained Windows tracker
  and creates the combined copy-ready ZIP and SHA-256 checksum in `release/`.

Milestone 6 is on hold while the 0.7 improvement cycle develops optional
quality-of-life features. Version `0.7.0` adds tracker-assisted starter
selection, persistent autoselect settings, and the Favorite Clause. See
`docs/IRONMON_MECHANICS.html` for the complete behavior reference,
`docs/ROADMAP.md` for cumulative scope, and `docs/guides/INSTALLATION.md` for
installation steps.

This repository is maintained independently from the official Pokemon Infinite
Fusion game repository.
