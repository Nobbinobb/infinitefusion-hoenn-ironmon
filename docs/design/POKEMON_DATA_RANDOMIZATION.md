# Pokemon data randomization

This document defines the shared data-ownership and determinism rules for
Milestone 3. Feature-specific limits, pools, and balance ranges are finalized
in the roadmap step that implements each randomizer.

## Deterministic generation and storage

Pokemon data is generated as a pure, run-specific result. A generated value is
derived from the run seed, generator schema version, data-type namespace,
species identity, and the relevant slot or entry identity.

Ability, base-stat, and learnset results are generated on demand. Implementations
may cache results in memory for the current session, but generated mappings are
not stored in the save. Save metadata stores only the information required to
reproduce and validate the result:

- Run seed.
- Generator schema version for each randomized data type.
- A fingerprint or rules version for any ordered candidate pool or restriction
  set that affects generation.

This keeps save growth independent of the number of possible fusion species.
Save/load reproduces the same results, while F7 creates a new seed and therefore
new results. A generator update must not silently reinterpret an active run;
the implementation must retain the compatible schema or report that the saved
run cannot be reproduced by the installed version.

Evolution generation may build a complete graph in memory when global
validation is required to prevent loops. The graph must still be reproducible
from the saved seed and version metadata; persistence is not a substitute for
determinism.

## Species-owned data

Randomized Pokemon data belongs to the resulting species rather than to the
individual Pokemon instance or to the Pokemon consumed to create it.

Consequently:

- Every normal copy of a species exposes the same generated data during a run.
- Every directly encountered and pivot-generated copy of the same fusion
  species exposes the same generated data during a run.
- A forced ability or other instance override must not permanently bypass the
  generated species data.
- Personal IDs, encounter order, preview order, save/load, and retrying an
  action cannot reroll species-owned data.

## Fusion component model

Let the two normal Pokemon offered to the fusion gamble be A and B. The gamble
maps them to a custom-sprite fusion C. C is not their natural A/B fusion. If C's
displayed species is composed of body X and head Y, C's randomized data is
derived from X and Y:

```text
A + B -> random fusion C
C = body X + head Y
```

A and B contribute instance-owned data through the normal fusion procedure,
but their species-owned randomized ability, base-stat, and learnset data do not
transfer to C.

The following rules apply to C:

- Typing continues to use the game's normal X/Y fusion calculation.
- Available abilities are inherited from the generated ability slots of X and
  Y.
- Base stats are calculated by applying the game's fusion-stat formula to the
  generated base stats of X and Y.
- The level-up learnset is combined from the generated learnsets of X and Y,
  subject to the duplicate, ordering, and safety rules finalized in Step 3.3.
- C's later evolution results belong to C and are deterministic for the run.

This component model applies regardless of whether C was encountered directly
or created by the pivot gamble. Reversal changes the displayed fusion species
and therefore uses the reversed species' component-owned data; it does not
carry data from the previous orientation.

## Fusion ability slots

Fusion ability slots follow the game's body/head structure, but read the
generated component slots:

- Normal slot 0 comes from body X's generated primary normal ability.
- Normal slot 1 comes from head Y's generated primary normal ability.
- Additional and hidden fusion slots follow the corresponding normal-game
  body/head fallback structure using X's and Y's generated normal and hidden
  slots.

The exact normal-slot and hidden-slot generation rules and the restricted
ability pool are defined in `ABILITY_RANDOMIZATION.md`. No ability from
sacrificed A or B is retained merely because it participated in the fusion
gamble.

## Fusion base stats and learnsets

The game currently derives a fusion's base stats and learnset from its displayed
components. Milestone 3 retains that relationship after randomization:

- Generate X's and Y's base stats, then apply the normal fusion-stat formula to
  obtain C's stats.
- Generate X's and Y's level-up learnsets, then apply the Milestone 3 fusion
  merge rules to obtain C's learnset.

The base-stat policy is defined in `BASE_STAT_RANDOMIZATION.md`: normal species
preserve their BST with values from 5 through 255, Wonder Guard owns actual
one-HP behavior, and standard fusions retain the native formula without
post-processing. Learnset size, duplicate behavior, type preference, and early
damaging-move guarantees remain feature-specific decisions for Step 3.3.

## Fusion evolution model

A fusion evolution is a deterministic evolution of the complete fusion
species. It does not simply replace one component with that component's normal
or randomized evolution target.

For C composed of X and Y:

- An eligible X-side evolution branch remains an evolution opportunity with
  its original method category, such as level, item, or friendship.
- An eligible Y-side evolution branch remains a separate evolution opportunity.
- Each branch maps C to its own deterministic random custom-sprite fusion.
- The target does not need to contain X, Y, or either component's evolution.
- The target's generated base-stat total must be within the higher-strength
  range finalized in Step 3.4.
- The same source fusion and branch always resolve to the same target during the
  run.
- Targets and chains must be valid, reachable, and loop-free.

For example:

```text
C = X + Y
X-side level branch -> random custom fusion D = P + Q
Y-side item branch  -> random custom fusion E = R + S
```

D derives its randomized data from P and Q. E derives its randomized data from
R and S. Evolution never performs a fresh runtime roll, so postponing,
retrying, saving, or reloading cannot change D or E.

Selecting complete custom-sprite fusion targets preserves the global exclusion
of autogenerated fusion sprites. Detailed strength ranges, stage constraints,
branch conflict behavior, special evolution methods, and terminal-species
handling are finalized in Step 3.4.

## Implementation order

The shared ownership model is fixed before Step 3.1 so every cumulative release
can implement its data type consistently:

1. Step 3.1 implements generated component ability inheritance.
2. Step 3.2 implements generated component base-stat inheritance.
3. Step 3.3 implements generated component learnset inheritance.
4. Step 3.4 implements deterministic complete-fusion evolution targets.
5. Step 3.5 audits the combined rules and closes fusion-specific integration
   gaps rather than redefining data ownership.
