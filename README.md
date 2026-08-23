# Ironmon for Pokemon Infinite Fusion 2

Ironmon is a challenge mode for Pokemon Infinite Fusion 2: Hoenn with seeded,
consistent randomization, fusion-aware encounter policies, increased difficulty,
and a fast reset to starter selection.

## Project layout

- `src/` groups the canonical Ruby source by subsystem; `src/load_order.json`
  maps each source file to its flat, numbered runtime filename.
- `docs/IRONMON_MECHANICS.html` is the complete public mechanics manual.
- `docs/README.md` is the compact documentation index.
- `docs/ROADMAP.md` tracks milestone scope and status.
- `docs/guides/` contains installation, configuration, and development guides.
- `docs/releases/` contains versioned release notes.
- `data/` contains release-generated runtime datasets.
- `packaging/` contains source templates copied into player distributions.
- `tracker/` contains the tracker, access-token generator, and their tests.
- `tests/runtime/` contains bundled-game runtime validation.
- `dist/` and `release/` are ignored generated output directories.
- `tools/Build-Distribution.ps1` validates the Ruby manifest and synchronizes
  flat, load-ordered scripts into `dist/` and the local game installation.
- `tools/Build-TrackerRelease.ps1` publishes the self-contained and
  runtime-required Windows trackers and creates both copy-ready ZIPs and
  SHA-256 checksums in `release/`.
- `tools/generation/` contains release-data generators and their runtime bridge.

Milestone 6 is on hold while optional quality-of-life features are developed.
Version `0.7.9` adds progressively loaded generated-evolution neighborhoods,
fast paged fusion reconstruction, transactional run preparation, and a more
modular game and tracker architecture. It also provides equivalent
self-contained and runtime-required Windows distributions while retaining
seeded-run sharing, battle-item control, type coverage, signed diagnostic
access, Favorite Clause, and the randomization safeguards from the 0.7 series.
See
`docs/IRONMON_MECHANICS.html` for the complete behavior reference,
`docs/ROADMAP.md` for cumulative scope, and `docs/guides/INSTALLATION.md` for
installation steps.

This repository is maintained independently from the official Pokemon Infinite
Fusion game repository.
