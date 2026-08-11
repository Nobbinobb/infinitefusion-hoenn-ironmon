# Step 5.1 validation

## Implemented boundary

The game now stores a versioned run ledger in `PokemonGlobalMetadata`. A
successful preset generation begins one active attempt with a monotonically
increasing per-save number, stable run ID, seed, and accumulated active game
time. `lost`, `won`, and `abandoned` all pass through one idempotent completion
method before the tracker is notified.

Manual F7 reads the checkpoint before abandoning the active attempt. It then
carries a snapshot of the completed ledger through the checkpoint load and
restores it before the next preset is generated. A missing or unreadable
checkpoint therefore leaves the current attempt active. The same snapshot and
restore boundary is available to the automatic-reset flow planned for Step
5.2.

Existing saves without a ledger migrate to zero aggregate counts, no invented
current attempt, and next attempt number 1. Active duration is accumulated from
the bundled runtime's monotonic clock. Save operations flush elapsed time, and
load operations resume from the serialized total without adding time while the
application was closed.

## Bundled-runtime matrix

After synchronizing all canonical scripts with `Build-Distribution.ps1`, a
temporary validator ran inside Pokemon Infinite Fusion's bundled runtime. All
26 assertions passed, covering:

- legacy-save migration and attempt 1 allocation;
- refusal to replace an already active attempt;
- monotonically increasing attempts 1, 2, and 3;
- stable run identity and seed ownership;
- active-time accumulation and closed-application exclusion;
- idempotent loss, win, and abandonment completion;
- exact started/lost/won/abandoned aggregate counts;
- preservation through metadata serialization; and
- replacement of an older checkpoint ledger with the carried F7 ledger.

The temporary validator and its report were removed after the pass.

## Regression and cleanup

- All 34 Ruby files match across canonical source, distribution, and the local
  game installation by SHA-256.
- The 41 tracker tests pass against the already-built test assembly.
- With the validator removed, the bundled game remained running at the
  ten-second hidden startup checkpoint.
- Only the exact game process started by the smoke test was stopped. No game
  process was already running.
- The first tracker test command stalled in build infrastructure. Every worker
  process it started was identified by exact PID and stopped before the
  no-build regression completed successfully.

Result: **Pass**.
