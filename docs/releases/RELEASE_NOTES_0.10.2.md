# Ironmon 0.10.2

> [!IMPORTANT]
> **Ironmon 0.10.2 is a mutable test prerelease.** This experimental build may
> contain regressions. Downloads may change without a version bump.

## Mutable test release

Use this experimental build at your own risk. Gameplay, tracker features, and
updates may have regressions. The files for v0.10.2 may be replaced during
testing. Keep a separate test installation or back up your game and saves
before upgrading. Finish any active Ironmon run before replacing the game.

If this release is refreshed, download it again. To install a replacement build
with the same version, rerun the latest Setup and explicitly select **0.10.2
(Test release)** under **Advanced: Ironmon version**, or reinstall the complete
matching ZIP. The tracker does not automatically replace a build with the same
version. Setup defaults to stable. In the tracker, **Include test releases**
opts in to newer test versions.

### One-time Setup update

From 0.10.1 or earlier, run the latest signed Setup and point it at your
existing game folder. Select **0.10.2 (Test release)** in the advanced version
options. After this migration, use **Update** in the tracker again.

## What's new

- Updates the base game to **Pokémon Infinite Fusion 2: Hoenn 1.3.2** (engine
  6.9.1). Setup and the combined updater install the matching game revision.
- Updates Ironmon's battle collection, save loading, and encounter handling for
  Hoenn 1.3.2. The game enables 576 base species for fusion.
- Moves Ironmon Tracker, Setup, and the updater to **.NET 11**.
- Fixes a .NET 11 packaging crash that could close Setup or the updater before
  its window opened. Download Setup again if you tried an earlier v0.10.2 build.
- Opens a separately signed updater from the tracker. It handles installation
  review and game file preparation, and can receive fixes independently of the
  game and tracker. The update archive uses the signed game file bytes on Windows.
- Renders headings, lists, links, and important notices in the tracker's
  **What has changed** section.

## Windows runtime

If you use **Runtime required**, install the **Windows x64 .NET 11 Runtime**
before updating. **Runtime included** supplies the runtime with the update.

https://dotnet.microsoft.com/en-us/download/dotnet/11.0#runtime-11.0.0

Both tracker packages require an x64 CPU that meets the .NET 11 hardware
baseline.
