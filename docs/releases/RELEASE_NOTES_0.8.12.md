# Ironmon 0.8.12

Ironmon 0.8.12 fixes installation and update failures caused by downloaded game
data, player files, and mutable assets.

## Installer and updater fixes

- Recognize supported game installations after the game refreshes its downloaded
  settings, Pokedex data, sprite lists, credits, and download caches. These files
  no longer determine whether the installed game matches a supported release.
- Keep saves, imported generation profiles, custom sprites, and other player
  files outside program-file verification. Unrelated locked files and large
  player-data directories no longer block an otherwise valid update.
- Preserve player files during update preparation, installation, and rollback.
  Game upgrades retain existing assets, including player edits and removals,
  and install newly introduced default assets only where the path is vacant.
- Allow completed installations to be renamed or copied before the next update,
  and recover from damaged optional discovery caches or unrelated history.
- Check both supported game executable variants before replacing program files.

## Updating from an affected release

If an older tracker's updater rejects your installation, download and run
**Ironmon-Setup-v0.8.12-win-x64.exe**, then select your existing game folder.
The old updater still runs its original verification code; the new Setup is
needed to apply these fixes when that code prevents an in-app update.

Save your game before updating. Setup reviews program-file conflicts and retains
the installed tracker package flavor.

## Compatibility and downloads

Ironmon 0.8.12 targets Pokemon Infinite Fusion 2 version 6.8.2. Install the game
scripts and tracker from the same release. The tracker remains required while
playing Ironmon. Existing runs retain their immutable generation profiles;
this release does not migrate them to newly generated data.

Use **Check for updates** in the tracker's Settings, the standalone Setup
executable, or either Windows x64 archive. The standard archive includes .NET;
the smaller runtime-required archive needs the Windows x64 .NET 10 Runtime.
Both provide the same player features. The updater reads the JSON assets
automatically, and `SHA256SUMS.txt` lists the release's payload and metadata
checksums.
