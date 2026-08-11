# Step 3.5 fusion integration audit

Status: **Complete**

Audited against the canonical source and validation records through release
`0.6.3`.

## Review boundary

Step 3.5 does not redefine Pokemon data ownership. It verifies that the four
implemented Milestone 3 randomizers apply the shared displayed-fusion model
consistently, regardless of how a fusion was obtained, and that fusion
transformations cannot leave stale or invalid data behind.

The design and source review was followed by a cumulative embedded-runtime
matrix in Pokemon Infinite Fusion 2's bundled runtime. The final run completed
in 176.025 seconds with no implementation failure.

## Documented rule audit

| Data type | Fusion rule | Documentation | Final result |
| --- | --- | --- | --- |
| Abilities | Read the genuine generated slots of the displayed body and head; replace exact-species-only abilities with deterministic universal fallbacks. | `design/POKEMON_DATA_RANDOMIZATION.md` and `design/ABILITY_RANDOMIZATION.md` | Pass |
| Base stats | Generate displayed component stats first, then apply the native separately floored body/head formula without fusion rescaling or clamping. | `design/POKEMON_DATA_RANDOMIZATION.md` and `design/BASE_STAT_RANDOMIZATION.md` | Pass |
| Move access | Merge generated displayed-component level, Egg, TM/TR, and ordinary-tutor channels; keep specialized Fusion Tutor access on the complete displayed identity. | `design/POKEMON_DATA_RANDOMIZATION.md` and `design/MOVE_ACCESS_RANDOMIZATION.md` | Pass |
| Evolutions | Preserve every eligible displayed-component branch but map each branch to a deterministic, stronger, complete custom-sprite fusion. | `design/POKEMON_DATA_RANDOMIZATION.md` and `design/EVOLUTION_RANDOMIZATION.md` | Pass |

Every randomized data type therefore already has one explicit documented
fusion rule. Reversal consistently changes the displayed identity and causes
all species-owned data to resolve from the new body/head orientation.

## Implementation audit

### Ability ownership

- `AbilityGenerator#fusion_slots_for` keys its cache by displayed fusion
  identity and reads generated slots from `body_pokemon` and `head_pokemon`.
- Missing component slots remain missing; fusion assembly does not manufacture
  secondary or hidden slots.
- Exact-species-only component abilities are replaced deterministically in the
  fusion namespace.
- `Pokemon#ability_id` resolves the current generated slot dynamically, while
  the species setter normalizes stale indexes and clears persistent overrides.

Final result: **Pass**. Direct and pivot-generated instances use the same
species-owned slot resolver.

### Base-stat ownership

- `BaseStatGenerator#fusion_stats_for` keys its cache by displayed fusion
  identity.
- The method generates the displayed body and head vectors first and applies
  the accepted formula independently to all six stats.
- `GameData::Species#base_stats` and `Pokemon#baseStats` route all standard
  fusion reads through that shared resolver while Ironmon randomization is
  active.

Final result: **Pass**. No acquisition or pivot-instance state participates
in the generated fusion vector.

### Move-access ownership

- Fusion level schedules are chronological generated-component unions with
  deterministic contextual-move replacement, duplicate removal, and the
  accepted level-1 damaging-move safety check.
- Egg, TM/TR, and ordinary tutor compatibility are deduplicated unions of the
  displayed components' generated channels.
- Specialized Fusion Tutor capacity is derived from native compatibility for
  the displayed fusion identity, then mapped deterministically into the
  generated run catalog.
- Current moves and learned-move history remain instance-owned. Pivot fusion
  and unfusion may retain legal instance moves without changing the species-
  owned access lists.

Final result: **Pass**. All generated access caches are keyed by displayed
species identity rather than Pokemon instance or acquisition origin.

### Complete-fusion evolution ownership

- `FusionEvolutionGenerator` accepts every standard two-component fusion and
  builds conceptual body- and head-side branches from the implemented normal
  graph.
- All branches for a displayed source are generated together, receive unique
  complete-fusion targets, and pass target-pool, family, type, stage, strength,
  and effective-method validation.
- Level, item, trade, forced, story, conflict, and Nincada duplicate routes use
  the same generated branch set.
- Triple and special fusions remain outside generated fusion handling.

Final result: **Pass**. Existing Step 3.4 evidence covers all 287 supported
normal conceptual branches and the fusion generator derives its supported
branches from that same validated catalogue.

### Pivot and transformation integration

- Directly encountered and pivot-generated fusions resolve abilities, base
  stats, move access, and evolutions from `pokemon.species_data`; origin markers
  do not affect those resolvers.
- Player-created fusions are immediately marked with no transformation right.
- Taking, reversing, or otherwise retaining a caught fusion marks it as
  processed and closes its one immediate transformation right.
- Unfusion produces a normal component, removes fusion-only state, recalculates
  stats, and leaves ability resolution to the generated normal-species slots.
- Complete-fusion evolution changes species directly and retains the existing
  origin and transformation-right fields, so evolution cannot grant another
  pivot, reversal, unfusion, or fusion-gamble right.
- Fusion and reversal item handlers remain blocked for active Ironmon runs.

Final result: **Pass**. The runtime matrix covered caught-fusion keep, reverse,
unfuse, player-created fusion locks, and complete-fusion evolution.

### Save/load and F7 integration

- Ability, base-stat, move-access, and evolution results are reconstructed from
  the run seed plus versioned pool, rules, and source fingerprints.
- Save/load suspends each active generator while data is being loaded, then
  validates metadata and rebuilds seed-scoped caches.
- F7 restores the checkpoint, creates a new seed, resets pivot mappings and
  discoveries, and prepares all four generators again.
- Fusion mappings are not persisted as Pokemon data, and per-fusion generated
  data is not stored in the save.

Final result: **Pass**. Cache reconstruction, marshalled metadata and Pokemon
reload, simulated F7 reseeding, and incompatible metadata were exercised in one
cumulative runtime session.

## Evidence gaps closed

1. The cumulative runtime session compared all four randomized data types on
   direct and pivot-generated copies of fusion `B132H494` with different
   personal IDs and instance data. Their species-owned snapshots matched.
2. Keep, reverse, unfuse, player-created fusion, and complete-fusion evolution
   all retained valid data and the documented transformation locks.
3. All 174,348 validated custom-sprite fusion identities passed exhaustive
   generated ability, base-stat, level, Egg, TM, TR, and ordinary-tutor checks.
4. All 287 supported conceptual evolution branches passed as Body branches and
   again as Head branches, for 574 validated complete-fusion branch mappings.
5. Existing Step 3.1 and later tracker validation records close the Wonder
   Guard, tracker presentation, and completed-run reconstruction items that
   remained listed as manual follow-up in the original Step 3.2 record. The
   cumulative session independently closed generated-stat save/load and F7
   behavior.

No fusion-integration implementation defect was found. The only interrupted
attempt used `GameData::Species.try_get` on the raw special-fusion boundary,
which asks the base game to dynamically construct an invalid normal fusion and
blocked in its own error presentation. The final validator used an allocated
boundary object to exercise Ironmon's exclusion predicates directly and
passed.

## Completed cumulative embedded-runtime matrix

### Same displayed species, different acquisition origins

- [x] Construct a directly encountered standard fusion and a pivot-generated
  copy with the same displayed species but different personal IDs and instance
  data.
- [x] Confirm identical generated normal and hidden ability slot arrays.
- [x] Confirm identical generated six-stat vectors and final BST.
- [x] Confirm identical level, Egg, TM/TR, ordinary tutor, and specialized
  Fusion Tutor access.
- [x] Confirm identical body- and head-side generated evolution branch sets.
- [x] Confirm current moves may differ as instance-owned data without changing
  either copy's species-owned access.

### Transformation matrix

- [x] Keep a caught fusion and verify that its transformation right closes
  without changing its generated species-owned data.
- [x] Reverse a caught fusion and verify all four data types resolve from the
  paired displayed identity while instance-owned data and the processed lock
  remain valid.
- [x] Unfuse a caught fusion and verify the retained normal component has valid
  generated abilities, stats, and move access with all fusion-only state
  cleared.
- [x] Create a pivot fusion and verify it begins permanently locked against
  reversal and unfusion.
- [x] Evolve a player-created fusion and a processed caught fusion; verify each
  retains its origin and no-transformation state on the complete-fusion target.
- [x] Verify fusion and reversal bag items remain blocked for every retained
  fusion origin.

### Determinism and lifecycle

- [x] Clear all seed-scoped caches and reproduce every checked fusion result.
- [x] Marshal and reload run metadata and Pokemon instances, then reproduce all
  species-owned data and transformation locks.
- [x] Simulate F7 preparation with a different seed and confirm the inspected
  fusion changes in at least one randomized data type while remaining valid.
- [x] Confirm direct and pivot-generated copies agree again under the new seed.
- [x] Confirm incompatible metadata for each generator fails explicitly rather
  than silently returning current-run data.

### Full-catalogue and branch safety

- [x] Audit every validated custom-fusion pool entry for valid generated
  ability slots, base stats, and every component-owned move-access channel;
  verify complete-fusion specialized-tutor access in the cross-origin matrix.
- [x] Audit every supported conceptual evolution branch as both a Body and a
  Head branch on generated standard-fusion sources.
- [x] Confirm every generated fusion target is a valid custom-sprite standard
  fusion, is strictly stronger, and passes its documented stage, family, type,
  uniqueness, and method rules.
- [x] Confirm triple and special fusions retain native data and evolution
  behavior.

### Game-runtime and cleanup

- [x] Synchronize canonical source with `tools/Build-Distribution.ps1` before
  testing.
- [x] Run the cumulative validator in Infinite Fusion's bundled runtime.
- [x] Remove temporary validator scripts and reports.
- [x] Launch a clean hidden startup smoke test and stop only the exact process
  started by the test.
- [x] Confirm canonical, distribution, and installed Ruby scripts match.

## Audit result

The cumulative matrix ran in the bundled game runtime and every check passed.
The audit required no production-code correction. The temporary validator and
report were removed, clean startup passed, and two release builds reproduced
the same `0.6.3` archive checksum.
