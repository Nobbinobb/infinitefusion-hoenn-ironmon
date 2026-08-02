# Step 2.6 validation

## Acquisition and story-path audit

- [x] Wild catches, visible encounters, static encounters, gifts, silent gifts,
  direct storage, and battle-peer storage all enter the protected acquisition
  boundary before ordinary party or PC storage.
- [x] Main-Pokemon trades remain direct replacements. Trading the utility
  Pokemon transfers its non-combat role to the received Pokemon.
- [x] Eggs remain excluded while unhatched; hatching starts an immediate pivot
  before normal play resumes.
- [x] Shedinja-style duplicate evolution results start an immediate pivot
  instead of being appended as a second battler.
- [x] Day Care deposit is blocked, while withdrawal from a legacy Day Care uses
  the ordinary acquisition boundary.
- [x] Wally accepts only the utility Pokemon in Ironmon and cannot remove the
  sole battler.
- [x] Utility Pokemon are excluded from battles, challenge eligibility,
  first-Pokemon encounter effects, highest-level helpers, passive post-battle
  rewards, and last-usable-Pokemon checks.
- [x] Single and multiselect PC movement cannot transfer a non-Egg between the
  party and storage; PC release counts ignore the utility Pokemon as a battler.

## Full embedded-runtime regression

- [x] All five current/candidate combinations expose the documented actions,
  including the conditional Slave action.
- [x] Take, Swap, Fuse, Reverse, Unfuse, and Slave all commit atomically and
  leave exactly one usable Pokemon.
- [x] Player-fusion discovery, caught-fusion transformation rights, utility
  replacement, and battle-party filtering remain intact.
- [x] Random Component and Player Choice retain their selected component after
  pivot-state serialization and reload.
- [x] A serialized pending pivot resumes and completes; an invalid action
  preserves both the current party and pending transaction.
- [x] F7 seed generation cannot reuse the previous seed.
- [x] The complete Custom Fusions Only trainer mapping contains only validated
  catalogue entries. Neither orientation of Bastiotrike (`308.529`) is present
  in the catalogue or generated mapping.

## Legacy-save migration

- [x] The pre-generator development save `File H` was loaded read-only through
  the real `Game.load` chain.
- [x] Its missing generator version and mapping tables were regenerated from
  the preserved seed and Normal Only/Custom Fusions Only policies.
- [x] Every migrated trainer result was a validated custom fusion and
  Bastiotrike was absent.
- [x] The source save's modification time remained unchanged throughout the
  compatibility test.

Both suites ran inside Infinite Fusion's embedded Ruby runtime after normal
game-data initialization. All temporary hooks and outputs were removed after
their clean passes. The canonical source, distribution, and local installation
were then synchronized and compared byte-for-byte.

The release script was run twice without source changes. Both builds produced
the same SHA-256 checksum, demonstrating reproducible packaging.
