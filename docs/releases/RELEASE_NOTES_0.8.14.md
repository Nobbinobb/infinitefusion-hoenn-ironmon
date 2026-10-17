# Ironmon 0.8.14

Ironmon 0.8.14 improves update discovery, adds advanced update version selection,
and closes the updater window after a successful tracker restart.

## Update directly to the latest version

- The tracker checks for the newest stable release on each startup. A recent
  successful check from an earlier session no longer hides a newly published
  version. Conditional requests and server rate limits remain respected.
- Updating defaults to the latest stable version. You can skip intermediate
  releases; each update installs the complete required program files.
- Expand **Advanced: Ironmon version** in the update panel to select an available
  intermediate release. Choices must be newer than your installed version and
  include authenticated metadata and available matching game downloads.
- Changing the target requires a fresh review and consent. If the selected
  version disappears or its signed metadata changes, updating stops so you can
  refresh or explicitly choose another version.

## Finish updates without an extra window

After a successful update, the updater closes automatically once it has restarted
the tracker and finished cleaning up its administrator session. Failed updates,
recovery operations, and unsuccessful tracker restarts retain their status and
recovery controls.

## Compatibility and downloads

Ironmon 0.8.14 targets Pokemon Infinite Fusion 2 version 6.8.2. Install the game
scripts and tracker from the same release. Existing runs retain their immutable
generation profiles.

Update through **Check for updates** in the tracker's Settings or run
**Ironmon-Setup-win-x64.exe** and select your existing game folder. Setup defaults
to the latest stable release and also provides advanced version selection.
Older installed trackers retain their original update-check behavior until
updated; use the manual check to refresh their offer. If an older updater cannot
update your installation, use the latest Setup.

The standard Windows x64 archive includes .NET; the smaller runtime-required
archive needs the Windows x64 .NET 10 Runtime. Both provide the same player
features. The updater reads the JSON metadata automatically, and `SHA256SUMS.txt`
lists the published payload and metadata checksums.
