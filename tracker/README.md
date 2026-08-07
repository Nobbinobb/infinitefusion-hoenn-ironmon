# Ironmon tracker workspace

The production tracker is being implemented in reviewable parts under `src/`
and `tests/`. See `IMPLEMENTATION_STATUS.md` for the completed boundary and
`../docs/design/TRACKER.md` for the agreed design. The exact implemented
connection subset is recorded in `PROTOCOL.md`.

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
