# Ironmon 0.3.2

This distribution targets Pokemon Infinite Fusion 2 version 6.8.0.

Copy the included `Data` directory into the game directory and merge it with
the existing `Data` directory. Only Ruby files are installed beneath
`Data/Scripts/997_Ironmon`.

Version 0.3.2 adds seeded, run-consistent ability randomization for normal and
fused Pokemon. Ability data is generated on demand without save-backed mapping
tables. Universal, exact-species, and component-compatible eligibility rules
preserve special ability mechanics, and fusions inherit eligible generated
slots from their displayed components. The release includes all Milestone 1 and
Milestone 2 behavior from version 0.2.3.

See the included `INSTALLATION.md` for complete installation, compatibility,
diagnostics, and removal instructions.
