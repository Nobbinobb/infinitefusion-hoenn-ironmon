# Ironmon for Pokémon Infinite Fusion 2

Ironmon is a deterministic challenge mode for **Pokémon Infinite Fusion 2:
Hoenn**. It combines seeded randomization, fusion-aware encounters, stricter
challenge rules, rapid resets, and a companion tracker designed around
hidden-information boundaries.

> [!IMPORTANT]
> Ironmon **0.8.8** targets **Pokémon Infinite Fusion 2 version 6.8.2**.
> The included tracker currently targets Windows x64.

## Features

- Deterministic, shareable seeded runs.
- Mixed, fusion-only, and normal-only encounter policies.
- Fusion-aware starter, encounter, gift, trainer, and evolution generation.
- Randomized abilities, base stats, move access, evolutions, and item rewards.
- A single-Pokémon pivot system with concealed choices and irreversible
  outcomes.
- Immutable generation profiles that preserve the exact data used by each run.
- Fast challenge resets and automatic attempt archiving.
- Progression safeguards for required story Pokémon, trades, HMs, and healing.
- A shared cosmetic wardrobe with default outfits, obtained-only filtering,
  and save-slot-specific equipped appearances.
- A local companion tracker with area lookup, Pokémon lookup, evolution graphs,
  run statistics, and completed-run archives.
- A standalone installer with optional sprite-sheet downloads, plus signed
  updates from inside the tracker.
- Privacy-aware tracker communication: concealed gameplay information is
  filtered by the game before it reaches the tracker.

The complete rules and edge cases are documented in the
[Ironmon Mechanics Manual](docs/IRONMON_MECHANICS.html).

## Download

Download the latest version from
[GitHub Releases](https://github.com/Nobbinobb/infinitefusion-hoenn-ironmon/releases/latest).

Choose the installer or one of the two Windows x64 ZIP packages:

| Package | Recommended for |
| --- | --- |
| `Ironmon-Setup-vX.Y.Z-win-x64.exe` | Recommended. Installs the game and Ironmon together, or uses a supported existing game folder. |
| `Ironmon-vX.Y.Z-win-x64.zip` | Manual installation into an existing game folder. Includes the required .NET runtime. |
| `Ironmon-vX.Y.Z-win-x64-runtime-required.zip` | Smaller manual download for players who already have the Windows x64 .NET 10 Runtime installed. |

You only need one of these downloads. Setup downloads the required game and
Ironmon files for you. The JSON files on the release page are read automatically
by the updater; `SHA256SUMS.txt` provides one checksum list for the release.

## Installation

### Recommended: use the installer

Setup can download the game for you and does not require Git to be installed.
It includes its own .NET runtime.

1. Download and open `Ironmon-Setup-vX.Y.Z-win-x64.exe` from the latest release.
2. Choose an empty folder for a full installation, or select your supported
   existing Infinite Fusion folder. Back up existing saves before making changes.
3. Optionally select the sprite-sheet download and a desktop tracker shortcut.
   For a new installation, keep **Runtime included** unless you want to use an
   already installed .NET runtime. Existing installations retain their package type.
4. Review the installation, save and close any running game, then choose
   **Install**. Setup downloads, verifies, and installs the required files and
   shows progress. Windows may request permission for a protected folder.
5. Open the tracker when Setup finishes. Start the game and select Ironmon when
   beginning a supported Hoenn run.

You can delete Setup afterward. Future updates are available inside the tracker.
Sprite sheets can also be downloaded later from **Settings > Custom sprite
library**. Setup offers to install WebView2 if the tracker needs it.

### Manual installation

1. Install the supported version of Pokémon Infinite Fusion 2, fully close the
   game, and back up any saves you care about.
2. Download one Ironmon ZIP package and extract the complete archive into the
   directory containing `InfiniteFusion2.exe`. Allow its `Data` directory to merge
   with the existing `Data` directory; keep the scripts and tracker together.
3. Start `Ironmon Tracker/Ironmon Tracker.exe`, then start the game and select
   Ironmon when beginning a supported Hoenn run.

The archive installs:

- Ironmon game scripts under `Data/Scripts/997_Ironmon`.
- The compatibility guard at `Data/Scripts/000_Ironmon_Guard.rb`.
- Generated runtime data under `Data/Ironmon`.
- The companion application under `Ironmon Tracker`.

### Updating

From Ironmon 0.8.8 onward, the tracker checks for updates at startup. Choose
**Update** to review an available release or **Later** to dismiss it for this
launch. You can also use **Check for updates** beside the Settings heading.
Save your game before approving an update. If the update requires a finished
Ironmon run, complete that run first; closing the game does not finish it.

To move from 0.8.7 or an older tracker without the updater, use Setup with a
supported existing installation or follow the manual installation instructions.

See the [installation guide](docs/guides/INSTALLATION.md) for compatibility,
recovery, sprite-library preparation, seeded runs, and troubleshooting.

## Version compatibility

| Ironmon version | Infinite Fusion 2 version | Status |
| --- | --- | --- |
| 0.8.8 | 6.8.2 | Current |
| 0.8.0–0.8.7 | 6.8.2 | Legacy |
| 0.2.1–0.7.9 | 6.8.0 | Legacy |

Use an Ironmon release only with its listed Infinite Fusion version.

Every new attempt is pinned to an immutable generation profile. Compatible
in-place updates preserve the data required by existing runs instead of
silently regenerating them with newer game data.

Pre-release saves and archive recipes from before the current profile baseline
cannot be migrated safely. Start a new run when installing the current baseline
for the first time.

## Companion tracker

The tracker communicates with the game locally through a versioned protocol.
The game remains authoritative for gameplay state and decides what information
may be exposed.

Tracker features include:

- Live player and opponent information, including temporary Imposter and
  Transform battle state.
- Area encounters, trainers, items, and discovery progress.
- Pokémon details and move presentation.
- Fusion-aware evolution and obtainability graphs.
- Attempt statistics and completed-run archives.
- Seeded-run export and import.
- Custom sprite-library synchronization.
- Signed release updates with startup notifications and a manual Settings check.
- Capability-limited diagnostic access.

Concealed species, fusion materials, sprites, and generated outcomes are not
sent merely because a tracker view could hide them visually.

## Documentation

- [Documentation index](docs/README.md)
- [Installation guide](docs/guides/INSTALLATION.md)
- [Configuration guide](docs/guides/CONFIGURATION.md)
- [Development and diagnostic access](docs/guides/DEVELOPMENT.md)
- [Mechanics manual](docs/IRONMON_MECHANICS.html)
- [Release notes](docs/releases/)

## Repository structure

```text
src/              Canonical Ironmon Ruby source
tracker/          Tracker application, libraries, tools, and tests
tests/runtime/    Bundled-game runtime validation
tools/            Build, generation, analysis, and validation tools
docs/             Mechanics, guides, audits, and release notes
packaging/        Templates copied into player distributions
```

Generated runtime catalogs, compiled tracker output, distributions, simulation
reports, and release archives are intentionally excluded from Git.

## Development

The canonical game scripts live under `src/`. The ordered manifest at
`src/load_order.json` maps every source file to its flat runtime filename.

To synchronize the canonical scripts into a local Infinite Fusion installation:

```powershell
.\tools\Build-Distribution.ps1
```

Ruby changes must be validated using the bundled Infinite Fusion runtime. A
separate system Ruby installation is not authoritative for this project.

The tracker solution is located at:

```text
tracker/Ironmon.Tracker.slnx
```

Maintainer release builds are produced with:

```powershell
.\tools\Build-TrackerRelease.ps1
```

The release builder regenerates runtime datasets, runs the tracker and
bundled-game validation suites, builds both Windows package variants, validates
their contents, and creates deterministic ZIP archives with SHA-256 checksums.

See the [development guide](docs/guides/DEVELOPMENT.md) for the complete
workflow.

## License

Ironmon's original source code and documentation are licensed under the
[MIT License](LICENSE).

Third-party components retain their respective licenses. The bundled Open Sans
font is licensed under the SIL Open Font License 1.1; see
[Third-Party Notices](THIRD_PARTY_NOTICES.md).

## Disclaimer

Ironmon is an independent, unofficial add-on and is not affiliated with or
endorsed by The Pokémon Company, Nintendo, Game Freak, Creatures Inc., or the
Pokémon Infinite Fusion project.

Pokémon and related names, characters, and properties belong to their
respective owners. This repository does not distribute the Pokémon Infinite
Fusion game or its proprietary assets.
