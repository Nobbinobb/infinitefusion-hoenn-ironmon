# Ironmon 0.7.2

This feature release adds a run-aware area lookup to the external tracker for
trainers, wild encounter slots, visible ground items, and hidden items.

## Area lookup

- Adds Trainer, Encounter, and Items tabs grouped by collapsible areas.
- Loads an area's detailed entries only when that area is opened.
- Shows total and completed counts for every area, including trainer defeats,
  discovered encounter slots, and collected items.
- Preserves discoveries in tracker storage for the current run and completed
  run archives.
- Refreshes discoveries while any tracker page is open through acknowledged,
  retryable game-to-tracker messages.

## Information visibility

- Keeps trainer party identities hidden until victory during ordinary runs;
  Debug and archived views show complete parties.
- Shows revealed trainer Pokemon with names, levels, local icons, and an
  enlarged sprite view. Concealed party positions use mystery tiles.
- Shows encounter probabilities and level ranges before discovery, then
  reveals the generated Pokemon and its icon for the exact discovered slot.
- Shows item locations and whether an item is hidden before collection while
  withholding its identity until collection.
- Marks hidden items in the game world so their locations are visible to an
  Ironmon player.

## Reliability and compatibility

- Uses a generated external area catalog rather than hard-coded Ruby or C#
  content data.
- Provides catalog generation, audit exports, and bundled-runtime validation
  for aligning a future Infinite Fusion update.
- Resolves live trainer parties through the same transformations used to build
  the actual battle party, including Ironmon Gym Leader additions.
- Keeps discovery persistence on the tracker side and validates requested
  discovery keys against their area before returning full details.
- Targets Pokemon Infinite Fusion 2 version 6.8.0.
