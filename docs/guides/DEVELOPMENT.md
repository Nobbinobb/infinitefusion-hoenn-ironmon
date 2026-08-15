# Ironmon development and diagnostic access

The local development helper forces the game's `$DEBUG` flag on. This exposes
the standard Debug commands and authorizes the external tracker Debug view.

This unrestricted developer override is intended only for local source work.
Normal support and testing should use a signed diagnostic-access token in the
ordinary Release tracker so each information capability can be selected
individually and optionally expire. The player release contains the public
verification key, but never the maintainer generator or private signing key.

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
sides must authorize the legacy developer override before the tracker exposes
unrestricted Debug access. Alternatively, activate a signed token through
**Diagnostic Access** in a Release tracker; the resulting pages and fields are
limited to that token's named grants. The in-game Ironmon Inspector was retired
after the tracker reached feature parity; the standard game Debug commands
remain available.

F9 also opens the game's general Debug menu while the player is on the map.

The token generator lives under `tracker/tools` and publishes only through
`tools/Publish-AccessGenerator.ps1` into the ignored `maintainer-dist`
directory. Keep the external PKCS#8 P-256 private key outside the repository,
game installation, tracker distribution, logs, and source-control staging.
Never copy generated `.ironmon-access` files into `dist` or `release`.
