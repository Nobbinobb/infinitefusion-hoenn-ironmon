# Updater acceptance and recovery

This checklist distinguishes automated component coverage from a complete player
installation rehearsal. Keep execution evidence in ignored local output or the
release job's evidence artifact. A developer machine with Git and .NET installed
cannot establish the clean-Windows requirement by hiding PATH.

Protected-folder installation uses a separate authenticated administrator worker.
Production signing requires the separately documented
[trust provisioning](UPDATER_RELEASES.md#one-time-production-trust-provisioning).
Do not publish fixture packages or place fixture private keys in production trust.

## Repeatable local checks

Run from the nested Ironmon repository in PowerShell 7. Generated game catalogs
and the selected game baseline must already match the tested game revision, as
for the existing release-data build. Keep test output under ignored
`data/updater/acceptance`; do not stage reports, installed copies or executables.

```powershell
./tools/Test-Updater.ps1 -IncludeNetwork
dotnet test tracker/tests/Ironmon.Tracker.Tests/Ironmon.Tracker.Tests.csproj -c Release -p:UseSharedCompilation=false --results-directory data/updater/acceptance/results
dotnet test tracker/tests/Ironmon.Tracker.App.Tests/Ironmon.Tracker.App.Tests.csproj -c Release -p:UseSharedCompilation=false --results-directory data/updater/acceptance/results
dotnet build tracker/Ironmon.Tracker.slnx -c Release -p:UseSharedCompilation=false
./tools/Test-GameAdoptionInventory.ps1
./tools/ci/Test-ReleaseAutomation.ps1
./tools/ci/Test-ReleasePublication.ps1
```

`Test-Updater.ps1` includes native Setup tests and serializes the updater's process
fixtures. For repeat runs without network, use `-Offline` after provisioning the
pinned MinGit archive. Offline runs exclude the live download/HTTPS case.
Automation contracts also run the historical-inventory restoration fixtures;
`tools/ci/Test-UpdateHistory.ps1` can run those independently.

Create a disposable game checkout under `data/updater/acceptance/game` at the
exact commit selected by the current generated baseline. Do not copy player
saves into it. For example, clone the local game repository with `--no-checkout`,
then detach the copy at that verified commit. These tests are for the developer
machine; their use of Git does not impose a player prerequisite.

```powershell
$acceptanceGame = Join-Path $PWD 'data/updater/acceptance/game'
$acceptanceDistribution = Join-Path $PWD 'data/updater/acceptance/dist'
./tools/Build-Distribution.ps1 -GameRoot $acceptanceGame -DistributionRoot $acceptanceDistribution
./tools/Test-UpdaterBootGuard.ps1 -GameRoot $acceptanceGame
./tools/Test-UpdaterGameStartup.ps1 -GameRoot $acceptanceGame
```

The guard test creates its own fixture and save directory. The startup test
requires a game below ignored `data`, verifies every installed canonical script,
isolates `System.data_directory` before upstream scripts load, and checks the full
production compatibility inventory before loading Ironmon and game catalogs.
It restores the harness's temporary loader bytes before the compatibility check;
the inventory is not reduced or rewritten to make the test pass. Both checks
launch only hidden, owned game processes. The startup test retains evidence in
an ignored `startup-<id>` directory.

Neither test navigates the real title screen, plays an attempt, exercises a real
save through an update, or drives the native Setup-to-tracker-to-helper flow.
`Test-GameRuntime.ps1 -GameRoot` forwards the selected installation to its build,
but that broad legacy suite is not a substitute for these isolated-save checks.

## Complete Windows rehearsal

Status: pending. No clean Windows environment is available for the current review.
Use a disposable Windows x64 machine or VM and restore its snapshot between
prerequisite cases. Use acceptance packages produced from the real player payload
with a dedicated test signing identity and synthetic versions; record the exact
source commit, game commit, package hashes and Windows version. The current
synthetic A/B/C fixtures exercise the real engine with small substitute payloads;
they are not full player packages or a native end-to-end rehearsal. A verified
test release/transport must be prepared for the native rehearsal before running
this checklist; do not add an environment-variable trust override to production
clients or publish test assets as a stable player release.

| Rehearsal | Required observations |
| --- | --- |
| Anonymous release access | Use the downloaded installer without maintainer credentials; discovery, signature and package downloads succeed. Authenticated CI against a private repository is not this check. |
| Installer presentation | Location field, Browse, buttons, radio choices and checkboxes match the tracker palette. Every page fits without scrolling at the minimum window size; check prerequisites, failed review, paged conflicts and optional-work errors. Verify keyboard focus and that each file approval survives navigation. No tracker view changes. |
| Installer and helper text | Native windows, statuses and shared errors resolve from the updater resource catalog. Check English fallback under another Windows UI culture and culture-specific number formatting; no resource keys or missing-text errors appear. |
| Fresh Windows, no game, Git or .NET | Standalone Setup starts, installs approved game and default self-contained tracker, tracker opens; no Git configuration or account prompt |
| Runtime choices | New shows exactly Runtime included and Runtime required, defaulting to included even with .NET installed; existing displays its package without a selector and retains it; runtime-required refuses before replacement without compatible .NET |
| Review failures and run consent | Failed discovery stops checking and shows an actionable error; Back clears stale review status. Run completion appears only for an existing installation whose game or signed compatibility requires it; Install remains unavailable until confirmed and reviewed. |
| WebView2 missing | Declining leaves installation unchanged; consenting installs the verified prerequisite and tracker subsequently opens |
| Existing ZIP, no system Git | Recognized historical game is adopted; combined update finishes; unknown baseline is refused with all original files intact |
| Official launcher checkout | Update with private Git; verify Git state after success; run official launcher update afterward in the disposable copy and verify index/obsolete-file behavior |
| Ironmon-only and combined releases | Correct scripts, tracker, helper and game revision after success; no game rewrite for Ironmon-only changes; active or unverified run blocks game change |
| Startup and navigation | No footer update link; startup invitation offers Update/Later once per launch; Settings heading starts a fresh check; current and failed checks have distinct states; details/back returns to previous context; Update starts automatic work; separate helper shows progress after tracker exits; tracker relaunch restores context |
| Running game and tracker | Save/closure warning appears before acceptance; normal close completes; refusal or delayed close waits/stops without killing the game |
| Optional sheets | Disabled does no preload; enabled downloads base/custom sheets; existing sheets reused; interruption and game-start race leave core install working and tracker retry available |
| Disposable Setup and shortcuts | Delete downloaded Setup after installation; shortcut starts installed tracker; repeated Setup does not replace unrelated shortcuts; subsequent updates and sprite management still work |
| Existing player data | Use disposable representative active/completed runs, archive, settings and diagnostic access; record hashes before; verify preservation and usable run/archive behavior afterward |
| Offline, rate limit and interrupted download | Existing installation works, actionable failure and retry, no unverified file promotion |
| Changed/deleted/extra/colliding files | Exact replaceable conflicts require consent and backup; protected data and unknown extra files retained; unsupported structure refused |
| Invalid metadata/package | Signature, schema, hash, helper-version and path failures refuse before promotion or execution |
| Disk full, lock and abrupt exit | Fail before writes where possible; otherwise verified rollback or persistent recovery; only terminate a recorded test-owned helper to simulate a crash |
| Competing writer/helper | One transaction only; detect a file changed after review; no silent competing replacement |
| Helper changes and tracker missing | New helper survives replacement; retained independent helper recovers while tracker executable is unavailable |
| Recovery failure | Preserve backups and error, block a new transaction, successfully retry after removing the fixture's injected failure |
| External unsupported game update | Tracker and early game guard reject incompatibility; Ironmon save stays byte-identical; ordinary non-Ironmon save remains readable |
| Current/repeated/missed releases | No rewrite when current; supported direct upgrade succeeds; obsolete engine or unsupported history is refused before modification |
| Protected folder / declined UAC | Test actual successful UAC and credential entry, cancellation before writes, post-UAC revalidation, original-user relaunch and independent protected recovery; native rehearsal remains pending |

For every row record pass/fail/pending, artifact identities, observed UI, relevant
hashes/logs and any injected failure. A checked unit-test assertion alone does not
close an interactive or prerequisite row. Keep screenshots and runtime evidence
outside tracked source. The complete feature acceptance gate remains open until
these required cases have evidence.

## Independent recovery

Prefer reopening the tracker and selecting its recovery action. If that cannot
start, use the retained helper beneath the affected game's
`.ironmon-update/recovery/<helper-version>/Ironmon.Updater.exe`. This copy is
separate from the tracker being replaced. Identify the pending transaction from
the `Id` field of `.ironmon-update/active.json`; do not edit the marker or journal.

The helper accepts this PowerShell command shape (replace all placeholders):

```powershell
& 'C:\Games\InfiniteFusion2\.ironmon-update\recovery\<helper-version>\Ironmon.Updater.exe' --recover 'C:\Games\InfiniteFusion2' '<transaction-id>'
```

Close the affected game and tracker first. The helper independently checks the
installation identity, signed release authority, journal and backups; the command
does not grant permission to arbitrary paths from an edited journal. A malformed
or unavailable transaction is a support case, not a reason to clear the marker.
Direct recovery reports its result in its own window; reopen the tracker normally
after successful restoration. Retain all transaction files on failure. Automatic
backup retention cleanup is not implemented.

## Administrator boundary checks

`ProtectedUpdateTests` runs without granting the test runner administrator access.
It uses the real signed worker, planner and transaction engine with an isolated
storage/process boundary. It verifies exact release and consent checks, a swapped
directory identity, immutable session selection, abandoned preparation, fresh
worker recovery, and sprite-only restrictions. Fixture signing keys remain inside
the tests; no production environment-variable trust override exists.

The suite also exercises real Windows permission probes, the administrator ACL
descriptor, named-pipe peer identification and cancellation acknowledgements.
UAC denial is injected at the process-launch boundary so tests do not display
unattended Windows permission prompts. These tests do not prove that Windows
granted a real elevated token, that over-the-shoulder administrator credentials
work on a fresh account, or that every native UI transition was observed.

For the native rehearsal, record the normal Setup/tracker process token and the
administrator worker token. Confirm that only the worker is elevated, the named
pipe is not accessible over the network, and replacing a reviewed directory or
helper during the permission prompt is rejected. Check that recovery state and
the shared private Git cache grant ordinary users no write access. Interrupt only
a test-owned worker; reopen normally and verify both restoration and the original
user's tracker navigation, settings and archives. Repeat sprite synchronization
after deleting Setup, including the option to retry unavailable sheets.

The current host has no clean Windows environment available. Real UAC acceptance,
fresh no-Git/no-.NET installation and the complete native rehearsal matrix remain
pending; successful fixture tests or publishing a self-contained executable must
not be recorded as those checks passing.
