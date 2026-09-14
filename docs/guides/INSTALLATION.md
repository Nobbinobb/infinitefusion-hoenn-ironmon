# Installing Ironmon 0.8.8

Ironmon `0.8.8` targets Pokemon Infinite Fusion 2 version 6.8.2.
It is an independent add-on and does not require committing to or modifying the
official game's Git repository.

## Version compatibility

Use an Ironmon release only with the Infinite Fusion version listed for it.
The game scripts and tracker must come from the same Ironmon archive.

Updater-enabled packages also include `Data/Scripts/000_Ironmon_Guard.rb` and
`Data/Ironmon/game-compatibility.json`. Install these with the rest of the archive.
An incomplete update or incompatible game prevents Ironmon hooks and Ironmon saves
from loading; the guard does not convert, delete or reset the save. Ordinary
non-Ironmon saves remain eligible for the normal game loader. Finish recovery or
install a matching complete release before continuing Ironmon. When removing
Ironmon, also remove `Data/Scripts/000_Ironmon_Guard.rb`.

| Ironmon version | Infinite Fusion 2 version | Status |
| --- | --- | --- |
| 0.8.8 | 6.8.2 | Current |
| 0.8.0–0.8.7 | 6.8.2 | Legacy |
| 0.2.1–0.7.9 | 6.8.0 | Legacy |

The documented release history begins with Ironmon 0.2.1. Historical release
notes and source tags do not guarantee that older binary downloads remain
available. Use the compatibility table when choosing an existing package.

Every new Ironmon attempt pins an immutable generation-profile package. Normal
in-place updates retain those content-addressed packages, so active runs and
completed-run lookup continue to use their original game data, audits, custom
fusion pool, and pinned algorithm versions. Ironmon never migrates a run to the
current package. If a pinned package is missing or damaged, the run is reported
as unavailable rather than reconstructed with different data.

## Install with Setup (updater-enabled releases)

These instructions apply when a release provides an `Ironmon-Setup-...exe`
download. They do not add an installer or updater to older published packages.

1. Download and open the release's standalone Setup executable. Setup includes
   its own .NET runtime; you do not need to install or configure Git.
2. Choose a new installation folder or a supported existing game folder. The
   default new folder is inside your Windows user account. For a protected folder,
   Windows requests administrator permission after you choose Install. Declining
   leaves the installed program files unchanged. Setup itself stays under your
   normal Windows account.
3. Optionally select the sprite-sheet download and desktop shortcut. Both are
   off by default. You can download the sprite library later from the tracker.
4. Review the release, game version and any file conflicts, then choose Install.
   Save your game before continuing. Setup downloads and verifies the required
   files, asks the game to close normally if necessary, and installs the game,
   Ironmon scripts and tracker together. If the game, tracker or official launcher
   is still running, Setup keeps the prepared files and waits. Close the named
   applications; installation continues automatically without another download.
5. Open the tracker when installation finishes. You can delete the downloaded
   Setup executable afterward; future updates start inside the tracker.

During installation, Setup shows the current stage, measured download or file
counts, elapsed time, and the time since the last progress report. Each progress
bar describes the current stage; it starts over when the next stage begins.
A moving bar means that stage's total is not known yet. Game preparation,
verification, and recovery backups can continue after downloading finishes.
Optional sprite sheets download after the core installation succeeds.

You can cancel while Setup waits for applications to close. After cleanup, you
can select the same new-install folder again; retained updater recovery files
do not make it an occupied game folder.

New installations default to the self-contained tracker, which includes .NET.
Existing installations keep their detected tracker package type. Setup does not
automatically choose a smaller package just because .NET
is installed. If you explicitly choose the runtime-required package, it checks
for a compatible Windows x64 .NET runtime before replacing the installation.
WebView2 is a separate tracker prerequisite; Setup offers its installation with
your consent when needed.

The optional sprite download happens after the main installation. A failed or
cancelled sprite download does not undo a successful game installation; complete
it later through **Settings > Custom sprite library**. Setup does not change the
game's **Download data** setting.

Start Setup and the tracker normally, including for protected folders. A verified
helper requests administrator access for installation, recovery or protected
sprite downloads when needed. The tracker and your desktop shortcut continue to
run under your own Windows account. Windows may require administrator credentials
if your account cannot approve the request itself.

## Update from the tracker (updater-enabled releases)

The tracker checks for a newer stable Ironmon release at startup. An available
update opens a small dialog over the tracker. Choose **Later** to dismiss it for
this launch, or **Update** to read the release notes and review any affected local
files. The dialog appears again on the next launch until you install the update.
You can also select **Check for updates** beside the Settings heading at any time.
This opens the update view and performs a fresh check. A successful check with no
newer release shows your installed versions and the last check time; an unavailable
check shows an explanation instead. Use the back link to return to your previous
tracker view without updating.

Save your game, approve any displayed replaceable file conflicts, and choose
**Update**. Downloading, verification and preparation then proceed automatically.
Protected folders can show a Windows administrator-permission prompt during
preparation. The reviewed release and file approvals are checked again afterward.
The update warns that it will close the game and tracker. After the tracker
closes, a separate updater window shows installation and recovery progress. The
tracker cannot display progress while it is closed. Successful updates relaunch
the tracker; start the game when you are ready to play. If the official launcher
is open, the updater waits and continues automatically after you close it.

Ironmon and its tracker always update together. If that release needs another
game revision, the same operation updates the game too. An active Ironmon run
blocks a game-changing update. Postpone it until the attempt has ended. The
updater supplies its own private Git tools when needed; there is no system Git
installation, account or configuration step.

Supported existing Git installations and recognized ZIP installations can be
adopted. Unknown game versions, unsupported Git states and protected local files
stop the update with an explanation. A replaceable conflict needs approval for
that exact file and is backed up before replacement. Saves, tracker history,
settings, access tokens and historical generation profiles are preserved.
Unrelated extra files are retained. Do not run the official launcher and Ironmon
updater at the same time. An independent launcher update can make the game
incompatible with the installed Ironmon release; the startup guard then prevents
Ironmon saves from loading until a matching release is installed.

If checking or downloading fails while offline, continue using the current
installation and retry later. Cancellation during preparation leaves the current
installation intact. Once replacement has started, the updater must finish or
restore the previous installation; it retains recovery information if it cannot.

### Interrupted updates and recovery

Reopen the tracker and follow its recovery link, or use **Retry recovery** in
the updater window. Recovery verifies the recorded transaction and backups before
restoring files. If the tracker cannot start, retain the entire game folder and
ask for help using the independent recovery helper retained under
`.ironmon-update/recovery`. Keep this copy even if the tracker executable is
temporarily unavailable.

Protected-folder recovery may request administrator permission again after a
restart. The retained helper is verified against signed release records and does
not need another download when those records and the recovery copy are intact.
Combined game recovery also needs the verified private Git cache retained from
preparation; if it was removed or damaged, restoring it requires a connection.

Do not delete or edit the game's `.ironmon-update` folder while recovery is
pending. Keep the error message and backups if recovery needs attention. A
completed update currently retains its transaction records and backups; automatic
backup cleanup is not implemented. Recovery does not migrate or reset saves.

## Install from the release archive

1. Fully close Pokemon Infinite Fusion 2.
2. Keep a backup of saves you care about.
3. Choose and extract one release archive into the game's root directory—the
   directory containing `InfiniteFusion2.exe`:
   - `Ironmon-v0.8.8-win-x64.zip` is self-contained and includes the .NET
     runtime.
   - `Ironmon-v0.8.8-win-x64-runtime-required.zip` is smaller and
     requires the Windows x64 .NET 10 Runtime to be installed.
4. Allow the archive's `Data` directory to merge with the existing `Data`
   directory. The package installs Ruby files under
   `Data/Scripts/997_Ironmon` and the selected tracker under `Ironmon Tracker`.
5. Double-click `Ironmon Tracker/Ironmon Tracker.exe`. The self-contained
   archive requires no .NET installation. The runtime-required archive
   requires the Windows x64 .NET 10 Runtime, but not the SDK or MAUI
   workload. Starting the game first also works.
6. Leave `Download data` enabled in Gameplay Options so missing sprites are
   downloaded automatically. On a new or reinstalled game, the first tracker
   views containing many Pokemon—especially evolution graphs—can take longer
   while Infinite Fusion downloads missing sprite sheets. Some custom sprites
   may be absent or temporarily use a fallback until the game's local download
   allowance resets. This improves naturally as the local sprite cache fills.
   To prepare or update the custom fusion and normal-species libraries, fully
   close Infinite Fusion and use **Settings > Custom sprite library** in the
   tracker. The first synchronization can be a large download. Later
   synchronizations use the official server's change metadata, keep current
   sheets without downloading their image data, and replace only missing or
   changed sheets. Updated sheets also clear their tracker-generated
   individual-sprite cache so new custom fusion sprites and normal-species
   variants appear immediately. The tracker does not change Infinite Fusion's
   `Download data` setting or its normal download limit.
7. Start the game and select Ironmon when beginning a supported Hoenn run.

## Activate diagnostic access

The ordinary Release tracker can accept a maintainer-issued diagnostic-access
token; users do not need a separate Debug tracker build.

1. Open **Diagnostic Access** with the key button in the tracker header.
2. Paste the complete token and select **Activate**, or load the supplied
   `.ironmon-access` file.
3. Review the displayed note, expiration, token and key identifiers, direct
   grants, and included grants.
4. Connect or keep using the game. The tracker updates the game-side grant
   immediately without requiring a reconnect.

Activating another valid token replaces the current token after a warning.
**Remove access** deletes the locally stored token and immediately closes or
clears protected views. An expired token is clearly marked Expired, grants no
capabilities, and can be removed or replaced. Ironmon stores only one token in
the current Windows user's local application data. The token, support note,
signature, expiration, and signing-key material are never sent to the game.

Only accept access files received through a channel you trust. A token grants
exactly its listed diagnostic information; it does not run code and does not
modify gameplay state.

Existing non-Ironmon saves retain their normal behavior. Pre-release Ironmon
saves and archive recipes from before the version reset are intentionally not
upgraded. Start a new run after installing this build. From this baseline onward,
compatible runs retain their original profile and deterministic implementation;
incompatible or incomplete metadata is rejected without rewriting the save.

## Share a seeded run

Open **Seeded runs** with the chain-link button while an active attempt is
connected, or select a completed attempt in **Archive**. Choose **Create
token**, then copy it or save it as an `.ironmon-seed` file. Ordinary seed
tokens identify tracker-generated data but are not maintainer-approved
challenges.

To import, connect an active Ironmon attempt, paste or load the complete token,
validate it, and confirm the replacement. The game independently checks the
version, configuration, data mode, and generator compatibility before it
abandons the current attempt. Keep the game scripts and tracker from the same
release together.

## Build the package from this repository

Run `tools/Build-TrackerRelease.ps1` from PowerShell. It first runs the Release
tracker tests and complete bundled-runtime suite, including seed-token and
deterministic-import checks. It then regenerates and validates the release
datasets, synchronizes `src`, and publishes both self-contained and
runtime-required Windows x64 trackers. It verifies both player outputs and
creates a deterministic release ZIP and SHA-256 checksum for each in `release`.
Any failed test, stale generation, or packaging violation aborts the release.
The player-package build rejects token and private-key files, maintainer or
release-data generators, audits, and development scripts.

## Diagnostic log

Ironmon writes `Ironmon.log` beside the game's save files. Each new run, loaded
run, and F7 reset records the Ironmon version, context, seed, both fusion
policies, custom-sprite pool size and fingerprint, and mapping counts. Each
entry also includes ability, base-stat, move-access, normal-evolution, and
fusion-evolution generator versions and fingerprints, plus the number of
recorded evolution events. A log write failure never prevents the game from
running.

## Remove Ironmon

Finish any pending recovery, then fully close the game and tracker. Remove
`Data/Scripts/997_Ironmon`, `Data/Scripts/000_Ironmon_Guard.rb` when present, and
`Ironmon Tracker`. Keep a backup of `Data/Ironmon` if you intend to resume old
Ironmon runs after reinstalling: it contains their historical generation profiles.
These removals do not remove or modify save files or tracker data in the user's
local application-data directory.
