# Step 3.4 Part 3 validation

## Review boundary

This part routes normal-species evolution gameplay through the deterministic
graph generated in Part 2. Complete-fusion runtime integration remains reserved
for Part 4.

## Evolution routing and eligibility

The automated suite ran after normal game-data initialization inside Pokemon
Infinite Fusion 2's bundled Ruby runtime with seed `123456`.

- [x] All 287 generated normal-species branches were reachable through their
  effective runtime methods.
- [x] Level, item, converted held-item, and forced-item routes returned their
  generated destinations rather than native destinations.
- [x] A converted `TradeItem` branch used its generated `LevelItem` behavior;
  its held item was consumed on success and its former trade route was disabled.
- [x] Egg, Shadow Pokemon, Everstone, and Battle Bond blockers retain their
  native prevention behavior.
- [x] Only the selected effective method's after-evolution callback ran.
- [x] Cancelled evolution cleared its pending selection.
- [x] Pending and committed selections are process-session state and were not
  included in a marshalled Pokemon instance.
- [x] Suspending evolution randomization, including the F7 regeneration path,
  clears all pending and committed evolution selections.

## Conflicts, special methods, and direct callers

- [x] A source with three simultaneously eligible branches selected the same
  branch across repeated calls and reversed query ordering.
- [x] Changing personal ID changed the selected conflict branch in the tested
  population, while each individual Pokemon remained deterministic.
- [x] Nincada's primary generated evolution and its independently generated
  Shedinja duplicate branch used different destinations and ran the native
  duplicate side effects exactly once.
- [x] Story progression through `getEvolution` returned the deterministic
  generated destination.
- [x] `pbForceEvo` used the deterministic generated destination.
- [x] Trainer-rematch-style direct species assignment completed the selected
  evolution state transition.
- [x] Fusions and legacy runs continued to use native runtime behavior.
- [x] Native evolution metadata and destinations remained unchanged.

## Pokemon state and growth curves

- [x] Evolution preserved personal ID, nature, IVs, EVs, happiness, current
  moves, ribbons, nickname, Poke Ball, markings, held item, and origin state.
- [x] The destination used its generated ability slot, base stats, and move
  access supplied by Steps 3.1 through 3.3.
- [x] Evolution preserved the exact Pokemon level across different growth
  curves by rebasing proportional progress toward the next level.
- [x] A level 30 Bulbasaur at exactly `1267/2534` progress evolved into the
  generated Pyukumuku destination without changing level. Its raw experience
  changed from `23027` to `22716`, the exact halfway point on the destination's
  growth curve.
- [x] Gaining one additional experience point after that evolution retained
  level 30, proving the growth-curve change cannot cause a delayed level drop.

## Packaging and cleanup

- [x] Canonical source was synchronized into the distribution and installed
  game through `tools/Build-Distribution.ps1` before embedded-runtime testing.
- [x] All 31 canonical Ruby scripts matched their distribution and installed
  copies byte-for-byte.
- [x] With the validator removed, the game loaded normally and remained running
  at the ten-second smoke-test checkpoint.
- [x] Only the exact hidden smoke-test process was stopped; no pre-existing
  Infinite Fusion process was present.
- [x] The temporary runtime validator and its output report were removed.
- [x] Repository whitespace validation passed.

## Player review

Part 3 is ready for review. Part 4 complete-fusion runtime integration has not
begun.
