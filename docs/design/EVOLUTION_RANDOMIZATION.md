# Evolution randomization

This document defines the Step 3.4 evolution-randomization rules. It
supplements the shared determinism, species ownership, and fusion component
rules in `POKEMON_DATA_RANDOMIZATION.md`.

Step 3.4 preserves the native evolution skeleton and activation opportunities
while replacing every eligible destination with a seeded, run-consistent
target. Generated evolution links are operational gameplay data. They do not
replace native family taxonomy used by breeding, baby-species lookup, stage
classification, or family exclusions.

## Scope

Step 3.4 covers:

- audited normal-species and mechanical-form evolution branches;
- deterministic normal-species destination generation;
- effective evolution-method preservation and explicit method conversion;
- deterministic complete-fusion evolution destinations;
- evolution checks, after-evolution effects, and instance-state continuity;
- generated incoming and outgoing evolution lookup;
- tracker Debug and completed-run target lists and graphs;
- existing-run compatibility and generator metadata; and
- local diagnostics needed to validate generation and runtime behavior.

Step 3.4 does not randomize breeding ancestry, Egg species, growth rates,
typing, forms, preserved method parameters, or the custom-sprite pool itself.
Triple and special fusions remain outside the randomizer.

## Native taxonomy and stage classification

The audited native evolution data defines four normal-species roles:

- **First stage:** no native pre-evolution and at least one outgoing branch.
- **Intermediate:** at least one native pre-evolution and at least one outgoing
  branch.
- **Final:** at least one native pre-evolution and no outgoing branch.
- **Standalone:** no native pre-evolution and no outgoing branch.

These roles are calculated before generated links exist. Mechanically distinct
forms use their audited form-specific evolution data. Cosmetic forms share
their base species' identity and mapping.

A native family is the complete connected family described by the audited
native graph. Native family membership remains authoritative for breeding,
baby-species selection, incense and Egg behavior, stage classification,
generated-target exclusions, and other mechanics that ask for biological or
native relatives.

Generated links control evolution eligibility, destinations, and generated
incoming/outgoing inspection only. A generated target may have several
unrelated generated predecessors, so generated links never redefine APIs that
assume one baby ancestor or a tree-shaped family.

## Preserved skeleton and conceptual branches

Only species with valid native outgoing branches receive generated outgoing
branches. Native terminal species remain terminal and never gain an evolution.

Evolution entries with the same source and original destination form one
**conceptual branch**. All alternate activation methods for that branch share
one generated destination. Level and item methods that lead from one source to
the same native destination therefore continue to reach the same generated
target.

Distinct original destinations remain distinct conceptual branches. Their
generated destinations must be different species and belong to different
native families within that source's branch set.

Different sources may converge on the same generated destination or family.
Family exclusion and branch uniqueness are local to the source currently being
generated. A later generated step may enter a family visited earlier in an
individual's path. This locality preserves one species-owned result regardless
of how the species was reached.

## Deterministic generation and storage

Normal branch sets are generated and validated when a new run or F7 reset
begins. All branches for one source are generated together so within-source
target and family uniqueness cannot depend on query order.

The normal graph is derived from:

- evolution-generator schema and rules versions;
- run seed;
- source species or mechanical-form identity;
- stable conceptual-branch identity;
- native taxonomy, evolution-source, and method-catalog fingerprints;
- eligible normal-target pool fingerprint; and
- the base-stat generator schema and source fingerprint.

Standard fusion branch sets are generated when a displayed fusion is first
encountered, inspected, or queried. All body- and head-side branches for that
fusion are generated together and cached for the session. Fusion generation
additionally includes the ordered displayed body/head identity, component side
and branch identity, custom-fusion pool fingerprint, and the generated
component-stat identities used by the fusion BST calculation.

On-demand fusion generation is pure and query-order independent. Reversal is
a completely different displayed fusion identity and receives an independent
mapping; it is not derived by reversing another mapping or its targets.

Generated mappings are not stored in the save. Save metadata stores the seed,
schema and rules versions, source fingerprints, target-pool fingerprints, and
dependent base-stat metadata required to reproduce them. Inspection, retrying,
postponing or cancelling evolution, save/load, encounter order, and process
restart consume no mutable random stream and cannot reroll a mapping.

## Normal target pool

Normal targets come from the validated normal base-species pool. The pool
includes ordinary species, standalones, legendaries, and mythicals. It uses
base forms only and excludes fusions, invalid identities, battle-only forms,
and special forms.

A source excludes every member of its own native family. The original
destination is therefore always excluded, together with native siblings,
ancestors, and other descendants. A generated branch may never retain its
original target.

Every target must be strictly stronger than the source by BST. Step 3.2
preserves each eligible normal species' BST, so registered and generated totals
are equal for normal-target filtering.

Every target must also share at least one type with the source Pokemon. The
comparison uses the source Pokemon's own types, not the types of its native
evolution destination.

Eevee's eight stone branches replace that source-type filter with the type
represented by the branch: Water Stone uses Water, Thunder Stone Electric,
Fire Stone Fire, Sun Stone Psychic, Moon Stone Dark, Leaf Stone Grass, Ice
Stone Ice, and Shiny Stone Fairy. The required stone type does not add Normal;
the target must contain the branch's elemental type.

## Normal stage selection

A first-stage source may target an intermediate, a final species from either a
two- or three-stage family, or a standalone. It may not target another
first-stage species.

The structural bucket is selected before the individual target:

- A branch whose original destination is terminal uses **60% terminal and 40%
  intermediate**.
- A branch whose original destination is intermediate uses **60% intermediate
  and 40% terminal**.
- An intermediate source uses **100% terminal** and can never target a first
  stage or another intermediate.

The terminal bucket combines final species from two- and three-stage families
with standalones. Candidate counts do not alter the category probability. If
one weighted category contains no otherwise valid in-range target, its weight
is redistributed to the available category.

These rules bound a normal generated chain to at most two generated steps:

```text
first stage -> intermediate or terminal -> terminal
```

Strictly increasing BST and forward-only stage roles prevent self-loops and
longer cycles by construction.

## Normal strength range and selection

For a native branch `source -> original destination`, let `S` be the source BST
and `R` be the original destination BST. The preferred inclusive target range
is:

```text
preferred minimum = ceil(R * 90 / 100)
preferred maximum = floor(R * 115 / 100)
effective minimum = max(preferred minimum, S + 1)
```

Every candidate in the chosen structural bucket and effective range has equal
selection weight. The asymmetric range deliberately permits more upside from
an evolution without explicitly weighting higher candidates.

Targets for separate conceptual branches are assigned without replacement by
species and native family. Generation solves the complete source branch set
rather than letting greedy branch order create a false dead end.

If no target in any allowed structural bucket lies in the preferred range, the
range preference is relaxed while these remain hard: target stage, strict BST
improvement, at least one shared source type, source-family and original-target exclusion, base-form
eligibility, and within-source species and family uniqueness.

The fallback selects candidates whose BST has the smallest absolute distance
from `R` across all allowed structural buckets. The 60/40 preference does not
apply during fallback. Equally close candidates are selected uniformly and
deterministically. If no complete valid assignment exists, run generation
fails rather than relaxing a hard rule.

## Evolution-method policy

Each original method entry is audited and either preserved or converted. A
converted entry remains an alternate activation method even when another valid
method already exists. Converted entries that become identical effective
triggers for the same branch are merged; diagnostics retain every original
method that contributed to the merge.

Every usable portion of a compound method is preserved. Requirements evaluate
the actual source Pokemon instance. Items are assumed reachable through item
randomization, so registered direct-use and held-item requirements are not
rejected because of campaign placement.

### Preserved methods

The following retain their exact registered parameters and conditions:

- ordinary level and level plus gender;
- day, night, morning, afternoon, and evening level methods;
- no-weather, sun, rain/fog, snow, and sandstorm level methods;
- cycling, surfing, diving, and generic dark-map level methods;
- Attack greater than Defense, equal Attack and Defense, and Defense greater
  than Attack;
- Silcoon/Cascoon personal-ID splits and the primary Ninjask method;
- ordinary friendship, gendered friendship, day/night friendship, and maximum
  friendship;
- held-item methods, including gender, time, and friendship combinations;
- direct-use item methods, including gender, time, and friendship
  combinations; and
- the Shedinja-style duplicate effect described separately below.

Exact levels, friendship thresholds, gender checks, time windows, weather and
movement states, items, item consumption, and stat comparisons remain
unchanged.

### Converted methods

Generic replacement levels depend on the native source role:

- first-stage source: **level 25**;
- intermediate source: **level 35**.

| Original method | Effective method |
|---|---|
| Exact `Location` or `Region` | Generic replacement level |
| `HasInParty` | Generic replacement level |
| `LevelDarkInParty` | Ordinary level at its existing threshold |
| `HasMove` or `HasMoveType` | Generic replacement level |
| `HappinessMove` or `HappinessMoveType` | Ordinary friendship at the native threshold |
| `Beauty` | Generic replacement level |
| Plain `Trade` | Generic replacement level |
| `TradeMale` or `TradeFemale` | Replacement level with the same gender restriction |
| `TradeDay` or `TradeNight` | Replacement level with the same time restriction |
| `TradeItem` | Level up while holding the same item; consume it afterward |
| `TradeSpecies` | Generic replacement level |

Move-dependent methods do not receive a generated move or type. Exact
location, region, Beauty, party-species, party-type, and every trade trigger
are converted unconditionally rather than audited per campaign.

An evolution method registered but unused by actual evolution data is reported
diagnostically. An unrecognized method used by an actual branch fails source
catalog preparation with the source, destination, method, and parameter.

## Branch activation and conflicts

One conceptual branch may expose several effective methods, all leading to one
generated destination. When more than one distinct branch is eligible at the
same time, Ironmon chooses uniformly using a stable hash of the run seed,
source identity, Pokemon personal ID, activation context, and sorted eligible
branch identities.

The same individual and eligible set resolve identically throughout a run. F7
may choose another branch because it creates a new seed. Native personality-ID
methods such as Silcoon and Cascoon retain their native personal-ID-only
behavior; the run-seeded selector is only the conflict resolver introduced by
Step 3.4.

Reachability is evaluated per branch. A branch is reachable when at least one
legally obtainable source instance and valid world state can satisfy its
effective requirement. One individual need not receive every gender,
personality, or stat-comparison outcome.

The runtime records the exact conceptual branch, effective trigger, and fusion
component side that activated. After-evolution effects run only for that
selected trigger. A held item is not consumed because an unused alternate
trigger for the same target mentions it.

## Nincada and duplicate evolution

The primary Ninjask-style result and Shedinja-style duplicate are distinct
conceptual branches with independently generated targets. They obey
within-source target and target-family uniqueness.

The duplicate branch uses Shedinja's original destination BST as its strength
reference, still requires a Poké Ball and free party space, and uses the same
preferred range, strict-strength rule, and closest-BST fallback as other
branches. It creates its independent result rather than copying the primary
target and continues through Ironmon's forced-pivot acquisition handling.

For a fusion containing Nincada, duplication is linked to the matching
component side's primary Ninjask-style branch. Evolving the unrelated component
does not trigger a duplicate. A Nincada/Nincada fusion activates only the
duplicate paired with the selected primary side.

## Fusion source branches

Every valid standard two-component fusion is an eligible randomized source,
regardless of whether its current sprite is custom or autogenerated. Triple
and special fusions are excluded as both sources and targets and retain native
or scripted behavior.

For displayed fusion `C = body X + head Y`, every conceptual branch of X
becomes a body-side opportunity and every branch of Y becomes a head-side
opportunity. Method preservation and conversion rules apply. The complete
fusion instance supplies level, gender, friendship, held item, calculated
stats, personal ID, and all other requirement state.

Body and head branches remain separate even when X and Y are the same species.
Simultaneously eligible fusion branches use the normal conflict resolver. The
player is not asked to choose head or body because neither side survives into
the unrelated complete-fusion target.

## Fusion target pool and stages

Fusion targets come only from the validated pool in `CUSTOM_FUSION_POOL.md`.
Autogenerated, alternate-only, malformed, triple, and special fusion targets
are never admitted, even as fallback.

A fusion candidate is:

- **Continuing:** at least one intermediate component and no first-stage
  component.
- **Terminal:** both components are final species or standalones.

Targets containing first-stage components are excluded. A target may contain
two related or identical component families when that displayed fusion passes
the other rules.

A target must share at least one resulting fusion type with the component that
is evolving. A Head branch compares against the source Head's types; a Body
branch compares against the source Body's types. The unevolving component and
the component's native destination do not supply the required type.

When the evolving fusion component is Eevee, its stone branch uses the same
elemental override as normal Eevee. The resulting complete fusion target must
contain the stone's required type instead of merely sharing Normal with Eevee.

Stage weighting follows the originating component branch:

- first-stage component whose original destination is intermediate: **60%
  continuing and 40% terminal**;
- first-stage component whose original destination is terminal: **40%
  continuing and 60% terminal**; and
- intermediate component: **100% terminal**.

The normal empty-bucket redistribution and closest-BST fallback rules apply.

## Fusion strength reference and exclusions

For one component-side branch, Ironmon calculates the generated BST of the
natural one-component fusion result. If body X evolves to X-prime while head Y
remains, the reference is `body X-prime + head Y`. That fusion is never used as
the actual destination.

Let `S` be the current displayed fusion's generated BST and `R` be the generated
BST of the natural one-component result. The custom target uses the same
inward-rounded 90%-115% interval and must have a generated BST strictly greater
than `S`. Reference and candidate totals use Step 3.2's generated component
vectors and Infinite Fusion's exact per-stat flooring formula.

Neither target component may belong to the native family of X or Y. This
excludes the source components, their natural evolutions, siblings, and every
other relative.

Separate branches from one displayed fusion must receive different exact
displayed fusion identities. They may use opposite orientations, share one
component or family, or use different members of the same component families.
Fusion targets have no native family identity beyond their exact ordered
displayed fusion for uniqueness purposes.

If no valid stronger custom target assignment exists after fallback,
generation fails explicitly. Autogenerated targets and hard-rule relaxation
are prohibited.

## Evolution lifecycle and transformation state

Normal and complete-fusion evolution preserve level, proportional progress
toward the next level, personal ID, nature, IVs, EVs, friendship, current moves,
ribbons, nickname, Poké Ball, markings, and the held item unless the selected
trigger consumes it. Raw experience is rebased onto the target's native growth
curve so evolution and the following experience gain cannot unexpectedly change
the Pokémon's level.

The target supplies its generated species-owned abilities, base stats, move
access, typing, and other species data. Evolution preserves the ability-slot
index where possible, applies the Step 3.1 missing-slot fallbacks, clears stale
forced or cached values, recalculates stats, and offers the target's generated
level-0 moves through the normal learning flow.

A complete-fusion evolution changes species directly. It does not rerun the
fusion procedure or merge component-instance data. It retains origin and
transformation state and never grants another pivot, reversal, unfusion, or
fusion-gamble right.

Generated behavior applies globally to eligible player, wild, trainer, and
story-owned Pokemon. Story flows that depend on a native destination receive
explicit compatibility handling rather than a context-dependent native graph.

## Tracker Debug and completed-run inspection

Generated destinations remain hidden throughout ordinary active play. They
are available only through authorized tracker Debug and completed-run lookup.
Inspection uses gameplay resolution, consumes no random values, and mutates no
run or discovery state.

### Target list

A normal Pokemon shows one list containing every candidate that could validly
have been selected for any conceptual branch. A standard fusion shows up to two
lists: **Head evolutions** and **Body evolutions**. Preferred candidates are
shown when they exist; only the closest valid fallback candidates are shown
when the preferred set is empty. Each candidate appears once across the
displayed source side.

A row shows only target icon, target name, and generated BST. The row and its
icon and name are clickable and navigate to the exact target species or fusion
orientation. Back navigation returns to the previous lookup.

### Evolution graph

Nodes show icon, name, and generated BST and are clickable lookup links. Each
conceptual branch has one edge labelled with all effective methods. Fusion
edges also identify `Head` or `Body`.

For a normal selected Pokemon, the graph contains its immediate generated
predecessors, the selected Pokemon, and its immediate targets. For a fusion,
the graph omits predecessors and shows the selected fusion with its immediate
head/body targets. Exhaustive fusion predecessors would require generating the
full ordered fusion-source space and are intentionally unavailable.

The UI does not show original destinations, original BSTs, buckets, preferred
ranges, fallback reasons, or generation internals. Those values may remain in
local validation diagnostics.

## Existing-run compatibility

A save created before Step 3.4 has no evolution metadata. Loading it leaves
native evolutions active so installing `0.6.0` cannot alter an in-progress run.
A new run or F7 records current metadata and enables generated evolutions.

A save using Step 3.4 rules version 1 or 2 is upgraded to rules version 3 when
its complete version-specific fingerprints and every unchanged dependency still
match. The upgrade preserves the run seed and Pokemon state, records current
metadata, and regenerates future normal and fusion mappings with the current
shared-type and Eevee stone rules. An already evolved Pokemon remains unchanged.

Other saves that declare evolution metadata must match the installed schema,
rules, source catalog, native taxonomy, normal pool, custom-fusion pool, and
dependent base-stat metadata. A mismatch reports the exact incompatibility
rather than falling back to native data or reinterpreting the graph. Completed
rules-version-1 and rules-version-2 recipes use the same exact migration for
authorized lookup.

## Local diagnostics

Internal validation may record source and branch identities, original and
effective methods, source/reference/target BSTs, preferred ranges, fallback and
bucket decisions, converted and merged entries, rejection and assignment
failures, conflict inputs, cache timings, and encountered evolution outcomes.
Active-run presentation never reveals hidden targets or aggregates.

## Review-gated implementation sequence

Step 3.4 is implemented in seven review-sized parts. Each part receives its own
`docs/validation/milestone-3/STEP_3_4_PART_<n>_VALIDATION.md` record. Work pauses
after validation for each part and does not begin the next part until the user
accepts the review boundary.

### Part 1: Audited source catalog and effective methods

- Build native taxonomy, stage, conceptual-branch, and method catalogs.
- Implement preserve, convert, and merge rules without replacing runtime
  destinations.
- Fingerprint source, taxonomy, method, and normal-target catalogs.
- Reject malformed branches and unknown used methods.
- Validate all 576 normal base species and supported mechanical forms in the
  embedded runtime.

### Part 2: Normal evolution graph generation

- Implement normal stage weighting, strength ranges, exclusions, uniform
  selection, full-source assignment, and closest-BST fallback.
- Generate the complete normal graph for new runs and F7.
- Add schema metadata, legacy behavior, cache reset, and reconstruction.
- Exhaustively validate strength, uniqueness, reachability, loop prevention,
  save/load, and F7 without replacing runtime checks yet.

### Part 3: Normal runtime evolution integration

- Route level, item, friendship, environment, and converted methods through
  generated branches.
- Add overlap selection and exact-trigger after-effects.
- Implement Nincada duplication, ability continuity, generated evolution
  moves, and instance-state preservation.
- Audit story-owned paths and runtime consumers while leaving fusion evolution
  for Part 4.

### Part 4: Complete-fusion evolution

- Build indexed continuing and terminal custom target pools.
- Implement on-demand displayed-fusion generation, reference BSTs, family
  exclusions, target uniqueness, and independent orientations.
- Replace the complete fusion at runtime, including simultaneous-side
  resolution, Nincada-side duplication, and transformation locks.
- Validate custom-only targets, strength, cache behavior, and standard sources.

### Part 5: Tracker target lists and protocol

- Extend authorized Debug and completed-run payloads with selected-target
  summaries, effective method labels, and paged valid-candidate queries.
- Add normal and separate Head/Body candidate lists with icon, name, BST,
  filtering, paging, click navigation, and Back behavior. Candidate lists use
  the preferred 90%-115% set, or only the closest fallback set when the
  preferred set is empty; selected destinations remain reserved for the graph.
- Confirm ordinary live responses expose no generated targets.

### Part 6: Evolution graph presentation

- Add the clickable one-step normal graph with predecessors and targets.
- Add the clickable fusion graph with current identity and outgoing Head/Body
  targets only.
- Render one edge per target with every effective method.
- Complete visual review for branching, convergence, standalones, long names,
  and fusion icons.

### Part 7: Metrics, integration, regression, and release

- Add local diagnostics and encountered-evolution metrics.
- Run cumulative normal, fusion, story, save/load, F7, legacy, performance,
  tracker, and information-boundary tests in the embedded runtime.
- Complete manual evolution, item, duplicate, navigation, and visibility
  checks.
- Build and verify `Ironmon-v0.6.0-evolutions.zip` and final validation evidence.

## Step 3.4 validation requirements

- Native branch count, conceptual destinations, terminal status, and preserved
  activation opportunities follow the documented skeleton.
- Every used method is recognized, preserved, or converted exactly as defined.
- Every normal target is outside the source family, permitted by stage,
  different from the original target, strictly stronger than its source, and
  shares at least one source type.
- Every Eevee stone branch replaces Normal with its documented elemental target
  requirement.
- Separate normal branches use different target species and native families.
- Preferred ranges use inward-rounded 90%-115% bounds; fallback chooses the
  closest valid stronger BST without weakening a hard rule.
- Normal graphs contain no loop, backward edge, or intermediate-to-intermediate
  edge.
- Alternate methods share one target, identical effective triggers merge, and
  overlapping branches resolve deterministically.
- Items and duplicate effects run only for the selected trigger and side.
- Standard fusion targets are validated custom sprites, contain no first-stage
  component, exclude both source families, are strictly stronger, and share at
  least one resulting type with the evolving component.
- An evolving Eevee fusion component applies its branch's stone-type override.
- Fusion references use the generated natural one-component result and the
  exact Step 3.2 formula.
- Reversed, directly encountered, pivot-generated, custom-sprite, and
  autogenerated-source fusions obey displayed-identity rules.
- Triple and special fusions remain native or scripted.
- Evolution preserves instance data, consumes generated species data, and
  cannot grant another transformation right.
- Normal mappings generate eagerly; fusion mappings generate on demand without
  lookup-order dependence.
- Save/load reproduces mappings and overlap decisions; F7 generates new ones.
- Legacy runs retain native evolutions and incompatible metadata fails.
- Debug, completed-run lookup, and runtime agree while ordinary live play
  reveals no generated targets.
- Lists and graph nodes show icons, names, and generated BSTs; graph edges show
  all effective methods and fusion side where applicable.
- Every implementation part is reviewed and accepted before the next begins.
