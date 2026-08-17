# Ironmon tools

## Main commands

- `Build-Distribution.ps1` synchronizes canonical Ruby scripts and runtime data
  into the local game and the ignored player-distribution staging directory.
- `Build-TrackerRelease.ps1` runs tracker and bundled-runtime tests, regenerates
  release data, publishes the tracker, validates the player package, and
  creates the release archive and checksum.
- `Publish-Tracker.ps1` publishes only the self-contained tracker application.
- `Test-GameRuntime.ps1` runs the complete bundled-game validation suite,
  including deterministic seeded-run import, against synchronized scripts.

## Generation helpers

`generation/` contains the release-data entry points, their Ruby exporters,
and the shared game-runtime and modular-script loading helpers. They are
implementation details of the release pipeline but remain directly runnable for
focused dataset regeneration.

The release build also regenerates `ITEM_RANDOMIZATION_GENERATED.csv` through
the bundled game runtime. Compatible item-data changes update the canonical
audit automatically; malformed pools, forbidden results, or exporter failures
stop the release. The audit includes every eligible item's exclusive category
and integer ground-selection weight, category ticket totals, and projected
slot counts without turning those projections into generation quotas.
