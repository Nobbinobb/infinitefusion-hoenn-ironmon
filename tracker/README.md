# Ironmon tracker workspace

The production tracker is being implemented in reviewable parts under `src/`
and `tests/`. See `IMPLEMENTATION_STATUS.md` for the completed boundary and
`../docs/design/TRACKER.md` for the agreed design. The exact implemented
connection subset is recorded in `PROTOCOL.md`.

## Run the desktop shell

The current review build is a Windows-only .NET 10 Blazor Hybrid application.
It contains the Player and Enemy shell but deliberately does not connect to the
game until Part 3.

```powershell
& 'C:\Program Files\dotnet\dotnet.exe' run `
  --project 'src\Ironmon.Tracker.App\Ironmon.Tracker.App.csproj'
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
