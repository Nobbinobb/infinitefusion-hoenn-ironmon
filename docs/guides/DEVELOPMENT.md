# Ironmon development and diagnostic access

Run the commands below from the nested `Ironmon` repository; do not stage or
commit runtime copies in the outer game repository. See
[the mechanics manual](../IRONMON_MECHANICS.html) for product behavior. Update the
manual when mechanics change. Personal AI instructions belong in the ignored
local `AGENTS.md`; each developer chooses their own agent setup.

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

## Tracker builds

Use Windows, the .NET 10 SDK, the Windows MAUI workload, and PowerShell 7 for the
repository scripts. The CI workflows specify the SDK version used for releases.
Generated catalogs must match the selected game revision. When regeneration is
needed, `tools/Build-TrackerRelease.ps1 -GenerateOnly` prepares catalogs and the
distribution using the game checkout above this repository, without packaging a
release.

### Native test profiles

C# tests use `dotnet test` with ordinary VSTest settings; there is no PowerShell
runner or impact-selection framework. Quick selects important catalog-independent
C# smoke tests and a small bundled-Ruby group. Full runs all non-network C# tests
and the same Ruby group. Affected validation uses a project or `--filter` selected
for the changed behavior.

```powershell
dotnet test tracker/tests/Ironmon.Tracker.Tests/Ironmon.Tracker.Tests.csproj -c Release --settings tracker/tests/Quick.runsettings -p:IronmonQuickTests=true --maxcpucount:1
dotnet test tracker/tests/Ironmon.Tracker.App.Tests/Ironmon.Tracker.App.Tests.csproj -c Release --settings tracker/tests/Full.runsettings --filter FullyQualifiedName~Ironmon.Tracker.App.Tests.Lookup --maxcpucount:1
dotnet test tracker/Ironmon.Tracker.slnx -c Release --settings tracker/tests/Full.runsettings --maxcpucount:1
```

Run full local validation before creating a PR. Routine GitHub checks intentionally
run the Quick C#/Ruby selection and offline Python sprite tests. A version increase does
not trigger a full candidate build on every PR update. Manual Tracker CI can run
the full C# suite with Ruby smoke checks; publication still validates the actual release inputs, native
C# suites, package contents, checksums, and signatures.

Quick builds use separate `bin/quick-tests` and `obj/quick-tests` folders and omit
generated game catalogs. Publishing these test-only builds is rejected. The
`GameCatalog` category excludes catalog-dependent settings interactions from
Quick; normal and Full builds retain the real generated resources. Full requires
current catalogs, the MAUI workload, and the pinned Git dependency described in
[the tracker README](../../tracker/README.md#validate-and-publish).

Both settings files disable parallel xUnit collections. Keep `--maxcpucount:1`
when testing a solution. Use `--no-build` or `--no-restore` only after preparing the
same configuration and output layout. For deliberate live network checks, omit
the settings file and pass `--filter Category=Network` together with
`-- xUnit.ParallelizeTestCollections=false`.
`TreatNoTestsAsError` rejects accidental empty selections.

The native Ruby entry point is `tests/runtime/Quick.rb`, launched by the ordinary
xUnit test `Runtime/BundledRubySmokeTests.cs`. It checks current-source startup,
deterministic hashing, battle Run behavior, and feature defaults/settings,
including BST/favorites, input suppression, overlays, and achievement notices.
It uses the bundled executable in an owned fixture, isolated save data, base
compiled data, and small window-skin assets. It never rewrites the installed
loader or generates gameplay catalogs. Logs and per-group timing stay in ignored
`data/ruby-smoke`. The owned engine is stopped on failure or timeout.

```powershell
dotnet test tracker/tests/Ironmon.Tracker.Tests/Ironmon.Tracker.Tests.csproj -c Release --settings tracker/tests/Quick.runsettings --filter Category=BundledRuby -p:IronmonQuickTests=true --maxcpucount:1
```

The default runtime is the game directory above the source checkout. Set
`IRONMON_TEST_GAME_ROOT` to use another installation. To run only the C# portion
of core Quick, add `--filter Category!=BundledRuby`. CI runs these as separate
required jobs, reusing a cache keyed by the pinned game revision and renderer
version. The runtime checkout excludes maps and large image libraries; metadata
is refreshed and renderer hashes are verified on each run. Cold cache setup can
still take longer than the smoke tests themselves.

The larger Ruby regression files remain under `tests/runtime`; this smoke entry
point deliberately selects only the four groups above. It does not restore the
removed PowerShell harnesses or automatically run long gameplay, generation,
or simulation suites. Native UI and clean-machine acceptance remain separate.

Wardrobe acceptance uses the bundled engine, installed cosmetic graphics and a
real loopback tracker listener, with isolated profile data under ignored
`data/wardrobe-runtime`. It covers the one-time claim, permanent purchases,
native views and palettes, named outfits, reconnect recovery, physical Back,
collection hooks and the paused attempt timer. This requires a normal build
with the generated connection catalogues; do not pass `IronmonQuickTests=true`.

```powershell
dotnet test tracker/tests/Ironmon.Tracker.Tests/Ironmon.Tracker.Tests.csproj -c Release --filter Category=WardrobeAcceptance
dotnet test tracker/tests/Ironmon.Tracker.App.Tests/Ironmon.Tracker.App.Tests.csproj -c Release --filter FullyQualifiedName~WardrobeViewTests
```

The component test also exports production Obsidian markup for compact,
expanded, purchase, profile and disconnected layout inspection. Browser layout
checks and native transport acceptance do not replace desktop WebView review.

For a normal build without tests:

```powershell
dotnet build tracker/Ironmon.Tracker.slnx -c Release -p:UseSharedCompilation=false
```

Normal builds write to ignored `bin` and `obj` directories. Player publication
uses `tools/Publish-Tracker.ps1`; `tools/Build-TrackerRelease.ps1` prepares release
data, runs native C# tests, and builds both player packages.

Source projects live under `tracker/src/Tracker`, `Setup`, `Updater`, and `Shared`.
Source files belong to the project that compiles them; use project references
instead of linked files. Shared UI lives in `tracker/src/Shared/Ironmon.Tracker.UI`, and
shared test fixtures in `tracker/tests/Ironmon.TestSupport`.

C# namespaces follow each project's configured root namespace plus the file's
relative folder path. Keep partial-type files together in one folder. Moves
must update imports, qualified references, XAML class declarations, reflection
names, and source-generator metadata. Files containing only global imports or
assembly attributes have no namespace declaration. The source-generator inputs
under `SqlGenerator/Fixtures` are isolated test compilations with deliberate
fixture namespaces; they are embedded test data, not types in the test assembly.

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

### History maintenance

Settings > Maintenance reviews conservative net disk savings in decimal MB, then
exports unrelated history to a user-selected `.irmhistory` file. Review uses the
SQLite backup API to capture the live database including WAL contents into an
owned temporary directory. It rehearses the real encrypted export, record removal
and compaction on that copy, measures the resulting database and archive, then
deletes the temporary files. The live database, WAL and recipes are not changed.
The estimate subtracts archive storage as though it stays on the same drive,
excludes uncertain recipe-deletion savings, reserves 64 KiB for small storage and
compression variations, and rounds down to 0.001 MB. Reconnecting a game clears
the review so changed gameplay requires a new measurement. Internal serialized
record sizes only detect eligible history and are not shown as disk savings.
The exporter opens every shipped available
achievement definition, including gated definitions, and uses the production
typed predicate evaluator to protect complete matching observations. It never
exports achievement checkpoints, private definitions, matched direct observations,
shared catalogue blocks, live reducer checkpoints or Pokemon configuration chains.
General family histories are a parallel reducer output rather than achievement
inputs. Unmatched additive and exact-set direct measures are eligible; extrema
and their witnesses remain resident. Whole SQL measure rows are retained or
archived together; there is no column-level pruning. Stable identity and tuple
scaffolding remains resident to support continued gameplay and restoration.

The version-2 archive starts with `IRMHIST2`, a random sixteen-byte HKDF salt
and eight-byte nonce prefix. The tracker derives the originating profile and
epoch key through its existing private-state protection, then uses HKDF-SHA256
with the `Ironmon.HistoryArchive.v2` context to derive a separate AES-256 archive
key. The player neither chooses nor receives a password or key. Brotli
`SmallestSize` output is streamed through 256-KiB AES-GCM chunks. Each chunk
authenticates the header, ordinal and length; a separately authenticated empty
final chunk detects truncation. Derived key buffers are cleared after cipher
initialization. Records are lossless typed JSON with 128-MiB per-record bounds;
plaintext temporary export files are never created.

This follows the existing public-client spoiler barrier: the application owns
the protection material, so a determined person reverse-engineering the client
can recover it. It does not promise secrecy against the device owner. The old
password-based `IRMHIST1` development format is rejected without importing data;
it is not reinterpreted as a version-2 archive. No collection schema or profile
identity changes are needed, and restarts or compatible achievement updates do
not replace the archive protection.

The sole collection worker writes and flushes the external archive, authenticates
it completely, then removes its source rows in a transaction. The database is
vacuumed and checkpointed so results show actual recovered database, WAL,
shared-memory and removed-recipe bytes. The completion message subtracts the
archive size to report net savings with the archive on the same drive, including
additional disk use when the result is negative. Small export
identities and dictionary-reference roots stay in the profile. Run recipes are
removed only after the database commit and only when their contents still match
the verified archive. Newer or locked recipes remain active and are reported.
`CompletedRunArchive.MaintainHistoryAsync` serializes file maintenance with recipe
writes and refreshes the visible archive list even after a maintenance failure.

Import authenticates before changes, requires the original profile and epoch,
and checks the old generic definition digests while permitting additive catalogue
updates. Changed existing projection semantics need an explicit archive migration;
never silently reinterpret old data. Import merges sums, extrema, coverage and
set membership with later gameplay, uses the newest ordered state, and leaves
newer recipe files intact. A transactional import identity prevents double
counting, including after restart and out-of-order imports. Permanent unlock
dates and current live reducer state are preserved, and achievements are
reevaluated from restored evidence. The encrypted archive can be deleted by its
owner, permanently losing the detached historical facts that future achievements
might need. It is tied to the original profile's retained identity scaffolding.

Focused tests cover encryption, tampering, truncation, whole-observation protection,
continued gameplay, reverse-order imports, duplicate imports, recipe conflicts,
automatic archive protection, profile/epoch rejection and settings cancellation.
The native Windows file picker must also be exercised in a desktop acceptance
run. Do not exercise destructive maintenance on the user's
live profile as part of testing; use an owned profile copy.

Achievement definitions ship in `data/achievements.pack`. Dormant definitions and
private unlock state are encrypted as a spoiler barrier. The client contains the
key; this is not an anti-cheat boundary. Rebuild the pack with
`tools/Build-AchievementPack.ps1 -DefinitionPath <private-json>`; keep plaintext
definitions outside the public repository. Stable identities preserve unlocks
when compatible definition packs change. Game notices remain pending until the
runtime acknowledges their display.

Approved threshold revisions retain the existing UUIDs, parent gates, evidence
filters and counter scopes. Existing earned IDs and original unlock dates survive
compatible pack rebinding; still-locked milestones use the revised thresholds
against retained evidence. Reproduce the current pack before updating a private
authoring source, and verify that unrelated encrypted records remain identical.

Achievement artwork ships separately in `data/achievement-icons.pack`, embedded
in the tracker assembly. `tools/Build-AchievementIcons.ps1 -DefaultIconPath
<private-png>` creates the shared test image. Optional `-MappingPath <private-json>`
accepts an array of `{ "IconId": "<stable-guid>", "Path": "<private-png>",
"Achievements": ["<stable-achievement-guid>"] }` entries. Paths are relative to
the mapping file. One group image can serve all its achievement tiers. Keep the
originals and mapping outside the public repository; only the authenticated
encrypted pack is committed. Separate artwork updates preserve the existing
definition-pack binding and permanent unlock identities.
To add approved stages without rebuilding other artwork, use
`tools/Build-AchievementIcons.ps1 -BasePackPath data/achievement-icons.pack
-MappingPath <private-json>`. The base pack's shared image, other encrypted icons
and unrelated assignments are retained. Each mapping uses its exact approved
achievement UUID; stage ordering must not be inferred from catalogue positions.
Approved PNGs up to 50-by-50 automatically retain their exact bytes and pixels.
Use the frozen approved display preview whenever available. If an early handoff
only contains the approved large master, fit it directly to a 50-by-50 transparent
canvas once. The builder has no intermediate 64-by-64 conversion. Add
`"PreserveSourcePixels": true` to require exact display input and reject larger
sources. Never rebuild an available approved preview from its master.

Tracker detail artwork lives separately in `data/achievement-artwork/`, embedded
through the same build-input targets. `tools/Build-AchievementArtwork.ps1` accepts
private `IconId`, `Path`, and `SourceSha256` entries for approved original masters.
It fits every image into a transparent 1024-by-1024 canvas, preserving aspect
ratio. The fixed installation contains a binary `index.pack` and encrypted
`part-NNN.pack` files bounded to 32 MiB each. Rebuild with `-BasePackRoot` into a
fresh output directory to preserve unrelated image payloads, then publish with
hash guards. The index starts with `IRMART01`, little-endian version and count;
each 68-byte entry contains a .NET GUID, part number, byte offset, ciphertext
length, width, height, twelve-byte nonce, and sixteen-byte tag. Detail encryption
uses the separate `achievement-artwork-detail-v1:` context and PNG bytes only.

`GetAchievementArtworkAsync` verifies the authenticated earned ledger before
resolving the existing thumbnail assignment. The reader keeps only the small
index, seeks to one bounded image, and limits decoding concurrency. The detail
viewer cancels superseded reads and releases its data URL on close; it never
extracts PNGs, caches images on disk, or records viewing history in the profile.
The clickable 50px row icon opens the shared accessible dialog with a responsive
preview up to 512 CSS pixels. The source detail remains 1024px for display scaling.
Locked, unknown, and unfinished image identities are not exposed. Details are
never included in game notification packets, and opening images cannot unlock
or acknowledge achievements. Keep the 50px PNG/RGBA snapshots unchanged when
updating detail artwork.

The service opens assigned icons only for earned identities. The shared fallback icon serves unassigned achievements.
Game notification protocol 2 sends bounded batches of at most eight earned names
and RGBA buffers of at most 64-by-64 pixels. Unlock conditions, PNG files and image paths are not sent
to the game. The approved layout places the 50-by-50 icon inside a rounded
340-by-64-pixel shared panel, separated from the title by a subtle vertical line.
The left-aligned, bold twenty-pixel title wraps to two lines and is vertically
centered. The complete current catalogue fits without truncation; future names that exceed two lines are ellipsized. Conditions are read in the tracker.
Current 50-by-50 artwork is blitted at its native size with seven pixels of
vertical padding. Older notification buffers remain compatible and are fitted
to the same display area.
The panel sits at the top left, slides in from the left over 0.18 seconds,
holds for 2.6 seconds, then slides out to the left over 0.18 seconds. Its
background is about 85% opaque; the icon and title retain their original opacity.
The game uploads the pixels using `Bitmap.raw_data=` and releases
each notification bitmap after its slide-out animation. The tracker displays
earned PNG data URLs in memory.
Image-bearing notification payloads are omitted from protocol
diagnostics so error reports do not retain decoded artwork.

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

### Retiring the first Alpha endpoint

The temporary endpoint is controlled by `FIRST_ALPHA_COMPLETION_ENABLED` in
`src/run/Alpha_Completion.rb`, not by release versions. The native
`LEADER_Wattson` victory calls ordinary `complete_run(:won)`, records the generic
`completion_milestone` and terminal trainer classes, and schedules one safe save
after the battle and map interpreter finish. The normal run ledger, collection
completion and completed-run recipe carry the same win. Recorded archive wins
cannot be replaced by later loss or abandonment reports for that run ID.

When a content update moves the challenge beyond this endpoint, disable that
endpoint and set `Available` to `false` on the protected achievement definition
`a935e5cc-1685-566a-a661-61610fb03ef2`. Keep its stable identity, encrypted
wording, artwork binding and definition in the pack. `Available` defaults to
`true` for existing definitions and affects only earning; earned records remain
readable and notification acknowledgements remain valid. Do not remove the
node or add a version cutoff. Ordinary bug-fix releases need no policy change.
