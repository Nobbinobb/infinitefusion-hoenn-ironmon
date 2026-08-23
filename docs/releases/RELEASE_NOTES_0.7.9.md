# Ironmon 0.7.9

Ironmon 0.7.9 adds fast, progressively loaded generated-evolution exploration,
strengthens run and tracker reliability, and reorganizes the implementation for
continued development.

## Generated evolution exploration

- Open generated evolutions in a dedicated graph dialog instead of calculating
  predecessors while the Evolutions tab loads. Candidate lists now appear
  first and generator details remain at the bottom.
- Resolve normal and fusion predecessors in eight-result pages. Fusion lookup
  starts from a release-generated structural index, then applies the run's BST,
  family, seeded ordering, and exact assignment checks. Invalid or missing
  custom-sprite fusions cannot enter the pool, index, or graph.
- Explore a bounded neighborhood around each selected Pokémon. The configurable
  depth defaults to one layer, loaded nodes are reused, and closing the dialog
  cancels outstanding work.
- Pan and zoom the canvas or maximize it to the monitor. Switch between the
  focused neighborhood and every loaded node without losing that choice when
  focus changes.
- Group nodes by native stage or fusion-component stages, order each level by
  generated BST, and choose expanded rows or one compact selectable BST range
  per level. Nodes per row are configurable and default to 15.
- Emphasize only the focused node's connections and requirements. Compact rows
  use counted range portals for connections whose endpoints are not currently
  visible.

## Randomization and gameplay correctness

- Change complete-fusion evolution ordering to rules version 4. Incremental
  Fisher-Yates ordering with rejection-sampled bounds keeps eligible positions
  uniformly random while allowing lookup to stop after the required prefix.
- Add an upward-only assignment rescue when ordinary preferred and closest
  ranges cannot produce a complete distinct fusion assignment. All custom
  sprite, type, family, stage, BST, and uniqueness rules remain mandatory.
- Randomize the forced Route 101 starter-rescue opponent through its scripted
  identity instead of reusing the current encounter-table result.
- Return the tracker to Player whenever the move menu first opens after any
  opponent replacement, even when the player's active Pokémon did not change.
- Make preset activation and checkpoint resets transactional so failed
  preparation restores the previous live attempt and generator state.

## Tracker responsiveness and reliability

- Load fusion material pairs separately after Overview renders and page them in
  groups of 10, preventing large reverse-mapping sets from delaying navigation.
- Load only the visible shared Pokémon-information tab and keep Debug Pokémon,
  Debug Lookup, and completed-run Archive on the same validated presentation
  path.
- Split transport, authorization, area, completed-run, event-processing, and
  request responsibilities into focused services. Add latest-request
  coordination, response caching, structural snapshot comparison, and atomic
  persistence for settings, knowledge, archives, and diagnostics.
- Preserve older completed-run recipes while removing the retired move-access
  and evolution analysis payloads and views.

## Source layout, validation, and packaging

- Organize the canonical Ruby source by subsystem and define its flat runtime
  filenames in one validated load-order manifest.
- Add repeatable randomization simulation and fusion-predecessor benchmark
  tooling, including bounded predecessor sampling in batch seed runs.
- Release two equivalent Windows x64 archives. `Ironmon-v0.7.9-win-x64.zip`
  includes the .NET runtime; `Ironmon-v0.7.9-win-x64-runtime-required.zip` is
  smaller and requires the matching Windows x64 .NET 10 Runtime.

## Compatibility

Ironmon 0.7.9 targets Pokemon Infinite Fusion 2 version 6.8.0. Existing 0.7
saves and completed-run recipes remain readable when their recorded generator
metadata is compatible. Pre-release saves using fusion evolution rules version
3 migrate to rules version 4 when every recorded catalog and dependency still
matches. Install the game scripts and tracker from the same archive.
