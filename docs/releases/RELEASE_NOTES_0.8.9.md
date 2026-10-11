# Ironmon 0.8.9

Ironmon 0.8.9 adds configurable keyboard and controller profiles, optional
automatic game startup, and improvements to installation and download estimates.

## Keyboard and controller controls

- Open **Settings > Controls** to configure game controls, tracker shortcuts,
  and the Ironmon reset hotkey together.
- Select the keyboard profile or a connected controller profile. Xbox,
  PlayStation, other mapped gamepads, and generic joystick buttons, directions,
  and axes are supported. Each controller keeps its own saved bindings.
- Set two independent bindings for each action, organized in category tabs.
  Restore the selected profile's defaults with its reset icon.
- Configure field interaction and the pause menu separately from menu
  confirmation, cancellation, and the guarded wild-battle Run shortcut.
- Assigning a conflicting binding removes it from the previous action and shows
  a warning. Actions used in separate contexts can still share a binding.

While the tracker is connected and the game is active, these profiles take
priority over the game's saved controls. Closing or disconnecting the tracker
restores native controls without rewriting the game's binding configuration.
The override also expires if the tracker stops responding.

## Tracker startup and settings

- Enable **Settings > Startup > Start game with tracker** to open Infinite
  Fusion automatically when launching the tracker. The setting defaults to Off.
- An existing game from the same installation is reused. Closing the tracker
  leaves the game open, and closing the game does not automatically restart it.
- Settings help icons explain options without keeping their descriptions
  permanently visible.

## Installation and updates

- Choose any parent folder, empty or not. Setup reuses a supported existing game
  or installs into a **Pokémon Infinite Fusion 2** subfolder.
- Setup and the updater retain prepared downloads while waiting for the game,
  tracker, or official launcher to close, then continue automatically.
- The installation review separates versions, selected components, prerequisites,
  and local file changes, with clearer selection indicators and save reminders.
- Download estimates include the selected tracker, the release's game revision,
  and available sprite-library and WebView2 sizes. Unknown or partial totals are
  identified, and sprite estimates show their measurement date on hover.
- Daily sprite-size measurement handles GitHub authentication and rate limits
  while retaining the last complete estimate if a refresh fails.

## Compatibility and downloads

Ironmon 0.8.9 targets Pokemon Infinite Fusion 2 version 6.8.2. Install the game
scripts and tracker from the same release. Existing updater-enabled trackers can
use **Check for updates** in Settings; save your game before installing an update.

Choose the standalone Setup executable or one of the two Windows x64 archives.
The standard archive includes .NET; the smaller runtime-required archive needs
the Windows x64 .NET 10 Runtime. Both provide the same player features.
The updater reads the JSON assets automatically, and `SHA256SUMS.txt` lists the
release's payload and metadata checksums.
