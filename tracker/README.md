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
  types, including strict aggregate type-coverage dataset validation.
- `src/Ironmon.Tracker.Protocol/` groups transport envelopes, connection
  payloads, live snapshots, lookup payloads, Pokémon data, and debug payloads.
- `tests/Ironmon.Tracker.Tests/` mirrors the production project areas.
- `tests/Ironmon.Tracker.App.Tests/` contains Windows-only UI tests that reference
  the compiled desktop app. App components and localization resources stay in
  the app project; no source files are linked or copied into the tests.

User-visible tracker text uses standard .NET resources. See
[`LOCALIZATION.md`](LOCALIZATION.md) for the translation workflow and resource
boundaries.

## Run the desktop shell

The current review build is a Windows-only .NET 10 Blazor Hybrid application
with permanent Player, Enemy, live Lookup, and completed-run Archive views.
Archive includes deterministic Pokemon lookup, fusion exploration, and session
Back/Forward navigation. Live Lookup contains Trainers, Encounters, Items, and
Type Coverage. The game connects locally on port `38521`; no browser or HTTP
server is used.

```powershell
& 'C:\Program Files\dotnet\dotnet.exe' run `
  --project 'src\Ironmon.Tracker.App\Ironmon.Tracker.App.csproj'
```

Debug builds request the unrestricted local developer override automatically
and still require a game runtime with the development helper enabled. The
ordinary Release tracker instead exposes **Diagnostic Access**, where a signed
token can be pasted or loaded from an `.ironmon-access` file. No special build
or launch flag is required for token-based access. The legacy Release launch
flag remains available only for the dual-sided local developer override:

```powershell
& 'C:\Program Files\dotnet\dotnet.exe' run `
  --project 'src\Ironmon.Tracker.App\Ironmon.Tracker.App.csproj' `
  -- --debug
```

Each token grants named pages and information groups independently, with an
optional expiration. All Active Pokemon includes and locks the Current Player
and Current Enemies quick-access grants. Evolution Results and Evolution
Candidates remain separate. Area-only access does not expose a Pokemon or raw
diagnostic page. Activation, replacement, removal, and expiration update a
connected game immediately, while the raw token and its metadata remain
tracker-local.

Debug Pokemon, Debug Lookup, and completed-run Archive use the same tabbed
Pokemon-information card: Overview, Abilities, Stats, Moves, and Evolutions.
Debug Pokemon merges live player/enemy diagnostics into those shared pages.
Overview wild locations, trainer locations, and fusion materials use bounded
10-row pages so high-collision fusions remain compact in the Overview.
The diagnostic tools hosted with Diagnostic Access also include tracker-owned
raw protocol and state diagnostics.
Protocol history, raw tracker state, and persisted knowledge are separately
granted groups. Diagnostic reports contain only currently authorized groups
and can be copied or exported to
`%LocalAppData%\IronmonTracker\diagnostics\`.
Protocol and connection failures are also captured automatically in
`%LocalAppData%\IronmonTracker\diagnostics\latest-protocol-error.json`. The
stable file retains the complete exception and recent message history across
run resets and tracker restarts.

The tracker embeds the release's aggregate type-coverage dataset. Type Coverage
uses current damaging-move types by default and permits hypothetical selections
across all standard types. The resource stores only defensive profiles and
their Normal/Fusion population counts, never individual species or
seed-generated opponents.

While the connected game owns the foreground window, `Ctrl+1`, `Ctrl+2`,
`Ctrl+3`, and `Ctrl+4` select Player, Enemy, Lookup, and Archive without focusing
the tracker. On an XInput controller, hold both triggers and flick the right
stick left, right, up, or down for those same views. Diagnostic tools have no
direct shortcut and remain unavailable unless signed access or the local
developer override authorizes them.

## Validate the implementation

```powershell
& 'C:\Program Files\dotnet\dotnet.exe' test `
  'tests\Ironmon.Tracker.Tests\Ironmon.Tracker.Tests.csproj' `
  --no-restore --nologo --disable-build-servers -m:1 `
  -p:UseSharedCompilation=false
```

Run the desktop UI tests on Windows:

```powershell
& 'C:\Program Files\dotnet\dotnet.exe' test `
  'tests\Ironmon.Tracker.App.Tests\Ironmon.Tracker.App.Tests.csproj' `
  --nologo --disable-build-servers -m:1 `
  -p:UseSharedCompilation=false
```

Building the shell or running its UI tests requires the Windows-only
`maui-windows` workload during development. The standard end-user package is
self-contained. A smaller runtime-required package requires the Windows x64 .NET 10 Runtime,
but neither package requires the SDK or MAUI workload.

## Publish the release tracker

Run `tools/Publish-Tracker.ps1` from the repository root to create the
self-contained `win-x64` application under `dist/Ironmon Tracker`. Pass
`-DeploymentMode RuntimeRequired` to create the smaller runtime-dependent
application under `dist-runtime-required/Ironmon Tracker`. Run
`tools/Build-TrackerRelease.ps1` to rebuild the Ruby distribution, publish both
tracker variants, and create a deterministic release ZIP and checksum for each.
The release command first regenerates and validates `area_catalog.dat`, the
semantic obtainability source catalog, the fusion-predecessor index, the
coverage dataset, and the item-randomization audit in the bundled game runtime.
It aborts rather than packaging stale data and excludes the generators and
audits from both player archives.
