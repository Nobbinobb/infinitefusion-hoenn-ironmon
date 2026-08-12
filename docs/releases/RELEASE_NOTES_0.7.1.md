# Ironmon 0.7.1

This maintenance release corrects Mixed encounter selection, removes costly
sprite-loading work, and brings the public mechanics documentation in line
with the current implementation.

## Encounter generation

- Corrects a deterministic hashing correlation that forced all three starter
  positions under Mixed to be either all normal Pokemon or all fusions.
- Uses species-generator schema 3 for new runs and F7 resets so each authored
  slot receives an independently mixed deterministic category decision.
- Preserves schema-2 mappings and generation behavior for existing and
  completed runs so an update cannot reroll an active run.

## Sprite loading

- Prefers an exact installed individual sprite before consulting or extracting
  a spritesheet.
- Reuses an existing local spritesheet instead of downloading it again merely
  because the base game's updated-spritesheet ledger lacks an entry.
- Caches the parsed sprite-credit catalog and live tracker sprite paths instead
  of repeatedly scanning or resolving the same data.
- Leaves `Download data` under player control. Automatic downloads remain the
  recommended default; installing the spritepack is optional. Players who
  disable downloads because of performance problems must keep the spritepack
  updated manually.

## Documentation

- Clarifies wild and trainer mapping independence, encounter-policy wording,
  tracker statistics wording, and current fusion-generation behavior.
- Removes obsolete milestone references from the public mechanics manual.
- Corrects move-dependent evolution documentation: current generated
  evolutions convert move requirements to the documented level or friendship
  methods, while legacy native evolutions and move-driven forms retain native
  move checks.

## Compatibility

- Targets Pokemon Infinite Fusion 2 version 6.8.0.
- Existing schema-2 species mappings remain reproducible.
- Existing ability, base-stat, move-access, evolution, lifecycle, and tracker
  data remain compatible.
