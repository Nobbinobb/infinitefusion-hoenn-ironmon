# Ironmon development and diagnostic access

`tools/Build-Distribution.ps1` synchronizes the canonical Ruby scripts and area
catalog into both the local game installation and the ignored `dist` staging
directory. Run it before testing game-script changes in Infinite Fusion.

`tools/Test-GameRuntime.ps1` performs the automated bundled-runtime checks. It
loads the synchronized ordinary scripts, validates area progress, deterministic
seeded-run import, and diagnostic capability authorization without leaving
development hooks in the game installation.

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
