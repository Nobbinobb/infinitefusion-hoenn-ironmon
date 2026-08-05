# Step 2.5 validation

## Embedded-Ruby component checks

- [x] Caught-fusion components are represented in canonical species-ID order,
  independent of which one is currently the head or body.
- [x] Random Component uses the run seed, stable acquisition identifier,
  namespace, schema version, and canonical component IDs.
- [x] Repeating the selection and serializing/reloading the pending pivot retain
  the same component.
- [x] Reversing the candidate orientation with the same acquisition identifier
  retains the same component species.
- [x] Stable identifiers across representative acquisitions can select either
  component rather than always favoring head or body.
- [x] Random Component does not display either identity before the party commit;
  the ordinary completion message reveals only the retained result afterward.
- [x] Player Choice displays only the two component identities, exposes no
  summary screen, ignores cancellation, and stores the selected canonical
  index before result preparation.

## Result and transformation-right checks

- [x] Unfusion installs only the selected normal component, discards the fusion
  and unused component, leaves one usable Pokemon, and clears pending state.
- [x] Original component data is used when present; directly caught fusions use
  the game's wild-unfusion level behavior and a newly prepared component.
- [x] Component shininess follows its caught-fusion body/head marker, while a
  fusion caught shiny without component markers transfers shininess to the only
  retained component.
- [x] Keeping a caught fusion marks it processed and closes its right.
- [x] Reversing a caught fusion marks the result processed and closes its right.
- [x] An unfused result is normal and has no transformation right.
- [x] Player-created and processed caught fusions fail the caught-right check.
- [x] Reverse-then-unfuse, unfuse-then-fuse, and all later transformation chains
  are therefore unavailable through the pivot action table and marker checks.

## DNA item-path checks

- [x] DNA Splicers, Super Splicers, Infinite Splicers, and Infinite Splicers 2
  are blocked through both party-target and field-use handlers in Ironmon.
- [x] DNA Reverser and Infinite Reversers are blocked through their party-target
  handlers in Ironmon.
- [x] Blocked handlers return failure, do not consume the item, do not mutate the
  Pokemon, and explain the restriction.
- [x] Every original handler remains captured and is called unchanged whenever
  Ironmon is inactive.

The complete matrix ran inside Infinite Fusion's embedded Ruby runtime after
normal game-data initialization. The temporary runtime hook and output were
removed after the clean pass.

The canonical source, copy-ready distribution, and local installation were
then synchronized and compared byte-for-byte.
