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
| 5 | Enemy tracking, remembered moves, and annotations | Reviewed |
| 6 | Deterministic post-run search and lookup | Reviewed |
| 7 | Debug-mode inspector parity | Inspector UI implemented; awaiting review |
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

Status: **Implemented; awaiting review**

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

Status: **Implemented; awaiting review**

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
  by all three inspector pages.

### Remaining Part 7 work

- tracker-owned connection and protocol history;
- raw event, request, response, current-state, and move-knowledge inspection;
- copy actions and diagnostic-report export; and
- final end-to-end debug-mode validation.

Part 7C must not begin until this inspector UI is accepted or revised.
