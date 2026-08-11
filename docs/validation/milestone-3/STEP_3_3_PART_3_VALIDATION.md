# Step 3.3 Part 3 validation

## Embedded-runtime catalogue pass

The automated machine compatibility suite ran after normal game-data
initialization inside Pokemon Infinite Fusion 2's embedded Ruby runtime.

- [x] The installed build contains 125 TM items teaching 125 distinct eligible
  moves.
- [x] The installed build contains no TR items; the independent TR channel is
  represented by an empty roster and remains dormant.
- [x] All 564 species with native TM compatibility preserved their exact
  capacity, totaling 20,363 compatibility slots.
- [x] The largest source capacity of 122 fits in the 125-move TM pool.
- [x] Every generated TM list contained distinct eligible TM moves.
- [x] Repeated resolution returned the same list for every species.
- [x] A second seed changed at least one non-empty TM list.
- [x] A real fusion matched the deduplicated union of both generated component
  TM lists.
- [x] Fusion teaching checks accepted a union move and rejected a roster move
  outside the union.
- [x] Machine compatibility was active only inside machine teaching context;
  an isolated generated TM match did not leak into native tutor compatibility.
- [x] HM items were excluded because the roster admits only items registered as
  TMs or TRs.

## TM rewards and migration

- [x] Infinite Fusion's saved TM-item permutation contains every registered TM
  exactly once.
- [x] Ironmon applies the saved mapping to every TM, including TM94, which the
  base randomizer normally exempts.
- [x] A compiled-map audit found 22 literal scripted TM references using mapped
  item paths and no direct TM inventory command bypass.
- [x] A separate common-event audit found no direct TM inventory changes.
- [x] The audited maps include Rustboro, Dewford, and Mauville Gyms in the
  currently installed early-access content.
- [x] Schema-3 review metadata migrated to schema 4 and recorded TM/TR roster
  and source metadata.
- [x] Migration preserved the accepted level-up and Egg results.
- [x] The TM roster fingerprint is `26d4e2c1dc68cd14`.
- [x] The TM source fingerprint is `02b8d6186d29d339`.
- [x] The empty TR roster fingerprint is `88d94c1f5d1fa3a3`.
- [x] The empty TR source fingerprint is `7b6f996bf22733a1`.

## Performance pass

- [x] Complete cold preparation measured approximately 74-139 ms across
  validation launches.
- [x] Repeated preparation measured approximately 0.007 ms.
- [x] First normal TM resolution measured approximately 0.18 ms.
- [x] First fusion TM resolution measured approximately 0.23 ms.
- [x] Cached normal and fusion TM resolution measured approximately 0.001 ms.

## Player validation disposition

- [x] Part 3 was accepted through code review and embedded-runtime validation.
- [x] The completed tracker exposes generated compatible and incompatible TM/TR
  access directly; the former manual trial-and-error check is retired.
- [x] Generated machine rewards and compatibility are reconstructed from the
  saved permutation in completed tracker lookup.
- [x] The cumulative Milestone 3 runtime pass validated save/reload and F7
  reconstruction for the completed multi-channel generator.

Ordinary tutor offerings and compatibility remain outside this Part 3 review
boundary.
