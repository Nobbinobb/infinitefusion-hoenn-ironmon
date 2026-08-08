# Installing Ironmon 0.3.3

Ironmon `0.3.3` targets Pokemon Infinite Fusion 2 version 6.8.0.
It is an independent add-on and does not require committing to or modifying the
official game's Git repository.

## Install from the release archive

1. Fully close Pokemon Infinite Fusion 2.
2. Keep a backup of saves you care about.
3. Extract `Ironmon-v0.3.3-tracker.zip` into the game's root directory—the
   directory containing `InfiniteFusion2.exe`.
4. Allow the archive's `Data` directory to merge with the existing `Data`
   directory. The package installs Ruby files under
   `Data/Scripts/997_Ironmon` and the self-contained tracker under
   `Ironmon Tracker`.
5. Double-click `Ironmon Tracker/Ironmon Tracker.exe`. No .NET installation or
   terminal command is required. Starting the game first also works.
6. Start the game and select Ironmon when beginning a supported Hoenn run.

Existing non-Ironmon saves retain their normal behavior. Ironmon 0.3.1 and
0.3.2 runs migrate automatically to the schema-3 fusion-slot rules without
rerolling genuine component assignments. Development saves using the older
schema-1 ability generator must start a fresh run with F7.

## Build the package from this repository

Run `tools/Build-TrackerRelease.ps1` from PowerShell. It synchronizes `src` into
`dist` and the adjacent local game installation, publishes the self-contained
Windows x64 tracker, then creates the release ZIP and SHA-256 checksum in
`release`.

## Diagnostic log

Ironmon writes `Ironmon.log` beside the game's save files. Each new run, loaded
run, and F7 reset records the Ironmon version, context, seed, both fusion
policies, custom-sprite pool size and fingerprint, and mapping counts. Each
entry also includes the ability-generator version and allowed-pool fingerprint.
A log write failure never prevents the game from running.

## Remove Ironmon

Fully close the game and tracker, then remove `Data/Scripts/997_Ironmon` and
`Ironmon Tracker`. This does not remove or modify save files or tracker data in
the user's local application-data directory.
