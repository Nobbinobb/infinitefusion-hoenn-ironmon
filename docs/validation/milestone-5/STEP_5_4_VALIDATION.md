# Step 5.4 validation

## Implemented behavior

The game emits an optional schema-version-1 attempt-statistics payload in
current-state and `run_started` messages and embeds the final payload in each
completed-run recipe. It includes attempt identity, active duration, result,
per-slot result totals, progress, healing, item-source counts, trainer species
frequency and names, and defeated-BST aggregates. Historical recipes remain
valid when the optional payload is absent.

Every completion stores its complete recipe in the save ledger before network
delivery. The next active attempt continues to expose that recipe for recovery,
so a tracker that missed the live completion can archive it even after an
automatic reset. Ledger normalization and checkpoint restoration preserve the
recipe.

The tracker archive distinguishes a new completion from a duplicate recovery.
A new live or recovered completion requests selection exactly once, navigates
to Lookup, and keeps that selection through the following battle cleanup and
immediate `run_started`. Duplicate reconnect messages update persistence
without overriding manual navigation. The Lookup screen shows full statistics
with expandable item and named-species breakdowns; legacy runs display an
explicit statistics-unavailable notice while retaining generated Pokemon
lookup.

Completed-run Lookup navigation remains stable through cleanup and immediate
attempt startup, then releases when the first enemy of the new attempt appears.
That encounter opens Enemy, while the following player move menu restores the
normal Player view.

## Bundled-runtime matrix

A focused validator ran after normal script initialization inside Pokemon
Infinite Fusion's bundled runtime. All 15 assertions passed, covering:

- schema, identity, item-source, and BST values in active statistics;
- active statistics in the current-state response;
- final statistics and updated save totals in the completed recipe;
- recoverable recipe storage before tracker delivery;
- distinct statistics for the immediately following attempt;
- retention of the missed completed recipe during automatic-reset startup; and
- recoverable-recipe preservation through ledger normalization.

The temporary validator and report were removed after the pass.

## Tracker regression and cleanup

- All 45 tracker tests pass, including statistics round trips, missing legacy
  statistics, archive persistence, schema rejection, exact newest-run
  selection, and duplicate recovery idempotency.
- The Windows tracker interface builds with zero warnings and zero errors.
- All 36 Ruby files match across source, distribution, and the local game
  installation by SHA-256.
- The validator-free game remained running at the ten-second hidden startup
  checkpoint.
- Only the exact game process started by the smoke test was stopped; no game
  process was already running.

Result: **Pass**.
