# Ironmon development mode

The local development helper forces the game's `$DEBUG` flag on. This exposes
the standard Debug commands and the Pokemon-level Ironmon Inspector.

Development mode is intentionally installed under
`Data/Scripts/998_Ironmon_Development`, outside the copy-ready Ironmon release
directory. `Build-Distribution.ps1` and release archives include only
`Data/Scripts/997_Ironmon`, so the helper is never shipped.

## Enable

Run `tools/Enable-Development.ps1` from PowerShell. Restart the game if it is
already running.

## Disable

Run `tools/Disable-Development.ps1` from PowerShell, then restart the game.

## Ironmon Inspector

The standalone summary-style inspector presents generated data on scrollable
pages. It currently includes Overview and Abilities; later Milestone 3 releases
extend it with Stats, Learnset, and Evolutions as defined in
`../design/IRONMON_INSPECTOR.md`.

With development mode enabled:

1. Open the pause menu and choose Pokemon.
2. Select the party Pokemon.
3. Choose Debug.
4. Choose Inspect Ironmon data.

Use Left and Right to change pages, Up and Down to scroll, Confirm to show all
details for the selected row, and Back to close the inspector. Normal Pokemon
show their own generated slots. Fusions additionally show their displayed body
and head, final inherited slots, and generated component slots.

F9 also opens the game's general Debug menu while the player is on the map.
