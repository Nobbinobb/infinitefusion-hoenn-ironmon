# Ironmon tools

Updater and Setup packaging, generated metadata, historical adoption inventories,
and protected signing are documented in
[Updater release production](../docs/guides/UPDATER_RELEASES.md).

## Main commands

- `Build-Distribution.ps1` synchronizes canonical Ruby scripts and runtime data
  into the local game and the ignored player-distribution staging directory.
  Pass `-GameRoot <owned-game-copy>` to target an acceptance installation and
  `-DistributionRoot <repository>/data/updater/acceptance/dist` to isolate its
  distribution output. The alternate output must stay below ignored `data`.
- `Build-TrackerRelease.ps1` runs tracker and bundled-runtime tests, regenerates
  release data, publishes both tracker deployment variants, validates both
  player packages, and creates their release archives and checksums. Versioned
  release artifacts are immutable: the command fails before doing any work when
  either archive or checksum already exists, so the tracker project version must
  be bumped deliberately before another release.
  Hosted workflows pass `-GenerationCacheDirectory` to reuse catalogs whose
  generator sources and resolved upstream inputs match exactly. The default
  local command still generates fresh data. Cache reuse verifies all generated
  file hashes and never skips tests or package validation.
- `Publish-Tracker.ps1` publishes the self-contained tracker application by
  default. Pass `-DeploymentMode RuntimeRequired` for the smaller package
  that requires the Windows x64 .NET 10 Runtime.
- `Test-GameRuntime.ps1` runs the complete bundled-game validation suite,
  including deterministic seeded-run import, against synchronized scripts. Pass
  `-BenchmarkFusionPredecessors` to run the isolated and sequential reverse
  evolution lookup benchmark in addition to the regression suite.
  Its `-GameRoot` also controls the preceding distribution build. This option
  alone does not isolate every test's Windows user-data access; use the dedicated
  updater guard fixture for save-isolated updater checks.
- `Test-UpdaterBootGuard.ps1 -GameRoot <synchronized-game-copy>` copies the
  selected bundled runtime into an owned fixture and isolates its save directory
  before loading game scripts. It tests compatibility and interrupted-update
  blocking, including byte-identical Ironmon saves and ordinary save reads.
- `Test-UpdaterGameStartup.ps1 -GameRoot <synchronized-game-copy>` requires an
  owned copy below ignored `data`; it verifies installed canonical hashes, the
  full game compatibility inventory and Ironmon/game-catalog script loading with
  isolated saves. It retains local evidence without navigating the title screen.

The [updater acceptance checklist](../docs/guides/UPDATER_ACCEPTANCE.md) records
automated coverage and the remaining native/clean-Windows checks. Put acceptance
reports under `data/updater/acceptance`; generated `tracker/**/TestResults` are
also ignored and must not be committed.

## Generation helpers

`generation/` contains the release-data entry points, their Ruby exporters,
and the shared game-runtime and modular-script loading helpers. They are
implementation details of the release pipeline but remain directly runnable for
focused dataset regeneration.

`Generate-Game-Adoption-Inventory.ps1` reads the exact Hoenn revision selected
for the release and writes the ZIP-recognition inventory and checksum manifest under ignored
`data/updater/baselines/`. `Build-TrackerRelease.ps1`, including `-GenerateOnly`,
generates and verifies these inputs with the other release data. The hosted catalog
and release workflows already check out the resolved newest game revision;
generation uses that checkout without resolving a different upstream revision.
Commit identity, file count and checksum are generated together, with no manual
version pins. The shared catalog artifact includes both outputs. Ordinary IDE and .NET builds
only embed the existing files and never trigger generation or Git operations.

`Test-GameAdoptionInventory.ps1` runs automatically in the full release gate and
ordinary CI validation; generation-only preparation does not repeat those tests.

Release and CI orchestration pass `-SkipBuild` to runtime test helpers after
preparing the distribution. Direct helper commands still prepare it by default.
The exact-input generation cache also contains move-power presentation and
battle color sheets. Only after verifying that cache does the release script use
`Build-Distribution.ps1 -ReuseGeneratedAssets`; scripts and package metadata are
still assembled from current source.
It checks two isolated Git revisions, explicit commit selection, refreshed metadata,
shallow CI checkouts, Unicode paths, CRLF alternatives and preservation of local modifications.

`Generate-Fusion-Predecessor-Index.ps1` produces the compact release-stable
superset used by reverse fusion evolution lookups. It indexes structural stage
and type compatibility only; seed-dependent BST and assignment decisions remain
runtime work.

`Generate-Player-Fusion-Worker-Catalog.ps1` exports the normal source stats,
ordered custom-fusion pool, normal evolution branches, type masks,
compatibility fingerprints, explicit autogenerated-sprite-author exclusions,
and Ruby-reference direct mappings and fusion-evolution assignments used by the
tracker's parallel obtainability worker. The release pipeline regenerates the
catalog before tracker tests, whose cross-runtime audit rejects any assignment
drift or missing autogenerated-sprite exclusion.

`Generate-Generation-Base-Catalog.ps1` exports the immutable, normal-species
game-data snapshot used by the generation-profile architecture. It records
species, types, abilities, moves, items, evolution methods, trainers, and
encounters, while deliberately excluding the independently changing custom
fusion pool. The command emits a SHA-256 component descriptor, audits normal
species scope and continuity, and verifies the descriptor against the exact
catalog bytes. `Test-GenerationProfile.ps1` validates the shared Ruby/C#
canonical profile vector in the bundled runtime.

`Generate-Custom-Fusion-Pool-Component.ps1` exports the independently changing
custom-sprite eligibility set as a validated row-major bitset. The compact
component retains the runtime pool's exact deterministic order and emits both a
SHA-256 descriptor and a source audit. `Generate-Generation-Profile.ps1`
regenerates the base and custom-fusion components, adds descriptors for the
area and obtainability source catalogs, and writes the canonical current
profile ID. The release gate regenerates this profile after its source audits
and installs it before generating type coverage. Coverage extraction selects
that exact profile and reads its packed custom-fusion component instead of the
mutable installation sprite index. The release then runs the shared
bundled-runtime contract test before the complete game suite.

The release build also regenerates `ITEM_RANDOMIZATION_GENERATED.csv` through
the bundled game runtime. Compatible item-data changes update the canonical
audit automatically; malformed pools, forbidden results, or exporter failures
stop the release. The audit includes every eligible item's exclusive category
and integer ground-selection weight, category ticket totals, and projected
slot counts without turning those projections into generation quotas.

The release also regenerates `OBTAINABILITY_FOUNDATION_GENERATED.csv` through
the bundled runtime, using the freshly generated temporary area catalog rather
than the previous canonical copy. It inventories permanent-acquisition routes,
encounter slots, player-reachable randomized item slots, evolution item
requirements, and required runtime interception points. Hook ownership,
unrecognized item-evolution consumption, invalid resources, or malformed input
stop the release. The same pass emits the fingerprinted
`data/obtainability_source_catalog.json` consumed by the game and embedded in
the tracker, so runs apply parsed semantic descriptors without rescanning map
event programming. See `docs/audits/OBTAINABILITY_FOUNDATION_AUDIT.md` for the
scope and the rules that future obtainability work must preserve.

## Cosmetic wardrobe audit and tests

`generation/Generate-Cosmetic-Audit.ps1` inventories player cosmetics, validates
matching graphics, derives point prices, and compares the previous report.
The release builder refreshes the audit automatically. Missing optional assets
are reported without imposing a runtime game-version or seed-compatibility gate.
See `docs/audits/COSMETIC_WARDROBE_AUDIT.md` for the scope and persistence rules.

`Test-Cosmetics.ps1` runs focused bundled-runtime tests with isolated profiles
and captures wardrobe screenshots under ignored `data/cosmetics-validation`.
It is also included in `Test-GameRuntime.ps1`.

## Obtainability foundation benchmark

`analysis/Run-Obtainability-Foundation-Benchmark.ps1` records preimplementation
costs for the exact access shapes needed by a graph filter and a single-Pokemon
lookup. It enumerates Classic and Remix wild slots under every species policy,
the run's exact randomized ground-item quantities, authored required-item gift
candidates, starter choices, normal evolution closure, player-fusion material
mappings at two bounded sizes, and target-to-material inverse lookups. Caught
fusion components participate in the relevant material scenarios; fused
starters do not. Pass `-FullMaterialScan` to add all normal materials reachable
in the largest mode/policy scenario. Reports are written beneath ignored
`simulation-results/`.
The benchmark is diagnostic and intentionally does not impose machine-speed
thresholds on release.

Bundled-runtime tools replace `Data/Scripts.rxdata` only for the lifetime of
their isolated game process. Before replacement they persist the normal loader
beside it as `Scripts.rxdata.ironmon-runtime-backup`; every runtime operation
and `Build-Distribution.ps1` restores an interrupted backup before continuing.
The tools reject a temporary Ironmon bootstrap as an input archive, preventing
one interrupted benchmark from becoming the baseline for later runs.

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


## Updater startup protection

`Test-Updater.ps1` provisions the pinned private Git test archive and runs the
updater and native Setup suites in disposable folders. Use `-Offline` after the
archive and dependencies are available; `-IncludeNetwork` additionally checks the
production network boundaries. Process fixtures run with test collections
serialized. Setup checks render the actual native controls into ignored test
output and test shortcut collisions in fixture directories, without changing the
player's desktop or installing Windows prerequisites.

`generation/Generate-Game-Compatibility.ps1` reduces the existing authenticated
Hoenn baseline to executable game code and data for the early game guard.
`Build-Distribution.ps1` invokes it during packaging, using the release workflow's
already selected commit. Generated outputs stay under ignored `data/updater/`;
ordinary IDE builds do not run this generator.

`Test-UpdaterBootGuard.ps1` copies the bundled runtime and game scripts into a
short, disposable `ironmon-guard` fixture under the system temporary directory,
including runtime libraries and the software renderer provisioned by hosted CI.
It isolates the save directory before
loading game code and tests compatibility, early folder interception and save
protection. Keeping the fixture outside the checkout avoids the bundled runtime's
startup path limits in deeply nested working folders. It never runs or changes
the installed game. The release gate and tracker CI invoke it after the existing
gameplay suite. Run Build-Distribution
before invoking it separately so the copied bootstrap matches canonical source.
