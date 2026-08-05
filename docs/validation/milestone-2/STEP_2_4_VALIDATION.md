# Step 2.4 validation

## Embedded-Ruby mapping checks

- [x] The player-fusion pool is non-empty and contains only normal two-species
  fusions with custom sprites in both orientations.
- [x] Repeated A body + B head requests return the same result in one run.
- [x] B body + A head returns exactly the reverse of the shared A+B result.
- [x] One unordered pair creates exactly one persisted mapping entry.
- [x] Mapping does not depend on generation call order or Pokemon personal IDs.
- [x] Same-species input pairs produce valid custom-sprite results.
- [x] Serialized pivot state reproduces the same result after reload.
- [x] A different run seed changes generated mappings.
- [x] Every stored and returned result is revalidated as a non-special custom
  fusion rather than silently rerolled if it becomes invalid.

## Discovery and transaction checks

- [x] Generating a mapping does not make its result visible.
- [x] A successful fusion commit records discovery atomically and later action
  labels show the known oriented result.
- [x] A mismatched or failed commit restores the previous party and discovery
  state while retaining the original hidden mapping, preventing rerolls.
- [x] No preview screen exists before the irreversible Fuse choice.
- [x] Opposite orientation and repeated calls reuse the stored canonical result
  instead of invoking the deterministic selector again.

## Normal fusion-data checks

- [x] The result uses the normal weighted level calculation and averaged IVs.
- [x] Moves from both consumed Pokemon are combined automatically when possible;
  the normal forced move-selection screen is used when more than four unique
  moves are available.
- [x] Nature selection occurs only after the irreversible Fuse choice and
  cannot be cancelled.
- [x] Both held items are staged into a cloned Bag and returned only as part of
  the successful party transaction. A storage failure restores the original
  party and Bag.
- [x] Original-component experience, shininess, learned moves, ownership, and
  transformation markers remain attached to the prepared fusion result.

The checks ran inside Infinite Fusion's embedded Ruby runtime after normal game
data initialization. Two clean passes covered the mapping/discovery matrix and
the normal instance-data/held-item transaction. Temporary runtime hooks and
their output were removed afterward.

The canonical source, copy-ready distribution, and local installation were
then synchronized and compared byte-for-byte.
