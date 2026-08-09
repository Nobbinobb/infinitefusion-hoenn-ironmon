# Step 3.3 validation

Validated on 2026-08-09 against Pokemon Infinite Fusion 2 version 6.8.0 and
the tracker .NET 10 Windows target.

## Outcome

Milestone 3 Step 3.3 is complete. Its seven implementation parts cover
level-up schedules, Egg moves, machines, ordinary tutors, the specialized
Fusion Move Tutor, tracker inspection, and local completed-run analysis.

The detailed mechanical and compatibility evidence remains in the individual
`STEP_3_3_PART_*_VALIDATION.md` records. The reset performance pass is recorded
separately in `STEP_3_3_RESET_PERFORMANCE_PASS.md`.

## Final validation

- Every channel uses the accepted mechanically safe move pool and contextual
  restrictions.
- Normal species preserve their authored schedules and per-channel capacities
  without within-channel duplicates.
- The effective level-1 starting four contains at least one damaging move.
- Save/load and F7 behavior use versioned generator metadata; legacy runs keep
  native move access and incompatible metadata fails explicitly.
- Standard fusion access follows the accepted chronological and full-union
  rules.
- Runtime, authorized Debug, and completed-run Lookup agree across Learnset,
  Egg, TM/TR, and supported Tutor data.
- The populated completed-run analysis was manually reviewed and accepted.
- All 36 tracker tests pass.
- The Release tracker build completes with 0 warnings and 0 errors.
- The published tracker and synchronized game runtime pass hidden startup smoke
  tests without adding an error-log entry.

## Release artifact

- Archive: `release/Ironmon-v0.5.0-move-access.zip`
- Size: 72,014,836 bytes
- Entries: 452
- Canonical Ruby scripts: 28
- Tracker executable: present
- PDB and generated XML documentation files: absent
- Supported culture directory: `en-us` only
- SHA-256:
  `fa1ae7967ffee287959dbc9fd1849c25cc4650df76d3620bfa3200fa2676f644`

Two consecutive release builds produced the same checksum.
