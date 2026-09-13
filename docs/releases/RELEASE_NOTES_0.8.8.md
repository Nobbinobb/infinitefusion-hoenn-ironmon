# Ironmon 0.8.8

Ironmon 0.8.8 adds a standalone installer and signed updates inside the tracker.
You can install the game and Ironmon together without installing or configuring
Git, then use the tracker for future updates.

## Standalone installer

- Install into a new folder or a supported existing Infinite Fusion folder.
- Optionally download sprite sheets and create a desktop tracker shortcut.
- Choose a tracker with the .NET runtime included or the smaller runtime-required
  package for a new installation. Existing installations retain their package type.
- Follow download and installation progress with stage measurements and elapsed
  time. Required downloads overlap, and fresh installs avoid redundant file copies.
- Request Windows administrator permission when a protected destination needs it,
  while keeping Setup and the tracker under your normal Windows account.

Setup includes its own .NET runtime and can be discarded after installation.
The runtime-required tracker checks for the compatible .NET runtime; WebView2
installation is offered separately when needed.

## Updates inside the tracker

- Check for a newer stable release at startup. Choose Update to review it or
  Later to keep using the tracker; an uninstalled update is offered next launch.
- Open Check for updates beside the Settings heading for a fresh manual check.
- Review release notes, installed versions, and any local file conflicts before
  approving the update. A failed check never claims that you are up to date.
- Download and verify approved content, prepare recovery files, then hand off to
  an independent helper while the tracker and game close for installation.

Update content is authenticated with signed release metadata and file hashes.
A game change or incompatible update requires finishing the current Ironmon run.
Save your game before installing an update; closing the game does not finish a run.

## Recovery and existing installations

Updates preserve saves and pinned generation profiles. Changed managed files
require review before replacement. Interrupted installations retain recovery
information so the independent helper can finish or restore the installation.
The compatibility guard prevents incomplete or incompatible Ironmon files from
loading an Ironmon save until the installation is repaired.

Older trackers do not gain an updater automatically. To move from 0.8.7, use
this release's Setup or install one complete player archive manually. Subsequent
updates are available inside the tracker.

## Compatibility and downloads

Ironmon 0.8.8 targets Pokemon Infinite Fusion 2 version 6.8.2. The release build
resolves and validates the newest upstream game revision before publication.
Install the game scripts and tracker from the same Ironmon release.

Choose the standalone Setup executable or one of the two Windows x64 archives.
The standard archive includes .NET; the smaller runtime-required archive needs
the Windows x64 .NET 10 Runtime. Both provide the same player features.
The JSON assets are used automatically by the updater. SHA256SUMS.txt contains
the checksums for all six payload and metadata files.
