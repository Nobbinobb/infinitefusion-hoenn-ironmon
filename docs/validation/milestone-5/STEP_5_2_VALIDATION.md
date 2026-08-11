# Step 5.2 validation

## Implemented behavior

Battle decisions 2 and 5 complete an active attempt as `lost` through the
game-owned idempotent lifecycle, regardless of the base game's `canLose`
setting. Completion still occurs inside the battle event so the tracker can
archive the result immediately, but reset and lock enforcement wait for a map
scene update after the current event interpreter finishes native cleanup.

With automatic reset disabled, a failed attempt blocks player movement and
interaction, new wild and trainer battle cores, and Pokemon acquisition
boundaries. The map event may finish its existing cleanup without accepting
new player input. F7 is checked before the lock and remains available. Tracker
updates continue through the independent graphics update hook.

Configuration schema 3 adds the persistent `automatic_reset` Boolean. It
defaults to false for new and migrated saves, appears in the pre-run screen and
confirmation, is included in diagnostics, and is retained by the existing F7
configuration snapshot.

With automatic reset enabled, the first safe map update requests the existing
checkpoint-reset flow without prompting. The completed ledger and tracker
recipe exist before checkpoint loading begins. A successful reset suppresses
the ordinary in-game reset notice. Read, generation, and save errors retain the
failed-run lock and use the existing actionable messages. A failed reset is not
retried on every frame. `won` and `abandoned` attempts do not activate this
path.

## Bundled-runtime matrix

A focused validator ran after normal script initialization inside Pokemon
Infinite Fusion's bundled runtime. All 27 assertions passed, covering:

- schema-version-3 defaults and schema-version-2-style hash migration;
- preservation of existing wild, trainer, and unfusion choices;
- automatic-reset opt-in and save serialization;
- idempotent loss completion and failed-run lock activation;
- one-time manual-lock notice behavior;
- one-shot deferred automatic-reset scheduling without a prompt;
- retention of the lock when automatic reset cannot start;
- central acquisition rejection after loss;
- wild and trainer battle-core rejection, outcome propagation, and rule
  cleanup;
- player movement-command and interaction blocking;
- silent successful automatic-reset notice handling; and
- automatic-reset retention through the checkpoint configuration snapshot.

The temporary validator and report were removed after the pass.

## Regression and cleanup

- All 35 Ruby files match across source, distribution, and the local game
  installation by SHA-256.
- All 41 tracker tests pass.
- The validator-free game remained running at the ten-second hidden startup
  checkpoint.
- Only the exact game process started by the smoke test was stopped; no game
  process was already running.

Result: **Pass**.
