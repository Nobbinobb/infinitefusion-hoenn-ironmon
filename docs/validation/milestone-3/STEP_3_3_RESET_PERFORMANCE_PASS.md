# Step 3.3 reset performance pass

Validated on 2026-08-09 against Infinite Fusion 6.8.0.

## Changes

- F7 checkpoint loading skips old-run Ironmon generator reconstruction because
  `apply_preset(:f7_reset)` immediately replaces that state.
- Current species mappings are generated and retained on first use instead of
  precomputing every wild table and trainer party at reset time.
- Base-stat source validation is reused after its first successful process-wide
  validation.
- Tracker scheduling converts the runtime's microsecond `System.uptime` value to
  seconds before applying reconnect, error-log, and state-update intervals.

Ordinary save loading still performs all compatibility checks, migrations,
difficulty enforcement, HM conversion, and diagnostics. The shortcut exists
only inside the F7 checkpoint restore boundary.

## Runtime benchmark

The benchmark loaded the existing `IronmonCheckpoint_File_H.rxdata` without
saving over it. Temporary benchmark scripts and output were removed afterward.

Previous equivalent work:

- full checkpoint load and old-run reconstruction: 1.611 seconds;
- new-run generation without eager maps: 0.317 seconds;
- eager wild and trainer mapping: 0.340 seconds;
- estimated total: 2.268 seconds.

Optimized path:

- reset-specific checkpoint restoration: 1.117 seconds;
- new-run generation: 0.392 seconds;
- total before scene and save overhead: 1.509 seconds.

The measured reduction is approximately 0.76 seconds, or 33%. Individual runs
vary with filesystem and runtime conditions.

## Correctness checks

- A new run receives a different seed.
- Wild and trainer mapping stores begin empty.
- Repeating the same wild context returns the same species and retains one map
  entry.
- Repeating the same trainer context returns the same species and retains one
  map entry.
- Completed-run lookup remains independent of stored current-run maps.
- A 100 ms sleep advances the runtime clock by approximately 100,027 units,
  confirming its microsecond scale.

## Manual review boundary

The bundled runtime benchmark and mapping assertions pass. The remaining check
is the player's normal F7 timing and starter-selection feel with the tracker
running in the actual UI flow.
