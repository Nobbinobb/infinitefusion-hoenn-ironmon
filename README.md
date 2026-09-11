# Ironmon for Pokémon Infinite Fusion 2

Ironmon is a deterministic challenge mode for **Pokémon Infinite Fusion 2:
Hoenn**. It combines seeded randomization, fusion-aware encounters, stricter
challenge rules, rapid resets, and a companion tracker designed around
hidden-information boundaries.

> [!IMPORTANT]
> Ironmon **0.8.7** targets **Pokémon Infinite Fusion 2 version 6.8.2**.
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
- Privacy-aware tracker communication: concealed gameplay information is
  filtered by the game before it reaches the tracker.

The complete rules and edge cases are documented in the
[Ironmon Mechanics Manual](docs/IRONMON_MECHANICS.html).

## Download

Download the latest version from
[GitHub Releases](https://github.com/Nobbinobb/infinitefusion-hoenn-ironmon/releases/latest).

Each release provides two Windows x64 packages:

| Package | Recommended for |
| --- | --- |
| `Ironmon-vX.Y.Z-win-x64.zip` | Most players. Includes the required .NET runtime. |
| `Ironmon-vX.Y.Z-win-x64-runtime-required.zip` | Smaller download for players who already have the Windows x64 .NET runtime installed. |

Download only one package. The game scripts and tracker included in an archive
must be kept together. Corresponding SHA-256 checksum files are provided with
every package.

## Installation

1. Install the supported version of Pokémon Infinite Fusion 2.
2. Fully close the game.
3. Back up any saves you care about.
4. Download one Ironmon release package.
5. Extract it into the directory containing `InfiniteFusion2.exe`.
6. Allow the package's `Data` directory to merge with the existing `Data`
   directory.
7. Start `Ironmon Tracker/Ironmon Tracker.exe`.
8. Start the game and select Ironmon when beginning a supported Hoenn run.

The archive installs:

- Ironmon game scripts under `Data/Scripts/997_Ironmon`.
- Generated runtime data under `Data/Ironmon`.
- The companion application under `Ironmon Tracker`.

See the [installation guide](docs/guides/INSTALLATION.md) for update
instructions, compatibility details, sprite-library preparation, seeded runs,
and troubleshooting.

## Version compatibility

| Ironmon version | Infinite Fusion 2 version | Status |
| --- | --- | --- |
| 0.8.7 | 6.8.2 | Current |
| 0.8.0–0.8.6 | 6.8.2 | Legacy |
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
