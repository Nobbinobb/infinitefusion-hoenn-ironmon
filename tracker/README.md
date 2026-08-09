# Ironmon tracker workspace

The production tracker is being implemented in reviewable parts under `src/`
and `tests/`. See `IMPLEMENTATION_STATUS.md` for the completed boundary and
`../docs/design/TRACKER.md` for the agreed design. The exact implemented
connection subset is recorded in `PROTOCOL.md`.

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

The Debug tab includes tracker-owned raw protocol and state diagnostics.
Complete diagnostic reports can be copied or exported to
`%LocalAppData%\IronmonTracker\diagnostics\`.

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
