# Move-access randomization

This document defines the Step 3.3 move-access randomization rules. It
supplements the shared determinism, species ownership, and fusion component
rules in `POKEMON_DATA_RANDOMIZATION.md`.

Step 3.3 randomizes how Pokemon gain access to moves. It does not alter move
data such as type, category, power, accuracy, PP, priority, or effects.

## Scope

Step 3.3 covers:

- level-1, ordinary level-up, and level-0 evolution entries;
- Egg-move lists;
- TM compatibility;
- ordinary tutor offerings and compatibility;
- the regular and legendary catalogs of the specialized Fusion Move Tutor;
- generated move-access inspection and post-run analysis; and
- creation-time move initialization.

HM items remain replaced by the permanent field tools defined in
`HM_TOOLS.md`. Former HM moves are ordinary candidates in the global move pool.
Move Relearner behavior, breeding inheritance, machine use, tutor teaching,
Sketch, Mimic, and other move effects continue through their native gameplay
paths using the generated access data defined here.

## Deterministic generation

Move-access results are deterministic, species-owned, and generated on demand.
Each result is derived from:

- move-access generator schema version;
- run seed;
- channel namespace;
- species or mechanical-form identity;
- source entry, compatibility position, or tutor-slot identity; and
- the relevant pool and source-data fingerprints.

The channel namespaces are independent for level-up entries, Egg entries, TM
compatibility, abstract tutor compatibility, ordinary tutor offerings, and
specialized Fusion Tutor catalogs and compatibility.

Generation never depends on personal ID, encounter order, inspection order,
which tutor is visited first, or whether a Pokemon is wild, owned, or
trainer-controlled. Duplicate rejection and collision resolution use stable
ordered pools and deterministic fallback. Every otherwise eligible unused move
has equal selection weight.

Generated mappings are not stored in the save. They may be cached in memory for
the current session. Save metadata stores:

- move-access generator schema version;
- global allowed-pool fingerprint;
- contextual-restriction fingerprint;
- source-data fingerprints;
- ordinary tutor-slot catalog fingerprint; and
- specialized regular/legendary Fusion Tutor catalog fingerprint.

Infinite Fusion's existing shuffled TM-item reward map remains separately saved
by the item randomizer. It is not replaced by the move-access generator.

## Audited source catalog and channel capacities

Before generation is enabled, Ironmon builds and validates an audited source
catalog from the supported game version. The catalog separates Infinite
Fusion's combined machine/tutor data into distinct acquisition channels.

The original capacity of a normal species or mechanical form is:

- **Level-up:** the number of original schedule entries, including level 0.
- **Egg:** the number of distinct original Egg moves.
- **TM:** the number of distinct registered non-HM TM moves accepted by the
  native compatibility rules.
- **Ordinary tutor:** the number of distinct non-machine tutor-compatibility
  entries in the native species data. Actual access is later filtered by which
  tutor moves exist in the run.
- **Specialized Fusion Tutor:** the number of distinct regular and legendary
  catalog entries for which that exact fusion originally qualified.

Repeated machines or tutors teaching the same move count once for capacity.
The combined number of unique moves across channels is diagnostic information,
not a generation constraint.

Each channel preserves its own original capacity. Generation prohibits
duplicates within a channel but permits the same move to appear in different
channels. Cross-channel overlap and unavailable abstract tutor entries may
therefore increase or decrease the final number of uniquely obtainable moves.
This variation is intentional and will be evaluated from completed-run data.

## Global permitted-move pool

A move enters the global pool when it is registered in the supported game
version and can execute safely as an ordinary selected move in at least one
reachable battle state. Eligibility is based on mechanical safety, not power,
rarity, usefulness, or competitive balance.

The pool includes:

- former HM moves;
- moves traditionally limited to Egg acquisition;
- Sketch, Mimic, Transform, Metronome, Copycat, and similar effects;
- OHKO and self-KO moves;
- fixed-damage moves and counterattacks;
- moves that require a reachable battle condition;
- unusually weak or powerful moves;
- moves with field effects such as Teleport, Dig, and Fly; and
- signature moves that remain functional ordinary moves for other species,
  even when a special secondary form effect applies only to the signature user.

The pool excludes:

- Struggle and other engine-selected fallback moves;
- placeholder, debug, obsolete, or unimplemented moves;
- battle-mode transformations that are not ordinary selectable moves;
- moves that crash, corrupt state, or invoke unavailable mechanics; and
- unregistered move IDs.

A move that rejects an incompatible user is removed from the universal pool
and becomes a contextual candidate for its supported exact species or form.
Contextual restrictions apply in every generated channel. An incompatible
fusion never inherits such a move merely because one component generated it;
the entry receives the channel's deterministic eligible replacement where the
channel preserves a slot, or is unavailable where access is an intersection.

The allowed pool contains no type preference and no general level-based power
bands. Level 1 is the sole safety exception: every Pokemon must have at least
four distinct moves available at level 1, and the four moves it starts with
must include at least one move with positive base damage. There is no later
damaging-move or power-progression guarantee.

## Level-up and evolution entries

Every eligible normal species or mechanical form preserves its complete
original schedule:

- number of entries;
- learning level;
- level-0 evolution triggers;
- order of multiple entries at the same level; and
- position of each entry in the schedule.

If fewer than four original entries are available by level 1, deterministic
supplemental level-1 entries are inserted before the later level-up schedule.
The final available level-1 slot is drawn from the damaging subset when the
four starting moves would otherwise contain no positive-base-damage move. This
is the only exception to preserving the original entry count and unconstrained
move category.

Only the move ID assigned to an entry changes. Every generated move ID is
unique across that species' complete level-up schedule. Level-0 entries use the
same global pool and rules as ordinary level-up entries; they do not receive a
special power, type, or category preference.

The game's native initialization, level-up, evolution-learning, and Move
Relearner paths consume the generated schedule. The generated schedule does not
retroactively rewrite an individual Pokemon's current four moves.

## Egg moves

An eligible normal species or mechanical form receives the same number of
distinct Egg moves as it originally had. Generated Egg moves are unique within
the Egg channel and are selected from the global permitted pool without type or
power preference.

Moves traditionally exclusive to breeding have no special restriction; they
are members of the same global pool as every other permitted move. The daycare
retains its native inheritance rule: a generated Egg move transfers only when
the relevant parent knows it. The Egg Move Tutor reads the generated Egg list
for the applicable baby species.

Egg access does not grant TM or ordinary tutor compatibility merely because the
same move appears in another channel.

## TM compatibility and item rewards

For each eligible normal species or mechanical form, Ironmon generates a set of
distinct compatible moves from the registered non-HM TM move roster. The set
contains exactly the species' original TM capacity. Selection has no type or
power preference.

Machine use checks this dedicated generated set. A move appearing in the
generated level-up, Egg, or tutor channel does not grant machine compatibility.
If the supported game contains one-use TR-style machines, they follow the same
channel rules with an independently preserved capacity. They appear with TMs in
inspection.

Ironmon retains Infinite Fusion's existing mapped TM-item shuffle. Found TMs
and every scripted TM gift, including Gym Leader rewards, pass through that
saved run-specific permutation. The selected item teaches its registered move,
and generated species compatibility determines who can use it. Receiving the
same original TM may still produce that TM by chance.

HM items never enter the TM reward or compatibility roster. Ironmon replaces
them with field-tool items. Their former moves remain global candidates for
the other generated channels.

## Ordinary tutors

Every ordinary world tutor offering is represented by a stable tutor slot. A
single-move tutor has one slot; a multi-move tutor has one ordered slot per
offering. Slot identity includes the stable event or tutor identity and the
position within that tutor.

Tutor slots receive unique generated moves from the global permitted pool
without replacement while candidates remain. Reopening a tutor, retrying an
interaction, saving, or loading cannot reroll an offering. Two tutors that
originally taught the same move have independent slots and may teach different
generated moves.

Each eligible normal species or mechanical form also receives an abstract
generated tutor-compatibility list. Its number of distinct moves equals the
species' original ordinary-tutor capacity. This list is generated independently
from tutor offerings.

A move is actually supported for a species only when:

1. the move appears in the species' generated abstract tutor list; and
2. an ordinary tutor slot offers that move during the run.

An abstract compatible move for which no tutor rolled that move is deliberately
unavailable. A tutor offering that does not appear in the species' abstract
list is deliberately incompatible. Level-up, Egg, and TM access do not bypass
this intersection.

The Egg Move Tutor is not an ordinary tutor slot. It continues to use the
generated Egg list.

## Specialized Fusion Move Tutor

The specialized Fusion Move Tutor retains separate regular and legendary
catalogs and their native progression gates. Every catalog entry is a stable
tutor slot whose offered move is randomized from the global permitted pool.

For each eligible standard fusion, Ironmon first determines how many regular
and legendary entries that exact fusion qualified for under the original game
rules. It then deterministically selects the same number of compatible
generated slots in each catalog. Original type, component, and prerequisite-
move conditions establish the source capacities but do not control generated
compatibility.

Directly encountered and pivot-generated copies of the same displayed fusion
receive identical specialized-tutor access. A reversed fusion uses its own
displayed fusion identity. The tutor's full-list screen displays the generated
catalog.

## Fusion access

A standard fusion derives ordinary move access from the generated data of its
displayed body and head. Pokemon consumed by the pivot gamble do not transfer
their species-owned access data.

### Level-up merge

The fusion receives the full union of both component schedules. Entries are
merged chronologically:

1. level-0 entries;
2. positive-level entries in ascending learning-level order; and
3. body entries before head entries when both components have entries at the
   same level.

Each component's internal order at a shared level is preserved. Every final
entry receives a stable `learn_order`.

When both components generated the same move, the fusion keeps the earliest
occurrence and removes later occurrences without replacement. A level-0
occurrence is earlier than a positive-level occurrence. At an equal level, the
body occurrence wins because it sorts first.

After the union is deduplicated, the level-1 safety rule is checked again. If
the fusion's four starting moves contain no positive-base-damage move, its last
starting slot receives a deterministic unique replacement from the universal
damaging subset. This safety replacement is the sole exception to the pure
component union.

### Other channel unions

The fusion's Egg, TM, and abstract ordinary-tutor compatibility are the
deduplicated unions of the corresponding body and head channels. Inspection
records body, head, or both as the source of each entry. Actual ordinary tutor
access is still limited to the intersection with generated tutor offerings.

Specialized Fusion Tutor access belongs to the complete displayed fusion and
follows its separately generated preserved counts rather than a component
union.

Fusion channel totals are not capped. Cross-component and cross-channel
duplicates naturally reduce the final unique total. No fusion-owned
replacement is generated merely to restore the sum of the component counts.

Reversal recomputes body/head ordering and source labels for the reversed
displayed identity. Direct encounters and pivot-generated copies resolve the
same access for the same displayed fusion.

## Species, forms, and exclusions

- Normal species are eligible for every generated channel they originally
  define.
- Mechanically distinct forms use independent identities, schedules, and
  channel capacities.
- Cosmetic forms share their base species' generated access.
- Standard two-component fusions follow the component and specialized-tutor
  rules above regardless of autogenerated or custom sprite availability.
- Triple and special fusions retain native level-up, Egg, and compatibility
  data during Step 3.3 because they do not follow the standard component model.
- Malformed or unsupported source data is rejected or covered by an explicit
  audited native-data exclusion. It is never partially randomized silently.

Randomized ordinary tutor offerings still affect what a native-data special or
triple fusion can actually obtain from those tutors. Existing shuffled TM-item
rewards likewise remain global item behavior.

## Instance-owned current moves

Generated access belongs to the species, but the moves currently known by an
individual Pokemon are instance-owned.

- Newly created wild, trainer, static, and gift Pokemon initialize from their
  generated level-up schedule.
- Ordinary trainer- or event-authored creation-time move overrides cannot
  bypass generated initialization, even when species randomization returns the
  original species.
- Once creation completes, learning, forgetting, replacing, copying, and
  teaching moves mutate only that individual Pokemon.
- Evolution preserves currently known moves and offers the target species'
  generated level-0 and current-level entries through the native path.
- Fusion preserves the participants' current moves through the native fusion
  move-selection procedure.
- A Pokemon may retain a move that its current species cannot obtain through
  any generated channel. If forgotten, that move may be permanently
  unavailable to the individual.
- Moves deliberately taught by later scripts are instance-owned.
- Shadow or other mechanically required move transformations are audited and
  may apply their required special moves after ordinary initialization.

Story events that require a particular known move must receive explicit
compatibility handling. They may not bypass generated access accidentally.

## Move-dependent evolutions and forms

Step 3.3 does not rewrite native evolutions or move-driven form effects. An
evolution that requires a specific move or move type checks the individual's
actual current moves. Random access may make such an evolution easier, delayed,
or unavailable. A form effect triggered by knowing or using a particular move
continues to behave natively when that move is acquired.

Step 3.4 replaces the evolution graph. Step 3.3 does not add temporary
guarantees or fallback evolution methods that would immediately be removed by
that later randomizer.

## Tracker Debug and post-run lookup

Complete generated move access is hidden during ordinary active play. The
external tracker's authorized Debug view and completed-run lookup add four
subtabs:

1. **Learnset:** chronological level-up and level-0 entries.
2. **Egg:** the complete generated Egg list.
3. **TM:** generated compatible TM or TR items and their taught moves.
4. **Tutor:** only moves supported by both generated compatibility and an
   actual generated tutor offering.

Rows identify body, head, both, ordinary tutor location or slot, and
specialized Fusion Tutor source where applicable. Original source entries may
appear in row details for validation. Cross-channel duplicates remain visible
in every applicable tab.

The authorized Player and Enemy Debug inspectors expose these same four move
subtabs and the same row details as Debug lookup. They must use the shared move
presentation rather than a reduced current-moves-only summary.

The Tutor tab may report supported and abstract compatibility counts, but it
does not list abstract entries that no generated tutor supports. Inspection
uses gameplay resolution paths, consumes no random values, and mutates no
Pokemon or discovery state.

Ordinary live tracker discovery remains observation-based. Complete generated
tabs never reveal an enemy's undiscovered access during a run.

## Post-run iteration data

The tracker stores local metrics for species and fusions actually encountered
or interacted with. Completed-run analysis includes:

- generated entry counts by channel;
- unique moves across all channels and cross-channel overlap;
- earliest damaging-move level, with the level-1 guarantee recorded;
- initial four-move composition at the encountered level;
- abstract versus supported ordinary tutor counts;
- fusion duplicate removals and component-to-fusion access growth;
- TMs obtained and compatible current party members at acquisition;
- tutor visits, compatible party members, and moves taught;
- acquisition events by level-up, Egg, TM, tutor, copy effect, or script; and
- moves actually used where the tracker already observes them.

The active tracker reveals no hidden aggregate. It records encountered data
rather than precomputing every possible fusion. Completed-run recipes retain
the metadata required to reconstruct theoretical access on demand.

## Existing-run compatibility

An Ironmon save created before Step 3.3 has no move-access generator metadata.
Loading it leaves native move access enabled so installing `0.5.0` cannot alter
a run in progress. Starting a new run or using F7 records the current metadata
and enables move-access randomization.

Schema-1 through schema-5 review saves migrate to schema 6 when their accepted
earlier-channel pool, contextual-restriction, and source fingerprints still
match. A
schema-1 migration adopts the level-1 safety schedule without rewriting the
four moves already known by an individual Pokemon. Migration also records the
Egg, machine, ordinary-tutor, and specialized Fusion Tutor source fingerprints
and enables the newly installed channels. Level-up hashing remains on rules
version 2, while Egg, TM, TR, and ordinary-tutor hashing remain on their
accepted versions, so adding the specialized tutor does not reroll earlier
channels.

A save that declares move-access metadata but does not match the installed
schema, pools, source catalog, restrictions, or tutor catalog is incompatible.
The game reports the mismatch instead of silently falling back to native or
newly interpreted data.

## Step 3.3 validation requirements

- Every generated move exists and is universally or contextually permitted.
- No excluded fallback, placeholder, obsolete, unsafe, or unregistered move is
  generated.
- Normal species preserve their exact level schedule and per-channel source
  capacities.
- Generated entries are unique within each normal-species channel.
- There is no type preference, power progression, or damaging-move guarantee.
- Repeated resolution, save/load, and inspection reproduce identical results
  without persisted generated mappings.
- F7 produces new results from the new run seed.
- Generation is independent of encounter, lookup, tutor-visit, and preview
  order.
- TM use consults only generated machine compatibility while the existing
  mapped found and scripted TM rewards remain functional.
- Egg inheritance and the Egg Move Tutor consume generated Egg lists.
- Ordinary tutor access is exactly the intersection of the abstract generated
  compatibility list and generated tutor offerings.
- Specialized Fusion Tutor catalogs and per-fusion preserved counts reproduce
  consistently.
- Standard fusion level-up schedules are chronological full unions with the
  documented duplicate rule; other component channels use deduplicated full
  unions without caps.
- Directly encountered and pivot-generated copies of the same displayed fusion
  expose identical access.
- Current moves remain instance-owned, while ordinary creation-time overrides
  cannot bypass generated initialization.
- Mechanical forms, cosmetic forms, special species, and exclusions follow the
  audited coverage rules.
- Legacy saves retain native access and incompatible declared metadata produces
  an explicit error.
- Debug and post-run lookup agree with runtime access while ordinary live play
  reveals no undiscovered move data.
- Completed-run metrics provide enough evidence to evaluate tutor mismatch,
  cross-channel overlap, status-only starts, and fusion access growth.
