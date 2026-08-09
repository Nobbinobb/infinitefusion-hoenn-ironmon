# Step 3.3 Part 7 validation

Validated on 2026-08-09 against Infinite Fusion 6.8.0 and the tracker .NET 10
Windows target.

## Scope

- Record local metrics only for Pokemon and move-access interactions actually
  encountered during the run.
- Keep the metrics hidden while the run remains active.
- Archive and display the analysis after a loss or win.
- Preserve completed recipes created before metric recording.

## Recorded analysis

- Generated and unique entry counts for Learnset, Egg, TM/TR, and Tutor.
- Unique moves across all channels and cross-channel overlap.
- Earliest positive-power learnset move.
- The effective initial four at the encountered level.
- Level-1 total entry count and damaging count in the effective last four.
- Abstract and supported ordinary-tutor compatibility.
- Fusion component totals, duplicate removals, and growth over the larger
  component for each channel.
- Found and scripted machine acquisitions with compatible party members.
- Ordinary, Egg, and specialized Fusion Tutor visits, party compatibility, and
  the Pokemon taught.
- Move acquisitions by level-up, Egg, TM/TR, tutor, copy effect, or script.
- Aggregated direct and copy-effect move uses for player and enemy Pokemon.

## Performance boundary

Active encounter recording stores only identity, level, side, and occurrence
count. Complete generated channel comparison is
deferred until run completion. Machine and tutor compatibility is evaluated
only when those interactions occur. No complete fusion access is precomputed.

## Automated validation

- All **36 tracker tests** pass.
- Archive tests round-trip a representative metrics snapshot.
- Archive validation rejects an unsupported metrics schema explicitly.
- The Windows tracker application builds in Release with 0 warnings and 0
  errors.
- The English resource contains 378 unique entries with no duplicates, and all
  366 literal source keys resolve. Dynamic metric-source keys are also present.
- The canonical scripts were copied into the local installation and loaded by
  Infinite Fusion's bundled runtime in a hidden startup smoke test. The exact
  process started by the test was stopped afterward.

## Manual review boundary

The populated completed-run analysis panel was reviewed and accepted on
2026-08-09. Broader statistical interpretation will continue once later
randomizers are installed and full-run data is available.
