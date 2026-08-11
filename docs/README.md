# Ironmon documentation

This index separates current project guidance and design decisions from
historical release notes and validation evidence.

## Start here

- [Roadmap](ROADMAP.md): milestone scope, status, and planned release sequence.
- [Installation](guides/INSTALLATION.md): install, update, and remove Ironmon.
- [Configuration](guides/CONFIGURATION.md): run settings and reset behavior.
- [Development mode](guides/DEVELOPMENT.md): local debug mode and tracker debug
  authorization.

## Design and architecture

- [Project structure](design/STRUCTURE.md): source responsibilities, load order,
  and packaging layout.
- [Checkpoint and reset lifecycle](design/CHECKPOINT_RESET.md): one checkpoint
  per save slot, unsaved runs, and legacy migration.
- [Challenge lifecycle](design/CHALLENGE_LIFECYCLE.md): Milestone 5 run results,
  failure locks, automatic reset, attempts, and statistics.
- [Step 5.1 validation](validation/milestone-5/STEP_5_1_VALIDATION.md):
  game-owned lifecycle, per-save attempt ledger, duration, and F7 carry-over.
- [Step 5.2 validation](validation/milestone-5/STEP_5_2_VALIDATION.md): failure
  lock, automatic reset, configuration migration, and runtime boundaries.
- [Step 5.3 validation](validation/milestone-5/STEP_5_3_VALIDATION.md): attempt,
  item, healing, encounter-frequency, and defeated-BST statistics.
- [Step 5.4 validation](validation/milestone-5/STEP_5_4_VALIDATION.md): tracker
  statistics, completed-run selection, recovery, and legacy compatibility.
- [Pivot system](design/PIVOT_SYSTEM.md): the complete one-Pokemon acquisition
  and fusion-gamble rules.
- [Pivot state](design/PIVOT_STATE.md): persisted transaction state and
  migrations.
- [Custom fusion pool](design/CUSTOM_FUSION_POOL.md): eligible custom-sprite
  fusion selection.
- [Species generators](design/SPECIES_GENERATORS.md): deterministic wild and
  trainer mappings.
- [Progression support](design/PROGRESSION_SUPPORT.md): temporary story Pokemon
  and progression safeguards.
- [HM tools](design/HM_TOOLS.md): permanent field-tool replacements.
- [Pokemon data randomization](design/POKEMON_DATA_RANDOMIZATION.md): shared
  Milestone 3 ownership, determinism, and fusion rules.
- [Ability randomization](design/ABILITY_RANDOMIZATION.md): Step 3.1 pools,
  slots, special mechanics, and migration rules.
- [Base-stat randomization](design/BASE_STAT_RANDOMIZATION.md): Step 3.2 totals,
  distribution limits, form handling, fusion formulas, and migration rules.
- [Move-access randomization](design/MOVE_ACCESS_RANDOMIZATION.md): Step 3.3
  level-up, Egg, TM, tutor, fusion, inspection, and migration rules.
- [Evolution randomization](design/EVOLUTION_RANDOMIZATION.md): Step 3.4
  graph, target, method, fusion, inspection, migration, and review-gate rules.
- [Ironmon Inspector](design/IRONMON_INSPECTOR.md): retired in-game inspection
  UI retained as historical design context.
- [Ironmon Tracker](design/TRACKER.md): external Blazor Hybrid tracker,
  protocol, live and post-run information, packaging, and debug mode.

## Release notes

- [0.6.4](releases/RELEASE_NOTES_0.6.4.md)
- [0.6.3](releases/RELEASE_NOTES_0.6.3.md)
- [0.6.2](releases/RELEASE_NOTES_0.6.2.md)
- [0.6.1](releases/RELEASE_NOTES_0.6.1.md)
- [0.6.0](releases/RELEASE_NOTES_0.6.0.md)
- [0.5.0](releases/RELEASE_NOTES_0.5.0.md)
- [0.4.0](releases/RELEASE_NOTES_0.4.0.md)
- [0.3.3](releases/RELEASE_NOTES_0.3.3.md)
- [0.3.2](releases/RELEASE_NOTES_0.3.2.md)
- [0.3.1](releases/RELEASE_NOTES_0.3.1.md)
- [0.3.0](releases/RELEASE_NOTES_0.3.0.md)
- [0.2.3](releases/RELEASE_NOTES_0.2.3.md)
- [0.2.2](releases/RELEASE_NOTES_0.2.2.md)
- [0.2.1](releases/RELEASE_NOTES_0.2.1.md)

## Validation records

- [Open validation closure](validation/OPEN_VALIDATION_CLOSURE.md): automated
  disposition of every formerly unchecked validation item.
- [Milestone 1](validation/milestone-1/): Steps 1.1 through 1.8.
- [Milestone 2](validation/milestone-2/): Steps 2.1 through 2.6.
- [Milestone 3](validation/milestone-3/): cumulative data-randomization
  validation beginning with Step 3.1.
- [Milestone 4 Step 4.0 audit](validation/milestone-4/STEP_4_0_AUDIT.md):
  compiled event inventory, boundary ownership, and remaining game-path gaps.

Validation records document the evidence for completed work. They are not the
source of current behavior rules; the design documents above are authoritative.
