# Step 3.3 Part 2 validation

## Embedded-runtime catalogue pass

The automated Egg move catalogue suite ran after normal game-data
initialization inside Pokemon Infinite Fusion 2's embedded Ruby runtime.

- [x] All 576 normal source species preserved their original Egg move capacity.
- [x] The 235 species with Egg access preserved all 2,563 source slots.
- [x] Every generated Egg list contained distinct eligible moves.
- [x] Repeated resolution returned the same list for every species.
- [x] A second seed changed at least one non-empty Egg list.
- [x] The Egg source fingerprint is `942efd5a082fa48f`.
- [x] A real fusion matched the body-first deduplicated union of both generated
  component Egg lists.
- [x] The native species property and `pbGetSpeciesEggMoves` lookup returned the
  generated list while move-access randomization was active.
- [x] Schema-2 review metadata migrated to schema 3 and recorded the Egg source
  fingerprint.
- [x] Schema-2 migration preserved the accepted level-up schedule.
- [x] Deliberately mismatched schema-2 source metadata remained incompatible.

## Performance pass

- [x] Complete cold preparation measured approximately 44-97 ms across the
  validation launches.
- [x] Repeated preparation measured approximately 0.007 ms.
- [x] First normal and fusion Egg-list resolution remained below 0.1 ms.
- [x] Cached normal and fusion Egg-list resolution measured approximately
  0.001 ms.
- [x] Production validation reuses immutable source catalogues; the exhaustive
  2,563-source-ID check remains in the dedicated validation suite.

## Player validation disposition

- [x] Part 2 was accepted through code review and embedded-runtime validation.
- [x] Daycare breeding is outside Ironmon gameplay and is not a required manual
  validation path. The implementation deliberately continues to supply the
  generated list to the game's native inheritance code.
- [x] Egg Move Tutor interaction is unavailable in the installed early-access
  build: no map event or common event calls its implementation. This is not a
  Part 2 release blocker; its native lookup path was covered by the embedded
  test.
- [ ] Fusion presentation will be checked later with the tracker move tabs.
- [ ] Save/reload and F7 presentation will be checked with the complete
  multi-channel tracker integration rather than through inaccessible gameplay.

TM/TR compatibility and rewards remain outside this Part 2 review boundary.
