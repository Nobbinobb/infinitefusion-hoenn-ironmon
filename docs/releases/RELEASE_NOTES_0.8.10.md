# Ironmon 0.8.10

Ironmon 0.8.10 requires the tracker while playing Ironmon, moves new-run setup
into the tracker, and starts the game automatically when the tracker opens.

## Required tracker and startup

The tracker handles preparation and lookup work moved out of the game's Ruby
runtime to improve performance. Requiring it gives Ironmon one supported path
for that work and run configuration, and lets future improvements rely on the
tracker consistently.

- Open Ironmon Tracker to launch the game. A game already running from the same
  installation is reused, including the performance executable.
- **Settings > Game session > Start tracker minimized** replaces the optional
  game-start setting. Minimized startup keeps background services active.
- Loading an Ironmon save requires a compatible connected tracker. Existing
  Ironmon saves can still be continued with the matching tracker connected.
- A lost connection pauses Ironmon and its attempt timer. Reconnecting resumes
  the same attempt and battle. Ordinary non-Ironmon play remains available
  without the tracker.

## Create and continue runs

- Use the **Play header action** to create or continue an Ironmon run from the
  game's title screen or main menu. Title-screen actions skip the keypress,
  startup messages, and native menu. During gameplay, return to the title
  screen or main menu first.
- The new-run wizard has Save, Rules, and Review steps. Choose the game's actual
  **File A–H** slots and configure wild Pokémon, trainer Pokémon, and unfusion
  rules using reusable local profiles and help beside each setting.
- Occupied saves require a separate overwrite confirmation. Failed creation
  restores the previous save and starter checkpoint, with native backups kept.
- Continue lists valid Ironmon manual saves and **Auto 1 / Auto 2**, with saved
  rules that expand across the full card below the right-aligned control.
- Shared seeds retain their separate action: start an Ironmon run, then import
  the seed into its active attempt.

## Settings, wardrobe, and updates

- Automatic reset after a loss is a live tracker setting, enabled by default.
  Change it during play without changing save-bound rules or seed identity.
- Game session groups startup and reset preferences. Starter selection groups
  automatic selection, maximum starter BST, and Favorite Clause.
- Obsidian run screens use clearer save cards, aligned rule controls, and
  concise review and overwrite-confirmation screens.
- Default starting hairstyles and outfits remain available without an unlock,
  fixing wardrobe confirmation when the unchanged default was reported locked.
- Update inspection preserves the tracker's private WebView2 browser data and
  skips its locked files, while retaining checks on other installation files.

## Compatibility and downloads

Ironmon 0.8.10 targets Pokémon Infinite Fusion 2 version 6.8.2. Install the game
scripts and tracker from the same release. Existing updater-enabled trackers
can use **Check for updates** in Settings; save your game before updating.

Choose the standalone Setup executable or one of the two Windows x64 archives.
The standard archive includes .NET; the smaller runtime-required archive needs
the Windows x64 .NET 10 Runtime. Both provide the same player features.
The updater reads the JSON assets automatically, and `SHA256SUMS.txt` lists the
release's payload and metadata checksums.
