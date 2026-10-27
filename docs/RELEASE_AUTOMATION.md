# Release automation

The workflows in [`.github/workflows`](../.github/workflows/) validate pull
requests, build release candidates, and prepare signed test releases. For package,
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

Candidates carry source and upstream identities. Test preparation builds a fresh
candidate with a sequence above authenticated stable and previous test builds.
Release preparations are serialized, and candidate reuse is disabled so an older
test helper identity cannot be assigned to changed bytes. A final upstream change
triggers another full gate, bounded to three refreshes; repeated changes or
failed checks stop publication.

## Publication and recovery

Merging a version increase starts `release.yml`. Its push filter watches the
tracker app project; the version check skips publication when no increase is
present. Manual dispatch accepts an approved 40-character `source_commit` and
normally uses `refresh_attempt` of `0`.

The publisher verifies remote sizes and SHA-256 digests for all seven assets
and leaves an unpublished prerelease draft: both player ZIPs, Setup, `update-manifest.json`,
`update-manifest.sig.json`, `update-data.json`, and `SHA256SUMS.txt`.

The installer is named `Ironmon-Setup-win-x64.exe`; its binary retains the build
version. It defaults to latest stable. Advanced selection also lists signed,
published test releases and marks them **Test release**. Testers must explicitly
select their version; downloading a newer Setup alone does not change the default.
Older versioned Setup downloads retain latest-only
behavior.

Stable and immutable releases cannot be replaced. A mutable prerelease can be
rebuilt from another approved `main` commit without increasing its display version.
The internal signed release sequence and helper version advance across all
release versions and branches. The read-only build verifies prior test metadata
but imports adoption inventories only from stable history. Before signing, the
write-token job rechecks all visible signed prereleases and drafts so an older
candidate cannot reuse an identity issued during its build. It then unpublishes the
old test release, updates its lightweight tag, replaces draft assets, and verifies
every uploaded digest. It never exposes a partially replaced test build publicly.
Unexpected attachments, unauthenticated history, stale sequences, stable releases,
or immutable releases stop the operation. A failed upload can resume by rerunning
its publication job with the original candidate; do not reset signed test history.

Sequence 9 belonged to the withdrawn 0.9.0 test release, and the disposable
0.8.16 bridge uses sequence 15. The allocator retains a floor of 16 even if these
temporary releases are deleted or hidden from a read-only build token. A 0.10.0
target must use a higher signed sequence than the installed bridge; display
version ordering alone does not authorize an upgrade. Recovery helper identities
remain distinct for every issued build.

### Publish a mutable test release

Before dispatching a replacement build, repository-admin access must verify that
the current mutable test tag can move. The separate **Immutable release tags**
ruleset protects all `refs/tags/v*` refs from updates and deletion. During 0.10
testing, exclude only `refs/tags/v0.10.0` from that ruleset while keeping it active
for every other version tag. Repository release immutability and this custom tag
rule are separate controls; both must permit the mutable testing phase. For a
future test version, substitute only its exact version tag; never exempt a stable
or immutable release.

The 0.10.0 testing phase is complete: that release is stable and immutable,
repository release immutability is enabled, and **Immutable release tags** has
no testing exceptions. The temporary 0.8.16 bridge release, tag, and remote branch
have been removed; its sequence remains reserved. Apply the mutable-publication
procedure below only to a future, explicitly approved test version.

After the preparation workflow succeeds, use authenticated repository-admin
access to publish the verified draft:

1. Confirm the intended version, approved source, seven asset digests, successful
   candidate gate, and `draft: true`, `prerelease: true`, `immutable: false`.
2. Disable repository release immutability with
   `DELETE /repos/Nobbinobb/infinitefusion-hoenn-ironmon/immutable-releases`.
3. Publish that release using `draft: false`, `prerelease: true`, and
   `make_latest: "false"`. Verify `immutable: false` and that latest stable did not
   change.
4. Leave repository release immutability disabled throughout testing. Verify
   `enabled: false` using the repository policy endpoint and `immutable: false`
   on the published test release. Do not re-enable the policy in cleanup or
   republish the test release under an enabled policy.

The Actions token never receives repository-administration permissions or disables
this policy. It only prepares the private signed draft. Existing immutable
releases remain locked throughout. Every replacement test publication verifies
that the policy is still disabled before publishing. Re-enable it only for the
explicitly approved freeze and stable promotion below.

Include the intended `tag_name` and `target_commitish` explicitly when updating
the draft or publishing it. Re-read the release by numeric ID and verify its tag
and flags before uploading assets; an `untagged-...` draft cannot be uploaded by
the intended version tag. Resume a failed publication job against its retained
verified candidate after repairing an empty private draft.

Test the downloaded Setup and both ZIP variants outside the development checkout.
Use Setup's advanced version selector for 0.10 test builds. Same-version replacement
requires rerunning Setup or reinstalling the complete matching ZIP; automatic
tracker updates still require a newer display version. Tracker checks default to
stable releases; **Include test releases** opts into published prereleases.
For a 0.8.16 bridge, follow the [bridge release order](guides/TEST_RELEASE_UPDATES.md#0816-bridge-and-0100-release-order)
and rebuild 0.10.0 after publishing the bridge. Keep native and
clean-machine acceptance distinct from automated build checks.

### Freeze and promote the accepted build

After explicit testing approval, retain the exact seven tested asset digests.
Remove the accepted tag's testing exception from **Immutable release tags**.
Enable repository immutability, temporarily unpublish the mutable prerelease,
then republish it with `prerelease: false` and `make_latest: "true"`. Verify
`immutable: true`, the unchanged asset digests and tag commit, and anonymous stable
updater discovery. This is a promotion of the tested bytes; do not dispatch a new
build or refresh upstream inputs as part of promotion. Merely enabling repository
immutability does not freeze an existing published test release.

For failed PR checks, fix the source and rerun validation. For a transient
post-merge failure, rerun the failed workflow or dispatch `Prepare test release` on
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
executes the native Ruby smoke entry point; larger Ruby regressions
remain separate.
