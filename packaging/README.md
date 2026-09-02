# Ironmon 0.8.4

This distribution targets Pokemon Infinite Fusion 2 version 6.8.2.

Copy the included `Data` directory into the game directory and merge it with
the existing `Data` directory. Only Ruby files are installed beneath
`Data/Scripts/997_Ironmon`.

Version 0.8.4 pins new runs to immutable generation profiles so saved runs,
completed-run lookup, and aggregate type coverage use the same exact data. It
adds a shared cosmetic wardrobe with per-save equipped appearances, smooths
background fusion preparation, synchronizes changed custom sprite sheets, and
refines defensive multipliers and stat ordering. Ironmon starts no longer run
the base game's redundant legacy randomization.
The player package contains no maintainer generator, release-data generator,
audit, or private signing-key material.

Ironmon is released in two equivalent Windows x64 distributions. The standard
`Ironmon-v<version>-win-x64.zip` archive includes the .NET runtime. The smaller
archive whose name ends in `win-x64-runtime-required` requires the Windows x64
.NET runtime matching the release. Both archives contain the same game scripts
and tracker features; install only one of them.

See the included `INSTALLATION.md` for complete installation, compatibility,
diagnostics, and removal instructions.

Ironmon's original source code and documentation are licensed under the MIT
License in `LICENSE`. Third-party components retain their own terms; see
`THIRD_PARTY_NOTICES.md` and `OPEN-SANS-LICENSE.txt`.
