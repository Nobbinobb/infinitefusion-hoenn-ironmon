# Obtainability foundation audit and benchmark

## Purpose

This is the validation contract for the run-specific Pokemon obtainability
prototype. It deliberately does not define a Pokemon as obtainable merely
because it appears in an encounter table. The result is shared by the evolution
graph and Pokemon lookup so both views answer the same question from the same
run recipe and cache.

The generated foundation audit remains an input inventory and drift detector,
not an obtainability result. Conditional events, mutually exclusive choices,
repeatable rewards, quantities, and prerequisite ordering need explicit
semantic source adapters before the prototype may prove a path possible.

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

## Prototype behavior

The shared service proves deterministic wild and starter sources, generated
normal evolutions, directly caught fusion reversal and player-choice unfusion,
player-created fusions, and generated fusion evolutions. Random-component
unfusion remains unresolved because its result depends on acquisition ordering.
Proof plans retain mutually exclusive starter choices and cumulative
evolution-item quantities. Player-fusion pairs, normal-evolution closure, and
fusion-evolution closure are processed as cooperative work units and cached per
run.

While an active run and tracker connection are both available, safe map frames
advance the shared service with a 4 ms target budget. Background work pauses in
battles, event interpreters, reset/import transitions, non-map scenes, and when
the tracker is disconnected. The authored-event inventory remains
foreground-only because it is conditional diagnostic input rather than a
proven source or usable resource and contains individual operations that cannot
be bounded to a game frame.

The evolution graph first renders its normal content and then renews a
short-lived foreground lease. While that lease is renewed, the game advances
the calculation in aggressive 250 ms chunks. Closing the graph or stopping a
lookup lets the lease expire and returns the active run to background work.
Pokemon lookup uses the same foreground lease when the user explicitly checks
one selected Pokemon. A positive result includes a witness path and required
evolution items.

Authored gifts, trades, eggs, static battles, specialized shops, and direct Bag
writes are inventoried but remain conditional candidates until their story and
resource prerequisites have semantic adapters. The prototype therefore reports
an unproven target as **Unknown**, never **Unobtainable**, while any audited
source or resource category remains unresolved. The graph label deliberately
says **proven**: filtering may hide an unresolved node, but does not claim that
node is impossible.

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
| Prototype source/resource adapter classifications | 18 |

The separate Shedinja dependency adds Poke Ball as a twenty-sixth relevant
resource type. Literal authored gift rows are marked as conditional candidates,
not guaranteed simultaneous inventory. The broader resource inventory records
ground-item, gift, standard-mart, and direct Bag-write entry points so mining,
specialized shops, and other bypass paths cannot disappear from the later
semantic review merely because they do not use randomized ground slots.

The 140 item slots intentionally exclude six `pbItemBall` calls
on the inaccessible event-template map. The item-randomization audit retains
those calls because its job is source-hook coverage; this audit's job is to
inventory player-reachable supply inputs.

Generation fails if a required acquisition interception point is no longer
owned by the expected Ironmon runtime integration, if a gameplay acquisition or
resource entry point has no prototype adapter classification, if an effective
item-based evolution method has no audited consumption semantics, if the fresh
area catalog is malformed, or if a referenced evolution item is invalid.

`tools/Build-TrackerRelease.ps1` generates a fresh area catalog first, passes
that temporary catalog into this audit, and only then updates the canonical
files. Consequently, release cannot accidentally validate against yesterday's
area data. Any later obtainability source adapter, resource consumer, or
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

The implemented-service benchmark uses the same fixed seed in the bundled
runtime. For Classic, Mixed, and random-component unfusion, the prototype found
332 normal materials and 55,278 unordered pairs. Initialization took 7.16 s and
the complete calculation took another 80.33 s, for 87.49 s total. Its 71 bounded
requests averaged 1.13 s; the 95th percentile was 3.18 s and the maximum was
5.77 s. Initialization, fusion-evolution warm-up, and material work were
separate requests in that pre-scheduler benchmark, so none crossed the
tracker's 10 s timeout on the measured machine.

The closest foundation sample mapped 32,896 pairs in about 35.0 s. The original
implemented service added proof-plan merging, resource and exclusive-choice
checks, and a complete graph-wide fusion-evolution closure, which explained its
higher cost. The current service keeps the universal material result shared but
evaluates fusion-evolution connections on demand for each loaded graph scope.
Only an individual Pokemon lookup that still lacks a direct proof requests the
complete fallback closure. The fixed Ruby fallback now completes the 55,278
material pairs and non-fusion setup in 41.35 s, or 42.38 s including benchmark
recipe preparation, instead of performing the unused 25 s graph-wide closure.

The release-generated tracker worker catalog contains 576 normal material
definitions and the exact 174,348-member custom-fusion pool. It records the
<code>japeal</code> autogenerated-sprite-author exclusion, version and pool
fingerprints, and 631 Ruby-reference mappings. Tracker tests require the
parallel .NET implementation to reproduce every reference exactly. On the
measured machine the representative 332-material, 55,278-pair worker pass took
649 ms.

The original tracker-batch estimate was invalidated after manual active-run
testing: its Ruby fixture generation and result resolution occurred in the same
game process before application timing, unintentionally warming the fusion data
that the real .NET worker leaves cold. It also underrepresented protocol
snapshots, excluded incoming batch parsing, and omitted graph-specific work. The
replacement benchmark generates the numeric fixture and 160 representative
graph edges in one bundled-runtime process, exits, then starts a clean game
process for setup, compact payload parsing, one initial and one post-batch
snapshot, validation, proof-plan application, repeatedly submitted graph-edge
classification, and foreground completion. Fixture generation is reported
separately and excluded.

Manual active-run diagnostics also retained 99 consecutive
<code>fusion_evolutions</code> responses after all material pairs had completed.
A passive Pokemon-card snapshot was registering its species as a complete-chain
lookup target, so merely opening a graph could start the expensive global fusion
evolution closure. Passive cards now read an existing proof without registering
that fallback; only the explicit individual-Pokemon check can request it.
Repeated graph polls are also deduplicated before resolving their species
endpoints, avoiding repeated construction of the same fused species records.
Bundled-runtime regression coverage verifies both invariants and runs in the
release pipeline.

The first follow-up active-run measurement still required about 29 s after the
graph rendered, despite the service-only result. The live path was making 27
foreground mapping exchanges, refreshing the UI after each, and issuing a
redundant work request between batches. The revised protocol transfers each
mapping as four consecutive numeric identifiers, raises the bounded foreground
batch to 8,192 pairs, and drains the next work description already returned by
the prior response. A representative pass therefore uses seven batches and
eight mapping snapshots instead of 27 batches and at least 54 request/response
crossings. A protocol test keeps the maximum compact foreground request below
the transport limit and is part of the release pipeline.

On the measured machine, the corrected graph-inclusive path used 4.93 s for
cold setup, 1.65 ms to register 160 graph edges, 31 ms to parse every compact
batch payload, 0.62 s to validate and apply all 55,278 pairs across seven
foreground batches, and 1.78 s for requested graph evolutions and final
authored-source work: 7.37 s of game-owned work. The measured 649 ms .NET worker
would make that approximately 8.01 s before
transport, graph construction and rendering, and frame scheduling. Those
excluded costs are stated explicitly; this benchmark is not labelled as
complete UI latency. Background application used 20 resumable slices averaging
4.008 ms with a 4.024 ms maximum for the 4 ms target. The game runtime can still
produce an atomic or garbage-collection outlier, so 4 ms remains a target rather
than a hard real-time guarantee. The worker and batch benchmarks remain explicit
diagnostics rather than machine-dependent release thresholds.

Run the bounded scaling benchmark with:

```powershell
tools\analysis\Run-Obtainability-Foundation-Benchmark.ps1
```

Include the complete material set for the larger of the Classic and Remix
normal-only run scenarios with:

```powershell
tools\analysis\Run-Obtainability-Foundation-Benchmark.ps1 -FullMaterialScan
```
