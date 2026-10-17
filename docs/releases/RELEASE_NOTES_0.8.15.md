# Ironmon 0.8.15

Ironmon 0.8.15 keeps update progress visible while you read or scroll the update
details.

## Visible update progress

- The progress bar and current operation now appear in the fixed footer beside
  **Cancel**, outside the scrollable release notes and file review.
- Checking, downloading, and preparing files all show their activity in the
  footer. Measured work and downloads retain their progress details.
- Narrow windows place progress above the button, keeping both visible without
  horizontal scrolling.

## Compatibility and updating

Ironmon 0.8.15 targets Pokemon Infinite Fusion 2 version 6.8.2. Install the game
scripts and tracker from the same release. Existing runs retain their immutable
generation profiles.

Update through **Check for updates** in the tracker's Settings or run
**Ironmon-Setup-win-x64.exe** and select your existing game folder. Setup defaults
to the latest stable release and also provides advanced version selection.

The standard Windows x64 archive includes .NET; the smaller runtime-required
archive needs the Windows x64 .NET 10 Runtime. Both provide the same player
features. `SHA256SUMS.txt` lists the published payload and metadata checksums.
