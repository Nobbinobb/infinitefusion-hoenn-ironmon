# Ironmon 0.8.13

Ironmon 0.8.13 adds version selection to Setup and gives the installer a consistent
download name.

## Choose an Ironmon version in Setup

- Download **Ironmon-Setup-win-x64.exe**. Setup defaults to the newest stable
  Ironmon release and automatically selects its matching Infinite Fusion revision.
- To install a specific older release, expand **Advanced: Ironmon version** on
  the first page. Available choices show their required game version.
- Setup verifies each release's signed metadata and checks that its exact game
  revision and installation downloads are available. Unavailable or unsupported
  choices are disabled with an explanation; **Refresh versions** checks again.
- Review and installation recheck the selected release. If it becomes unavailable
  or changes, Setup stops so you can review your choice again.
- Older versions can be installed into a new folder. Existing installations retain
  their forward-update rules and cannot be downgraded.

The installer filename no longer includes a version number. Its build version
remains recorded internally. Use Setup from the latest release to get these
version-selection features. Historical releases without supported signed metadata
can still be installed manually from their versioned ZIP and matching game.

## Compatibility and downloads

Ironmon 0.8.13 targets Pokemon Infinite Fusion 2 version 6.8.2. Install the game
scripts and tracker from the same release. Existing runs retain their immutable
generation profiles.

Update through **Check for updates** in the tracker's Settings or run Setup and
select your existing game folder. If an older updater cannot update your
installation, use the latest Setup.

The standard Windows x64 archive includes .NET; the smaller runtime-required
archive needs the Windows x64 .NET 10 Runtime. Both provide the same player
features. The updater reads the JSON metadata automatically, and `SHA256SUMS.txt`
lists the published payload and metadata checksums.
