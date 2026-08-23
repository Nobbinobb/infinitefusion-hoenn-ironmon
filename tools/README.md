# Ironmon tools

## Main commands

- `Build-Distribution.ps1` synchronizes canonical Ruby scripts and runtime data
  into the local game and the ignored player-distribution staging directory.
- `Build-TrackerRelease.ps1` runs tracker and bundled-runtime tests, regenerates
  release data, publishes both tracker deployment variants, validates both
  player packages, and creates their release archives and checksums.
- `Publish-Tracker.ps1` publishes the self-contained tracker application by
  default. Pass `-DeploymentMode RuntimeRequired` for the smaller package
  that requires the Windows x64 .NET 10 Runtime.
- `Test-GameRuntime.ps1` runs the complete bundled-game validation suite,
  including deterministic seeded-run import, against synchronized scripts. Pass
  `-BenchmarkFusionPredecessors` to run the isolated and sequential reverse
  evolution lookup benchmark in addition to the regression suite.

## Generation helpers

`generation/` contains the release-data entry points, their Ruby exporters,
and the shared game-runtime and modular-script loading helpers. They are
implementation details of the release pipeline but remain directly runnable for
focused dataset regeneration.

`Generate-Fusion-Predecessor-Index.ps1` produces the compact release-stable
superset used by reverse fusion evolution lookups. It indexes structural stage
and type compatibility only; seed-dependent BST and assignment decisions remain
runtime work.

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
deterministic sample of custom-sprite fusions and a bounded sample of paged
fusion-predecessor lookups. The JSON report separates hard mechanic violations
from statistical alerts and records the exact sampled seeds, sample sizes,
generator versions, and relevant fingerprints needed to reproduce a finding.
Ability results include full observed counts, per-species eligibility
expectations, and standardized deviations instead of treating the most common
result as suspicious by rank alone.

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

The simulator checks up to 12 paged fusion-predecessor lookups per run by
default. Use `-FusionPredecessorSamples` to change that bounded sample or set it
to `0` when only forward randomization statistics are needed.

The runtime timeout scales automatically with the requested seed and sample
counts, up to 24 hours. Pass `-TimeoutSeconds` to replace the automatic budget
when a slower machine or an especially deep run needs more time.
