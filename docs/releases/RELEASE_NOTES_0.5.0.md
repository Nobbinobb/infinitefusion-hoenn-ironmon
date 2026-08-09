# Ironmon 0.5.0

This release completes Milestone 3 Step 3.3 with deterministic move-access
randomization, expanded tracker inspection, and local post-run analysis.

## Move-access randomization

- Randomizes complete level-up schedules while preserving authored levels and
  entry counts.
- Ensures the effective level-1 starting four contains at least one damaging
  move, including species with more than four level-1 entries.
- Randomizes Egg moves, TM and TR compatibility, ordinary tutor compatibility
  and offerings, and both specialized Fusion Move Tutor catalogs.
- Uses one mechanically safe global pool without type preference or power
  progression.
- Preserves distinct per-species channel capacities without within-channel
  duplicates.
- Generates access deterministically from the run seed without storing complete
  per-species move mappings.

## Fusion access

- Combines displayed body and head level schedules chronologically.
- Uses the documented full-union and duplicate rules for Egg, machine, and
  ordinary tutor access.
- Keeps specialized Fusion Tutor compatibility deterministic for regular and
  legendary catalogs.
- Preserves individual Pokemon moves as instance-owned data while routing
  ordinary creation and relearning paths through generated access.

## Machines, tutors, and compatibility

- Retains the existing found and scripted TM-item shuffle, including Gym Leader
  rewards.
- Keeps HM rewards replaced by permanent field-tool items while HM moves remain
  eligible in the global move pool.
- Routes TM/TR use, Egg inheritance and tutoring, ordinary tutors, and the
  specialized Fusion Move Tutor through their generated channels.
- Keeps runs created before 0.5.0 on native move access unless a new run or F7
  reset records the current generator metadata.

## Tracker and post-run analysis

- Adds Learnset, Egg, TM, and supported Tutor tabs to completed-run Lookup and
  authorized Debug views.
- Restores Physical, Special, and Status icons and orders machine rows by the
  authored TM and TR rosters.
- Displays abstract and supported ordinary tutor counts without exposing
  unsupported tutor entries.
- Records encountered channel totals, overlap, initial moves, level-1 safety,
  tutor mismatch, fusion growth, machine and tutor interactions, acquisitions,
  and actual move use for completed-run analysis.
- Adds hierarchical English localization infrastructure with an explicit
  supported-culture build filter for future translations.

## Performance and compatibility

- Avoids reconstructing discarded old-run generators during F7 checkpoint
  restoration.
- Generates wild and trainer mappings on demand instead of enumerating every
  authored slot during reset.
- Corrects tracker scheduling for Infinite Fusion's microsecond runtime clock,
  restoring the intended reconnect and state-update throttles.
- Reduces measured reset restoration and generation work by approximately 33%
  in the local bundled-runtime benchmark.

This cumulative release retains ability and base-stat randomization and targets
Pokemon Infinite Fusion 2 version 6.8.0.
