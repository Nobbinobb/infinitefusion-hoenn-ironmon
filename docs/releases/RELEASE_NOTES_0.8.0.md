# Ironmon 0.8.0

Ironmon 0.8.0 adds exact run-aware Pokémon obtainability throughout Lookup and
the generated-evolution graph, while moving the expensive fusion calculations
from the game’s Ruby runtime into the native tracker.

## Run-aware obtainability

- Classify every supported Pokémon as Calculating, Obtainable, or Not
  obtainable for the represented run. There is no unresolved or permanently
  calculating terminal state.
- Show one consistent compact obtainability mark on Pokémon search results,
  relation cards, fusion outcomes, evolution destinations and candidates, and
  graph nodes. The selected Pokémon uses the same state beside its name.
- Include direct encounters and gifts, authored acquisition calls, starters,
  caught-fusion reversal and unfusion, item-consuming evolutions, player fusion
  materials, and recursively executable fusion-evolution paths.
- Generate a release-time semantic acquisition and resource catalog so normal
  runtime work does not reopen and scan thousands of map event pages.
- Keep active and archived calculations isolated by run identity. Switching an
  archived run cancels or abandons the previous view’s request and starts or
  rejoins only the selected run’s native work.

## Native fusion and graph performance

- Move player-fusion material mapping, resource-aware proof closure, exact
  fusion-evolution assignment, and executable-edge indexing into parallel .NET
  tracker work.
- Start the compatible active-run assignment and material indexes during
  current-state recovery so most graph and fusion-material requests can reuse
  already completed background work.
- Return one compact atomic closure containing the obtainability bitset and a
  delta-packed executable-edge index. Ruby no longer applies progressive
  witness batches or continues a duplicate fusion closure.
- Reuse the completed closure across Pokémon cards, graph filters, and distant
  fusions instead of restarting target-specific calculations.
- Serve fusion-material reverse lookups and exact evolution predecessor
  assignments from cached native indexes, improving the first Overview,
  Evolutions, Head/Body candidate, graph, and material-list loads.
- Keep the game responsive after completion by publishing graph status changes
  immediately without delayed game-thread work or a later garbage-producing
  refresh pass.

## Evolution and Lookup interface

- Place the compact Open graph action with the evolution-candidate heading
  instead of using a large standalone button or an action inside the page tabs.
- Clearly label normal, Head, and Body lists as evolution candidates and retain
  their bounded server-side filtering and paging.
- Refresh every loaded graph node’s obtainability mark when the shared result
  arrives, without requiring the graph to be closed and reopened.
- Add a connection-area activity spinner and concise progress tooltip while
  native background obtainability preparation is running.
- Keep the completed tooltip compact and omit the no-longer-useful average
  fusion-rate diagnostic.

## Architecture and validation

- Reduce the game/tracker protocol to a fusion-closure work request and one
  final closure result. Remove retired batch offsets, witness streams,
  continuation phases, Ruby fallback counters, and mixed terminal states.
- Share one deterministic fusion-assignment recipe between active graph,
  candidate, material, and obtainability preparation.
- Remove obsolete Ruby-fallback and tracker-batch benchmarks that no longer
  represented the production architecture.
- Extend tracker, protocol, source-catalog, native-worker, graph, lookup, and
  bundled-game runtime coverage for the new responsibility boundary.

## Compatibility

Ironmon 0.8.0 targets Pokemon Infinite Fusion 2 version 6.8.0. Existing 0.7
saves, seeded-run tokens, and completed-run recipes remain readable when their
recorded generator metadata and installed game data are compatible. Install
the game scripts and tracker from the same 0.8.0 archive.
