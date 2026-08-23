# Ironmon development and diagnostic access

`tools/Build-Distribution.ps1` synchronizes the canonical Ruby scripts and area
catalog into both the local game installation and the ignored `dist` staging
directory. Run it before testing game-script changes in Infinite Fusion.

`tools/Test-GameRuntime.ps1` performs the automated bundled-runtime checks. It
loads the synchronized ordinary scripts, validates area progress, deterministic
seeded-run import, and diagnostic capability authorization without leaving
development hooks in the game installation.

`tools/Build-TrackerRelease.ps1` also regenerates correctness audits. The
obtainability foundation audit is one of those release gates and must be
extended whenever obtainability later depends on a new acquisition source,
resource consumer, or runtime interception point. Performance measurements are
kept separate in `tools/analysis/Run-Obtainability-Foundation-Benchmark.ps1` so
machine-dependent timings remain diagnostic rather than release failures.
The release pipeline treats an existing versioned ZIP or checksum as immutable
and fails before generation, testing, publication, or packaging begins. Set a
new `ApplicationDisplayVersion`, numeric `ApplicationVersion`, and `Version` in
the tracker project before intentionally creating the next release.

`tools/analysis/Run-Obtainability-Prototype-Benchmark.ps1` measures both cold
foreground completion and a complete cold pass through the 4 ms cooperative
Ruby fallback scheduler. The standalone benchmark explicitly disables the
tracker mapping worker because it cannot fulfill the mapping work advertised
by a snapshot. Recipe preparation and deferred service construction are timed
separately so startup request overhead is not confused with benchmark fixture
setup. Its report must include the maximum slice and phase so a new indivisible
operation cannot be hidden by an acceptable average.

## Tracker builds

The tracker solution is `tracker/Ironmon.Tracker.slnx`. Normal Debug and Release
builds write only to ignored `bin` and `obj` directories. Player publication is
handled by `tools/Publish-Tracker.ps1`; the complete release pipeline uses
`tools/Build-TrackerRelease.ps1`.

## Diagnostic access

Normal support and testing should use a signed diagnostic-access token so each
information capability can be selected individually and optionally expire. The
player release contains the public verification key, but never the maintainer
generator or private signing key.

The generator source lives under `tracker/tools`. Build its Release project and
run `Ironmon.AG.exe` from the project's Release `bin` output. Keep the external
PKCS#8 P-256 private key outside the repository, game installation, tracker
distribution, logs, and source-control staging. Never copy generated
`.ironmon-access` files into `dist` or `release`.
