# Ironmon for Pokémon Infinite Fusion 2

Ironmon is a seeded, fusion-aware challenge mode for **Pokémon Infinite Fusion
2: Hoenn**, with a single-Pokémon pivot system and a companion tracker.

> [!IMPORTANT]
> Ironmon **0.8.15** targets **Pokémon Infinite Fusion 2 version 6.8.2**.
> The tracker requires Windows x64.

This README describes the current source tree. Published downloads may contain
fewer features; see the [release notes](docs/releases/) for each version.
The current Alpha challenge ends after defeating Wattson in his normal Hoenn
Gym battle.

## Features

- Shareable seeded runs with randomized Pokémon, abilities, stats, moves,
  evolutions, and items.
- Fusion-aware encounters, concealed pivot choices, and fast challenge resets.
- A companion tracker with live battle information, world and Pokémon lookup,
  run history, and persistent achievements.
- Configurable keyboard and controller controls, speed profiles, and battle
  item shortcuts.
- A shared cosmetic wardrobe with unlockable appearances.

## AI usage disclosure

**Ironmon contains AI-generated code and AI-generated assets.** AI tools have
been used to generate and modify code and create mod artwork, including
achievement icons and detail artwork.

Existing credits for Pokémon Infinite Fusion, community sprites, and
third-party components remain in place.

## Tracker required

Keep **Ironmon Tracker** running and connected while playing an Ironmon run.
Opening the tracker starts the game automatically. Use the **Play** header
button to create or continue a run from the game's title screen or main menu.
If the connection is lost, the attempt pauses until the tracker reconnects.

For a less visible tracker, enable **Settings > Features > Game session > Start
tracker minimized**. Ordinary non-Ironmon play does not require the tracker.

## Download

Get the latest version from
[GitHub Releases](https://github.com/Nobbinobb/infinitefusion-hoenn-ironmon/releases/latest).
Choose **one** download:

| Package | Use |
| --- | --- |
| `Ironmon-Setup-win-x64.exe` | Recommended. Downloads and installs the game and Ironmon, or updates a supported existing installation. |
| `Ironmon-vX.Y.Z-win-x64.zip` | Manual installation into an existing game folder; includes the required .NET runtime. |
| `Ironmon-vX.Y.Z-win-x64-runtime-required.zip` | Smaller manual package; requires the Windows x64 .NET 10 Desktop Runtime. |

## Installation

### Recommended: use the installer

1. Download and open `Ironmon-Setup-win-x64.exe`.
2. Choose an installation folder or a supported existing game folder. Back up
   existing saves before making changes.
3. Optionally select sprite-sheet downloads and a desktop tracker shortcut.
   Keep **Runtime included** unless the required .NET Desktop Runtime is
   already installed.
4. Save and close any running game, review the installation, then choose
   **Install**.
5. Open **Ironmon Tracker** when Setup finishes, then use **Play** to create or
   continue a run.

Setup does not require Git and offers to install WebView2 when needed.
Advanced options can select an available older Ironmon release with its
matching game revision. Sprite sheets can also be downloaded later from
**Settings > General > Custom sprite library**.

### Manual installation

1. Install the supported Pokémon Infinite Fusion 2 version, close the game,
   and back up existing saves.
2. Extract one complete Ironmon ZIP into the folder containing
   `InfiniteFusion2.exe`. Merge the `Data` folders and keep the supplied tracker
   and scripts together.
3. Open `Ironmon Tracker/Ironmon Tracker.exe`, then use **Play** to create or
   continue an Ironmon run.

Use matching Ironmon and game versions. See the
[installation guide](docs/guides/INSTALLATION.md) for compatibility, older
releases, recovery, and troubleshooting.

## Updating

The tracker checks for updates at startup. Choose **Update** when offered, or
use **Check for updates** beside the Settings heading.

Save your game before applying an update. If the update requires a finished
Ironmon run, complete that run first; closing the game does not finish it.
For older trackers without an updater, use Setup or follow the manual
installation instructions.

## Documentation

- [Full feature list](docs/FEATURES.md): detailed gameplay and tracker features.
- [Mechanics manual](docs/IRONMON_MECHANICS.html): complete gameplay rules and tracker features.
- [Configuration guide](docs/guides/CONFIGURATION.md): run settings and controls.
- [Installation guide](docs/guides/INSTALLATION.md): setup, updates, and troubleshooting.
- [Development guide](docs/guides/DEVELOPMENT.md): source builds and validation.
- [Release notes](docs/releases/): changes included in each release.
- [Documentation index](docs/README.md): all guides and maintainer references.

## License

Ironmon's original source code and documentation use the [MIT License](LICENSE).
Third-party components retain their respective licenses; see
[Third-Party Notices](THIRD_PARTY_NOTICES.md).

## Disclaimer

Ironmon is an independent, unofficial add-on and is not affiliated with or
endorsed by The Pokémon Company, Nintendo, Game Freak, Creatures Inc., or the
Pokémon Infinite Fusion project. Pokémon and related properties belong to
their respective owners. This repository does not distribute the base game
or its proprietary assets.
