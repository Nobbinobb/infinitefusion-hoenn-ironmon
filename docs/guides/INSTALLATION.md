# Installing Ironmon

Ironmon **0.8.15** supports **Pokémon Infinite Fusion 2: Hoenn 6.8.2** on
Windows x64. Use the game scripts and tracker from the same release.

## Install with Setup

1. Download `Ironmon-Setup-win-x64.exe` from the
   [release page](https://github.com/Nobbinobb/infinitefusion-hoenn-ironmon/releases/latest).
   Setup includes its own .NET runtime and supplies Git tools when needed.
2. Choose a folder. Setup reuses a supported game in that folder; otherwise it
   installs into a **Pokémon Infinite Fusion 2** subfolder. The review shows the
   final path. Back up existing saves before continuing.
3. Optionally select sprite sheets and a desktop shortcut. Both are off by
   default. New installations default to **Runtime included**; existing
   installations retain their detected tracker package type.
4. Review the versions, prerequisites and any local file conflicts, save your
   game, then choose **Install**. Setup downloads and verifies the files before
   replacing them. If an application must close, it names it and waits; close
   that application to continue without downloading again.
5. Open `Ironmon Tracker/Ironmon Tracker.exe` when installation finishes.
   It starts the game automatically. Use the tracker's **Play** header action
   from the game's title screen or main menu to create or continue an Ironmon run.

Setup defaults to the newest stable release and its matching game revision.
Expand **Advanced: Ironmon version** on the first page to choose an older
available release. Signed metadata, the exact game revision, and required
downloads must be verifiable; disabled choices explain why. **Refresh versions**
checks again. Review and installation recheck the selection and stop if it changes
or disappears. Existing installations cannot be downgraded. Older versioned Setup
downloads always select latest; use a versioned ZIP and matching game for releases
without supported signed installer metadata.

Start Setup and the tracker normally. A verified helper requests administrator
permission when a protected folder requires it; the tracker itself continues
under your Windows account. Setup offers to install WebView2 when needed.
The runtime-required package additionally needs the Windows x64 .NET 10 Runtime.

Progress bars describe the current stage, not the entire installation. Sprite
sheets download after the core installation succeeds; cancelling or failing that
optional download does not undo the installed game. You can finish it later from
**Settings > Custom sprite library**. You can delete Setup after installation.

## Install from a ZIP

1. Install the supported game version, fully close the game and tracker, and back
   up saves you care about.
2. Download one package:
   - `Ironmon-v0.8.15-win-x64.zip` includes the .NET runtime.
   - `Ironmon-v0.8.15-win-x64-runtime-required.zip` requires the Windows x64
     .NET 10 Runtime; the SDK and MAUI workload are unnecessary.
3. Extract the **complete archive** into the directory containing
   `InfiniteFusion2.exe`. Merge its `Data` directory with the existing one.
4. Start `Ironmon Tracker/Ironmon Tracker.exe`, then use **Play** to create or
   continue a run.

The archive installs `Data/Scripts/997_Ironmon`,
`Data/Scripts/000_Ironmon_Guard.rb`, generated data under `Data/Ironmon`, and the
`Ironmon Tracker` application. Keep these components together.

## Playing and sprite downloads

The tracker must stay connected during Ironmon play. A lost connection pauses
the game until it reconnects, preserving the attempt. **Settings > Game session >
Start tracker minimized** keeps background services active. Ordinary non-Ironmon
play remains available without the tracker.

Leave the game's **Download data** option enabled for missing sprites. Initial
lookups can take longer while sprite sheets download, and temporary download
limits can cause fallback images. To prepare the library in advance, close the
game and use **Settings > Custom sprite library**. Later synchronizations reuse
unchanged sheets and refresh changed ones, including the tracker's extracted
sprite cache. This does not change the game's Download data setting or limit.

For run rules, resets, profiles and control bindings, see
[configuration](CONFIGURATION.md). The
[mechanics manual](../IRONMON_MECHANICS.html) explains the challenge rules.

## Updating

The tracker performs a fresh update check at each startup, including after an
update relaunch. **Later** dismisses an available update for that launch;
**Update** opens its review. You can also use **Check for updates** beside the
Settings heading.

Updates default directly to latest; intermediate releases are unnecessary.
Expand **Advanced: Ironmon version** to select an available version newer than
your installation and no newer than latest. **Refresh versions** checks availability
again. Changing the target requires a fresh review and file approvals; a target
that disappears or changes stops the update instead of selecting another version.

Save your game, review the release notes and any replaceable local-file
conflicts, then choose **Update**. Preparation downloads and verifies files.
Progress and the current operation stay in the fixed footer beside **Cancel**
while details scroll; narrow windows place progress above the button.
The updater then closes the game and tracker and shows progress in its own
window. Close the official launcher if the updater asks; it continues
automatically. A successful update relaunches the tracker and game, then closes
the updater after the launch and administrator-session cleanup succeed. Errors,
rollback, recovery, or a failed restart keep the updater visible.

Ironmon scripts and tracker update together. A game-changing update requires the
active Ironmon attempt to finish first; closing the game does not finish it.
Updates preserve saves, tracker history, settings, browser data and historical
generation profiles. Approved local-file replacements are backed up.

Avoid updating through the official launcher at the same time. An independent
game update can make the installed Ironmon release incompatible. The startup
guard blocks incompatible or incomplete Ironmon installations from loading
Ironmon saves without converting or resetting them. Ordinary saves retain their
normal loading behavior.

If checking or downloading fails, keep using the current compatible installation
and retry later. If an older tracker reports **The updater file could not be
opened safely**, close the game and tracker and run the latest Setup against the
existing game folder.

### Interrupted updates

Reopen the tracker and follow its recovery link, or choose **Retry recovery** in
the updater. Keep the entire game folder and `.ironmon-update` contents intact
until recovery finishes. If the tracker cannot start, the retained independent
helper under `.ironmon-update/recovery` provides the recovery path; keep the
error message and backups when requesting help.

Recovery may need administrator permission again. Verified retained files can
be reused offline; missing or damaged recovery dependencies can require another
download. Once replacement starts, allow the updater to finish or restore the
previous installation. Completed updates currently retain recovery records and
backups; automatic cleanup is not implemented.

## Version compatibility

| Ironmon | Infinite Fusion 2 |
| --- | --- |
| 0.8.15 | 6.8.2 |
| 0.8.0–0.8.14 | 6.8.2 |
| 0.2.1–0.7.9 | 6.8.0 |

Each attempt pins its generation profile. In-place updates preserve these
packages, so active and archived runs keep their original data and algorithms.
A missing or damaged package makes its run unavailable; Ironmon does not
reconstruct it using different data. Runs predating the immutable-profile
baseline need a new save rather than an automatic conversion.

## Diagnostic access

The normal Release tracker accepts maintainer-issued diagnostic tokens:

1. Open **Diagnostic Access** using the header's key button.
2. Paste the token and choose **Activate**, or load its `.ironmon-access` file.
3. Review its note, expiration and granted capabilities.

Access updates immediately without reconnecting. A replacement token requires
confirmation. **Remove access** clears the stored token and protected views;
expired tokens grant no access. Tokens are stored for the current Windows user.
Accept files only from a trusted source. Tokens grant only their listed
capabilities; some include development controls that can change gameplay.

For troubleshooting, `Ironmon.log` beside the game's save files records run
versions, seeds and generation fingerprints.

## Share a seeded run

Open **Seeded runs** using its header button while an active attempt
is connected, or select a completed attempt in **Archive**. Choose **Create
token**, then copy it or save an `.ironmon-seed` file. Ordinary tokens are shared
runs, not maintainer-approved challenges.

To import, connect an active Ironmon attempt, paste or load the token, validate
it, and confirm replacing that attempt. Compatibility is checked before the
replacement. Keep scripts and tracker from the same release.

## Remove Ironmon

Finish pending recovery, then close the game and tracker. Remove
`Data/Scripts/997_Ironmon`, `Data/Scripts/000_Ironmon_Guard.rb` and
`Ironmon Tracker`. Back up `Data/Ironmon` if you intend to resume existing
Ironmon runs after reinstalling: it holds their historical generation profiles.
These removals leave saves and tracker data in Windows application data intact.

To build a package from source, use the [development guide](DEVELOPMENT.md) and
[release guide](UPDATER_RELEASES.md).
