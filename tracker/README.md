# Ironmon tracker workspace

The Windows tracker, installer, and updater share the solution
[`Ironmon.Tracker.slnx`](Ironmon.Tracker.slnx). Product behavior is described in
the [mechanics manual](../docs/IRONMON_MECHANICS.html#tracker), and communication
contracts are in [`PROTOCOL.md`](PROTOCOL.md).

## Source layout

| Directory | Responsibility |
| --- | --- |
| `src/Tracker/` | Desktop app, connection, collection, domain rules, protocol, and seed support |
| `src/Setup/` | Native installer and setup logic |
| `src/Updater/` | Updater, installation planning, and update infrastructure |
| `src/Shared/` | SQLite definitions/execution, sprite library, diagnostic access, and shared UI |
| `tests/` | Tracker, app, setup, and updater tests, plus isolated test hosts |
| `tools/` | SQL source generator, release and sprite-size tools, and access-token generator |

`Ironmon.Tracker.Collection` groups query definitions, mapped tables, and result
types under `Queries/`, persistence under `Storage/`, and accumulation under
`Reduction/`. Its two public entry points remain at the project root.

`Ironmon.Sqlite` contains the C# query definitions and execution helpers. The
SQL generator stays in `tools/Ironmon.Sqlite.QueryGenerator` because it runs
during compilation.

App components and assets stay in their owning project; shared code uses project
references. See [`LOCALIZATION.md`](LOCALIZATION.md) for translated UI text.

## Build and run

Run these commands from the repository root on Windows with the .NET 10 SDK
and `maui-windows` workload installed:

```powershell
dotnet build tracker/Ironmon.Tracker.slnx -c Release
dotnet run --project tracker/src/Tracker/Ironmon.Tracker.App/Ironmon.Tracker.App.csproj
```

Prepare the generated runtime data first using the
[development guide](../docs/guides/DEVELOPMENT.md). Ordinary .NET builds use
existing data and do not generate it or install game scripts.

## Validate and publish

Run Quick smoke tests against the relevant project using VSTest configuration:

```powershell
dotnet test tracker/tests/Ironmon.Tracker.Tests/Ironmon.Tracker.Tests.csproj -c Release --settings tracker/tests/Quick.runsettings -p:IronmonQuickTests=true --maxcpucount:1
```

CI applies the same settings directly to the core, app, updater, and overlay test
projects, with the bundled Ruby category in a separate required job. For affected coverage, select a project and optionally `--filter`. Before a
PR, prepare matching generated catalogs and run the complete native C# suite:

```powershell
dotnet test tracker/Ironmon.Tracker.slnx -c Release --settings tracker/tests/Full.runsettings --maxcpucount:1
```

Updater integration tests require the pinned official MinGit archive. Download
it once from the repository root; the C# fixture validates it before extraction:

```powershell
curl.exe --create-dirs --fail --location --output tracker/tests/Ironmon.Updater.Tests/bin/test-assets/MinGit.zip https://github.com/git-for-windows/git/releases/download/v2.55.0.windows.5/MinGit-2.55.0.5-64-bit.zip
```

Quick never uses the private Git fixture. Both profiles serialize xUnit
collections and exclude live network cases. Full requires normal generated
resources; Quick uses isolated test-only outputs that cannot be published.

Core Quick and Full include an xUnit host for `tests/runtime/Quick.rb`. It runs
four Ruby smoke groups in the real bundled game: source startup, deterministic
hashing, battle Run, and feature settings. Set `IRONMON_TEST_GAME_ROOT` when the
game is not the source checkout's parent directory. The host uses a disposable
runtime and isolated saves; evidence remains under ignored `data/ruby-smoke`.
Select `--filter Category=BundledRuby` to run just Ruby smoke, or
`--filter Category!=BundledRuby` to run core C# checks alone. Larger Ruby suites
remain separate. No PowerShell regression launchers are used.

`tools/Publish-Tracker.ps1` publishes the tracker. The
`tools/Build-TrackerRelease.ps1` command generates data, runs native C# tests,
and builds both player packages. Packaging, signing, and recovery are documented
in the [updater release guide](../docs/guides/UPDATER_RELEASES.md).
