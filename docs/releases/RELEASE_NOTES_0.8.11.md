# Ironmon 0.8.11

Ironmon 0.8.11 improves tracker and game performance, shows the visual components
of fusion sprites, and makes installation progress easier to follow.

## Performance and loading

- Reduce repeated work when assembling live tracker updates, including move,
  inventory, and knowledge data.
- Move compatible name searches, move-power presentation, defense calculations,
  and area-page assembly into the tracker, and reuse prepared trainer-location
  and wild-encounter indexes.
- Reduce repeated fusion-pairing, normal-stat, and evolution-requirement work
  during run preparation and gameplay while retaining each run's pinned data.
- Speed up encrypted game-data loading and prepare the pinned sprite catalog
  while loading a save to reduce delays when choosing a starter.

## Fusion components and sprite viewer

- Player and enemy cards show the head and body Pokemon that make up the
  displayed fusion sprite. These are its visual components, independent of the
  materials used by Ironmon's fusion rules.
- The component line uses the existing species-line space, preserving the card
  height and badge layout. Nicknames remain the main heading; the full fusion
  species name is available in the sprite viewer.
- The shared sprite viewer lets you switch between the fusion, head, and body
  sprites and enlarge each one in the same dialog wherever that viewer is used.
- Previously recorded sprites without component metadata keep their existing
  presentation until the game exports them again. Missing archived area sprites
  can be restored without changing the recorded Pokemon or sprite variant.

## Installer and fixes

- Avoid duplicate installation verification where the same verified content can
  safely be reused.
- Show clearer installation stages, file or download counts, elapsed time, and
  time since the last progress update. Progress bars describe the current stage.
- Fix the updater helper startup race by waiting for its connection before
  checking process identity.
- Correct text encoding in the first-rival reward message and retry tracker
  images that were temporarily unavailable.

## Compatibility and downloads

Ironmon 0.8.11 targets Pokemon Infinite Fusion 2 version 6.8.2. Install the game
scripts and tracker from the same release. The tracker remains required while
playing Ironmon, as introduced in 0.8.10. Existing runs retain their immutable
generation profiles; this release does not migrate them to newly generated data.

Use **Check for updates** in the tracker's Settings, the standalone Setup
executable, or either Windows x64 archive. Save your game before updating.
The standard archive includes .NET; the smaller runtime-required archive needs
the Windows x64 .NET 10 Runtime. Both provide the same player features.
The updater reads the JSON assets automatically, and `SHA256SUMS.txt` lists the
release's payload and metadata checksums.
