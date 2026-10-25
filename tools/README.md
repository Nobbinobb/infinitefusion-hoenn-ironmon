# Ironmon tools

Updater and Setup packaging, generated metadata, historical adoption inventories,
and protected signing are documented in
[Updater release production](../docs/guides/UPDATER_RELEASES.md).

## Main commands

- `Build-AchievementPack.ps1 -DefinitionPath <private-json>` creates the encrypted
  achievement asset from private authoring data kept outside the repository.
- `Build-AchievementIcons.ps1 -DefaultIconPath <private-png>` embeds encrypted
  shared artwork; optional private mappings assign one image to several stable
  achievement identities.
  Use `-BasePackPath <existing-pack> -MappingPath <private-json>` to add or replace
  approved assignments while retaining the other encrypted artwork.
  Approved PNGs of at most 50×50 retain their exact bytes and pixels automatically.
  Larger masters are fitted directly to a 50×50 transparent canvas. Use the approved
  review preview whenever available; `"PreserveSourcePixels": true` rejects larger
  inputs instead of converting them.
- The tracker includes `Ironmon.Tracker.AchievementOverlay` for desktop-resolution
  achievement notifications. The game owns animation time and acknowledgements;
  renderer loss falls back to the game toast. The renderer opens earned artwork.
  Native renderer checks are in `tracker/tests/Ironmon.Tracker.AchievementOverlay.Tests`.
- `Build-AchievementArtwork.ps1 -MappingPath <private-json> -OutputRoot <new-directory>`
  builds encrypted 1024×1024 tracker detail artwork from approved original PNGs.
  Each private entry supplies `IconId`, `Path`, and `SourceSha256`. Rectangular
  masters fit the same transparent square without cropping or stretching.
  `-BasePackRoot <existing-directory>` retains unmapped artwork.
  `-IconPackPath <verified-small-icon-candidate>` can validate new assignments
  before either candidate replaces the canonical packs. Verify the complete
  candidate and source hashes before publishing its index and bounded parts to
  `data/achievement-artwork/`; the small icon pack and achievement rules stay unchanged.
- `Build-Distribution.ps1` synchronizes canonical Ruby scripts and runtime data
  into the local game and the ignored player-distribution staging directory.
  Pass `-GameRoot <owned-game-copy>` to target an acceptance installation and
  `-DistributionRoot <repository>/data/updater/acceptance/dist` to isolate its
  distribution output. The alternate output must stay below ignored `data`.
- `Build-TrackerRelease.ps1` runs native C# tests, regenerates
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

The exact-input generation cache also contains move-power presentation and
battle color sheets. Only after verifying that cache does the release script use
`Build-Distribution.ps1 -ReuseGeneratedAssets`; scripts and package metadata are
still assembled from current source.

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

`Generate-Custom-Fusion-Pool-Component.ps1` exports the independently changing
custom-sprite eligibility set as a validated row-major bitset. The compact
component retains the runtime pool's exact deterministic order and emits both a
SHA-256 descriptor and a source audit. `Generate-Generation-Profile.ps1`
regenerates the base and custom-fusion components, adds descriptors for the
area and obtainability source catalogs, and writes the canonical current
profile ID. The release gate regenerates this profile after its source audits
and installs it before generating type coverage. Coverage extraction selects
that exact profile and reads its packed custom-fusion component instead of the
mutable installation sprite index. Native C# tests validate the generated
profile and its retained cross-language reference data.

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
event programming. Current obtainability behavior is defined in the
[mechanics manual](../docs/IRONMON_MECHANICS.html#evolutions-obtainability-prototype).

## Cosmetic wardrobe audit

`generation/Generate-Cosmetic-Audit.ps1` inventories player cosmetics, validates
matching graphics, derives point prices, and compares the previous report.
The release builder refreshes the audit automatically. Missing optional assets
are reported without imposing a runtime game-version or seed-compatibility gate.
The [mechanics manual](../docs/IRONMON_MECHANICS.html#shared-cosmetic-wardrobe) defines
wardrobe behavior and persistence.

## Defense presentation data

Edit `resources/DEFENSE_PRESENTATION_RULES.json` for reviewed defense rules.
From the repository root, refresh them with `tools/generation/Generate-Defense-Presentation.ps1`.

The generator resolves public names and move metadata from the bundled game;
it does not infer new mechanics or re-audit battle handlers. Review changed
handlers when updating definitions, preserve concealed enemy state, and never
invoke mutating combat handlers to calculate presentation. Both generation and
runtime decoding use native JSON parsing rather than Ruby evaluation.

`data/defense_presentation.json` and
`docs/audits/generated/DEFENSE_PRESENTATION_GENERATED.csv` are generated outputs;
do not edit them by hand. A `modeled` entry means a definition exists, not that
every effect is covered. Release builds regenerate and validate both outputs;
ordinary distribution builds reuse a present catalog. Restart the game after a
refresh. Provenance hashes and dates do not block gameplay; unreadable data
disables only the optional overview. See the
[protocol](../tracker/PROTOCOL.md#defensive-overview) for its privacy contract.

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


## Native tests

Regression sources live in `tracker/tests` (C#) and `tests/runtime` (Ruby).
PowerShell test scripts and profile helpers have been removed from `tools`.
Use standard `dotnet test` commands with `tracker/tests/Quick.runsettings` or
`tracker/tests/Full.runsettings`; see [the tracker README](../tracker/README.md#validate-and-publish).
Core Quick/Full also run the small native Ruby entry point in `tests/runtime/Quick.rb`
through an xUnit host. The larger Ruby suites and native UI acceptance remain
separate; see the development guide for the bundled runtime selection.

Build, generation, signing, and publication scripts remain here. Production
metadata and archive checks are part of those operations; they validate actual
inputs and artifacts rather than running synthetic regression fixtures.
