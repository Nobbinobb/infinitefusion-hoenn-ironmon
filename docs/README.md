# Ironmon documentation

## Mechanics reference

- [Ironmon Mechanics Manual](IRONMON_MECHANICS.html): the complete public
  reference for challenge lifecycle, encounter generation, pivots, randomized
  Pokemon data, progression safeguards, evolutions, and tracker behavior.

The manual is a self-contained HTML document. Open it in any modern browser;
it does not require a web server or an internet connection. Its print layout is
also suitable for saving as PDF.

## Guides

- [Installation](guides/INSTALLATION.md): install, update, and remove Ironmon.
- [Configuration](guides/CONFIGURATION.md): run settings and reset behavior.
- [Development and diagnostic access](guides/DEVELOPMENT.md): tracker builds,
  runtime validation, and capability-limited diagnostic access.

## Audits

- `audits/generated/` contains the current release-generated area, type coverage,
  item randomization, obtainability, and defense presentation audits.
- [Defense audit workflow](audits/WEAKNESS_MODIFIERS_AUDIT.md#defense-catalog-generation)
  describes the reviewed defense rules and release-generated informational data.
- `audits/area-content/` contains the detailed area-content snapshot used while
  the area catalog was designed.

## Project history

- [Roadmap](ROADMAP.md): milestone scope and release sequence.
- [Release notes](releases/): version-specific changes from 0.2.1 onward.

Historical validation records and the former collection of design notes were
removed after their current behavior was consolidated into the mechanics
manual. Release notes remain separate because they describe version history
rather than current mechanics.
