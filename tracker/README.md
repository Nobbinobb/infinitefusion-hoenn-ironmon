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

```powershell
dotnet test tracker/tests/Ironmon.Tracker.Tests/Ironmon.Tracker.Tests.csproj
dotnet test tracker/tests/Ironmon.Tracker.App.Tests/Ironmon.Tracker.App.Tests.csproj
```

Use `tools/Test-Updater.ps1` for the updater and Setup suites, including their
pinned private Git fixture. See the [tools index](../tools/README.md) for the
bundled-game tests and focused validation commands.

`tools/Publish-Tracker.ps1` publishes the tracker. The full
`tools/Build-TrackerRelease.ps1` command generates data, runs validation, and
builds both player packages. Packaging, signing, and recovery are documented in
the [updater release guide](../docs/guides/UPDATER_RELEASES.md).
