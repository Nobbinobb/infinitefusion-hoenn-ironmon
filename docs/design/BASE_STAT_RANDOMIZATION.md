# Base-stat randomization

This document defines the Step 3.2 base-stat randomization rules. It supplements
the shared determinism and fusion data-ownership rules in
`POKEMON_DATA_RANDOMIZATION.md`.

## Generation model

Base stats are deterministic, species-owned, and generated on demand. A normal
species or mechanically distinct form receives one six-stat vector derived
from:

- Base-stat generator schema version.
- Run seed.
- Base-stat namespace.
- Species or mechanical-form identity.
- The original six-stat source data and rules fingerprint.

Generated mappings are not stored in save data. A memory-only cache may retain
vectors for the current session. Save metadata stores the generator schema and
source-data fingerprint required to reproduce and validate the result.

Generation never depends on a Pokemon's personal ID, encounter order, lookup
order, preview order, ownership, or whether it is wild or trainer-controlled.

## Preserved total and distribution

Every eligible normal species or mechanical form preserves its original
base-stat total exactly. The total is redistributed across the six main stats
in this stable order:

1. HP.
2. Attack.
3. Defense.
4. Special Attack.
5. Special Defense.
6. Speed.

Each stat receives an independently derived positive deterministic weight.
After assigning the hard minimum to every stat, the remaining points are
apportioned according to those weights with integer arithmetic. Fractional
remainders and cap overflow are resolved deterministically in stat order after
weight ranking. This prevents platform floating-point behavior or generation
order from changing a result.

Normal species and mechanical-form values obey these hard limits:

- Minimum: `5`.
- Maximum: `255`.

The generator must reject source data whose total cannot fit within those
limits rather than silently changing its BST. Generated base HP is therefore
never `1`. The engine's separate Wonder Guard check remains the only path that
forces actual maximum HP to 1.

## Forms and special species

- Registered mechanically distinct forms use distinct generator identities and
  preserve their own original BST.
- Cosmetic forms share their base species identity when the engine does not
  provide distinct mechanical source data.
- Pumpkaboo and Gourgeist size categories are separate stat identities because
  the game assigns different stats to each size. The average category uses the
  registered species identity; Small, Large, and Super include the size in the
  identity and preserve that category's native BST.
- Special and triple fusions outside the standard two-component fusion range
  retain the game's native stats in Step 3.2.

Shedinja is not given a permanent one-HP base stat. It receives a normal
generated HP value while preserving its original total. Wonder Guard remains
responsible for setting the active holder's actual maximum HP to 1. Shedinja
without Wonder Guard, and any other species without Wonder Guard, uses its
generated HP normally.

## Fusion calculation

For a standard displayed fusion C with body X and head Y, generate X and Y
first and then apply Infinite Fusion's exact formula independently to each
stat:

```text
floor((2 * dominant) / 3) + floor(other / 3)
```

The dominant component is:

| Stat | Dominant component |
|---|---|
| HP | Head |
| Attack | Body |
| Defense | Body |
| Special Attack | Head |
| Special Defense | Head |
| Speed | Body |

The final fusion is not rescaled to its original BST and is not clamped after
the formula. Although X and Y each preserve their own BST, the final fusion BST
may rise or fall depending on whether their strongest generated values align
with their dominant fusion roles. Reversal changes those roles and therefore
may change both the distribution and the total.

The game's separate flooring is retained. Two component values of `5` produce
a final fusion value of `4`:

```text
floor(10 / 3) + floor(5 / 3) = 3 + 1 = 4
```

Four is therefore the effective minimum for a standard fusion stat. This is a
valid result and is not corrected to the normal-species minimum.

## Evolution and runtime ownership

Natural evolution targets use their own independently generated vectors until
Step 3.4 replaces the evolution graph. Stat roles and proportions do not carry
from the pre-evolution. Evolution recalculates the Pokemon's battle stats from
the target species vector through the normal game path.

Every runtime consumer of an eligible species' base stats uses the generated
vector while Ironmon base-stat randomization is active. Original values remain
available only to the generator, fingerprints, diagnostics, and explicit
original-versus-generated tracker presentation.

## Existing-run compatibility

An Ironmon save created before Step 3.2 has no base-stat generator metadata.
Loading it must leave base-stat randomization disabled so installing the update
cannot change a run in progress. Starting a new run or using F7 records the
current schema and source fingerprint and enables base-stat randomization.

A save that declares base-stat metadata but does not match the installed schema
or source fingerprint is incompatible. The game reports the incompatibility
instead of silently falling back to original or newly interpreted stats.

## Tracker inspection

The authorized tracker Pokemon Debug page's Stats subtab displays, for the
selected Pokemon:

- Original final stats.
- Generated final stats.
- Per-stat differences.
- Original and generated BST.
- Run seed, generator schema, minimum, maximum, and source fingerprint.

The table visually emphasizes generated values. Both Debug inspection and
post-run lookup can switch to generated-only bars or bars whose changed segment
is colored by positive or negative delta.

For a standard fusion, the page identifies whether the body or head is dominant
for each final stat. It does not duplicate either component's original or
generated stat table. The displayed body and head remain independently
navigable so their complete values can be inspected on their own pages.

Post-run lookup reconstructs the same original and generated final values from
the completed-run recipe without storing a species mapping.

## Step 3.2 validation requirements

- Every eligible normal species preserves its exact original BST.
- Every eligible normal stat is between 5 and 255 inclusive.
- Repeated resolution of one identity returns the same vector during a run.
- Save/load reproduces the same vector without a persisted mapping.
- F7 creates different vectors from the new run seed.
- Results do not depend on lookup, encounter, or preview order.
- Registered mechanical forms and Pumpkaboo/Gourgeist sizes use their documented
  identities and totals.
- Shedinja uses generated HP normally and reaches 1 actual HP only while Wonder
  Guard is active.
- Standard fusions use generated displayed components and the exact native
  formula without rescaling or clamping.
- Legacy saves without Step 3.2 metadata retain original stats.
- Incompatible declared metadata produces an explicit error.
- Tracker Debug and post-run lookup agree with runtime values while avoiding
  duplicated component stat tables on fusion pages.
