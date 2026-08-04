# Ironmon development mode

The local development helper forces the game's `$DEBUG` flag on. This exposes
the standard Debug commands and the Pokemon-level Ironmon ability inspector.

Development mode is intentionally installed under
`Data/Scripts/998_Ironmon_Development`, outside the copy-ready Ironmon release
directory. `Build-Distribution.ps1` and release archives include only
`Data/Scripts/997_Ironmon`, so the helper is never shipped.

## Enable

Run `tools/Enable-Development.ps1` from PowerShell. Restart the game if it is
already running.

## Disable

Run `tools/Disable-Development.ps1` from PowerShell, then restart the game.

## Ability inspector

With development mode enabled:

1. Open the pause menu and choose Pokemon.
2. Select the party Pokemon.
3. Choose Debug.
4. Choose Inspect Ironmon abilities.

F9 also opens the game's general Debug menu while the player is on the map.
