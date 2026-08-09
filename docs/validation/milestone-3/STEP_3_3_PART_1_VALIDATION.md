# Step 3.3 Part 1 validation

## Embedded-runtime catalogue pass

The automated catalogue suite ran after normal game-data initialization inside
Pokemon Infinite Fusion 2's embedded Ruby runtime.

- [x] All 576 normal source species preserved every original level and entry;
  only the required level-1 safety supplements changed an entry count.
- [x] All species expose at least four moves by level 1, and the last four
  contain at least one move with positive base damage.
- [x] 273 species received supplemental level-1 entries because their source
  schedules contained fewer than four.
- [x] 241 species already had more than four level-1 entries. Their source
  entries remained intact, and direct Pokemon creation selected the last four
  with the damaging-move guarantee.
- [x] Every generated schedule contained unique eligible moves.
- [x] Repeated resolution returned the same schedule for every source species.
- [x] A second seed changed at least one generated schedule.
- [x] The mechanically safe universal pool contains 677 moves.
- [x] The pool fingerprint is `721511c3f49a737a`.
- [x] The contextual-restriction fingerprint is `d3e53fdde3f852c0`.
- [x] The source fingerprint is `18ddd461cbb1e0db`.
- [x] Dark Void remained reachable for Darkrai but was replaced when inherited
  by an incompatible fusion.
- [x] A real fusion produced the full chronological union of its generated
  body and head schedules, with body-first tie handling and earliest duplicate
  retention, followed by the level-1 damaging-move safety check.
- [x] Normal Pokemon and fusion creation consumed their generated schedules.
- [x] Cosmetic forms resolved to the base species' shared schedule, matching
  the game's form-data model.
- [x] Current run metadata activated the generator, while metadata-free legacy
  runs retained original move access.
- [x] The move-access generator schema is `2`.
- [x] A matching schema-1 review-build save migrated to schema 2 and activated
  the level-1 safety schedule.
- [x] Schema-1 metadata with a deliberately mismatched source fingerprint was
  still rejected.
- [x] The exact hidden game process used by the test was stopped afterward.

## Performance pass

- [x] The original full runtime catalogue regeneration measured approximately
  312 ms on its first pass and 96 ms after its data caches were warm.
- [x] Production preparation now performs cached structural, capacity, and
  fingerprint validation instead of regenerating all 576 schedules.
- [x] Complete cold preparation measured approximately 36 ms.
- [x] Repeated preparation measured approximately 0.007 ms.
- [x] First normal and fusion schedule resolution measured approximately
  0.11 ms and 0.13 ms respectively; cached resolution measured approximately
  0.001 ms.
- [x] The separate exhaustive 576-species generation pass still completed and
  rechecked counts, uniqueness, four starting moves, and damaging-move safety.

## Player validation still required

- [ ] Start a new Ironmon run and confirm a normal Pokemon receives generated
  level-up moves at its current level.
- [ ] Confirm a fusion receives moves from the chronological union of both
  generated component schedules.
- [ ] Level up and evolve Pokemon, then confirm move prompts use the generated
  schedule for their current species.
- [ ] Save and reload the run, then confirm the same schedules remain.
- [ ] Use F7 and confirm at least one inspected species receives a different
  schedule under the new seed.

The remaining Step 3.3 access channels and tracker presentation are intentionally
outside this Part 1 review boundary.
