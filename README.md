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
- `tools/Build-Milestone3.ps1` retains the historical cumulative ability-release
  build.
- `tools/Build-TrackerRelease.ps1` publishes the self-contained Windows tracker
  and creates the combined copy-ready ZIP and SHA-256 checksum in `release/`.

Milestone 5 is complete. Version `0.6.5` adds the game-owned challenge
lifecycle, failure lock and optional automatic reset, per-attempt statistics,
and completed-run tracker presentation. See
`docs/design/CHALLENGE_LIFECYCLE.md` for the rules, `docs/ROADMAP.md` for
cumulative scope, and `docs/guides/INSTALLATION.md` for installation steps.

This repository is maintained independently from the official Pokemon Infinite
Fusion game repository.
