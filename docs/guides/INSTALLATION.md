# Installing Ironmon 0.7.9

Ironmon `0.7.9` targets Pokemon Infinite Fusion 2 version 6.8.0.
It is an independent add-on and does not require committing to or modifying the
official game's Git repository.

## Install from the release archive

1. Fully close Pokemon Infinite Fusion 2.
2. Keep a backup of saves you care about.
3. Choose and extract one release archive into the game's root directory—the
   directory containing `InfiniteFusion2.exe`:
   - `Ironmon-v0.7.9-win-x64.zip` is self-contained and includes the .NET
     runtime.
   - `Ironmon-v0.7.9-win-x64-runtime-required.zip` is smaller and
     requires the Windows x64 .NET 10 Runtime to be installed.
4. Allow the archive's `Data` directory to merge with the existing `Data`
   directory. The package installs Ruby files under
   `Data/Scripts/997_Ironmon` and the selected tracker under `Ironmon Tracker`.
5. Double-click `Ironmon Tracker/Ironmon Tracker.exe`. The self-contained
   archive requires no .NET installation. The runtime-required archive
   requires the Windows x64 .NET 10 Runtime, but not the SDK or MAUI
   workload. Starting the game first also works.
6. Leave `Download data` enabled in Gameplay Options so missing sprites are
   downloaded automatically. You can optionally install the current Infinite
   Fusion spritepack to reduce downloads during play. Disable `Download data`
   only if sprite loading causes performance problems; if you disable it,
   install and update the spritepack manually to keep sprites current.
7. Start the game and select Ironmon when beginning a supported Hoenn run.

## Activate diagnostic access

The ordinary Release tracker can accept a maintainer-issued diagnostic-access
token; users do not need a separate Debug tracker build.

1. Open **Diagnostic Access** with the key button in the tracker header.
2. Paste the complete token and select **Activate**, or load the supplied
   `.ironmon-access` file.
3. Review the displayed note, expiration, token and key identifiers, direct
   grants, and included grants.
4. Connect or keep using the game. The tracker updates the game-side grant
   immediately without requiring a reconnect.

Activating another valid token replaces the current token after a warning.
**Remove access** deletes the locally stored token and immediately closes or
clears protected views. An expired token is clearly marked Expired, grants no
capabilities, and can be removed or replaced. Ironmon stores only one token in
the current Windows user's local application data. The token, support note,
signature, expiration, and signing-key material are never sent to the game.

Only accept access files received through a channel you trust. A token grants
exactly its listed diagnostic information; it does not run code and does not
modify gameplay state.

Existing non-Ironmon saves retain their normal behavior. Ironmon runs created
before 0.6.0 retain native evolutions because they do not declare evolution
generator metadata. Start a new run or use F7 to enable generated evolutions.
Development saves using evolution rules version 1 or 2 migrate to rules version
3 when their exact legacy metadata still matches; already evolved Pokemon remain
unchanged. Pre-release saves using normal evolution rules version 3 with fusion
evolution rules version 3 migrate their fusion ordering to version 4 when every
recorded catalog and dependency still matches. Their seed and current Pokemon
state remain unchanged, while future fusion evolutions use the current ordering.
Existing ability, base-stat, and move-access metadata continues to reproduce its
assignments.

## Share a seeded run

Open **Seeded runs** with the chain-link button while an active attempt is
connected, or select a completed attempt in **Archive**. Choose **Create
token**, then copy it or save it as an `.ironmon-seed` file. Ordinary seed
tokens identify tracker-generated data but are not maintainer-approved
challenges.

To import, connect an active Ironmon attempt, paste or load the complete token,
validate it, and confirm the replacement. The game independently checks the
version, configuration, data mode, and generator compatibility before it
abandons the current attempt. Keep the game scripts and tracker from the same
release together.

## Build the package from this repository

Run `tools/Build-TrackerRelease.ps1` from PowerShell. It first runs the Release
tracker tests and complete bundled-runtime suite, including seed-token and
deterministic-import checks. It then regenerates and validates the release
datasets, synchronizes `src`, and publishes both self-contained and
runtime-required Windows x64 trackers. It verifies both player outputs and
creates a deterministic release ZIP and SHA-256 checksum for each in `release`.
Any failed test, stale generation, or packaging violation aborts the release.
The player-package build rejects token and private-key files, maintainer or
release-data generators, audits, and development scripts.

## Diagnostic log

Ironmon writes `Ironmon.log` beside the game's save files. Each new run, loaded
run, and F7 reset records the Ironmon version, context, seed, both fusion
policies, custom-sprite pool size and fingerprint, and mapping counts. Each
entry also includes ability, base-stat, move-access, normal-evolution, and
fusion-evolution generator versions and fingerprints, plus the number of
recorded evolution events. A log write failure never prevents the game from
running.

## Remove Ironmon

Fully close the game and tracker, then remove `Data/Scripts/997_Ironmon` and
`Ironmon Tracker`. This does not remove or modify save files or tracker data in
the user's local application-data directory.
