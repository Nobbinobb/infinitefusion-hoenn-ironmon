# Updater acceptance and recovery

Use this checklist to record acceptance for specific artifacts. Automated fixture
coverage does not establish native Windows, real UAC, or clean-machine behavior.
Keep logs, hashes, and screenshots in ignored output or release evidence, not
tracked source. Use dedicated test signing trust for rehearsal packages; never
publish fixtures as a player release. Production trust is documented in
[Updater release production](UPDATER_RELEASES.md#one-time-production-trust-provisioning).

## Repeatable local checks

Use the native C# tests on Windows, with matching generated catalogs and the
pinned Git dependency described in [the tracker README](../../tracker/README.md#validate-and-publish).

```powershell
dotnet test tracker/tests/Ironmon.Updater.Tests/Ironmon.Updater.Tests.csproj -c Release --settings tracker/tests/Full.runsettings --maxcpucount:1
dotnet test tracker/tests/Ironmon.Setup.Tests/Ironmon.Setup.Tests.csproj -c Release --settings tracker/tests/Full.runsettings --maxcpucount:1
dotnet test tracker/tests/Ironmon.Tracker.App.Tests/Ironmon.Tracker.App.Tests.csproj -c Release --settings tracker/tests/Full.runsettings --maxcpucount:1
```

The Full settings exclude live network cases. To run those deliberately, omit
the settings file and use `--filter Category=Network` with
`-- xUnit.ParallelizeTestCollections=false`. Synthetic test packages and keys do
not establish acceptance of player payloads.

Use an owned game checkout under ignored `data/updater/acceptance/game`, pinned
to the generated baseline. Synchronize it with `Build-Distribution.ps1` using an
isolated distribution directory. Verify native startup, update blocking, and
save preservation in that owned runtime. The former PowerShell runtime fixtures
are removed; C# test success does not establish those game acceptance cases.
Retain evidence for the following rehearsal separately.

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
| Runtime choice and run consent | New installs default to Runtime included. Runtime required stays visible but disabled without the registered x64 .NET 10 Desktop Runtime; its tooltip and keyboard-accessible question-mark button explain installing it and restarting Setup. Check missing .NET, core-only, x86-only, wrong major, and matching serviced runtime cases. Existing installs retain their flavor; signed package runtime verification still blocks incompatible replacement. Run-completion consent appears when game/compatibility requires it. |
| Download sizes and future storage | Ironmon shows its complete signed tracker/helper package size, including embedded achievement artwork and the bundled .NET runtime when selected. The package-choice row does not repeat those bytes. Verify optional sprite/WebView2 totals and unavailable estimates. The separate run-history note describes future disk-space growth without counting it as an installation download or promising a fixed maximum. |
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
