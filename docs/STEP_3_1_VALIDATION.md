# Step 3.1 validation

## Embedded-runtime ability catalogue pass

The validation suite ran after normal game-data initialization inside Pokemon
Infinite Fusion 2's embedded Ruby runtime.

- [x] All 576 available base species retained the shape and number of their
  defined normal and hidden ability slots.
- [x] Every generated slot resolved to a registered member of the allowed pool.
- [x] No species received a duplicate across its defined normal and hidden
  slots.
- [x] None of the mechanically restricted abilities appeared in the 221-entry
  allowed pool.
- [x] Registered strong and detrimental abilities, including Huge Power, Pure
  Power, Speed Boost, Moody, Truant, Slow Start, and Defeatist, remained in the
  allowed pool.
- [x] Clearing the memory cache reproduced every assignment for the same seed.
- [x] Changing the seed changed assignments for all 576 checked species.
- [x] The allowed-pool fingerprint was `1eb10426b3fd19c4`.

## Fusion and Pokemon-instance pass

- [x] Fusion `B1H2` received normal slots from the generated primary slots of
  its displayed body and head.
- [x] Its four fusion hidden slots matched the documented generated component
  slots and fallback structure.
- [x] Independently constructed copies of the fusion exposed identical normal
  and hidden slots.
- [x] Assigning `WONDERGUARD` directly to an Ironmon Pokemon did not bypass its
  generated slot.
- [x] A secondary normal slot fell back to slot 0 when the Pokemon changed to a
  species with only one normal slot.
- [x] A missing hidden slot fell back to an available normal slot.
- [x] The hidden-acquisition helper selected and resolved a generated hidden
  slot.
- [x] The inspector output contained original and generated slot data. Fusion
  output also included its displayed body, head, and component assignments.
- [x] Battle battlers retain their separate ability field and temporary battle
  ability setter; the Ironmon hook changes only persistent `Pokemon` resolution.

## Save/load and reset pass

A second embedded-runtime suite serialized and reloaded the real metadata and a
Pokemon containing a deliberately stale forced-ability cache.

- [x] All 576 base-species assignments were reproduced after serialization,
  metadata reload, and memory-cache reconstruction.
- [x] Fusion `B25H133` reproduced the same component-derived normal and hidden
  slots after reload.
- [x] The stale forced ability did not override the reloaded generated slot.
- [x] Ability metadata serialized to 1,425 bytes as part of an otherwise empty
  test `PokemonGlobalMetadata` object; no ability mapping table was present.
- [x] Simulating F7 with a new seed and fresh generator metadata changed all 576
  checked species.

## Creation and mutation path audit

- [x] Trainer-authored `ability` values cannot bypass generated slots; authored
  `ability_index` values continue to select a generated slot.
- [x] Hidden-ability map encounters are normalized to generated hidden slots
  after the game's original encounter modifier runs.
- [x] Eggs hatched on hidden-ability maps are normalized through the same
  generated hidden-slot helper before entering the pivot flow.
- [x] Ability Ball reads the generated hidden list and stores its generated slot
  index.
- [x] Ability Capsule reads `Pokemon#getAbilityList`, which now contains the
  generated slots, and switches the persistent slot index.
- [x] The supported game version has no Ability Patch handler; the documented
  behavior remains reserved if that mechanic is later added.
- [x] Species changes preserve the current slot when valid and normalize missing
  normal or hidden slots without retaining the old cached ability.
- [x] Loaded trainer parties normalize authored or invalid indexes after party
  construction; later species-policy replacement also passes through the
  species-change normalization hook.
- [x] Direct fusion encounters and pivot-generated fusions use the same dynamic
  `GameData::Species` component slots.

## Packaging

- [x] Canonical source, distribution, and local installation contain the same
  22 Ruby files byte-for-byte.
- [x] A final embedded-runtime smoke test loaded the synchronized source after
  the trainer and Egg-hatch integrations and resolved a generated ability.
- [x] Temporary runtime validation hooks and output files were removed.
- [x] The release archive contains no validation-only scripts.
- [x] Two consecutive builds produced the same SHA-256 checksum:
  `6ef74e32ea67be178f7c888cfdf193d6d455afdaea1b97e58900046173703656`.

## Contextual-eligibility revision (0.3.1)

The embedded-runtime suite was rerun after replacing the blanket mechanical
restriction list with contextual eligibility rules.

- [x] Generator schema 2 exposes a 225-entry universal pool with fingerprint
  `9510823976fc73b6`.
- [x] Wonder Guard, Stance Change, Multitype, and Flower Gift are registered
  universal candidates in the supported game data.
- [x] Every registered exact-species or component-dependent ability is absent
  from the universal pool.
- [x] All 576 base-species assignments were checked; no contextual ability was
  assigned outside its documented species relationship.
- [x] Forecast was selectable for Castform (example deterministic seed 414)
  but was replaced by a deterministic universal ability when that Castform
  assignment was read through a fusion.
- [x] Disguise was selectable for Mimikyu (example deterministic seed 38) and
  passed into a fusion containing Mimikyu.
- [x] Wonder Guard was selectable for an arbitrary species (example
  deterministic Bulbasaur seed 76), and recalculating that Pokemon's stats
  produced an actual maximum HP of 1.
- [x] Randomized Multitype and Shields Down now consult the generated active
  ability in the affected out-of-battle Pokemon checks.
- [x] Temporary runtime validation hooks and output files were removed after
  the pass.
- [x] Canonical, distribution, and installed 0.3.1 ability source copies match
  byte-for-byte.
- [x] Two consecutive 0.3.1 package builds produced SHA-256
  `a1cdc9da8ea8eb4a7730c753c26c7a7ae60145f8141781f71298b6b3e19ddb6f`.

## Performance revision (0.3.2)

An identical embedded-runtime benchmark measured generated normal and hidden
ability reads before and after the optimization:

- [x] 40,000 base-species slot reads improved from 0.197 seconds to 0.070
  seconds, approximately 2.8 times faster.
- [x] 10,000 fusion slot reads improved from 0.178 seconds to 0.018 seconds,
  approximately 9.7 times faster.
- [x] Repeated fusion reads return the same cached frozen slot result.
- [x] Clearing all seed-scoped caches reproduces the same base and fusion
  assignments.
- [x] All 576 base species still satisfy contextual ability eligibility.
- [x] Castform's seed-414 Forecast assignment remains blocked on its fusion.
- [x] Mimikyu's seed-38 Disguise assignment remains inherited by its fusion.
- [x] Suspending randomization disables generated reads during load; validating
  the saved metadata enables them again.
- [x] Generator schema 2 and fingerprint `9510823976fc73b6` are unchanged, so
  0.3.1 saves retain the same deterministic assignments.
- [x] Temporary benchmark and regression hooks were removed after the pass.
- [x] Two consecutive 0.3.2 builds produced SHA-256
  `897efd5d87069634f27346e5ce1de78979de222e27b0e87886fa7e80b992096f`.

## Pre-run startup investigation

The delay reported before choosing or loading any run was traced independently
of Ironmon generation:

- [x] Base-game `Data/messages.dat` loading: 20.179 seconds.
- [x] Plugin startup: 0.0002 seconds.
- [x] Compiler entry: effectively 0 seconds.
- [x] Base-game data initialization: 3.813 seconds.
- [x] Title-system setup: 0.767 seconds.
- [x] Ability preparation, species mappings, the custom-fusion pool, checkpoint
  saving, and starter creation had not started during the 20-second message
  load.
- [x] Temporarily deferring forced debug mode did not change the delay, so the
  development helper was restored unchanged.
- [x] All temporary startup profiling code and output were removed.
- [x] The rejected local-only starter preview experiment was completely
  reverted and was not retained as a release.
