# Step 3.3 Part 6 validation

Validated on 2026-08-09 against Infinite Fusion 6.8.0 and the tracker .NET 10
Windows target.

## Scope

- Completed-run Pokemon Lookup exposes Learnset, Egg, TM, and Tutor groups.
- Authorized active Debug Lookup exposes the same groups.
- Player Debug and Enemy Debug use the same shared move-access presentation.
- Tutor output contains only moves backed by an available ordinary tutor or a
  specialized Fusion Tutor catalog.
- Completed-run recipes retain every source fingerprint required to reproduce
  generated move access.
- Recipes predating Step 3.3 retain native move-access lookup behavior.

## Automated game-runtime validation

The canonical scripts were copied into the local game and loaded by Infinite
Fusion's bundled runtime. The validation covered a normal Pokemon and a fusion,
then compared each returned channel directly with the active generator.

- Normal snapshot: 17 learnset, 14 Egg, 26 machine, and 0 supported tutor rows.
- Fusion snapshot: 36 learnset, 0 Egg, 62 machine, and 9 supported tutor rows.
- All 9 fusion tutor rows matched the generated specialized tutor catalogs.
- Unsupported abstract ordinary-tutor moves were excluded.
- Completed-run Lookup and active Player/Enemy Debug used identical generated
  move-access data.
- A changed TM source fingerprint was rejected as incompatible.
- A different run seed changed the generated move-access tabs.
- Snapshot construction measured about 1.4 ms for the normal Pokemon and about
  68 ms for the first fusion request, including first-use specialized tutor
  catalog generation that is cached afterward.

## Tracker validation

- All 35 tracker tests pass.
- The Windows tracker application builds with 0 warnings and 0 errors.
- Serialization round-trips all four channels, source labels, item/tutor
  acquisition metadata, move details, and tutor counts.
- Archive validation accepts complete new recipes and old recipes without any
  move metadata, but rejects partial new move metadata.

## Manual review boundary

Code and runtime contracts are ready for review. Visual confirmation of tab
navigation and move-detail presentation remains a manual tracker UI check.

## Review fixes

- Restored Physical, Special, and Status icons throughout the tracker. Their
  complete layout and color rules now also live in the always-loaded main
  stylesheet rather than depending on scoped-style packaging.
- Machine rows now follow the authored TM roster followed by the authored TR
  roster instead of generated compatibility order.
- Acquisition, move identity, and component source now occupy separate row
  columns; source text is no longer stacked beneath the level or machine item.
