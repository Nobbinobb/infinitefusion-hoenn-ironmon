# Ironmon 0.6.3

This release completes the Milestone 3 fusion-integration audit. It retains the
randomization behavior from 0.6.2 and verifies that every implemented Pokemon
data randomizer follows the same displayed-fusion ownership model.

## Fusion integration

- Confirms directly encountered and pivot-generated copies of the same fusion
  expose identical generated abilities, base stats, move access, and evolution
  branches.
- Confirms reversal resolves every species-owned data type from the paired
  displayed fusion identity.
- Confirms keep, reverse, unfuse, player-created fusion, and complete-fusion
  evolution preserve the documented transformation locks.
- Confirms save/load reconstructs fusion data from versioned metadata and F7
  changes the seed without introducing acquisition-origin differences.

## Cumulative validation

- Audits all 174,348 validated custom-sprite fusion identities for generated
  abilities, base stats, level-up moves, Egg moves, TM/TR access, and ordinary
  tutor access.
- Audits all 287 supported conceptual evolution branches as both Body and Head
  branches, covering 574 deterministic complete-fusion mappings.
- Verifies incompatible metadata for every Pokemon data generator fails
  explicitly.
- Closes the outstanding Step 3.2 cumulative validation follow-ups using the
  Step 3.5 runtime pass and later tracker validation evidence.

## Compatibility

- Contains no generator-schema or rules change, so compatible 0.6.2 runs retain
  their existing deterministic mappings.
- Targets Pokemon Infinite Fusion 2 version 6.8.0.
