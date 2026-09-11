# Installing Ironmon 0.8.7

Ironmon `0.8.7` targets Pokemon Infinite Fusion 2 version 6.8.2.
It is an independent add-on and does not require committing to or modifying the
official game's Git repository.

## Version compatibility

Use an Ironmon release only with the Infinite Fusion version listed for it.
The game scripts and tracker must come from the same Ironmon archive.

| Ironmon version | Infinite Fusion 2 version | Status |
| --- | --- | --- |
| 0.8.7 | 6.8.2 | Current |
| 0.8.0–0.8.6 | 6.8.2 | Legacy |
| 0.2.1–0.7.9 | 6.8.0 | Legacy |

The documented release history begins with Ironmon 0.2.1. Historical release
notes and source tags do not guarantee that older binary downloads remain
available. Use the compatibility table when choosing an existing package.

Every new Ironmon attempt pins an immutable generation-profile package. Normal
in-place updates retain those content-addressed packages, so active runs and
completed-run lookup continue to use their original game data, audits, custom
fusion pool, and pinned algorithm versions. Ironmon never migrates a run to the
current package. If a pinned package is missing or damaged, the run is reported
as unavailable rather than reconstructed with different data.

## Install from the release archive

1. Fully close Pokemon Infinite Fusion 2.
2. Keep a backup of saves you care about.
3. Choose and extract one release archive into the game's root directory—the
   directory containing `InfiniteFusion2.exe`:
   - `Ironmon-v0.8.7-win-x64.zip` is self-contained and includes the .NET
     runtime.
   - `Ironmon-v0.8.7-win-x64-runtime-required.zip` is smaller and
     requires the Windows x64 .NET 10 Runtime to be installed.
4. Allow the archive's `Data` directory to merge with the existing `Data`
   directory. The package installs Ruby files under
   `Data/Scripts/997_Ironmon` and the selected tracker under `Ironmon Tracker`.
5. Double-click `Ironmon Tracker/Ironmon Tracker.exe`. The self-contained
   archive requires no .NET installation. The runtime-required archive
   requires the Windows x64 .NET 10 Runtime, but not the SDK or MAUI
   workload. Starting the game first also works.
6. Leave `Download data` enabled in Gameplay Options so missing sprites are
   downloaded automatically. On a new or reinstalled game, the first tracker
   views containing many Pokemon—especially evolution graphs—can take longer
   while Infinite Fusion downloads missing sprite sheets. Some custom sprites
   may be absent or temporarily use a fallback until the game's local download
   allowance resets. This improves naturally as the local sprite cache fills.
   To prepare or update the custom fusion and normal-species libraries, fully
   close Infinite Fusion and use **Settings > Custom sprite library** in the
   tracker. The first synchronization can be a large download. Later
   synchronizations use the official server's change metadata, keep current
   sheets without downloading their image data, and replace only missing or
   changed sheets. Updated sheets also clear their tracker-generated
   individual-sprite cache so new custom fusion sprites and normal-species
   variants appear immediately. The tracker does not change Infinite Fusion's
   `Download data` setting or its normal download limit.
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

Existing non-Ironmon saves retain their normal behavior. Pre-release Ironmon
saves and archive recipes from before the version reset are intentionally not
upgraded. Start a new run after installing this build. From this baseline onward,
compatible runs retain their original profile and deterministic implementation;
incompatible or incomplete metadata is rejected without rewriting the save.

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
