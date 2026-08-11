# Ironmon tracker workspace

The production tracker is being implemented in reviewable parts under `src/`
and `tests/`. See `IMPLEMENTATION_STATUS.md` for the completed boundary and the
[Ironmon Mechanics Manual](../docs/IRONMON_MECHANICS.html#tracker) for the
current product behavior. The exact implemented connection subset is recorded
in `PROTOCOL.md`.

## Project organization

- `src/Ironmon.Tracker.App/Components/` groups the Blazor UI by `Common`,
  `Player`, `Enemy`, `Lookup`, `Debug`, and `Navigation` features.
- `src/Ironmon.Tracker.Connection/` separates transport, run state, persisted
  knowledge, diagnostics, and completed-run storage.
- `src/Ironmon.Tracker.Core/` contains move rules and completed-run domain
  types.
- `src/Ironmon.Tracker.Protocol/` groups transport envelopes, connection
  payloads, live snapshots, lookup payloads, Pokémon data, and debug payloads.
- `tests/Ironmon.Tracker.Tests/` mirrors the production project areas.

User-visible tracker text uses standard .NET resources. See
[`LOCALIZATION.md`](LOCALIZATION.md) for the translation workflow and resource
boundaries.

## Run the desktop shell

The current review build is a Windows-only .NET 10 Blazor Hybrid application
with live Player and Enemy views plus deterministic completed-run lookup,
fusion exploration, and session Back/Forward navigation. The game connects
locally on port `38521`; no browser or HTTP server is used.

```powershell
& 'C:\Program Files\dotnet\dotnet.exe' run `
  --project 'src\Ironmon.Tracker.App\Ironmon.Tracker.App.csproj'
```

Debug builds request the development-only Debug tab automatically and still
require a game runtime that reports debug access. Release builds additionally
require the explicit tracker launch flag:

```powershell
& 'C:\Program Files\dotnet\dotnet.exe' run `
  --project 'src\Ironmon.Tracker.App\Ironmon.Tracker.App.csproj' `
  -- --debug
```

Debug Pokemon, Debug Lookup, and completed-run Lookup use the same tabbed
Pokemon-information card: Overview, Abilities, Stats, Moves, and Evolutions.
Debug Pokemon merges live player/enemy diagnostics into those shared pages.
Overview wild locations, trainer locations, and fusion materials use bounded
50-row pages so high-collision fusions cannot exceed the protocol frame.
The Debug tab also includes tracker-owned raw protocol and state diagnostics.
Complete diagnostic reports can be copied or exported to
`%LocalAppData%\IronmonTracker\diagnostics\`.
Protocol and connection failures are also captured automatically in
`%LocalAppData%\IronmonTracker\diagnostics\latest-protocol-error.json`. The
stable file retains the complete exception and recent message history across
run resets and tracker restarts.

While the connected game owns the foreground window, `Ctrl+1`, `Ctrl+2`,
`Ctrl+3`, and `Ctrl+4` select Player, Enemy, Lookup, and Debug without focusing
the tracker. On an XInput controller, hold both triggers and flick the right
stick left, right, up, or down for those same views. Debug requests are ignored
unless the game authorized development access.

## Validate the implementation

```powershell
& 'C:\Program Files\dotnet\dotnet.exe' test `
  'tests\Ironmon.Tracker.Tests\Ironmon.Tracker.Tests.csproj' `
  --no-restore --nologo --disable-build-servers -m:1 `
  -p:UseSharedCompilation=false
```

Building the shell requires the Windows-only `maui-windows` workload during
development. End users will receive a self-contained executable in the release
packaging part and will not need the SDK or workload.

## Publish the release tracker

Run `tools/Publish-Tracker.ps1` from the repository root to create the
self-contained `win-x64` application under `dist/Ironmon Tracker`. Run
`tools/Build-TrackerRelease.ps1` to rebuild the Ruby distribution, publish the
tracker, and create the combined deterministic release ZIP and checksum.
