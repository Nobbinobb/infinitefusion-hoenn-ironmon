# Ironmon Tracker implementation status

This document records which reviewed implementation parts exist. The stable
product behavior is defined by the
[Ironmon Mechanics Manual](../docs/IRONMON_MECHANICS.html#tracker).

## Part overview

| Part | Scope | Status |
| --- | --- | --- |
| 1 | Production solution, domain foundation, protocol envelope and framing | Reviewed |
| 2 | .NET 10 Blazor Hybrid desktop shell | Reviewed |
| 3 | Persistent TCP connection and Ruby bridge | Reviewed |
| 4 | Player battle tracking and healing inventory | Reviewed |
| 5 | Enemy tracking, remembered moves, and annotations | Reviewed |
| 6 | Deterministic post-run search and lookup | Reviewed |
| 7 | Debug-mode inspector parity | Reviewed |
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

Status: **Reviewed and committed**

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

Part 5 was accepted and committed as `f78cecf`.

## Adjustment Part A: Tracker data expansion

Status: **Reviewed**

Implemented on 2026-08-07:

- Preserved both active opposing positions for the upcoming double-battle
  selector.
- Persisted the highest encountered level per run and species/form.
- Added legal multi-ability discovery from player ownership and opposing
  ability splash activation, including localized descriptions.
- Added player learnset progress as learned unique level-up moves, total unique
  level-up moves, and the next learn level.
- Added consistently ordered evolution choices with compact level, item, or
  other requirement metadata while withholding the destination species.
- Added player nature adjustments for Attack, Defense, Special Attack, Special
  Defense, and Speed.
- Added move category and localized description metadata to player and observed
  enemy moves.
- Added standard type-only effectiveness calculation covering neutral, half,
  quarter, double, quadruple, and immune results.
- Added a once-per-send-out `player_move_menu_opened` signal. The UI keeps its
  automatic Enemy switch and returns to Player when the move menu first opens.

### Validation

- The tracker application builds with 0 warnings and 0 errors.
- The test suite contains **29 passing test cases**, including type-chart,
  highest-level, multiple-ability, persistence, and live move-menu coverage.
- Canonical scripts were synchronized through `Build-Distribution.ps1`.
- The bundled game remained running after the new hooks and snapshots loaded;
  only the exact validation process was stopped.

### Deliberately deferred to Adjustment Parts B and C

- double-battle enemy selection controls;
- revised stat, move, evolution, and ability presentation;
- nature and effectiveness indicators;
- clickable move and ability detail panels.

Adjustment Part B must not begin until Adjustment Part A is accepted or
revised.

## Adjustment Part B: Tracker card presentation

Status: **Reviewed**

Implemented on 2026-08-07:

- Added an Enemy 1/Enemy 2 selector while retaining one focused enemy card.
- Applied the selected enemy consistently to its sprite, annotations,
  remembered moves, abilities, and player-move effectiveness.
- Displayed the highest encountered level and all legally discovered abilities
  for the selected enemy. Multiple abilities use a compact expandable list.
- Added player learnset progress in `x/y (z)` form and compact evolution
  requirements without exposing destination species.
- Reordered both stat grids into `SPE/HP`, `ATK/DEF`, and `SpA/SpD` rows.
- Added maximum HP to the player stat grid and green/red nature arrows to all
  nature-adjustable player stats.
- Added Physical, Special, and Status labels plus type-colored move names and
  borders. The labels were then refined into category icons placed before a
  larger, vertically centered move name.
- Applied the exact reference-image type palette and reused it for the typing
  displayed beneath each Pokemon name; redundant player species text was
  removed from that line.
- Added type-only effectiveness indicators: one or two green up arrows, one or
  two red down arrows, and a red immunity cross, with larger indicators and
  explicit color specificity.
- Moved healing beside player condition, learnset progress into the Moves
  heading, and player Ability, Item, and Evolution into the card's upper-right
  information group.
- Mirrored enemy Ability and Item into the same fixed-size upper-right group,
  removed the redundant enemy move-count chip, widened the type border, and
  replaced text glyphs with dedicated SVG category icons.
- Left-aligned upper-right chip contents, strengthened nature and effectiveness
  contrast, and persisted the last valid tracker window size. First launch now
  defaults to `500 x 860` rather than the earlier narrow `440 x 680` window.
- Preserved automatic Enemy selection for newly appearing opponents and the
  one-time return to Player when the player's move menu first opens.
- Refactored the tracker UI into focused `PlayerCard`, `EnemyCard`, player and
  enemy stat-grid, player and enemy move-list, shared move-row, and move-category
  icon components. `Home` now owns only the application shell, connection
  display, and primary navigation.
- Moved all component C# logic into matching `.razor.cs` partial classes and
  removed inline `@code` blocks from the Razor files. Shared sprite loading and
  move presentation rules now live in dedicated helpers.

### Validation

- The native tracker application builds with 0 warnings and 0 errors.
- All **29 test cases** pass after the presentation changes.
- The component-refactored application builds with 0 warnings and 0 errors,
  and all **29 test cases** continue to pass.
- The exact native tracker process remained running during its startup smoke
  test and was then stopped.
- The complete card presentation was accepted after UI and code review.

### Deliberately deferred to Adjustment Part C

- clickable full move information; and
- clickable full ability information.

Adjustment Part C must not begin until Adjustment Part B is accepted or
revised.

## Adjustment Part C: Move and ability information

Status: **Reviewed**

Implemented on 2026-08-07:

- Made every known player and discovered enemy move row selectable.
- Added a shared move-information panel showing type, category, PP, power,
  accuracy, visible type effectiveness, description, and the known learn level
  for remembered enemy moves.
- Made the player's legally known ability selectable from its existing header
  chip and added a shared ability-information panel with its localized
  description.
- Made each discovered enemy ability selectable. A single ability opens from
  its header chip, while multiple abilities remain behind the compact
  disclosure and can each open their information panel.
- Kept all new Razor logic in matching partial `.razor.cs` files and reused
  shared move and ability detail components across both cards.

### Validation

- The native tracker application builds with 0 warnings and 0 errors.
- All **29 test cases** pass after the detail-panel changes.
- The move and ability information panels were accepted as the first-version
  detail experience.

## Part 6: Deterministic post-run search and lookup

Status: **Reviewed**

Implemented on 2026-08-07:

- Added a game-owned `run_completed` event for battle loss or Hall of Fame
  completion and included the completed recipe in connection recovery.
- Added compact recipes containing only the run ID, seed, result, versions,
  configuration, species, ability, and player-fusion generator versions, and
  source-pool fingerprints.
- Persisted recipes atomically under the tracker data directory and explicitly
  kept reconstructed Pokemon results transient.
- Added correlated `pokemon_search` and `pokemon_lookup` requests over the
  existing persistent duplex connection, including timeout, disconnect, and
  structured game-error propagation.
- Added game-side validation for completed-run state, request/run identity,
  supported generator implementations, and normal-species, ability, and
  custom-fusion pool fingerprints.
- Added deterministic name search over the normal and custom-fusion categories
  allowed by the historical run configuration, with complete server-backed
  result paging in groups of 20.
- Added complete lookup reconstruction for identity, sprite, typing, all six
  base stats, BST, randomized ability slots, the complete level-up learnset,
  navigable current evolution destinations and direct pre-evolutions with
  sprites, displayed fusion components, deterministic Ironmon reverses, and
  the normal-material pairs mapped to a fusion.
- Replaced the broad custom-fusion relationship list with a normal-material
  search that calculates only the two seeded Ironmon orientations for the
  selected pair.
- Added session-only Back and Forward navigation shared by search results,
  evolutions, displayed components, reverse fusions, materials, and outcomes.
- Replaced full fusion-object construction during name search with a reusable
  lightweight search index derived from stable fusion IDs and split names.
- Added bounded game-side caches for search results and Pokemon lookups plus
  reusable fusion mappers, material scans, and sprite paths. Added
  connection-scoped tracker caches so repeated search pages, Pokemon lookups,
  and fusion previews require no additional game request.
- Disabled and guarded search, paging, result, and material controls while a
  request is active so synchronous game work cannot accumulate duplicate
  requests that later expire.
- Serialized post-run requests at the connection boundary, so requests from
  different nested components wait locally and begin their response timeout
  only after the preceding game request completes.
- Added a third Lookup view with completed-run selection, name search, result
  selection, generated-data presentation, and reusable move and ability detail
  panels.
- Prevented view hotkeys from firing while the user operates lookup form
  controls, added damage-category icons to the complete learnset, and removed
  its redundant type label.
- Cleared empty-search messaging after a result is selected so it cannot appear
  above a successfully loaded Pokemon.
- Bound lookup search text on each input event so a new query cannot reuse the
  previous query when Search is clicked before the field loses focus.
- Kept current natural evolution relationships distinct from the seeded
  evolution generator planned for Ironmon 0.6.0; the tracker does not invent
  future generated targets.
- Retained a fixed level-100 request value for compatibility with game
  processes running the earlier Part 6 script, without restoring the level
  filter or level-specific move display.
- Kept complete lookup unavailable while the loaded Ironmon run is active,
  even if a client supplies a previously completed recipe.
- Corrected live-view navigation so ordinary state updates no longer override
  a manually selected Enemy or Lookup view.

### Validation

- The complete .NET solution builds with 0 warnings and 0 errors.
- The test suite contains **30 passing tests**, including completed-recipe
  persistence, correlated live search, lookup, and fusion-preview requests,
  and connection-scoped response-cache reuse.
- Canonical Ruby scripts were synchronized to both distribution and local game
  copies.
- Infinite Fusion's bundled runtime loaded the Part 6 completion hooks and
  lookup handlers and remained running throughout the startup smoke test; only
  the exact test process was stopped.
## Part 7A: Debug inspector data and authorization

Status: **Reviewed**

Implemented on 2026-08-07:

- Added a dual debug-access gate requiring both tracker `--debug` launch intent
  and game-authorized `$DEBUG` mode. Both the tracker service and game bridge
  enforce the gate independently.
- Added `debug_inspect_pokemon` for the initialized player Pokemon, an active
  enemy battler position, or a player party position.
- Reproduced the current in-game inspector contract: identity, form, displayed
  fusion components, active ability, generator metadata, original/generated
  normal and hidden slots, final fusion slots, component slots, eligibility,
  source slots, and restricted-source replacements.
- Added `debug_run_diagnostics` for run identity, versions, configuration,
  fusion and ability pool metadata, and mapping counts.
- Kept both requests read-only and based on actual current Pokemon objects and
  existing generator APIs.
- Deliberately rejected fabricated arbitrary-species Pokemon targets because
  Infinite Fusion's ordinary Pokemon constructor consumes random values. A
  future species-only inspector must avoid that constructor.

### Validation

- The complete .NET solution test run contains **31 passing tests**.
- Tests cover successful authorized inspector and diagnostic requests plus
  tracker-side rejection without launch authorization.

### Remaining Part 7 work

- Debug-tab navigation and Pokemon inspector presentation;
- tracker-owned protocol history, raw state, and move-knowledge diagnostics;
- copy and diagnostic-report export; and
- final release-mode enforcement and end-to-end validation.

## Part 7B: Debug inspector navigation and presentation

Status: **Reviewed**

Implemented on 2026-08-08:

- Added a Debug primary tab only when the tracker was launched with `--debug`
  and the connected game authorizes debug access.
- Added Player and active Enemy target choices. Changing the target immediately
  refreshes the inspector; future party choices can be populated dynamically
  if Ironmon supports story-controlled team battles.
- Added separate Overview, Abilities, and Run Diagnostics pages.
- Reproduced inspector identity, sprite, item, form, displayed fusion
  components, active ability, and generator metadata on Overview.
- Grouped current, normal generated, final fusion, body generated, and head
  generated ability slots with original abilities, eligibility, fusion source,
  and restricted-source replacement details.
- Presented game-owned configuration, generator, pool, mapping, run, and battle
  diagnostics without adding C# reconstruction logic.
- Added the `D`/`4` authorized-view shortcut and automatically returns to the
  Player view if debug authorization is lost.
- Added symbol-only gender presentation to the player card and debug inspector.
  A distinct nickname remains the primary heading while its species is shown
  once as secondary metadata; identical nickname/species names are not repeated.
- Allowed battle losses now respect Infinite Fusion's `canLose` battle rule, so
  the protected opening encounter does not incorrectly complete a run.
- Resynchronized the tracked player with the live party object outside battle,
  allowing nickname and other overworld changes to update without waiting for
  another send-out.
- Made development tracker builds request debug authorization automatically so
  restarting the tracker preserves the Debug tab. Release builds continue to
  require an explicit `--debug` launch flag.
- Removed the unnecessary active-run requirement from authorized debug
  requests, allowing the inspector to work during reconnect recovery before a
  run identifier is available.
- Added an authorized active-run Lookup page with paged search, full generated
  Pokemon details, relation navigation, Back/Forward history, and fusion
  exploration. It reuses the post-run presentation while bypassing completion
  only through separately gated debug commands.
- Replaced lookup sprite resolution through temporary Pokemon construction with
  direct species sprite resolution, keeping active-run debug lookup from
  consuming gameplay randomness.

### Validation

- The complete .NET solution contains **31 passing tests**.
- The native .NET 10 Blazor Hybrid application builds with 0 warnings and 0
  errors.
- The authorization tests from Part 7A continue to cover the source data used
  by all four inspector pages.

### Remaining Part 7 work

- tracker-owned connection and protocol history;
- raw event, request, response, current-state, and move-knowledge inspection;
- copy actions and diagnostic-report export; and
- final end-to-end debug-mode validation.

## Part 7C: Tracker protocol diagnostics and report export

Status: **Reviewed**

Implemented on 2026-08-08:

- Added a bounded tracker-owned history containing the newest 200 connection
  lifecycle transitions and raw incoming/outgoing protocol messages.
- Retained the latest protocol or connection error independently of the bounded
  history.
- Added immutable diagnostic snapshots for current connection state, live run
  state, and persisted moves, abilities, highest levels, and annotations.
- Added a Protocol page with live formatted JSON, individual copy actions,
  history clearing, and complete report copy/export.
- Exported reports are written beneath
  `%LocalAppData%/IronmonTracker/diagnostics/` with timestamped filenames.
- Prevented automatic battle navigation from switching away while the Debug
  primary view is selected; manual navigation remains available.

### Validation

- The complete .NET solution contains **33 passing tests**.
- Tests cover bounded protocol history, raw JSON retention, last-error state,
  clearing, connection-service message capture, and immutable knowledge export.
- The native .NET 10 Blazor Hybrid application builds with 0 warnings and 0
  errors.
- The Release configuration also builds with 0 warnings and 0 errors; automatic
  debug requests remain limited to builds compiled with the `DEBUG` symbol.

Part 7 was accepted before Part 8 release packaging.

## Post-Part 7 structural refactor

Status: **Complete**

Implemented on 2026-08-08:

- Reorganized the application components into Common, Player, Enemy, Lookup,
  Debug, Navigation, Layout, and Pages feature folders. Feature components now
  use namespaces matching their folders.
- Reorganized the Connection project into transport, run-state, knowledge,
  diagnostics, and completed-run storage areas.
- Reorganized Core into move and run domain areas, and Protocol into transport,
  connection, live-state, Pokémon, lookup, and debug contract areas.
- Reorganized tests to mirror the Connection, Core, and Protocol production
  areas.
- Aligned namespaces with every new production and test folder, and adopted
  `System.Threading.Lock` for private lock-only synchronization gates.
- Extracted the duplicated active-run and completed-run search, paging, lookup,
  error, and Back/Forward navigation behavior into the shared
  `PokemonLookupExplorer` component. The post-run component now owns only run
  selection, while the debug component owns only active-run presentation.
- Extracted game-request ownership from `TrackerConnectionService` into
  `TrackerRequestClient` and `TrackerRequestSession`. The client owns request
  validation, debug authorization, and lookup caches. The session owns the
  active writer, serialization locks, pending completions, response
  correlation, timeout, and disconnect failure behavior. UI request consumers
  now depend directly on the client, while the connection service retains
  listener, handshake, live-event, recovery, and response-routing
  orchestration.
- Preserved the existing protocol contracts and runtime behavior.

### Validation

- All **33 tests** pass.
- Debug and Release builds complete with 0 warnings and 0 errors.

## Part 8: Self-contained release packaging

Status: **Complete**

Implemented on 2026-08-08:

- Assigned the release assembly and executable name `Ironmon Tracker` and the
  first tracker version `0.1.0`.
- Added a reproducible Windows x64, unpackaged, self-contained publish script.
- Limited published MAUI and WinUI localization resources to English because
  the tracker does not currently provide a localized interface.
- Excluded generated assembly XML documentation and PDB files from the runtime
  package while retaining the required dependency and runtime configuration
  files.
- Added a deterministic combined Ironmon release archive with a SHA-256
  checksum. Its copy-ready layout contains `Data/Scripts/997_Ironmon` and
  `Ironmon Tracker/Ironmon Tracker.exe` with supporting runtime files.
- Excluded the 182 MB generated publish directory and generated tracker release
  archives from Git while retaining their reproducible build scripts.
- Added startup detection for the Evergreen WebView2 Runtime. A missing runtime
  now shows a native recovery page linking to Microsoft's download page instead
  of attempting to initialize a blank embedded browser.
- Updated installation and developer documentation for the double-click release
  workflow.

### Validation

- All **33 tests** pass in Release configuration.
- The self-contained tracker publish contains **420 files** and is **178.40 MB**.
  Its only framework culture directory is `en-us`.
- The combined release archive is **68.59 MB** and contains **447 entries**,
  including **25 Ruby scripts**, the tracker executable, and no XML
  documentation or PDB files.
- The generated archive matches its published SHA-256 checksum:
  `ff7bbad6735097a6524f6f69fcd3ea73c9495f5f3ba5ae235f26c2c3ae121534`.
- The published tracker executable passed a startup smoke test and its exact
  test process was stopped afterward.

## Step 3.3 Part 6: Tracker move-access inspection

Status: **Complete**

Implemented on 2026-08-09:

- Added shared Learnset, Egg, TM, and Tutor tabs to completed-run Lookup,
  authorized active Debug Lookup, Player Debug, and Enemy Debug.
- Included acquisition details for generated level-up, evolution, Egg, TM/TR,
  ordinary-tutor, and specialized Fusion Tutor moves.
- Limited the Tutor tab to moves backed by an available tutor while retaining
  the abstract ordinary-tutor compatibility count for comparison.
- Added complete Step 3.3 move generator and source fingerprints to completed
  run recipes and active debug recipes.
- Preserved old completed-run recipes by falling back to native move access
  when no Step 3.3 move metadata is present, while rejecting partial or
  incompatible new metadata.
- Kept the legacy lookup learnset field synchronized for older tracker clients.

### Validation

- Protocol serialization covers all four move-access channels and acquisition
  metadata.
- Completed-run archive tests cover full metadata persistence and reject
  fingerprints without a move-access generator version.
- The tracker app builds with 0 warnings and 0 errors.
- Infinite Fusion's bundled runtime verifies normal and fusion snapshots,
  lookup/debug agreement, supported tutor filtering, recipe compatibility,
  and seed-dependent results.

## Tracker semantic constants refactor

Status: **Complete**

Implemented on 2026-08-09:

- Centralized protocol command names, event names, framing values, search
  limits, compatibility level, schema defaults, request identifier format, and
  timeout values.
- Centralized tracker storage paths, filenames, extensions, diagnostic export
  naming, diagnostic history limits, and lifecycle entry names.
- Centralized application window settings, preference keys, command-line
  switches, font identity, WebView2 download location, and sprite media types.
- Centralized move-access tab IDs, debug target IDs and position rules,
  keyboard shortcuts, gender IDs, move-data sentinels, base-stat scaling, and
  behavior-selected CSS classes.
- Retained enums for typed state and kept user-visible copy, CSS layout values,
  type-chart data, test fixtures, and natural zero-based boundaries local to
  their presentation or algorithm.

### Validation

- All **35 tracker tests** pass.
- The Windows tracker application builds with 0 warnings and 0 errors.
- A source audit finds no remaining inline protocol command/event names,
  request timeout values, tracker storage names, or move/debug tab identifiers.

## Tracker localization infrastructure

Status: **Complete**

Implemented on 2026-08-09:

- Added one shared .NET localization resource set for native MAUI views and
  Blazor components, with English (`en-US`) as the neutral fallback.
- Registered resource lookup once during tracker startup and made the shared
  localizer available to every Razor component.
- Migrated user-visible text across the native window, WebView2 recovery view,
  shared dialogs, Player, Enemy, Lookup, Debug, page titles, navigation,
  accessibility labels, generated status messages, and formatted summaries.
- Retained an explicit supported-culture build filter so unsupported framework
  and tracker satellite assemblies are excluded; the list currently contains
  only English (`en-US`).
- Made release packaging read that project allowlist and remove every other
  culture directory instead of maintaining a separate hard-coded language.
- Organized localization keys as `Area.Component.Meaning` and kept identical
  English text separate when it appears in different translation contexts.
- Declared the localization root namespace explicitly because the executable
  assembly name (`Ironmon Tracker`) differs from the resource namespace
  (`Ironmon.Tracker.App`).
- Added a startup resource probe so a future discovery regression fails clearly
  instead of silently rendering localization keys.
- Documented the culture naming, fallback, translation, and resource-boundary
  rules in `tracker/LOCALIZATION.md`.

### Validation

- A localization audit matches all **327 source keys** to exactly **327 English
  resource entries**, with no missing or unused entries.
- The self-contained publication contains only the supported `en-us` culture
  directory.
- The published executable passed a hidden startup smoke test and its exact
  test process was stopped afterward.
- The Debug executable passed the localization startup probe in a hidden smoke
  test and its exact test process was stopped afterward.
- All **35 tracker tests** pass.
- The Windows tracker application builds in Debug configuration with 0
  warnings and 0 errors; the Release publication also completes successfully.

## Step 3.3 Part 7: Post-run move-access iteration data

Status: **Complete**

Implemented on 2026-08-09:

- Added save-backed, schema-versioned observations for encountered player and
  enemy Pokemon, obtained TMs/TRs, tutor interactions, move acquisitions, and
  moves actually used.
- Kept active collection lightweight: encounters retain identity, level, side,
  and count, while complete generated access is reconstructed
  only when the run ends.
- Added completed-run summaries for channel entry counts, unique moves,
  cross-channel overlap, earliest damaging-move level, and the theoretical
  initial four at each encountered level.
- Recorded the special level-1 rule against the effective last four entries
  available by level 1, including the total level-1 entry count and damaging
  count in that starting four.
- Added abstract-versus-supported ordinary tutor counts and per-channel fusion
  duplicate removal and growth over the larger component.
- Recorded party compatibility when a found or scripted machine is obtained,
  party compatibility and teaching outcomes at tutor visits, acquisitions by
  level-up, Egg, TM/TR, tutor, copy effect, or script, and aggregated move use.
- Added a collapsed completed-run analysis panel beneath Pokemon Lookup. Older
  completed recipes show a clear unavailable state and remain valid.
- Localized every new analysis label with the hierarchical tracker key scheme.

### Validation

- All **36 tracker tests** pass, including metric archive round-tripping and
  explicit rejection of an unsupported metrics schema.
- The Windows tracker application builds in Release configuration with 0
  warnings and 0 errors.
- The current English resource contains **378 unique entries**; all **366
  literal source keys** resolve, and the remaining dynamic metric-source keys
  are present.
- Infinite Fusion's bundled runtime loads all canonical scripts successfully in
  a hidden startup smoke test, and the exact test process is stopped afterward.

## F7 reset and tracker performance pass

Status: **Complete**

Implemented on 2026-08-09:

- Added a reset-specific checkpoint-load boundary. F7 now restores native save
  state but skips rebuilding the checkpoint's old species, ability, base-stat,
  move-access, difficulty, and diagnostic state immediately before the new run
  replaces it.
- Changed current-schema wild and trainer species mappings to their designed
  on-demand behavior. New runs no longer enumerate every authored encounter and
  trainer slot during reset; each stable context is generated and retained when
  first used.
- Kept completed-run occurrence lookup unchanged because it already
  reconstructs all theoretical wild and trainer slots directly from the recipe.
- Cached successful base-stat source validation across resets because the
  installed source catalog does not change with the run seed.
- Corrected the tracker clock conversion for Infinite Fusion 6.8.0. Its
  `System.uptime` uses microseconds, so reconnect/error throttles and the 100 ms
  player/enemy snapshot interval previously behaved as effectively unthrottled.

### Validation

- A bundled-runtime benchmark against `IronmonCheckpoint_File_H.rxdata`
  measured the previous equivalent work at about **2.268 seconds**: 1.611
  seconds for a full post-load rebuild, 0.317 seconds for new generation, and
  0.340 seconds for eager mappings.
- The optimized bundled-runtime path measured **1.509 seconds** for checkpoint
  restoration plus generation: 1.117 seconds to restore and 0.392 seconds to
  generate. This is roughly **0.76 seconds / 33% less work** before scene and
  save overhead.
- Runtime assertions verified that wild and trainer maps begin empty, identical
  contexts remain deterministic, and first use retains exactly one entry.
- A 100 ms runtime sleep advanced `System.uptime` by about 100,027 units,
  confirming the required 1,000,000-units-per-second conversion.

## Ironmon 0.5.0 move-access release

Status: **Complete**

Built on 2026-08-09:

- Marked Milestone 3 Step 3.3 design and implementation complete.
- Updated the game version, installation guide, protocol examples, project
  overview, and release notes to Ironmon `0.5.0`.
- Published the self-contained Windows x64 tracker and combined it with 28
  canonical Ruby scripts in `Ironmon-v0.5.0-move-access.zip`.

- Rebuilt the release independently and obtained the same SHA-256 checksum.

### Validation

- All **36 tracker tests** pass.
- The tracker application builds in Release with 0 warnings and 0 errors.
- The archive contains **452 entries**, one tracker executable, no PDB files,
  no generated XML documentation, and only the supported `en-us` culture
  directory.
- The published tracker and synchronized Infinite Fusion runtime passed a
  concurrent hidden startup smoke test. Only the exact processes started by
  the test were stopped.
- SHA-256:
  `fa1ae7967ffee287959dbc9fd1849c25cc4650df76d3620bfa3200fa2676f644`.

## Ironmon 0.6.0 evolution release

Status: **Complete**

Built on 2026-08-09:

- Added deterministic normal and complete-fusion evolution target generation,
  runtime integration, save compatibility, and story-path consumers.
- Added authorized candidate lists and clickable one-step graphs to Debug and
  completed-run lookup while keeping targets hidden during active play.
- Added versioned encountered-evolution metrics and completed-run analysis.
- Published the cumulative game scripts and self-contained tracker in
  `Ironmon-v0.6.0-evolutions.zip`.
- Rebuilt the release independently and obtained the same SHA-256 checksum.

### Validation

- All **40 tracker tests** pass.
- The tracker application builds with 0 warnings and 0 errors.
- The archive contains **457 entries** and all **33 canonical Ruby scripts**,
  with no PDB files or generated XML documentation.
- The packaged tracker and synchronized Infinite Fusion runtime passed a
  concurrent hidden startup smoke test. Only the exact processes started by
  the test were stopped.
- SHA-256:
  `8506ebc50d522056e100fc78549fd06778377f53093437e98b26798f6609107d`.

## Pokemon information entry-point alignment

Status: **Complete**

- Added one shared Overview, Abilities, Stats, Moves, and Evolutions tab set to
  Debug Pokemon, Debug Lookup, and completed-run Lookup.
- Combined its live `DebugPokemonInspectorSnapshot` with the same active-run
  `PokemonLookupSnapshot` used by Debug Lookup inside that shared card.
- The shared Abilities tab reconstructs original, generated, component, and
  final-fusion slot diagnostics in all three entry points, including
  eligibility, source component and slot, source ability, and restricted
  replacements, alongside clickable generated-ability detail panels.
- Debug Pokemon uses the same tab and merges in the current live slot and its
  active state.
- Moved ability-generator metadata from Overview to Abilities and added the
  same deterministic generator diagnostics to the Stats, Moves, and Evolutions
  tabs in all three entry points.
- Removed the iconless duplicate displayed-component block from the live
  Overview and corrected the malformed player/enemy selector separator.
- Distributed authored relationships and occurrences, fusion information,
  generated stats and move access, and evolution data across the same pages in
  every entry point.
- Debug Pokemon and Debug Lookup reconstruct against the active authorized run;
  completed-run Lookup reconstructs against its selected archived recipe.
- Split lookup and live-inspector payloads by Overview, Abilities, Stats,
  Moves, and Evolutions. Only the selected page is generated and transferred;
  opened lookup pages are cached independently for the represented Pokemon.
- Release build completed with zero warnings and errors, and all 40 tracker
  tests passed.
- Reused the active run's prepared species and player-fusion generators for
  debug lookup, avoided dynamic fusion construction during occurrence scans,
  cached each encounter table's fusion target set, and replaced the per-target
  material-pair scan with one compact reverse index. In the bundled runtime, a
  repeated normal-only wild-fusion occurrence lookup fell from roughly 283 ms
  to 3 ms. Material lookup fell from roughly 800 ms for every new fusion to a
  one-time 307 ms index build and 0.3 ms thereafter. A complete second fusion
  lookup measured about 98 ms; reconstructed ability slots remained below 1 ms.

## Hoenn starter battle experience handoff

Status: **Complete**

- Confirmed that the Route 101 event stores the starter object returned by the
  selection scene, while Ironmon's automatic starter acquisition places a
  cloned result in the party.
- After the rescue battle, refreshes `VAR_HOENN_STARTER` from the matching
  post-battle party Pokemon before the map event removes that temporary party
  entry.
- The lab therefore gives back the battle-updated Pokemon and preserves earned
  experience, level, and other battle mutations.
- The bundled game runtime passed the focused handoff test and the clean
  ten-second startup smoke test.

## Ironmon sprite-download enforcement

Status: **Complete**

- Locked automatic sprite downloading off while Ironmon is active and removed
  Download data from its Gameplay Options menu.
- Normalized old Ironmon saves and F7 checkpoint loads to the disabled value;
  direct assignments cannot enable it again during the run.
- Added a configuration warning that the spritepack must be installed before
  starting Ironmon.
- The bundled runtime passed legacy-value, direct-assignment, and menu-lock
  checks.

## Ironmon 0.6.1 patch release

Status: **Complete**

- Updated the runtime, installation guide, protocol examples, documentation
  index, and release tooling to 0.6.1.
- Created `Ironmon-v0.6.1-evolutions.zip` with 457 entries and the SHA-256
  checksum
  `463c4bd24e6c9aababe68e2265fa7cff5fe2d3e684ceb4ecb455178bc0f1bd4f`.
- Two consecutive release builds were byte-for-byte reproducible.
- All 40 tracker tests and the combined game/tracker startup smoke test passed.

## Ironmon 0.6.2 patch release

Status: **Complete**

- Unified the Pokemon information models used by live Debug Pokemon,
  active-run Debug Lookup, and completed-run Lookup while retaining live slot
  diagnostics and tab-specific generator details.
- Nested completed-run recipe and run-diagnostic payloads, paged large
  occurrence and fusion-material searches, and added automatic protocol error
  reports with request and response context.
- Added foreground-safe `Ctrl+1` through `Ctrl+4` navigation and the XInput
  dual-trigger/right-stick controller chord.
- Created `Ironmon-v0.6.2-evolutions.zip` with 457 entries and the SHA-256
  checksum
  `e32e3193188aed807ad13bf8dcce25f03777f030318c22916ac2cb6d3c78f67f`.
- Two consecutive release builds were byte-for-byte reproducible.
- All 41 tracker tests and the combined game/tracker startup smoke test passed.

## Ironmon 0.6.3 fusion-integration audit release

Status: **Complete**

- Updated tracker and runtime release metadata to 0.6.3 without changing any
  generator schema or deterministic mapping rules.
- Passed the cumulative fusion-integration runtime audit across all 174,348
  custom-sprite fusions and 574 Body/Head evolution branch mappings.
- Created `Ironmon-v0.6.3-fusion-integration.zip` with 457 entries and the
  SHA-256 checksum
  `14734850b1f7cc7a42649c5169dc71e0a7e36a3358e35ff07dbdd8d4273e93bb`.
- Two consecutive release builds were byte-for-byte reproducible.
- All 41 tracker tests and the combined game/tracker startup smoke test passed.

## Ironmon 0.6.4 milestone regression release

Status: **Complete**

- Completed the Milestone 3 integration matrix across all four Pokemon data
  randomizers without changing their generator schemas or deterministic rules.
- Validated 576 eligible normal species and 287 normal evolution branches in
  the bundled runtime across cache rebuild, Marshal reload, and three seeds.
- Verified live inspection, active generated lookup, and complete structured and
  text diagnostics for deterministic reconstruction.
- Created `Ironmon-v0.6.4-randomization.zip` with 457 entries and the SHA-256
  checksum
  `6b073324420ae072d52c0d7d8054361e922b4ba7b2afea48dde9986b749ee414`.
- Two consecutive release builds were byte-for-byte reproducible.
- All 41 tracker tests and the combined game/tracker startup smoke test passed.

## Ironmon 0.6.5 challenge lifecycle release

Status: **Complete**

- Added game-owned attempt results, failure enforcement, optional automatic
  reset, authoritative challenge statistics, and completed-run tracker
  presentation without changing deterministic Pokemon generator schemas.
- Preserved missed completed recipes through the immediate automatic-reset
  startup race and kept duplicate reconnect recovery idempotent.
- Passed 44 cumulative and 9 focused trainer-name bundled-runtime assertions,
  plus all 45 tracker tests.
- Created `Ironmon-v0.6.5-challenge-lifecycle.zip` with 460 entries and the
  SHA-256 checksum
  `661fc320cc62e20f41a5f9fde6c03a2c746e55b4ebf04e73f54d2ad8fa3c147b`.
- Two consecutive release builds were byte-for-byte reproducible, and the
  packaged tracker and game passed their concurrent startup smoke test.

## Ironmon 0.7 starter tracker improvements

Status: **Complete**

- Added a three-slot starter-selection presentation that withholds candidate
  identity until the corresponding ball is opened in the game, then displays
  its sprite, species name, and generated BST.
- Added a stable seed-derived Random Pick that does not consume gameplay random
  values and survives tracker reconnection.
- Added a Settings page and persistent Autoselect starter preference. When
  enabled, all tracker candidates reveal immediately and the game normally
  selects the Random Pick after a fixed two-second delay without player input.
- Added an unlimited, tracker-owned Favorite Clause list stored outside the
  release directory, with debounced normal-Pokemon search suggestions, paging,
  duplicate prevention, and removal.
- Synchronized autoselect and favorites through the handshake and live settings
  updates. Favorite matching accepts normal Pokemon and fusions whose body or
  head is in the saved normal-species list.
- Marked revealed qualifying starters as Favorite. Manual selection remains
  unrestricted; autoselect immediately accepts a favorite Random Pick, or shows
  a named list containing the Random Pick and every qualifying favorite when
  the Random Pick is not a favorite.
- Updated the mechanics manual and protocol reference with the complete final
  behavior and visibility boundaries.
- All 48 tracker tests pass, the desktop tracker builds with zero warnings and
  errors, and the synchronized bundled game and tracker remained running during
  the connected startup smoke test.
- Created `Ironmon-v0.7.0-starter-selection.zip` with 461 entries, including all
  37 canonical Ruby scripts and the self-contained tracker executable, with no
  PDB or generated XML documentation files.
- Two independent release builds were byte-for-byte reproducible with SHA-256
  `4abb4eb0fe01d4a75f52c26ad8cbbd35273c4203bda3097384de0fed22e169cb`.

## Ironmon 0.7.4 diagnostic-access foundation

Status: **Reviewed**

- Added the shared `Ironmon.Tracker.Access` project with the 21 stable
  diagnostic capability definitions and immutable catalog metadata for future
  tracker and generator presentation.
- Added transitive implication expansion so All Active Pokemon includes Current
  Player and Current Enemies while retaining distinct direct and effective
  grants.
- Added a bounded ES256 JWT contract with fixed type, issuer, audience,
  application version, key ID, token ID, issue time, optional expiration,
  optional note, and sorted capability claims.
- Added a P-256 public-key ring and strict Microsoft IdentityModel validation
  that rejects unsecured or unexpected algorithms, types, signing keys,
  signatures, application claims, duplicate or unsorted capabilities, malformed
  values, and expired tokens.
- Preserved signed unknown future capabilities for user reporting without
  granting them or implicitly authorizing future features.
- Added 22 focused capability and JWT cases. All 82 tracker tests and the full
  tracker solution build pass with zero warnings in Debug and Release.
- No production public key, token persistence, tracker UI, generator UI, or
  game-protocol authorization is introduced by this foundation slice.

## Ironmon 0.7.4 maintainer token generator

Status: **Reviewed**

- Added a separate Windows .NET MAUI Blazor Hybrid generator application and a
  platform-neutral signing core under `tracker/tools`; the player distribution
  continues to publish only the tracker application.
- Added strict external PKCS#8 P-256 private-key import, deterministic public-key
  fingerprint IDs, ES256 signing, and immediate clearing of decoded private-key
  bytes after import. No private key is persisted, logged, or returned.
- Added editable local presets, note and temporary-or-lifetime expiration
  controls, and individual checkboxes for all 21 capabilities.
- Enforced All Active Pokemon as the direct grant that selects and locks Current
  Player and Current Enemies, while preserving separate direct and effective
  grant summaries.
- Added pre-generation dependency validation and a review summary containing
  the key ID, token ID, UTC expiration, and direct and effective capabilities.
- Added clipboard output and native save-file output using identical compact
  JWT text and the `.ironmon-access` extension.
- Kept clipboard and native-save failures inside the generator form without
  clearing or replacing the generated token.
- Added the same resource-based localization infrastructure and startup probe
  used by the tracker, including localized native text, form text, presets,
  capability groups, and all capability names and descriptions.
- Extracted native window and font settings, token-form formats and defaults,
  preset IDs, localization key construction, and capability presentation groups
  from the application shell and generator page.
- Added a separate maintainer-only self-contained publish script whose output
  remains outside the player distribution and fails if it contains private-key
  or generated-token file extensions.
- Added 37 generator-focused cases, including every capability with its minimum
  valid dependencies. All 119 tests pass, full Debug and Release solution
  builds complete with zero warnings, and both the build output and published
  generator passed hidden startup smoke tests.
- Completed maintainer review by creating a real P-256 key and signed
  `.ironmon-access` token through the packaged generator.
- No tracker token activation, token persistence, game protocol negotiation, or
  capability-gated debug presentation is introduced by this slice.

## Ironmon 0.7.4 tracker diagnostic-access lifecycle

Status: **Reviewed**

- Added an always-reachable Diagnostic Access screen beside Settings in the
  ordinary Release tracker, independent of game connection state.
- Added paste-first activation and bounded `.ironmon-access` file loading with
  localized validation outcomes and a clear active-token replacement warning.
- Embedded only the supplied maintainer P-256 public key in the tracker and
  added strict SubjectPublicKeyInfo import with the same deterministic key ID
  derivation used by the generator. The private key remains external.
- Added tracker-owned single-token persistence under Local App Data with
  temporary-file replacement, immediate removal, invalid-replacement
  protection, and no raw token in public state or UI after activation.
- Added no-access, active, lifetime, expired, invalid, and clearly identified
  Debug developer-override states. Active presentation includes the signed
  note, expiration, token ID, key ID, direct grants, included grants, and
  unknown future identifiers.
- Added timer-driven expiration and immediate navigation recomputation after
  activation, replacement, removal, or expiration. Expired access has no
  effective capabilities and cannot leave a protected view selected.
- Updated the tracker application version to 0.7.4 and added localized access
  presentation following the existing resource infrastructure.
- Added six lifecycle tests and three strict public-key import tests. All 128
  tests pass in Release, the tracker builds with zero warnings in Debug and
  Release, and the real Release WebView passed access-screen and malformed-input
  interaction smoke tests.
- Game protocol negotiation, live grant synchronization, and named request
  authorization are implemented in the following review slice.

## Ironmon 0.7.4 protocol negotiation and game authorization

Status: **Reviewed**

- Added bounded optional game-supported and tracker-granted capability lists to
  the existing handshake without removing the 0.7.3 development fields.
- Added live `diagnostic_access_changed` replacement events for activation,
  replacement, removal, and expiration without reconnecting or transmitting
  token metadata.
- Intersected effective tracker grants with game support before either the
  handshake or a live update reaches Ruby.
- Made malformed, duplicated, unknown, and oversized Ruby capability lists
  fail closed to an empty grant.
- Replaced blanket tracker and Ruby debug decisions with named availability and
  information checks for live Pokemon inspection, arbitrary active-run lookup,
  candidates, occurrences, fusion tools, areas, and run diagnostics.
- Preserved the dual `$DEBUG` and `debug_requested` path as the unrestricted
  local developer override, while valid release-token access no longer depends
  on `$DEBUG`.
- Filtered run configuration, seed, generator manifests, and evolution
  generator details before Ruby serialization when their individual grant is
  absent.
- Documented exact 0.7.3 pairing behavior and the request-to-capability mapping
  in the protocol reference and synchronized the mechanics information boundary.
- Added four protocol and signed connection cases, including supported-set
  intersection and immediate live removal. All 132 tests pass, the Debug
  tracker build completes with zero warnings, and Infinite Fusion's bundled
  runtime loaded the synchronized scripts for a 15-second hidden smoke test.

## Ironmon 0.7.4 capability-aware information surfaces

Status: **Reviewed**

- Made Debug navigation and every shared Pokemon tab reflect the independently
  authorized surfaces, including an Area-only empty state.
- Split exact evolution result and graph serialization from paged evolution
  candidates so either grant is useful without the other.
- Added target-bound player and enemy tool requests. The game resolves the
  represented live Pokemon and replaces the submitted species identifier;
  target-free candidate, occurrence, material, search, and lookup requests
  continue to require All Active Pokemon.
- Applied wild and trainer capabilities consistently to Area-first and
  Pokemon-first lookup, and retained ordinary discovered trainer, encounter,
  and collected-item behavior without their diagnostic grants.
- Filtered Overview relationships, reverse occurrences, material pairs, exact
  evolution data, and evolution generator metadata before active-run
  serialization instead of relying on visual concealment.
- Made tracker protocol history, raw state, persisted knowledge, copy actions,
  and exports independently visible and authorized. The shared report builder
  serializes only granted groups and never receives token metadata as report
  content.
- Clear protected Pokemon, Area detail, run diagnostic, and tracker diagnostic
  state immediately when access changes.
- Added target-tampering, candidate-only inspection, and per-report-group
  allow/deny coverage. All 135 tests pass in Debug and Release, both tracker
  application builds complete with zero warnings, and Infinite Fusion's
  bundled runtime kept the synchronized scripts loaded during a 15-second
  hidden startup smoke test.

## Ironmon 0.7.4 documentation, runtime validation, and release candidate

Status: **Awaiting review**

- Updated the game version, mechanics manual, protocol reference, project and
  tracker READMEs, installation and development guides, localization guidance,
  distribution summary, and versioned release notes for the final 0.7.4 token
  behavior.
- Documented activation, grant review, replacement, removal, expiration, local
  persistence, legacy developer override, and the separation between the
  player tracker and maintainer-only generator/private key.
- Removed the unnecessary Pokemon-availability prerequisite from standalone
  evolution-generator diagnostics so generator and runtime dependency rules
  agree.
- Added a bundled-runtime capability harness covering all 18 game-owned named
  allow and deny paths, malformed replacements, live removal/expiration,
  target mappings, and developer override. Its temporary installed test hook
  was removed after a passing run.
- All 135 unit tests pass in Debug and Release. The complete tracker and
  generator solution builds with zero warnings in both configurations.
- The packaged tracker and game passed a 15-second hidden connected startup
  smoke test; only the two processes launched by the test were stopped.
- Added player-package rejection for generator, token, private-key, maintainer,
  and development files. The final archive contains 475 inspected entries and
  none of the forbidden content.
- Two clean builds of `Ironmon-v0.7.4-diagnostic-access.zip` produced the same
  SHA-256 checksum:
  `e3d85fc44fbca4f8bce006f5bb8493a9a5d49466e9acd16f8b38f0d10b6026ef`.

## Ironmon 0.7.5 navigation and lookup composition

Status: **Implemented; awaiting review**

- Replaced conditional Debug navigation with four permanent Player, Enemy,
  Lookup, and Archive views.
- Made Lookup active-run only and moved completed-run selection, shared world
  lookup, Pokemon lookup, statistics, move-access analysis, and evolution
  analysis into Archive.
- Retained one world-lookup implementation whose active or archived source
  parameters continue to control disclosure, loading, caching, and requests.
- Moved Ctrl+4 and the matching controller direction to Archive.
- Hosted capability-controlled Debug tools with the always-reachable Diagnostic
  Access screen and removed their direct primary-view shortcut.
- All 136 tracker tests pass in Debug and Release. The tracker application
  builds with zero warnings in both configurations; Release was validated in
  an isolated output because the user's existing Release tracker process was
  left running and retained ownership of its normal build files.

## Ironmon 0.7.5 release-precalculated coverage dataset

Status: **Implemented; awaiting review**

- Added a bundled-runtime maintainer generator using the exact normal and
  eligible custom-fusion pools plus the game's authoritative fusion type rules.
- Collapsed the population into 170 unique one- or two-type defensive profiles
  with separate counts totaling 576 normal Pokemon and 174,348 custom fusions.
- Added pool schema, size, and fingerprint metadata without storing species
  identities, a seed, generated mappings, or a future trainer roster.
- Embedded the deterministic JSON in the tracker and added strict startup
  validation for its schema, metadata, canonical type order, uniqueness, and
  population totals.
- Added an aggregate-only CSV audit and shared the safe bundled-runtime launch,
  script-archive restoration, and exact-process cleanup used by release data
  generators.
- Two unchanged-input generations produced the same 13,389-byte resource with
  SHA-256
  `ac89cb1ba9da5362a5a0456cf93f080a75aa815b7227b9afd4e5f81acaa93999`.
- All 144 tracker tests pass in Debug and Release. The tracker application
  builds with zero warnings in both configurations and contains the expected
  13,389-byte embedded resource.

## Ironmon 0.7.5 coverage context and calculation

Status: **Implemented; awaiting review**

- Added optional active-run coverage context to both run-start events and
  current-state recovery with only trainer policy and aggregate pool metadata.
- Kept older games compatible by treating an absent context as coverage-only
  unavailability rather than a protocol failure.
- Added policy-aware compatibility checks: Normal Only requires the normal
  population, Custom Fusions Only requires the fusion population, and Mixed
  requires both plus the matching game release.
- Added best-selected-type aggregation into all six effectiveness buckets.
  Mixed counts are raw combined populations while percentages retain the
  generator's category-first 50/50 weighting.
- Added stable current Physical/Special move-type selection, hypothetical type
  toggling, Current Moves reset behavior, same-Pokemon manual retention, and
  different-Pokemon reset behavior for the upcoming shared interface.
- Added no coverage request, seed, mapping, trainer slot, future opponent, or
  species identity to the protocol.
- All 165 tracker tests pass in Debug and Release, including every attacking
  type over the real release dataset for all three policies. Both tracker builds
  complete with zero warnings, and the bundled runtime verifies every policy's
  live context against the unchanged generated dataset. The synchronized game
  scripts remained loaded through the hidden startup smoke test, after which
  only the exact test process was stopped.

## Ironmon 0.7.5 coverage interface and release validation

Status: **Implemented; awaiting review**

- Added the four-section live Lookup surface for Trainers, Encounters, Items,
  and Type Coverage while retaining the shared configurable world explorer for
  Archive instead of duplicating lookup behavior.
- Added the complete 18-type selector, current damaging-move coloring, manual
  hypothetical toggles, Current Moves reset, compatibility states, and the six
  current-count plus percentage result buckets. Unavailable types remain gray
  even when manually selected.
- Moved coverage selection ownership above the live Lookup sections so manual
  choices survive section changes and same-Pokemon refreshes while the existing
  selection state resets for a different player Pokemon.
- Made the release pipeline regenerate and validate both `area_catalog.dat` and
  `type_coverage.json` before distribution or publication. Fresh temporary
  generator outputs are compared by hash so unchanged data does not trigger a
  nondeterministic MAUI-generated assembly rebuild.
- The release gate verifies the distributed area catalog, the exact embedded
  coverage resource, and the absence of generators, audits, temporary runtime
  hooks, access generators, development scripts, and private key material.
- All 165 tests pass in Debug and Release. Both applications build with zero
  warnings, the synchronized game and packaged tracker pass hidden startup
  smoke checks, and two complete releases reproduce SHA-256
  `ac0e6c988cc6c7414ae423742f67cc684c527cda796034fd7dc1679e0044900a`.

## Deterministic item-slot randomization

Status: **Implemented; awaiting review**

- Added a versioned seed-derived physical-slot generator. Ground ordinary
  items and found TMs use one full-chaos pool; scripted TM gifts use a TM-only
  pool; ordinary gifts and trainer held items remain authored.
- Added versioned result bans. Rules version 2 excludes DNA Splicers, Super
  Splicers, DNA Reverser, Dynamite, all Mail and Apricorn items, Exp. Share,
  and all nine permanent HM tools while structurally excluding other key
  items, HMs, and unsupported registrations. Authored HM-tool ground pickups
  randomize away, while converted HM rewards remain protected.
- Filtered each standard Poke Mart's own inventory to its existing balls and
  Repel-family items without changing vending machines, order, or prices.
- Added saved generator manifests, legacy mapping compatibility, tracker area
  reconstruction, completed-run protocol support, and generator diagnostics.
- Added a bundled-runtime release audit which automatically updates canonical
  pool, slot, gift, and Mart records on compatible game-data drift while unsafe
  structural failures still stop the release.
- Added a dedicated deterministic ground-pool slot for the post-rescue starter
  reward and excluded six dialogue-only hidden-item decoys from Lookup so every
  catalogued item row has a revealable identity.
