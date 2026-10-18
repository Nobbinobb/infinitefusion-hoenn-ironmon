# Ironmon development and diagnostic access

Run the commands below from the nested `Ironmon` repository. See
[AGENTS.md](../../AGENTS.md) for repository boundaries and C# conventions, and
[the mechanics manual](../IRONMON_MECHANICS.html) for product behavior. Update the
manual when mechanics change.

## Game scripts and runtime checks

Visual Studio builds the tracker; it does not synchronize Ruby scripts into the
game. Synchronize the intended installation before testing game-script changes:

```powershell
./tools/Build-Distribution.ps1 -GameRoot 'C:\Program Files (x86)\InfiniteFusion2'
```

This also prepares `dist`. Add `-ReuseGeneratedAssets` only when the generated
assets already match the current inputs. Restart a game that was running during
synchronization. With a shared installation or worktree, use an owned game copy;
[updater acceptance](UPDATER_ACCEPTANCE.md#repeatable-local-checks) shows the
isolated game and distribution paths.

`tools/Test-GameRuntime.ps1 -GameRoot <game-copy>` runs gameplay regressions in the
bundled runtime, including game management and controller checks. Individual
runtime test scripts exercise narrower areas. Ruby changes require bundled-game
validation; a system Ruby syntax check is supplementary. Automated checks must
stop only the hidden game process they started.

`tools/Test-FusionChoices.ps1` checks the fixed fusion inheritance examples,
spread-label boundaries, concealed choices, and historical stat profiles in the
bundled runtime. It also renders the native clue panel to
`data/fusion-choice-validation/fusion-clue.png` for visual inspection.

## Tracker builds

Use Windows, the .NET 10 SDK, the Windows MAUI workload, and PowerShell 7 for the
repository scripts. The CI workflows specify the SDK version used for releases.
Generated catalogs must match the selected game revision. When regeneration is
needed, `tools/Build-TrackerRelease.ps1 -GenerateOnly` prepares catalogs and the
distribution using the game checkout above this repository, without packaging a
release.

```powershell
dotnet build tracker/Ironmon.Tracker.slnx -c Release -p:UseSharedCompilation=false
dotnet test tracker/tests/Ironmon.Tracker.Tests/Ironmon.Tracker.Tests.csproj -c Release -p:UseSharedCompilation=false -- xUnit.ParallelizeTestCollections=false
dotnet test tracker/tests/Ironmon.Tracker.App.Tests/Ironmon.Tracker.App.Tests.csproj -c Release -r win-x64 -p:UseSharedCompilation=false
./tools/Test-Updater.ps1 -Configuration Release
```

`Test-Updater.ps1` includes Setup tests. It provisions the pinned MinGit test
archive; use `-Offline` only after the archive and restored dependencies exist.
Normal builds write to ignored `bin` and `obj` directories. Player publication
uses `tools/Publish-Tracker.ps1`; the full release gate uses
`tools/Build-TrackerRelease.ps1`.

Source projects live under `tracker/src/Tracker`, `Setup`, `Updater`, and `Shared`.
Source files belong to the project that compiles them; use project references
instead of linked files. Shared UI lives in `tracker/src/Shared/Ironmon.Tracker.UI`, and
shared test fixtures in `tracker/tests/Ironmon.TestSupport`.

Each desktop app keeps its own icons, splash image, and fonts. Canonical catalogs,
updater metadata, and cross-language fixtures retain their repository locations.
`EmbeddedResourceInput` and `TestDataInput` declarations let
`tracker/Directory.Build.targets` copy them into intermediate resources or test
output.

## Fusion-generation compatibility

New profiles use player-fusion version 2: retry the complete reverse roster with
5-BST tolerance increases, capped at the full target-BST span. Shared components
remain forbidden. Version 1 stays unchanged for pinned runs and archives; it can
still reject strength-isolated targets such as seed `1271448503`, fusion `271035`.
There is no automatic migration. Any future repair of existing data must require
explicit opt-in and a backup while preserving old implementations and mappings.

## Gameplay collection

The tracker stores collected gameplay event data in a local SQLite database.
Database queries are authored in C#, and a source generator produces their SQL
at build time.

Achievement definitions ship in `data/achievements.pack`. Dormant definitions and
private unlock state are encrypted as a spoiler barrier. The client contains the
key; this is not an anti-cheat boundary. Rebuild the pack with
`tools/Build-AchievementPack.ps1 -DefinitionPath <private-json>`; keep plaintext
definitions outside the public repository. Stable identities preserve unlocks
when compatible definition packs change. Game notices remain pending until the
runtime acknowledges their display.

## Git and release workflow

Start from up-to-date `main`. Use `feature/<name>`, `fix/<name>`, `issue/<number>-<name>`,
or `release/<version>` as appropriate. Keep version numbers unchanged for feature,
fix, and issue work. Stage, commit, and push only when authorized.

Release preparation synchronizes the Ruby version, tracker version fields,
release notes, and affected versioned documentation. Existing release artifacts
are immutable. The workflows select and verify upstream inputs, run the release
gate, and publish reviewed packages. See [Release automation](../RELEASE_AUTOMATION.md)
for the workflow and [Updater release production](UPDATER_RELEASES.md) for package
and signing requirements.

## C# XML documentation

Document every class, constructor, method, and property, including private members
and tests. Use multiline summaries and remarks; document exact parameter names,
generic parameters, and non-void returns:

```csharp
/// <summary>
/// Finds the display name for one species.
/// </summary>
/// <param name="speciesId">The species identifier.</param>
/// <returns>The localized name, or null when the species is unknown.</returns>
private string? FindSpeciesName(int speciesId)
```

Describe units, nullable outcomes, and asynchronous completion where relevant.
Primary constructors need constructor-specific class remarks and parameter tags.
Private fields must not have XML comments. Compiler checks do not enforce all
these requirements; review the actual declarations and project style.

## Diagnostic access

Debug builds grant local developer access automatically. Release builds require
signed capabilities; the `--debug` argument requests diagnostics but does not
grant access by itself.

Normal support uses signed diagnostic-access tokens with individually selected
capabilities and optional expiry. The player application contains public
verification material only. Build `tracker/tools/Ironmon.Tracker.AccessGenerator.App`
in Release and run `Ironmon.AG.exe` from its output to create tokens.

Keep the external PKCS#8 P-256 private key outside the repository, game
installation, distribution, and logs. Never place generated `.ironmon-access`
files in `dist` or `release`. Use the generator's review step before saving a
token. For access behavior and development controls, see
[Controlled diagnostic access](../IRONMON_MECHANICS.html#tracker-debug-mode).
