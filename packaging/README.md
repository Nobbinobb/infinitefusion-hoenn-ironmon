# Ironmon 0.8.2

This distribution targets Pokemon Infinite Fusion 2 version 6.8.2.

Copy the included `Data` directory into the game directory and merge it with
the existing `Data` directory. Only Ruby files are installed beneath
`Data/Scripts/997_Ironmon`.

Version 0.8.2 includes the cumulative Ironmon mechanics, consistent effective
level scaling across battles and lookup, safer early-Hoenn acquisition and
healing support, preserved authored multi-trainer battles, and exact
multi-opponent tracker targeting and PP state. Shared sprite presentation,
run-aware Pokémon obtainability, seeded-run sharing, battle-item control,
aggregate type coverage, and signed, optionally expiring diagnostic access
remain available.
The player package contains no maintainer generator, release-data generator,
audit, or private signing-key material.

Ironmon is released in two equivalent Windows x64 distributions. The standard
`Ironmon-v<version>-win-x64.zip` archive includes the .NET runtime. The smaller
archive whose name ends in `win-x64-runtime-required` requires the Windows x64
.NET runtime matching the release. Both archives contain the same game scripts
and tracker features; install only one of them.

See the included `INSTALLATION.md` for complete installation, compatibility,
diagnostics, and removal instructions.
