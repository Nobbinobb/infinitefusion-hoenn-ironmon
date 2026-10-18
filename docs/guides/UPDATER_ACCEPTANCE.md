# Updater acceptance and recovery

Use this checklist to record acceptance for specific artifacts. Automated fixture
coverage does not establish native Windows, real UAC, or clean-machine behavior.
Keep logs, hashes, and screenshots in ignored output or release evidence, not
tracked source. Use dedicated test signing trust for rehearsal packages; never
publish fixtures as a player release. Production trust is documented in
[Updater release production](UPDATER_RELEASES.md#one-time-production-trust-provisioning).

## Repeatable local checks

Run from the nested Ironmon repository in PowerShell 7 on Windows. Generated
catalogs and the selected baseline must match the game revision being tested.

```powershell
./tools/Test-Updater.ps1 -Configuration Release -IncludeNetwork
dotnet test tracker/tests/Ironmon.Tracker.Tests/Ironmon.Tracker.Tests.csproj -c Release -p:UseSharedCompilation=false --results-directory data/updater/acceptance/results -- xUnit.ParallelizeTestCollections=false
dotnet test tracker/tests/Ironmon.Tracker.App.Tests/Ironmon.Tracker.App.Tests.csproj -c Release -r win-x64 -p:UseSharedCompilation=false --results-directory data/updater/acceptance/results
dotnet build tracker/Ironmon.Tracker.slnx -c Release -p:UseSharedCompilation=false
./tools/Test-GameAdoptionInventory.ps1
./tools/ci/Test-ReleaseAutomation.ps1
./tools/ci/Test-ReleasePublication.ps1
```

`Test-Updater.ps1` includes Setup tests and serializes updater process fixtures.
For repeat runs without network, replace `-IncludeNetwork` with `-Offline` after
provisioning the pinned MinGit archive and restoring dependencies. Automation
contracts include historical-inventory restoration; `tools/ci/Test-UpdateHistory.ps1`
can run those cases independently. These tests use synthetic payloads and fixture
keys, not full player packages.

Create an owned game checkout under `data/updater/acceptance/game`, detached at
the exact revision in the generated baseline. Do not use player saves. Synchronize
and validate that copy:

```powershell
$acceptanceGame = Join-Path $PWD 'data/updater/acceptance/game'
$acceptanceDistribution = Join-Path $PWD 'data/updater/acceptance/dist'
./tools/Build-Distribution.ps1 -GameRoot $acceptanceGame -DistributionRoot $acceptanceDistribution
./tools/Test-UpdaterBootGuard.ps1 -GameRoot $acceptanceGame
./tools/Test-UpdaterGameStartup.ps1 -GameRoot $acceptanceGame
```

The boot-guard test creates its own fixture and save directory. The startup test
requires a game under ignored `data`, checks installed scripts and the full
production compatibility inventory, and isolates saves before upstream scripts
load. Both launch only hidden, owned processes. Startup evidence is retained in
`data/updater/acceptance/startup-<id>`. They do not navigate the title screen, play
a run, or rehearse Setup-to-tracker-to-helper operation.

## Native Windows rehearsal

Use full player payloads with synthetic versions and dedicated test trust on a
disposable Windows x64 machine or VM. Restore its snapshot between prerequisite
cases. Record the source/game commits, package hashes, Windows version, and
pass/fail/pending evidence for each row. Hiding PATH on a development machine does
not establish absence of Git or .NET. Prepare a verifiable test transport before
rehearsal; do not add trust overrides to production clients.

| Case | Required observations |
| --- | --- |
| Anonymous download and prerequisites | Download without maintainer credentials. On clean Windows without game, Git, or .NET, Setup starts and installs the self-contained tracker. Exercise missing WebView2 consent and refusal. |
| Setup presentation and localization | Minimum-size pages fit; focus, path tooltips, file approvals, review failures, Back, and optional-work errors remain usable. Check English fallback under another UI culture and formatted numbers. |
| Runtime choice and run consent | New installs default to Runtime included and offer Runtime required; existing installs retain their flavor. Missing .NET blocks runtime-required replacement. Run-completion consent appears when game/compatibility requires it. |
| Existing ZIP and official checkout | Adopt a recognized historical ZIP without system Git; refuse an unknown baseline unchanged. Update an official checkout and verify subsequent official launcher Git behavior. |
| Ironmon-only and combined updates | Correct target revisions and helper after success; no game rewrite for tracker/mod-only changes. Active or unverified runs block required game changes. |
| Player data and runtime downloads | Preserve latest saves, archives, settings, profiles, downloaded settings/Pokedex/catalogs, caches, sprites, edits, renames, and removals during review, shutdown, commit, and rollback. Locked unrelated saves do not block updates. Seed new defaults only into vacant paths. |
| Installation identity and state | Completed relocated installs update again. Relocated pending transactions refuse. Damaged optional discovery/history state can refresh, while missing current authority and rollback attempts refuse. |
| Shutdown and navigation | Check startup invitation, Settings check/retry, details/back, separate helper progress, and restored navigation after relaunch. Normal game closure works; refusal or delay never kills the game. Repeat standard/performance executable, multiple-window, and unrelated-installation cases. |
| Optional sheets and shortcuts | Disabled means no preload; enabled reuses existing sheets and supports retry/interruption without breaking core install. Delete Setup afterward; shortcuts, later updates, and sprite management still work. |
| Conflicts and authenticity | Changed/deleted/colliding managed files require exact consent and backup. Preserve unknown extras/protected data. Reject invalid signature, schema, hash, helper version, paths, unsupported history, and obsolete engines before mutation. |
| Failures and concurrency | Exercise offline/rate-limit/download interruption, disk full, locks, competing helpers, changed-after-review files, and abrupt exit of a recorded test-owned process. Require safe rollback or persistent recovery; never promote unverified bytes. |
| Independent recovery | Recover with the tracker missing; verify retained-helper replacement and retry after an injected recovery failure. Keep evidence and backups on failure, and block a new transaction until recovery succeeds. |
| Unsupported external game update | Tracker and early guard reject incompatibility; Ironmon save remains byte-identical and an ordinary non-Ironmon save remains readable. |
| Current and missed releases | Current install is not rewritten; supported direct upgrades succeed; unsupported history refuses before modification. |
| Protected folders and UAC | Exercise actual consent, credential-based elevation, refusal before writes, post-UAC identity revalidation, cancellation, and protected recovery. Only the worker is elevated; tracker relaunch uses the original user. Verify local authenticated pipe access, protected caches/state, and rejection of reviewed-directory/helper replacement during UAC. |

`ProtectedUpdateTests` exercise signed worker boundaries, permission probes, pipe
peer identity, and injected UAC denial without elevating the test runner. They do
not prove real credential entry or native UI transitions. Record native evidence
separately; a passing unit test does not close an interactive checklist case.

## Independent recovery

Prefer the tracker's recovery action. If it cannot start, use the retained helper
under the affected game's `.ironmon-update/recovery/<helper-version>/`.
Read the transaction `Id` from `.ironmon-update/active.json`; do not edit the marker
or journal. Close the affected game and tracker, then replace these placeholders:

```powershell
& 'C:\Games\InfiniteFusion2\.ironmon-update\recovery\<helper-version>\Ironmon.Updater.exe' --recover 'C:\Games\InfiniteFusion2' '<transaction-id>'
```

The helper independently verifies installation identity, signed authority, journal,
and backups. A malformed or unavailable transaction requires investigation; do
not clear its marker to bypass recovery. The helper reports the result in its own
window. Reopen the tracker after success; retain all transaction files on failure.
