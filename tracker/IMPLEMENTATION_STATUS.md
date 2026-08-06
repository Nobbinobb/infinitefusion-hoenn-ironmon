# Ironmon Tracker implementation status

This document records which reviewed implementation parts exist. The stable
product behavior remains defined by `../docs/design/TRACKER.md`.

## Part overview

| Part | Scope | Status |
| --- | --- | --- |
| 1 | Production solution, domain foundation, protocol envelope and framing | Reviewed |
| 2 | .NET 10 Blazor Hybrid desktop shell | Reviewed |
| 3 | Persistent TCP connection and Ruby bridge | Implemented; awaiting review |
| 4 | Player battle tracking and healing inventory | Not started |
| 5 | Enemy tracking, remembered moves, and annotations | Not started |
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

Status: **Implemented; awaiting review**

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

Part 4 must not begin until Part 3 is accepted or revised.
