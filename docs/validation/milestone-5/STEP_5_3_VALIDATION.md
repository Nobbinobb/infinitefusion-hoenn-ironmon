# Step 5.3 validation

## Implemented behavior

Every active attempt owns a schema-version-1 statistics record. It retains
battles completed, the highest usable player Pokemon level reached, badges
earned, actual and wasted item healing, consumed resources by item and source,
trainer species frequency with display names, and defeated trainer Pokemon BST
aggregates. The existing ledger remains authoritative for attempt identity,
seed, duration, result, and per-slot result totals.

Battle statistics use one generated token per battle. Completion and each
opposing Pokemon identity are recorded idempotently, so repeated callbacks,
switch-ins, tracker updates, and reconnects cannot inflate the totals. Trainer
species and BST values come from the displayed generated Pokemon data, which
also covers fused identities.

Bag resources count only when their quantity decreases after a successful use.
Battle-item hooks count consumed Bag items and Poke Balls at their successful
gameplay boundaries. Held resources retain whether they originated with the
player through theft, transfer, and switching, and count when the battle engine
consumes them. Enemy items, item displacement, failed uses, and non-item
healing are excluded.

## Bundled-runtime matrix

A focused validator ran after normal script initialization inside Pokemon
Infinite Fusion's bundled runtime. All 29 assertions passed, covering:

- default statistics and persistence through normalization and completion;
- monotonic highest-level and badge progress;
- battle-completion idempotency;
- trainer encounter uniqueness, frequency, distinct counts, and ties;
- defeated count, exact BST total and average, and tied extrema species;
- combined Bag and Held item totals with source and item breakdowns;
- actual healing, over-cap wasted healing, and non-item-healing exclusion; and
- player-owned held-item provenance through theft and a same-battle switch.

The temporary validator and report were removed after the pass.

## Regression and cleanup

- All 36 Ruby files match across source, distribution, and the local game
  installation by SHA-256.
- All 41 tracker tests pass.
- The validator-free game remained running at the ten-second hidden startup
  checkpoint.
- Only the exact game process started by the smoke test was stopped; no game
  process was already running.

Result: **Pass**.
