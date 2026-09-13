# Ironmon 0.8.8

This distribution targets Pokemon Infinite Fusion 2 version 6.8.2.

Copy the included `Data` directory into the game directory and merge it with
the existing `Data` directory. Ironmon's main Ruby scripts are installed beneath
`Data/Scripts/997_Ironmon`. Updater-enabled packages also install the early
`Data/Scripts/000_Ironmon_Guard.rb` and compatibility data under `Data/Ironmon`;
copy the complete archive so these files stay together.

Version 0.8.8 adds a standalone installer and signed updates inside the tracker.
Setup can install the game and Ironmon together, optionally download sprite sheets,
and create a desktop shortcut. The tracker checks for updates at startup and
provides a manual check in Settings. Updates verify release content and preserve
recovery information before replacing installed files.
The player package contains no maintainer generator, release-data generator,
audit, or private signing-key material.

Ironmon is released in two equivalent Windows x64 distributions. The standard
`Ironmon-v<version>-win-x64.zip` archive includes the .NET runtime. The smaller
archive whose name ends in `win-x64-runtime-required` requires the Windows x64
.NET runtime matching the release. Both archives contain the same game scripts
and tracker features; install only one of them.

See the included `INSTALLATION.md` for complete installation, compatibility,
diagnostics, and removal instructions. Releases that include the standalone Setup
download also support installation without a preinstalled game or system Git.
Their tracker announces available updates at startup and offers a manual check
in Settings. Setup can be discarded after installation; updates continue inside
the tracker.

Ironmon's original source code and documentation are licensed under the MIT
License in `LICENSE`. Third-party components retain their own terms; see
`THIRD_PARTY_NOTICES.md` and `OPEN-SANS-LICENSE.txt`.
