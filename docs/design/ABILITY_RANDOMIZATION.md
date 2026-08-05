# Ability randomization

This document defines the Step 3.1 ability-randomization rules. It supplements
the shared determinism and fusion data-ownership rules in
`POKEMON_DATA_RANDOMIZATION.md`.

## Generation model

Ability assignments are deterministic, species-owned, and generated on demand.
Each result is derived from:

- Ability-generator schema version.
- Run seed.
- Ability namespace.
- Species identity.
- Ability slot kind and index.

Generated ability mappings are not stored in save data. The implementation may
use a memory-only cache for the current session. Save metadata stores the
generator schema version and a fingerprint or rules version for the ordered
allowed-ability pool so an active run cannot be silently reinterpreted after an
update.

Assignments must not depend on personal ID, encounter order, generation order,
preview order, or whether a Pokemon is wild, owned, or trainer-controlled.

## Species and slot rules

- Preserve the species' existing number of defined normal and hidden slots.
- Randomize every defined slot independently.
- Draw normal and hidden slots from the same allowed-ability pool.
- Keep generated hidden abilities in hidden slots; acquisition mechanics still
  determine whether a Pokemon receives a normal or hidden slot.
- Give each allowed ability equal selection weight.
- Do not assign the same ability to more than one defined slot of a species when
  enough allowed abilities exist.
- Allow the same generated ability to appear on different species.
- Use stable pool ordering and deterministic collision resolution so rejecting
  a within-species duplicate cannot introduce call-order dependence.

A species with one native normal slot continues to have one normal slot. A
species with two native normal slots continues to have two. Randomization does
not manufacture additional choices for species that did not originally have
them.

## Forms

- Mechanically distinct forms receive distinct assignments when the engine
  gives them distinct species identities.
- Cosmetic forms share their base species' assignments.
- Form classification must be audited against the supported game version before
  release.

Abilities that require a particular species or component are removed from the
general pool and added only to the eligible pool for that context. They remain
random candidates and are never guaranteed merely because the species is
eligible for them.

## Ability eligibility

The pool follows a mechanical-safety policy, not a power-balance policy.
Abilities remain eligible merely because they are unusually strong, unusually
weak, or actively detrimental.

The universal pool includes every registered, functional ability that does not
need a particular species relationship. This includes the following formerly
restricted abilities:

- `WONDERGUARD`
- `STANCECHANGE`
- `MULTITYPE`
- `FLOWERGIFT`

`WONDERGUARD` may therefore be assigned to any species. The game calculates the
active holder's actual maximum HP as 1. Step 3.2 still randomizes the species'
base HP normally: HP depends on the currently selected ability slot, not on a
different permanent stat mapping.

The following abilities are random candidates only for the exact base species
whose mechanics support them. A fusion containing that species is not eligible:

- `FORECAST`: Castform.
- `ZENMODE`: Darmanitan.
- `SCHOOLING`: Wishiwashi.
- `POWERCONSTRUCT`: Zygarde.
- `BATTLEBOND`: Greninja.
- `RKSSYSTEM`: Silvally.
- `ICEFACE`: Eiscue, if Eiscue and the ability are registered in the supported
  game version.

The following abilities are random candidates for the named species and may be
inherited by a fusion containing that component, matching the game's fusion
support:

- `DISGUISE`: Mimikyu and Mimikyu-component fusions.
- `SHIELDSDOWN`: Minior and Minior-component fusions.

An unavailable or unregistered ability is not placed into any pool. Eligibility
rules and their species relationships are included in the generator schema and
pool fingerprint, so a rules change cannot silently reinterpret a saved run.

Powerful and harmful abilities remain allowed. This explicitly includes, when
present and functional in the supported game version:

- Huge Power and Pure Power.
- Speed Boost.
- Moody.
- Shadow Tag and Arena Trap.
- Truant.
- Slow Start.
- Defeatist.

## Fusion inheritance

For a random fusion C displayed as body X and head Y, C reads ability slots from
the generated assignments of X and Y. It never inherits an ability merely
because normal Pokemon A or B were consumed to create C.

Fusion slots preserve the normal game's component structure:

- Normal fusion slot 0 uses X's generated primary normal ability.
- Normal fusion slot 1 uses Y's generated primary normal ability.
- Hidden-array slot 0 uses X's generated secondary normal ability only when X
  genuinely defines that slot.
- Hidden-array slot 1 uses Y's generated secondary normal ability only when Y
  genuinely defines that slot.
- Hidden-array slots 2 and 3 use X's and Y's generated hidden ability
  respectively, only when that component genuinely defines a hidden slot.

Missing component slots remain empty. Fusion assembly does not duplicate a
primary, secondary, or hidden ability merely to fill another position. This
retains every genuinely generated component option without manufacturing
fallback choices.

A component-compatible ability is inherited only when its required component
is present. If an exact-species-only ability occupies a component slot, the
fusion replaces that slot with a deterministic universal-pool result; an exact
species ability never leaks into a fusion.

Directly encountered and pivot-generated copies of C expose the same available
ability slots. Reversing a caught fusion changes the displayed species and uses
the reversed species' component assignments. It does not carry the previously
active ability across as species-owned data.

Ability-generator schema 3 introduces the missing-slot rule while retaining
assignment algorithm version 2. Migrating a schema-2 run therefore preserves
all genuine component assignments. An active legacy duplicate slot is remapped
to the real slot that supplied the same ability.

## Evolution behavior

Evolution preserves the Pokemon's ability slot index where the target species
defines a corresponding slot. The evolved species then resolves that slot
through its own generated assignment, so the actual ability may change.

If the corresponding slot does not exist:

- A missing secondary normal slot falls back to normal slot 0.
- A missing hidden slot follows the engine's normal hidden-to-normal fallback.

Evolution must clear stale cached or forced ability values before resolving the
new species-owned assignment.

## Ability-changing mechanics

Permanent creation-time overrides must not bypass species-owned assignments:

- Trainer-authored forced abilities resolve through generated species slots.
- Hidden-ability encounters select an available generated hidden slot.
- Special capture items or scripts that grant hidden abilities use generated
  hidden slots.
- Ability Capsule switches between available generated normal slots.
- Ability Patch selects an available generated hidden slot if that mechanic is
  present and permitted by the game.

Temporary battle mechanics continue to work normally. Trace, Skill Swap,
Mummy-style effects, Gastro Acid, and other temporary copying, replacement, or
suppression effects may alter the active battle ability without changing the
species' generated assignments.

When a temporary or explicit override ends, the Pokemon must resolve its
species-owned generated slot again rather than retaining a stale ability.

## Inspection and diagnostics

The in-game inspector is a development and validation tool. It is unavailable
in ordinary release play so it cannot reveal an undiscovered fusion gamble.

For a selected normal species, it displays:

- Species identity and form identity.
- Original normal and hidden slots.
- Generated normal and hidden slots.
- Generator schema version, pool rules version or fingerprint, and run seed.

For a selected fusion, it additionally displays:

- Displayed body and head species.
- The component and generated component slot supplying each fusion slot.
- The final available fusion ability slots.

Diagnostic output must provide enough identifiers to reproduce an assignment
without logging or precomputing every species and fusion mapping.

## Step 3.1 validation requirements

- Repeated resolution of a species and slot returns the same ability in one
  run.
- Save/load returns the same assignments without a persisted mapping.
- F7 changes the run seed and generated assignments.
- Normal, hidden, trainer-forced, and special hidden-ability creation paths all
  resolve generated slots.
- Defined slots contain no within-species duplicates.
- Every generated ability exists and is permitted by the audited pool.
- Contextual abilities appear only for their documented exact species or
  compatible component fusion.
- Universal abilities including Wonder Guard remain selectable for arbitrary
  species.
- An active Wonder Guard holder has 1 HP without changing its species-owned
  randomized base HP.
- Strong and detrimental permitted abilities remain selectable.
- Evolution preserves or safely falls back from the previous slot index and
  clears stale ability caches.
- Directly encountered and pivot-generated copies of the same fusion expose the
  same component-derived slots.
- Temporary battle ability effects do not mutate generated assignments.
- The debug inspector agrees with runtime resolution for normal Pokemon and
  fusions without exposing data in ordinary release play.
