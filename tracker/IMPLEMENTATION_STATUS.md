# Ironmon Tracker implementation status

This document records which reviewed implementation parts exist. The stable
product behavior remains defined by `../docs/design/TRACKER.md`.

## Part overview

| Part | Scope | Status |
| --- | --- | --- |
| 1 | Production solution, domain foundation, protocol envelope and framing | Reviewed |
| 2 | .NET 10 Blazor Hybrid desktop shell | Reviewed |
| 3 | Persistent TCP connection and Ruby bridge | Reviewed |
| 4 | Player battle tracking and healing inventory | Reviewed |
| 5 | Enemy tracking, remembered moves, and annotations | Implemented; awaiting review |
| 6 | Deterministic post-run search and lookup | Not started |
| 7 | Debug-mode inspector parity | Not started |
| 8 | Release packaging and end-to-end validation | Not started |

Implementation stops at each part boundary for review before the next part
begins.

## Part 1: Foundation and protocol contracts

Status: **Reviewed**

Implemented on 2026-08-06:

- Added the production `.NET 10` solution at `Ironmon.Tracker.slnx`.
- Added `Ironmon.Tracker.Core` for tracker-owned domain rules.
- Added `Ironmon.Tracker.Protocol` for versioned duplex communication.
- Added repository-wide tracker build settings that enable nullable analysis,
  XML documentation output, C# 14, and warnings as errors.
- Added a compact completed-run recipe that stores deterministic reconstruction
  inputs rather than generated Pokemon data.
- Added move discovery source and origin models.
- Implemented the newest-four remembered-move projection.
- Preserved older discoveries for later lower-level encounters.
- Added same-level ordering through a stable `learn_order` value.
- Excluded non-level-up player moves from enemy discovery projection.
- Added the versioned event/request/response envelope.
- Added snake-case JSON serialization and semantic envelope validation.
- Added structured failed responses.
- Added persistent NDJSON stream readers and writers.
- Added the initial game and tracker handshake payloads.
- Added protocol limits and the loopback port constant.
- Applied the reviewed C# conventions across the Part 1 implementation and
  tests: explicit enum values, brace-free single-line control bodies, compact
  parenthesized expressions, switch expressions, and collection expressions
  where applicable.

The browser-based connectivity prototype was removed after it proved that the
bundled game runtime can send structured data to a .NET process. Only the
production foundation remains in the tracker workspace.

### Validation

The Part 1 test project contains 12 passing tests covering:

- completed-run recipe construction and invalid generator versions;
- four-newest move projection;
- lower-level recovery of retained move discoveries;
- same-level `learn_order` ties;
- exclusion of machine and tutor moves;
- duplicate move observations;
- canonical snake-case protocol JSON;
- required request identifiers;
- consecutive NDJSON messages over one stream;
- line-feed-only framing; and
- structured failed responses.

Validation command:

```powershell
& 'C:\Program Files\dotnet\dotnet.exe' test `
  'tracker\tests\Ironmon.Tracker.Tests\Ironmon.Tracker.Tests.csproj' `
  --no-restore --nologo --disable-build-servers -m:1 `
  -p:UseSharedCompilation=false
```

Result: **12 passed, 0 failed, 0 skipped**.

### Deliberately not implemented

Part 1 does not contain:

- a desktop executable or user interface;
- a TCP listener or reconnecting connection service;
- production Ruby tracker integration;
- battle, Pokemon, move-detail, healing, or enemy event payloads;
- persistence files;
- post-run request handlers;
- debug inspection requests; or
- release publication.

These belong to later reviewable parts.

### Part 1 review points

Review should confirm:

1. the production solution and project boundaries;
2. the common event/request/response envelope;
3. retaining all move discoveries while displaying only the newest four;
4. the distinction between move learn source and discovery origin;
5. the completed-run recipe fields; and
6. the initial handshake fields and fixed loopback port.

Part 1 was accepted after its C# convention revisions.

## Part 2: Blazor Hybrid desktop shell

Status: **Reviewed**

Implemented on 2026-08-06:

- Added the Windows-only `.NET 10` MAUI Blazor Hybrid application project.
- Added the application project to the production solution and referenced the
  existing Core and Protocol projects.
- Configured an unpackaged native Windows executable with no HTTP server or
  external browser.
- Added a compact resizable native window with minimum dimensions.
- Added the embedded Razor component host and production tracker styling.
- Added visible waiting-for-game connection state.
- Added Player and Enemy tabs with mouse and `P`/`E` keyboard switching.
- Added player and enemy waiting cards that communicate when their respective
  game events will initialize them.
- Added disabled enemy annotation boxes as layout placeholders without
  implementing Part 5 behavior early.
- Replaced the generated .NET branding with tracker icon and splash assets.
- Removed generated Android, iOS, and Mac Catalyst targets and sample assets.

### Validation

- The Windows application builds with 0 warnings and 0 errors.
- The application process remains running throughout a 10-second startup smoke
  test and only the exact smoke-test process is stopped afterward.
- The complete foundation test suite still passes: **12 passed, 0 failed, 0
  skipped**.

### Deliberately not implemented

Part 2 does not contain:

- a TCP listener, reconnection, or game handshake;
- live player, enemy, healing, move, or battle state;
- persisted settings or window placement;
- functional always-on-top or display scaling controls;
- enabled enemy annotations;
- a Debug tab;
- WebView2 installation recovery UI; or
- self-contained release publication.

These behaviors remain assigned to later reviewable parts.

### Part 2 review points

Review should confirm:

1. the Windows-only MAUI Blazor Hybrid project boundary;
2. the compact native window size and dark tracker presentation;
3. Player and Enemy tab behavior and keyboard shortcuts;
4. the waiting states before the game connection exists; and
5. the deliberately inactive data and annotation controls.

Part 2 was accepted after application and code review.

## Part 3: Persistent connection and Ruby bridge

Status: **Reviewed**

Implemented on 2026-08-06:

- Added a platform-neutral connection library used by the desktop application.
- Added a single-client TCP listener bound only to IPv4 loopback port `38521`.
- Added a five-second first-message timeout and validation that
  `game_connected` is the first game message.
- Added tracker/game handshake exchange and tracker version reporting.
- Added an immediate correlated `current_state` recovery request after every
  handshake.
- Added connection status snapshots for waiting, handshaking, connected,
  error, and stopped states.
- Connected the Razor header indicator to background connection changes.
- Added game and run connection-level state payload contracts.
- Added persistent run identifiers and monotonically increasing tracker event
  sequences to Ironmon save metadata.
- Added `run_started` notification when a run begins while connected.
- Added the production Ruby bridge using a background connection thread and
  zero-timeout readiness checks for established socket reads and writes.
- Added restrained retry, safe disconnect, unknown-command errors, framing
  limits, JSON-key normalization for the bundled runtime, and reconnect state
  recovery.
- Added the exact implemented connection subset to `PROTOCOL.md`.

### Validation

- The complete solution builds with 0 warnings and 0 errors.
- The test suite contains **15 passing tests**, including duplex handshake,
  current-state recovery, run-start updates, invalid first messages, and silent
  handshake timeouts.
- The canonical scripts were synchronized through `Build-Distribution.ps1`.
- Infinite Fusion's bundled runtime loads the bridge without JSON, socket API,
  schema, or timeout errors.
- The game remains running when the tracker is absent.
- Tracker-first and game-first startup establish the loopback connection.
- Closing and restarting the tracker causes the running game to reconnect and
  recover state without restarting the game.
- The final restart test retained exactly one established game connection and
  stopped only the exact processes started by the test.

### Deliberately not implemented

Part 3 does not contain:

- battle lifecycle identifiers or events;
- player Pokemon snapshots or updates;
- healing inventory state;
- enemy state or move discovery events;
- persisted tracker-owned discoveries, annotations, or settings;
- post-run lookup commands;
- debug inspection commands; or
- release publication.

These behaviors remain assigned to later reviewable parts.

### Part 3 review points

Review should confirm:

1. the connection library boundary between MAUI and protocol code;
2. the fixed IPv4 loopback listener and one-client lifecycle;
3. the handshake and immediate `current_state` recovery sequence;
4. the bundled-runtime-compatible non-blocking Ruby design;
5. the UI connection states and error presentation; and
6. the new persisted run identifier and sequence metadata.

Part 3 was accepted and committed as `e3c30f4`.

## Part 4: Player battle tracking and healing inventory

Status: **Reviewed**

Implemented on 2026-08-06:

- Added battle identifiers and `battle_started`/`battle_ended` lifecycle events.
- Initialized the player card only after the game's real player send-out path.
- Added complete player snapshots with identity, species/form, sprite path,
  level, HP, status, types, ability, held item, calculated stats, BST, nature,
  moves, PP, power, accuracy, and healing inventory.
- Added a throttled snapshot comparison that sends `player_state_changed` only
  after legal live data changes.
- Continued player and healing-inventory updates outside battle after the first
  legal send-out initializes the player card.
- Covered damage, healing, status, levels, stat recalculation, move learning or
  replacement, PP, held items, and bag healing changes through complete state.
- Added temporary confusion tracking alongside persistent major status, with
  combined display such as `Poisoned · Confused` when both apply.
- Added game-owned healing calculations for fixed heals, Sitrus Berry,
  maximum-HP medicine, healing berries, and the conditional Rage Candy Bar.
- Added a thread-safe tracker run-state store and reconnection recovery for the
  active battle and player snapshot.
- Replaced the player waiting placeholders with the populated live card,
  including a locally loaded sprite, HP bar, details, stats, healing capacity,
  and four move rows.
- Applied review revisions that use culture-independent HP-bar widths, label
  unknown HP bars, preserve the detail and summary rows before initialization,
  retain four move slots for Pokemon with shorter move lists, and give the
  enemy waiting card the same overall information structure.
- Extended the exact protocol document with the Part 4 event payloads and
  healing rules.

### Validation

- The complete solution builds with 0 warnings and 0 errors.
- The test suite contains **17 passing tests**, including complete player
  serialization, Part 3 recovery compatibility, and live battle/player event
  dispatch through the persistent connection.
- Canonical scripts were synchronized through `Build-Distribution.ps1`.
- Infinite Fusion's bundled runtime loads the new battle hooks and remains
  running without the tracker.
- The native tracker and bundled game remain running together and establish
  both sides of one IPv4 loopback connection on port `38521`.
- Only the exact tracker and game processes started for smoke validation were
  stopped afterward.

### Deliberately not implemented

Part 4 does not contain:

- enemy snapshots, automatic Enemy-tab selection, or move discovery;
- functional enemy annotations;
- tracker-owned persistence;
- post-run lookup commands;
- debug inspection commands; or
- release publication.

These behaviors remain assigned to Parts 5 through 8.

### Part 4 review points

Review should confirm:

1. initialization happens on actual send-out rather than party position;
2. the complete player card remains current during battle;
3. healing item counts and potential HP match the game-owned rules;
4. the local sprite and compact populated layout render correctly; and
5. battle/player recovery works after reconnecting the tracker.

Part 4 was accepted and committed as `0fcf8b0`.

## Part 5: Enemy tracking, remembered moves, and annotations

Status: **Implemented; awaiting review**

Implemented on 2026-08-06:

- Added legal enemy snapshots for both wild activation and trainer send-outs.
- Added active enemy recovery to `current_state` and support for multiple
  opposing battler positions while displaying the first position initially.
- Added automatic Enemy-tab selection for a newly sent-out opponent and return
  to Player when battle ends without overriding later manual tab selection.
- Added `enemy_move_used` observations with generated learned level, stable
  learn order, source, move details, and observed remaining PP.
- Detects move use through both the battle callback and the proven periodic
  enemy-state path, deduplicated by enemy, move, and remaining PP.
- Includes the last legally observed regular move in enemy snapshots so move
  discovery uses the same transport path as the working enemy card.
- Added player-assisted level-up move discoveries to complete player snapshots.
- Kept non-level-up player moves out of remembered enemy moves while retaining
  directly observed enemy moves if learn-level resolution is unavailable.
- Displayed the newest four applicable remembered moves and updated observed
  PP after repeated use.
- Added functional HP, Attack, Defense, Special Attack, Special Defense, and
  Speed annotations with forward left-click and reverse right-click cycling.
- Added tracker-owned atomic persistence under
  `%LocalAppData%/IronmonTracker/runs/` for discoveries and annotations.
- Restored discoveries and annotations when a species/form returns or the
  tracker reconnects, without adding data to the game save.
- Selects the run identifier carried by every live event, including when the
  tracker connected at the title screen before an existing save was loaded.
- Derives newly assigned run identifiers from the saved deterministic Ironmon
  seed so reloading a legacy save selects the same tracker knowledge file.
- Populated the legal enemy card with local sprite, name, level, types, BST,
  hidden-field labels, annotations, and remembered move rows.

### Validation

- The complete solution builds with 0 warnings and 0 errors.
- The test suite contains **20 passing tests**, including newest-four persisted
  projection, repeated-use PP replacement, player assistance, unknown-source
  direct observation, reload, and bidirectional annotation cycling.
- Canonical scripts were synchronized through `Build-Distribution.ps1` and
  matched both distribution and local runtime copies.
- The native tracker and bundled game remained running together with both
  sides of one established IPv4 loopback connection after the new battle and
  battler hooks loaded.
- Only the exact tracker and game processes started for validation were stopped.

### Deliberately not implemented

Part 5 does not contain:

- deterministic post-run search and lookup;
- completed-run recipe persistence;
- debug inspection commands or UI;
- settings and window-state persistence; or
- self-contained release publication.

These behaviors remain assigned to Parts 6 through 8.

### Part 5 review points

Review should confirm:

1. wild and trainer opponents automatically open a legal enemy card;
2. used enemy moves and player-assisted level-up moves become visible;
3. the four newest applicable discoveries return on later encounters;
4. observed PP updates after repeated enemy move use;
5. annotations cycle in both directions and survive tracker restarts; and
6. battle end returns to Player while manual P/E switching remains available.

Part 6 must not begin until Part 5 is accepted or revised.
