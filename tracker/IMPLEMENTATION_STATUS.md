# Ironmon Tracker implementation status

This document records which reviewed implementation parts exist. The stable
product behavior remains defined by `../docs/design/TRACKER.md`.

## Part overview

| Part | Scope | Status |
| --- | --- | --- |
| 1 | Production solution, domain foundation, protocol envelope and framing | Reviewed |
| 2 | .NET 10 Blazor Hybrid desktop shell | Implemented; awaiting review |
| 3 | Persistent TCP connection and Ruby bridge | Not started |
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

Status: **Implemented; awaiting review**

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

Part 3 must not begin until Part 2 is accepted or revised.
