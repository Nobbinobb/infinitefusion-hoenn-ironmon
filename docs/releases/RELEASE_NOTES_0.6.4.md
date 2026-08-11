# Ironmon 0.6.4

This release completes Milestone 3 with a cumulative integration and regression
pass across all four deterministic Pokemon data randomizers.

## Cumulative regression

- Validates generated abilities, base stats, level-up moves, Egg moves, TM/TR
  access, ordinary tutor access, and the complete normal evolution graph across
  all 576 eligible normal species.
- Reconstructs generated data after seed-scoped cache clearing and marshalled
  metadata/Pokemon reload for three deterministic seeds.
- Confirms each reseed changes the inspected generated data while remaining
  valid and internally deterministic.
- Retains the 0.6.3 exhaustive evidence for all 174,348 custom-sprite fusions
  and all 574 Body/Head complete-fusion evolution branch mappings.

## Inspection and diagnostics

- Verifies the live Debug Pokemon overview, abilities, and stats sections for
  normal and fused Pokemon.
- Verifies active generated lookup for overview, abilities, stats, moves, and
  evolutions for normal and fused Pokemon.
- Confirms structured and log diagnostics report the runtime version, seed,
  configuration, and every generator recipe needed to reproduce a run.

## Compatibility and exclusions

- Contains no generator-schema or rules change, so compatible 0.6.3 and earlier
  Milestone 3 runs retain their deterministic assignments.
- Generated evolution metadata remains opt-in; legacy runs without it retain
  native evolutions until a new run or F7 reset prepares current metadata.
- Triple and special fusions remain outside the standard two-component fusion
  randomizers. Current generated-data inspection requires an active run; full
  deterministic lookup is available after completion.
- Targets Pokemon Infinite Fusion 2 version 6.8.0.
