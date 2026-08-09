# Installing Ironmon 0.6.0

Ironmon `0.6.0` targets Pokemon Infinite Fusion 2 version 6.8.0.
It is an independent add-on and does not require committing to or modifying the
official game's Git repository.

## Install from the release archive

1. Fully close Pokemon Infinite Fusion 2.
2. Keep a backup of saves you care about.
3. Extract `Ironmon-v0.6.0-evolutions.zip` into the game's root directory—the
   directory containing `InfiniteFusion2.exe`.
4. Allow the archive's `Data` directory to merge with the existing `Data`
   directory. The package installs Ruby files under
   `Data/Scripts/997_Ironmon` and the self-contained tracker under
   `Ironmon Tracker`.
5. Double-click `Ironmon Tracker/Ironmon Tracker.exe`. No .NET installation or
   terminal command is required. Starting the game first also works.
6. Start the game and select Ironmon when beginning a supported Hoenn run.

Existing non-Ironmon saves retain their normal behavior. Ironmon runs created
before 0.6.0 retain native evolutions because they do not declare evolution
generator metadata. Start a new run or use F7 to enable generated evolutions.
Development saves using evolution rules version 1 or 2 migrate to rules version
3 when their exact legacy metadata still matches; already evolved Pokemon remain
unchanged. Existing ability, base-stat, and move-access metadata continues to
reproduce its assignments.

## Build the package from this repository

Run `tools/Build-TrackerRelease.ps1` from PowerShell. It synchronizes `src` into
`dist` and the adjacent local game installation, publishes the self-contained
Windows x64 tracker, then creates the release ZIP and SHA-256 checksum in
`release`.

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
