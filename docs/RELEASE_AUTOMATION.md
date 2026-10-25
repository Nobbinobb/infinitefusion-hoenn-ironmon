# Release automation

The workflows in [`.github/workflows`](../.github/workflows/) validate pull
requests, build release candidates, and publish approved versions. For package,
metadata, and signing details, see the [updater release guide](guides/UPDATER_RELEASES.md).

## Pull requests

`tracker-ci.yml` runs for PR openings, updates, reopenings, and ready-for-review
events, including drafts. Routine PRs run important native C# smoke tests,
bundled Ruby smoke tests, offline Python sprite tests, release metadata checks,
and upstream snapshots.
They do not generate catalogs or run complete regression suites. The Ruby job
caches the pinned runtime and renderer, refreshes verified metadata, and loads
canonical source in an owned process. Full C# hosted validation with Ruby smoke
is an explicit manual choice. Developers run full local validation
before creating a PR. A version increase does not build a candidate on every
PR update; approved publication prepares and validates the release on `main`.

For a release PR:

1. Increase `Ironmon::VERSION` in `src/foundation/Core.rb` and the tracker app's
   `ApplicationDisplayVersion`, `Version`, and numeric `ApplicationVersion` in
   `tracker/src/Tracker/Ironmon.Tracker.App/Ironmon.Tracker.App.csproj`.
2. Add `docs/releases/RELEASE_NOTES_<version>.md` headed `# Ironmon <version>`.
   Update the player version references in the repository README,
   `docs/DISTRIBUTION_README.md`, and `docs/guides/INSTALLATION.md`.
3. Review local full-test evidence and release notes before merging. Review the
   generated candidate artifacts and fusion-pool comparison before publication. Ordinary feature and fix PRs keep
   their version unchanged.

Build and publication permissions remain separate. PR code runs with read-only
tokens; publication checks that its source is an approved commit on `main`.
Repository rulesets and environment permissions must be checked in GitHub when
changing release policy; source files do not prove their current configuration.

## Fresh inputs and reuse

Each validation resolves the upstream Hoenn revision and current sprite/settings
metadata. Downloads are validated by path, format, length, and SHA-256 before
installation. Refresh failures do not fall back to bundled metadata.

An exact-input cache reuses only explicitly listed generated catalogs and audit
reports. Gameplay source, generators, resources, and upstream fingerprints form
its identity. Restored files must match their manifest hashes. Tests and package
assembly still run; executables, signing material, and test results are not
restored from this cache.

Candidates carry source and upstream identities. Publication reuses their bytes
only while those identities and release history still match. Otherwise it
dispatches a fresh full build of the approved source. A final upstream change
triggers another full gate, bounded to three refreshes; repeated changes or
failed checks stop publication.

## Publication and recovery

Merging a version increase starts `release.yml`. Its push filter watches the
tracker app project; the version check skips publication when no increase is
present. Manual dispatch accepts an approved 40-character `source_commit` and
normally uses `refresh_attempt` of `0`.

The publisher verifies remote sizes and SHA-256 digests for all seven assets
before publishing the draft: both player ZIPs, Setup, `update-manifest.json`,
`update-manifest.sig.json`, `update-data.json`, and `SHA256SUMS.txt`.

The installer is named `Ironmon-Setup-win-x64.exe`; its binary retains the build
version. It defaults to latest, with verified historical selection as described
in the installation guide. Older versioned Setup downloads retain latest-only
behavior.

Identical retries and matching partial drafts can resume. Conflicting tags,
assets, or published versions require investigation; published files are never
overwritten. A newer published version also prevents an older version becoming
latest.

For failed PR checks, fix the source and rerun validation. For a transient
post-merge failure, rerun the failed workflow or dispatch `Publish release` on
`main` for the approved commit. An expired or changed candidate is rebuilt
automatically. Keep native installer/update acceptance separate from automated
checks; use the [acceptance guide](guides/UPDATER_ACCEPTANCE.md).

## Scheduled maintenance

- `upstream-watch.yml` checks every six hours and refreshes eligible open PR
  validation when upstream inputs change or expire. Old runs outside GitHub's
  rerun window need a branch update. Forks retain contributor approval.
- `sprite-size-estimates.yml` measures sprite download sizes daily and writes
  the informational `sprite-size-estimates` data branch. Failed scans retain
  the previous complete estimate; they do not modify versioned release assets.

Schedules can be delayed. Each publication independently checks fresh inputs.
Workflow files define artifact retention; download evidence before it expires.
Use an isolated game checkout for local release validation: the default build
commands synchronize scripts into the parent game installation.


Native C# tests are invoked directly with `dotnet test`. PowerShell regression
scripts and their offline automation fixtures have been removed. An xUnit host
executes the four-group native Ruby smoke entry point; larger Ruby regressions
remain separate.
