# Obtainability foundation audit and benchmark

## Purpose

This is the validation contract for run-specific Pokemon obtainability. It
deliberately does not define a Pokemon as obtainable merely
because it appears in an encounter table. The result is shared by the evolution
graph, searches, relations, and Pokemon lookup so every view answers the same
question from the same run recipe and cache.

The generated foundation audit remains an input inventory and drift detector,
not an obtainability result. Acquisition expressions, mutually exclusive
choices, repeatable rewards, quantities, and prerequisite ordering need
explicit semantic adapters before the service may prove a path possible. A
release that introduces an expression the service cannot classify is a failed
calculation, not a new Pokemon status.

## Intended mechanic contract

For one exact run recipe and seed, a Pokemon is obtainable when at least one
valid acquisition path can produce it. The fixed-point calculation must include:

- randomized starters and wild encounters;
- static, scripted, and gift Pokemon;
- NPC trades and eggs after they hatch;
- evolution duplicates such as Shedinja;
- player fusion from two obtainable normal materials;
- components made available by a caught fusion's supported reversal or
  unfusion behavior; and
- generated normal and fusion evolutions from an already obtainable
  predecessor.

Rental and other explicitly temporary Pokemon are acquisition events but not
permanent obtainable sources. A path can also require resources. Evolution
items and Poke Balls therefore form a quantity-bearing supply, not a Boolean
set. Alternative evolution methods are alternatives, while multiple consumed
resources along one path are cumulative. Choice events and other exclusive
sources must not be combined as though every branch could occur in one run.

The implementation exposes one run-scoped service with graph-wide and
single-target snapshots. The graph needs a complete set for filtering; lookup
needs one target plus a reason or witness path. Neither caller rebuilds
player-fusion mappings or independently reinterprets acquisition rules. The
service owns private speculative species and player-fusion mappings so
precalculation cannot mutate live gameplay state.

## Implemented behavior

The shared service proves deterministic wild and starter sources; authored
static, gift, trade, egg, and scripted-battle sources; generated normal
evolutions; caught-fusion reversal and unfusion; player-created fusions; and
generated fusion evolutions. It parses acquisitions in ordinary script commands
and event-condition expressions. Literal species, `fusionOf` orientation,
local variables, game variables, fossil conversion tables, and common-event
arguments have explicit adapters. Each authored source has a stable one-use
identity.

Random-component unfusion records both acquisition-order outcomes under one
exclusive constraint. Either component may therefore prove an existential run
path, but the same caught fusion cannot supply both components to a player-fusion
proof. Proof plans also retain mutually exclusive starter choices and cumulative
evolution-item quantities. Applying the release-generated semantic source
catalog and normal-evolution closure remain cooperative game work. Player-fusion
pairs and complete resource-aware
fusion-evolution closure run in the tracker and are cached per run.

While an active run and tracker connection are both available, safe map frames
advance the shared service with a 6 ms target budget. Background work pauses in
battles, event interpreters, reset/import transitions, non-map scenes, and when
the tracker is disconnected. Authored map pages and nested common events are
parsed once during release generation. Runtime calculation applies the compact
semantic descriptors without reopening those map files.

The evolution graph first renders its normal content and then renews a
short-lived foreground lease. While that lease is renewed, the game advances
the calculation with an aggressive 250 ms work budget. Waiting for the
tracker closure result is not runnable game-thread work and consumes no slice.
Closing the graph
or stopping a lookup lets the lease expire and returns the active run to
background work.
Pokemon lookup and search cards read passive status and do not create a polling
loop. A positive result includes a witness path and required evolution items.

Physical item slots, authored item gifts, direct Bag writes, standard and
specialized mart stocks, randomized TM gifts, and repeatable mining resources
contribute resolved quantity-bearing supply. During finite work a target is
**Calculating**. After the complete fusion-evolution closure it is exactly
**Obtainable** or **Not obtainable**. Any unclassified acquisition or resource
puts the service in an explicit error state, so release drift cannot leave a
large population permanently Calculating or silently turn it negative.

## Release-gated foundation audit

`generated/OBTAINABILITY_FOUNDATION_GENERATED.csv` is regenerated inside
Infinite Fusion's bundled runtime. The current audited installation contains:

| Inventory | Count |
|---|---:|
| Required runtime interception points | 23 |
| Gameplay map/common-event acquisition calls | 48 |
| Gameplay source acquisition calls | 33 |
| Starter choice slots | 3 |
| Classic and Remix encounter-table slots | 729 |
| Player-reachable randomizable item slots | 140 |
| Map/common-event item-resource calls | 283 |
| Gameplay source item-resource calls | 78 |
| Authored required-item gift calls | 11 |
| Effective item evolution branches | 62 |
| Registered evolution item types used by those branches | 25 |
| Source/resource adapter classifications | 18 |
| Generated semantic acquisition descriptors | 71 |
| Generated semantic resource descriptors | 147 |

The separate Shedinja dependency adds Poke Ball as a twenty-sixth relevant
resource type. The broader resource inventory records ground-item, gift,
standard-mart, and direct Bag-write entry points so mining, specialized shops,
and other bypass paths cannot disappear from semantic review merely because
they do not use randomized ground slots. The audit also verifies that the
Hoenn specialized-mart city adapter matches the installed runtime method.

The 140 item slots intentionally exclude six `pbItemBall` calls
on the inaccessible event-template map. The item-randomization audit retains
those calls because its job is source-hook coverage; this audit's job is to
inventory player-reachable supply inputs.

Generation fails if a required acquisition interception point is no longer
owned by the expected Ironmon runtime integration, if a gameplay acquisition or
resource entry point has no obtainability adapter classification, if an effective
item-based evolution method has no audited consumption semantics, if the fresh
area catalog is malformed, or if a referenced evolution item is invalid.

`tools/Build-TrackerRelease.ps1` generates a fresh area catalog first, passes
that temporary catalog into this audit, emits the fingerprinted
`data/obtainability_source_catalog.json`, and only then updates the canonical
files. The same catalog is shipped to the game and embedded in the tracker,
which rejects a game job carrying a different catalog fingerprint.
Consequently, release cannot accidentally validate against yesterday's area
data. Any later obtainability source adapter, resource consumer, or
semantic classification that correctness depends on must extend this generated
audit and remain a release gate.

## Performance baseline

`tools/analysis/Run-Obtainability-Foundation-Benchmark.ps1` exercises the
anticipated graph and lookup access shapes without implementing the service.
Its JSON report is written beneath the ignored `simulation-results/` directory.
The fixed seed is `1187411801` by default.

One full local bundled-runtime run produced this reference baseline:

| Operation | Work | Time |
|---|---:|---:|
| Enumerate one encounter mode/policy | 701-715 slots | 8-63 ms |
| Generate exact randomized ground supply | 140 slots | 2.0 ms |
| Inventory authored required-item gift candidates | all gameplay events | 84 ms |
| Build generated normal evolution graph | 287 branches | 72 ms |
| Compute normal reachability after graph build | 391-512 material seeds | below 1 ms |
| Resolve player fusions | 2,080 material pairs | 11.6 s |
| Resolve player fusions | 32,896 material pairs | 35.0 s |
| Resolve player fusions | 131,328 material pairs | 102.5 s |
| Invert one fusion target to material pairs | one target | about 3.1 s |

These timings are diagnostics, not release thresholds; wall-clock gates would
be machine-dependent and brittle. Correctness audits gate release. Benchmarks
are rerun deliberately when algorithms or access patterns change.

The largest scenario is Remix with Custom Fusions Only: direct catches expose
512 distinct normal materials through the free, one-time caught-fusion
unfusion choice. Fused starter choices are not included in that expansion
because Ironmon does not permit them to unfuse.

The result already constrains the design: encounter, item, and normal-evolution
work are cheap enough to build once, but player-fusion reachability must be
shared and cached. The graph and lookup must not each scan material pairs, and
single-target lookup should reuse a completed run index whenever it exists.

The live tracker performs the complete fusion-evolution closure after material
mapping and returns one compact final obtainability bitset and a delta-packed
executable-edge index. The game applies that result atomically; it does not
retain an incremental Ruby mapping or fusion-evolution fallback.

The release-generated tracker worker catalog contains 576 normal material
definitions, 287 conceptual evolution branches, and the exact 174,346-member
custom-fusion pool. It records the <code>japeal</code>
autogenerated-sprite-author exclusion, generator versions, compatibility
fingerprints, and Ruby-reference direct mappings and fusion-evolution
assignments across four seeds. Tracker tests require the parallel .NET
implementation to reproduce every reference exactly, including standard and
upward-rescue assignments. A full direct-mapping and fusion-evolution worker
job completes in about two seconds on the measured machine.

The graph and lookup clients reuse the same completed run closure. Repeated
filters and distant graphs therefore read membership and executable-edge
indexes without registering target-specific Ruby work or restarting a
predecessor traversal. Machine-dependent timing remains diagnostic rather than
a release threshold.

Run the bounded scaling benchmark with:

```powershell
tools\analysis\Run-Obtainability-Foundation-Benchmark.ps1
```

Include the complete material set for the larger of the Classic and Remix
normal-only run scenarios with:

```powershell
tools\analysis\Run-Obtainability-Foundation-Benchmark.ps1 -FullMaterialScan
```
