# Ironmon development mode

The local development helper forces the game's `$DEBUG` flag on. This exposes
the standard Debug commands and authorizes the external tracker Debug view.

Development mode is intentionally installed under
`Data/Scripts/998_Ironmon_Development`, outside the copy-ready Ironmon release
directory. `Build-Distribution.ps1` and release archives include only
`Data/Scripts/997_Ironmon`, so the helper is never shipped.

## Enable

Run `tools/Enable-Development.ps1` from PowerShell. Restart the game if it is
already running.

## Disable

Run `tools/Disable-Development.ps1` from PowerShell, then restart the game.

## Tracker Debug view

Start a Debug tracker build while development mode is enabled in the game. Both
sides must authorize debug access before the tracker exposes its Debug tab. The
in-game Ironmon Inspector was retired after the tracker reached feature parity;
the standard game Debug commands remain available.

F9 also opens the game's general Debug menu while the player is on the map.
