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

## Randomization simulation

`analysis/Run-Randomization-Simulation.ps1` runs a batch of seeds
inside Infinite Fusion's bundled runtime. It exercises the species, ability,
base-stat, move-access, evolution, and item generators, including a
deterministic sample of custom-sprite fusions. The JSON report separates hard
mechanic violations from statistical alerts and records the exact sampled
seeds and sample sizes needed to reproduce a finding. Ability results include
full observed counts, per-species eligibility expectations, and standardized
deviations instead of treating the most common result as suspicious by rank
alone.

The default report is written beneath the ignored `simulation-results/`
directory. A short run can be started with:

```powershell
tools\analysis\Run-Randomization-Simulation.ps1 -SeedCount 25
```

Each invocation samples fresh, unique seeds across Ironmon's full valid seed
space. The report and terminal output include a selection key. Reuse it to
reproduce exactly the same random sample:

```powershell
tools\analysis\Run-Randomization-Simulation.ps1 -SeedCount 25 -SelectionSeed 12345
```

For targeted debugging, request a consecutive range explicitly. Supplying
`StartSeed` without `SeedMode` also selects this sequential mode for backwards
compatibility:

```powershell
tools\analysis\Run-Randomization-Simulation.ps1 -SeedMode Sequential -StartSeed 1689 -SeedCount 1
```

Increase `SeedCount`, `FusionSamplesPerSeed`, and `ItemSlotsPerSeed` for a
deeper audit. A temporary progress file exists only while the runtime is
working and drives a live seed-and-phase progress bar in the terminal. It is
removed when the command finishes.

The runtime timeout scales automatically with the requested seed and sample
counts, up to 24 hours. Pass `-TimeoutSeconds` to replace the automatic budget
when a slower machine or an especially deep run needs more time.
