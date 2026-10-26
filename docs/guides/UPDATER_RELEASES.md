# Updater release production

The release workflow builds Setup, both tracker packages, and updater metadata
together. Ordinary IDE builds do not produce a signed release.
`tools/Build-TrackerRelease.ps1 -GenerateOnly` prepares catalogs and the game
baseline without packaging. See [Release automation](../RELEASE_AUTOMATION.md)
for candidate selection, CI, and publication.

## Mutable testing and stable promotion

The preparation workflow leaves a signed prerelease draft. Repository-admin
publication makes it a mutable test release while keeping the existing stable
release as latest. Setup offers test releases only through explicit advanced
selection; normal tracker discovery remains stable-only.

Replacement test builds retain the display version but advance the signed
release sequence and immutable recovery-helper identity. Existing helpers and
authenticated stable adoption inventories are retained. Testers rerun Setup for
same-version repair, with fresh review if signed metadata changed. Setup uses
the installed build's retained signed inventory as its replacement baseline,
even after the test release URLs serve a newer build. Equal or older signed
sequences cannot replace an installed build with different content. An unchanged
build remains an ordinary repair. An unpublished draft cannot be selected or
installed.

After testing is accepted, freeze and promote the exact tested assets without
rebuilding. See the [publication and promotion procedure](../RELEASE_AUTOMATION.md#publication-and-recovery)
for repository policy transitions and required verification.

## Published artifacts

Publication exposes seven files, in addition to GitHub's automatic source
archives:

| Asset | Purpose |
| --- | --- |
| `Ironmon-v<V>-win-x64.zip` | Self-contained tracker, scripts, and data |
| `Ironmon-v<V>-win-x64-runtime-required.zip` | Runtime-required tracker, scripts, and data |
| `Ironmon-Setup-win-x64.exe` | Standalone installer, defaulting to latest |
| `update-manifest.json` | Versions, compatibility, roles, sizes, and hashes |
| `update-manifest.sig.json` | Signature over the exact manifest bytes |
| `update-data.json` | Current and historical inventories and release notes |
| `SHA256SUMS.txt` | Checksums for the six other published files |

`candidate.json`, `release-evidence.zip`, loose inventories, and per-file checksum
sidecars belong to the Actions candidate artifact, not the public release.
Both tracker ZIPs contain the signed helper payload under `Ironmon Tracker/Updater`
and an `update-package.json` inventory. Helper versions and paths are immutable;
Setup and tracker binary versions must match the release version.

Setup defaults to the newest stable signed release and its matching game revision.
Advanced selection offers older releases only while signed metadata, matching game
history, and required downloads can be verified. Explicit choices are rechecked
at review and installation without substituting another version. Existing installs
cannot downgrade. Older published versioned Setup downloads keep their latest-only
behavior; releases without supported signed metadata require a versioned ZIP.

Each tracker process performs a fresh conditional latest-release check at startup,
respecting server retry limits. Updates default directly to latest; advanced
selection offers versions newer than installed and no newer than the latest
observation. Changing or refreshing the target discards old review consent, and
preparation reauthenticates the exact selection. Successful tracker restart and
administrator-session cleanup close the updater; errors, rollback, and recovery
remain visible. Update progress stays in the fixed footer beside Cancel.

Setup and tracker discovery use anonymous HTTPS. The configured repository and
assets must be publicly downloadable. Authenticated CI publication to a private
repository does not prove anonymous player installation.

## One-time production trust provisioning

`resources/updater/trusted-keys.json` maps key identifiers to base64 DER
SubjectPublicKeyInfo public keys. The checked-in identity is
`ironmon-updater-2026`. Signatures use ECDSA P-256, SHA-256, and 64-byte IEEE P1363
encoding. Keep this key separate from diagnostic-access signing keys.

When provisioning or rotating a key:

1. Create and retain the private key outside the repository and game installation.
2. Add its public key and identifier to reviewed trust before building clients.
3. Set `IRONMON_UPDATER_SIGNING_KEY` to base64 DER PKCS#8 private bytes and
   `IRONMON_UPDATER_SIGNING_KEY_ID` to the identifier in the protected `release`
   environment used by `.github/workflows/release.yml`.

Candidate jobs receive no private signing key. Publication builds the trusted
metadata tool before exposing the secret, checks the public identity, and signs
the frozen manifest bytes. Never put private material in arguments, repository files,
release notes, logs, or artifacts. Fixture keys must never enter production trust.
Clients use embedded trust; downloaded files and environment variables cannot
replace it. Rotate with overlapping public trust and retain keys needed to verify
historical releases.

## Candidate and history requirements

The workflow selects the current upstream game revision and refreshed metadata.
Generated catalogs and the signed baseline must describe those exact inputs.
`tools/ci/Restore-UpdateHistory.ps1` restores prior stable release evidence;
existing installs are recognized from authenticated history, never from files
supplied by a player. Missing or invalid history fails the release gate.

`tools/Build-TrackerRelease.ps1` validates inputs, runs runtime and C# tests,
publishes both tracker flavors and self-contained Setup/helper, and packages the
candidate. `tools/ci/New-ReleaseCandidate.ps1` freezes hashes and evidence.
Publication rechecks the source tree, candidate bytes, upstream fingerprint, and
release history. Stale candidates are rebuilt before signing. Uploads remain a
draft until every remote size and digest matches; retries retain the exact
existing signature and never overwrite published assets.

## Ownership and recovery boundaries

Signed inventories distinguish program files from mutable player/runtime data.
Saves, profiles, downloaded catalogs, sprite assets, caches, and settings downloaded
by the game must survive updates and rollback. Fresh installs receive signed
defaults; game upgrades preserve existing assets and seed newly introduced assets
only into vacant paths. Required program changes still require reviewed conflicts
and verified backups. Published generation profiles retain their ownership policy.

A completed installation may be relocated; a pending transaction cannot be
retargeted. Preparation and application verify the selected installation and
refuse incompatible games or unfinished recovery. Protected folders use an
authenticated administrator worker; the normal UI and relaunched tracker stay
under the original user. The independent helper retains recovery capability if
the tracker cannot start. See [Acceptance and recovery](UPDATER_ACCEPTANCE.md)
for commands and validation cases.

## Local verification and localization

Use native `dotnet test` commands with `tracker/tests/Full.runsettings` for the
updater and Setup projects. The pinned Git dependency and direct commands are
listed in [the tracker README](../../tracker/README.md#validate-and-publish).
Synthetic `ReleaseBundleTests` exercise signed bundles through the real engine.
Native Windows, real UAC, and clean-machine behavior still require the
[acceptance checklist](UPDATER_ACCEPTANCE.md).

Setup, the helper, and shared engine messages use `UpdaterText` and
`tracker/src/Updater/Ironmon.Updater.Core/Resources/Localization/UpdaterResources.resx`.
Add matching culture-specific resource files for translations. The tracker update
panel uses its own `TrackerResources.resx`. Keep protocol identities and signing
data unlocalized.
