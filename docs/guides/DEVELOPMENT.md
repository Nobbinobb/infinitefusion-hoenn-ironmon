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

## Git and pull-request workflow

Create each change from an up-to-date `main` branch. Use `feature/<name>` for
new behavior, `fix/<name>` for defects, `issue/<number>-<name>` when an issue is
the primary work item, and `release/<version>` for release preparation. Keep
version numbers unchanged on feature, fix, and issue branches.

Push the working branch and open a pull request into `main`. The repository's
GitHub Actions workflow first downloads the newest Hoenn `releases` revision and generates
catalogs from the source being tested, using `tools/Build-TrackerRelease.ps1
-GenerateOnly`. Both tracker test jobs consume those catalogs from the same
workflow run. They never restore catalogs from a previous Ironmon release.
Generated artifacts remain outside source control. Tracker-only catalogs expire
after one day; upstream snapshots and release candidates are retained for seven
days. Each run records the game revision and refreshed online sprite metadata.
Tests compare generated metadata with those resolved inputs. Publication reuses
a tested candidate only when its source tree and upstream fingerprint still
match; otherwise it rebuilds and fully retests the approved source.

After the desired feature and fix pull requests have merged, create a release
branch from the updated `main`. Add the new release notes and synchronize the
Ruby version, both tracker project versions, package documentation,
installation guide, protocol examples, and distribution release-note pointer.
Open the release pull request before producing packages so the final scope and
wording can be reviewed.

The release PR runs `tools/Build-TrackerRelease.ps1` on a hosted runner and
produces both packages, checksums, provenance, and audit evidence. Review the
candidate and fusion-pool comparison, then merge after all four required checks
pass. Merging the version increase starts automatic publication. A changed
upstream input triggers a complete rebuild and retest before publication; a
failed gate publishes nothing. No manual tag or local package upload is needed.

See [Release automation](../RELEASE_AUTOMATION.md) for the current workflow and
recovery instructions. Local runtime or release validation must use an isolated
game checkout when another worktree shares the installed game's parent directory.

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

The generator uses the tracker's dark presentation and shared outline icons.
Permission groups scroll within the editor without changing the window size;
development controls remain individually selectable. Load a signing key, choose
a preset or individual grants, then use **Review & generate** to inspect the
direct and effective grants before signing. The result dialog supports copying,
saving, and creating another token while retaining the form choices.
**Choose file** opens the owned Windows picker directly; cancellation keeps the
loaded key. Copying flushes token text to the Windows clipboard before reporting
success, so the token remains available after the generator exits.

### Diagnostic Tools

The Tools area uses the shared redesigned Pokémon research views for current
Pokémon inspection and active-run lookup. Current inspection also retains the
individual Pokémon's nickname, level, gender, held item, form, identity, and
active ability-slot details. Pages and controls remain filtered by their
individual diagnostic capabilities.

Development applies level, ability, move, and evolution selections immediately.
Use the remove button in an occupied move slot to clear it; the Pokémon must
retain at least one move and cannot have duplicate moves. Failed actions restore
the last confirmed player values. Giving an item still requires selecting an
item, specifying a quantity, and pressing Give. Only available evolution
directions are shown: one direction fills the section, and the section is
omitted when neither direction is available.

Run diagnostics groups generator fingerprints and item metadata into expandable
sections. Protocol uses the same disclosure component for raw state and history;
copying an entry does not toggle it. Report copy and export include only authorized
groups, and clearing history leaves the current state and persisted knowledge
available under their existing grants.
