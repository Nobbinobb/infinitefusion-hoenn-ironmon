# Ironmon Tracker

This document defines the agreed architecture and first-version behavior of the
external Ironmon Tracker.

Status: **First-version design agreed; Part 3 connection awaiting review**

An earlier browser-based TCP prototype proved that the bundled game runtime can
send structured Pokemon data to a .NET process. It was removed before
production implementation began; its Blazor Server architecture and one-shot
protocol are not part of the design described here.

## Purpose

The tracker is an optional companion application for an Ironmon run. It shows
legal live information about the player's active Pokemon, records information
discovered about opposing Pokemon, and provides unrestricted generated-data
inspection only after a run has ended or when explicit development mode is
active.

The game remains authoritative for gameplay state and information visibility.
The tracker owns presentation, remembered move discoveries, manual enemy-stat
annotations, user settings, and compact completed-run recipes.

The tracker must never be required to play Ironmon. If it is absent, closed, or
incompatible, the game continues normally without delay or repeated visible
errors.

## Technology and application model

The first version uses:

- .NET 10;
- Blazor Hybrid, targeting Windows initially;
- Razor components and CSS for the tracker interface;
- an in-process C# connection service and run-state store;
- `System.Text.Json` for protocol serialization;
- a loopback-only persistent TCP connection;
- newline-delimited JSON for framing; and
- small JSON files for settings, discoveries, annotations, and run recipes.

Blazor Hybrid runs the Razor components inside the desktop process. It does not
host an HTTP server, open an external browser, use WebAssembly, or depend on a
Blazor Server circuit. The native shell owns the window and embedded WebView.

The initial target is 64-bit Windows. Cross-platform support is not a
first-version requirement.

## Release and startup

The tracker is built as part of every Ironmon release and placed beside the mod
in the copy-ready distribution:

```text
InfiniteFusion2/
|-- InfiniteFusion2.exe
|-- Data/
|   `-- Scripts/
|       `-- 997_Ironmon/
`-- Ironmon Tracker/
    |-- Ironmon Tracker.exe
    `-- supporting runtime files
```

The tracker is published as an unpackaged, self-contained Windows application.
The user does not install the .NET SDK, run a terminal command, select a port,
or open a browser. The normal workflow is:

1. Extract the Ironmon release into the game installation.
2. Double-click `Ironmon Tracker.exe`.
3. Start Infinite Fusion and play Ironmon.

Starting order does not matter. If the game starts first, it retries the local
connection at a restrained interval. If the tracker starts first, it waits. A
reconnected tracker requests the current state, so restarting it does not
require restarting the game.

The game does not launch the tracker automatically in the first version.
Automatic startup or a combined launcher may be added later as an opt-in
feature.

The application checks for the WebView2 runtime at startup. If it is missing,
the tracker presents a clear installation action. The release may include the
Microsoft Evergreen bootstrapper, but missing WebView2 must never produce a
silent blank window.

## Process architecture

```text
Infinite Fusion Ruby runtime
        |
        | persistent duplex TCP on 127.0.0.1
        v
C# connection and protocol service
        |
        v
Run-state store and information policy
        |
        +------------------+
        |                  |
        v                  v
Blazor player view   Blazor enemy/debug views
```

The game initiates the connection to the tracker. The connection is duplex:

- the game sends live events and responses;
- the tracker sends state and lookup requests; and
- either side can detect disconnection and re-establish its state.

Ruby must not block the game loop while waiting for tracker input. Incoming
commands are polled non-blockingly and resolved on the game thread. The C#
application performs network reading outside the UI thread and dispatches
state changes through its run-state store.

Only one tracker controls the fixed tracker port. Both sides bind or connect
only to IPv4 loopback. No protocol endpoint is exposed to the LAN.

## Protocol envelope

Every message is one UTF-8 JSON object followed by a newline. All messages use
a common envelope:

```json
{
  "schema_version": 1,
  "type": "event",
  "event": "player_sent_out",
  "run_id": "run-123",
  "battle_id": "battle-8",
  "sequence": 27,
  "sent_at": "2026-08-06T18:00:00Z",
  "payload": {}
}
```

The supported message types are:

- `event`: an unsolicited game-state or lifecycle update;
- `request`: a tracker command with a unique `request_id`; and
- `response`: the corresponding result with the same `request_id`.

`run_id`, `battle_id`, and monotonically increasing `sequence` values prevent
messages from different runs or battles from being combined and allow the
tracker to detect gaps.

The connection handshake exchanges:

- game and Ironmon versions;
- tracker version;
- protocol version;
- current run and battle identifiers;
- whether Ironmon mode is active;
- whether development/debug access is available; and
- the game installation directory needed for local sprite resolution.

An incompatible protocol produces a clear tracker message and does not affect
the game.

## Reconnection and state recovery

Important transitions send complete snapshots rather than relying entirely on
small deltas. After connecting or detecting a sequence gap, the tracker sends a
`current_state` request. The game returns the legal current view of:

- the run descriptor;
- the active battle;
- the player's active Pokemon;
- active opposing Pokemon;
- current healing inventory;
- current HP, status, held-item and PP values; and
- other state needed to continue the live display.

The game does not return undiscovered enemy moves as part of recovery. The
tracker merges its persisted discovery records with the current enemy species
and level.

## Main window

The tracker is a compact, resizable desktop window inspired by established
Ironmon tracker layouts. It provides:

- optional always-on-top behavior;
- configurable integer or smooth display scaling;
- remembered window position and size;
- a visible connection state;
- Player and Enemy tabs;
- keyboard switching between the two views; and
- a Debug tab only when development access is authorized.

When an opponent is sent out, the tracker automatically shows the Enemy view.
The player can switch back to Player at any time. Ending the battle returns to
the Player view.

The state model supports multiple opposing battlers even if the first UI shows
only one active enemy at a time. This avoids redesigning the protocol for
double or triple battles.

## Player view

The player view is initialized when the player's Pokemon is actually sent into
battle. Merely entering party slot one does not initialize the production
tracker.

The initial `player_sent_out` snapshot includes:

- stable Pokemon and species identifiers;
- nickname and species name;
- resolved sprite path;
- level;
- current and maximum HP;
- status;
- types;
- ability;
- held item;
- calculated Attack, Defense, Special Attack, Special Defense, and Speed;
- base-stat total;
- nature where allowed by the tracker rules;
- four current moves;
- current and total PP, power, and accuracy for each move; and
- the current healing-inventory summary.

The live player display updates for:

- HP damage and healing;
- status changes;
- level-ups;
- learning, forgetting, or replacing a move;
- PP changes;
- held-item changes or consumption; and
- changes to relevant healing items in the bag.

Level-ups and move changes send a complete player snapshot after the game has
committed the change. Complete snapshots reduce drift and make unusual move or
fusion paths safe.

## Healing inventory

The tracker displays the number of usable HP-healing items in the bag and their
combined healing capacity as a percentage of the active Pokemon's maximum HP:

```text
total potential HP restored / maximum HP * 100
```

The value may exceed 100 percent. It is recalculated when:

- a relevant item is added, removed, bought, sold, used, deposited, or
  withdrawn;
- the active Pokemon changes; or
- the active Pokemon's maximum HP changes after a level-up or form change.

First-version rules are:

- count usable, non-revival HP-healing items in the bag;
- include healing berries;
- exclude status-only medicine;
- exclude held items from the bag total;
- treat full-heal HP items as one maximum-HP heal each; and
- send a complete healing-inventory snapshot after a relevant mutation.

The game calculates effective healing because it owns the item rules. The
tracker does not duplicate the complete item-effect database.

## Enemy view

The enemy view initially shows only information permitted by the run rules:

- species name and stable species/form identifier;
- sprite;
- visible types;
- level if permitted;
- base-stat total; and
- previously discovered moves applicable at the enemy's current level.

Unknown HP, stats, moves, ability, and held-item information remains hidden.
Post-run and development lookups use different information policies and do not
change the live view rules.

Each enemy stat has a clickable manual annotation box. Its states cycle:

```text
empty -> plus -> minus -> empty
```

Left-click cycles forward and right-click cycles backward. The six independent
annotations cover HP, Attack, Defense, Special Attack, Special Defense, and
Speed. They belong to the tracker and are not transmitted to or interpreted by
the game.

Enemy state is retained when an opposing trainer switches Pokemon. Returning
to a previously seen opponent restores its view, discoveries, and annotations.

## Enemy move discovery

The tracker, not the game save, remembers live move discoveries. Discovery
records are scoped by run and stable species/form identifier.

An `enemy_move_used` event contains only the now-observable move and the
metadata required to reconstruct level-up order:

```json
{
  "event": "enemy_move_used",
  "run_id": "run-123",
  "battle_id": "battle-8",
  "payload": {
    "enemy_id": "enemy-2",
    "species_id": "BELLOSSOM",
    "enemy_level": 25,
    "move": {
      "id": "MEGADRAIN",
      "name": "Mega Drain",
      "learned_level": 20,
      "learn_order": 36,
      "source": "level_up",
      "type": "GRASS",
      "power": 40,
      "accuracy": 100,
      "total_pp": 15,
      "pp_after_use": 14
    }
  }
}
```

`learn_order` is a stable position within the applicable generated learnset. It
breaks ties when multiple moves are learned at the same level.

For an enemy at a particular level, the tracker:

1. selects all discovered level-up moves for that species/form whose learned
   level is not above the enemy's level;
2. orders them by learned level and then learn order;
3. takes the newest four; and
4. displays those four in chronological moveset order.

Discovering a fifth newer move removes the oldest move from the displayed four
but not from persistent discovery history. Retaining the older discovery is
necessary because a later lower-level enemy can still know it.

Repeated use updates observed PP without duplicating the move. A discovered
move is restored automatically when the same species/form is encountered again
and the move belongs among its four newest discovered moves at that level.

## Player-assisted move discovery

The player's Pokemon contributes to the same per-run species discovery record.
The tracker records:

- applicable current level-up moves in the initial player snapshot; and
- each subsequently learned level-up move.

Forgetting a move does not erase the discovery. Moves learned through TMs, HMs,
tutors, Eggs, events, debug commands, or player-only fusion choices do not
teach the tracker about an enemy learnset unless the game explicitly classifies
that source as part of the enemy's deterministic level-up moveset.

Each move sent for discovery therefore includes `source`, `learned_level`, and
`learn_order` rather than requiring the tracker to reproduce Ruby learnset
logic.

## Persistence

Tracker-owned state is stored outside the release directory so an update does
not erase it:

```text
%LocalAppData%/IronmonTracker/
|-- settings.json
`-- runs/
    `-- <run-id>.json
```

The tracker persists only:

- UI settings and window state;
- observed enemy and player-assisted move discoveries;
- manual enemy-stat annotations;
- compact completed-run recipes; and
- minimal protocol bookkeeping needed for recovery.

It does not persist complete generated Pokemon records, species mappings,
ability mappings, or post-run lookup results.

## Run recipes and deterministic post-run inspection

Generated data is recalculated rather than archived. A completed-run recipe
contains the inputs needed to reproduce the run:

```json
{
  "run_id": "run-123",
  "seed": 918273645,
  "result": "lost",
  "game_version": "6.8.0",
  "ironmon_version": "0.4.0",
  "configuration": {},
  "species_generator_version": 3,
  "ability_generator_version": 2,
  "player_fusion_generator_version": 2,
  "species_pool_fingerprint": "...",
  "ability_pool_fingerprint": "...",
  "fusion_pool_fingerprint": "..."
}
```

Post-run lookup has no arbitrary expiration. It remains reproducible for as
long as Ironmon retains the requested generator implementation and compatible
source data. Generator algorithms are schema-versioned, and older deterministic
implementations should remain callable after a current algorithm changes.

Fingerprints detect incompatible species, ability, or custom-fusion pools. The
game must report an explicit incompatibility instead of silently producing a
different result.

Generated facts such as mappings, ability slots, base stats, learnsets, and
deterministic teams can be recalculated. Historical actions such as damage,
consumed items, exact final PP, player move choices, encounter order, and manual
tracker annotations are not reconstructable from a seed and are not presented
as generated lookup data.

## Post-run search and lookup

The tracker provides a name-based search UI, but requests use stable internal
species/form identifiers. Names alone are unsuitable identifiers because of
localization, nicknames, forms, and fusions.

The search flow is:

1. The tracker sends `pokemon_search` with a text query and run ID.
2. The game returns a page of matching names, stable identifiers, and the
   complete match count.
3. The user selects a match.
4. The tracker sends `pokemon_lookup` with the selected ID and run ID.
5. The game reconstructs and returns complete generated information.

Search results use pages of 20. Post-run evolution destinations and direct
previous evolutions include their sprites and can be selected for another
lookup. Until the evolution generator planned for Ironmon 0.6.0 exists, these
are explicitly the current natural evolution graph, not invented seeded
targets. Fusion results expose their displayed body and head components, their
seeded Ironmon reverse, and the ordered normal-material pairs that produce
them. Encounter and pivot inputs do not replace a fusion's displayed
components.

Lookup also reconstructs every authored wild and trainer slot which maps to
the selected species. Wild rows show data mode, route, encounter type, slot,
source species, and the authored table percentage. Trainer rows show trainer
identity, party slot, and source species. Normal Only fusion encounters show
their ordered pair of route slots and the pair probability conditional on the
fusion event triggering.

Legacy schema-version-1 runs use the wild and trainer source maps stored in the
currently loaded save. Their occurrence lists cannot be reconstructed later
from an archived compact recipe because the legacy maps were not slot-derived.

For a normal Pokemon, the user searches for a second normal material. The game
calculates only the two seeded Ironmon outcomes for those materials: first as
body plus second as head, then the reversed orientation. It does not return a
broad list of compatible custom sprites. All relationship and result cards use
stable identifiers and share session-only Back and Forward navigation.

Post-run search uses a reusable lightweight index and does not instantiate
complete fusion species merely to obtain names. Deterministic lookup results,
fusion mappers, material scans, and resolved sprite paths are cached for the
current compatible runtime. The tracker also reuses successful responses for
the current connection and clears that cache when the game disconnects.
Post-run requests are serialized before being sent because the game handles
them synchronously; a queued request does not begin its response timeout until
the preceding request completes.

During an active run, lookup responses contain only legally discovered
information. After the run ends, complete generated data is available. The
game enforces this policy at the source; the tracker does not receive hidden
fields and merely conceal them visually.

## Sprite resolution

The game includes its installation directory in the handshake and sends the
resolved relative sprite path for player and enemy snapshots. This supports
ordinary, fusion, alternate, custom, and dynamically downloaded sprites
without reproducing Infinite Fusion sprite-selection logic in C#.

The tracker loads the sprite from the local installation and uses a neutral
silhouette when the file is temporarily unavailable. Sprite loading failure
does not reject the remainder of an event.

## Debug mode

The first tracker version includes a development-only Debug tab that replaces
the retired in-game Ironmon Inspector and exposes protocol diagnostics.

Debug access requires both:

- the game reporting that Ironmon development/debug mode is active; and
- the tracker being launched with `Ironmon Tracker.exe --debug`.

A release game never sends unrestricted debug data solely because a tracker
requests it. This source-side gate prevents the normal tracker from revealing
hidden run information.

### Pokemon inspection

Debug mode can inspect the current player, current enemy, or a party Pokemon.
Arbitrary-species inspection uses a species-only path because Infinite
Fusion's ordinary Pokemon constructor consumes random values. The tracker owns
the presentation of these debug values. Pokemon inspection is one top-level
Debug page with Overview, Abilities, and Stats as subtabs. Lookup, Run
Diagnostics, and Protocol remain separate top-level Debug pages.

Overview includes:

- Pokemon identity, level, gender, sprite, and held item;
- normal/fusion kind, species ID, form, and form name;
- displayed body and head for a fusion;
- active ability slot, name, and ID;
- run seed;
- generator schema and pool-rules versions; and
- pool size and fingerprint.

Abilities includes:

- original and generated normal slots;
- original and generated hidden slots;
- final fusion slots;
- active slot;
- ability IDs and names;
- universal, exact-species, or component-compatible eligibility;
- fusion component and source slot; and
- restricted-source replacement details.

The Stats subtab is added with Step 3.2. It displays original and generated final
stats, differences, totals, and base-stat generator metadata. A fusion labels
the dominant component for each stat but does not duplicate either component's
stat table; the existing component navigation opens those Pokemon separately.
The presentation can switch between a comparison table, generated-only bars,
and bars whose changed segment is colored green or red for positive or negative
deltas. Generated values are the visual focus in the table.
Learnset and Evolutions pages are added when their corresponding randomizers and
tracker contracts are implemented. The tracker does not invent placeholder
generated values.

The game resolves all debug values on demand through the same runtime paths
used by gameplay. Requests are read-only, consume no random values, perform no
rerolls, and do not mutate the selected Pokemon or run.

Completed-run lookup is based on the selected archived recipe rather than the
currently loaded save. A lost or won run therefore remains inspectable from the
main menu and while another run is active. Recipes that are themselves active
or incomplete remain unavailable.

Every battle loss or draw completes an Ironmon run, including encounters the
base game marks as safe to lose without a blackout, such as the early rival
battle. The base game's continuation behavior does not weaken Ironmon's run
result.

### Run and protocol diagnostics

The Debug tab also shows:

- Ironmon and game versions;
- run seed and diagnostic context;
- wild, trainer, and unfusion policies;
- custom-fusion pool size and fingerprint;
- ability generator version, pool size, and fingerprint;
- wild and trainer mapping counts;
- tracker and protocol versions;
- connection and reconnection history;
- last protocol error;
- raw events, requests, and responses;
- current tracker state; and
- persisted move-discovery state.

Values and raw JSON can be copied, and a diagnostic report can be exported.
The tracker Debug view is the supported Ironmon inspection interface.

## Information and safety boundaries

- Normal live responses include only information permitted during a run.
- Post-run responses include complete generated information only for a run
  recorded as ended.
- Debug responses require game-authorized development mode.
- The tracker never decides that hidden information is safe merely by hiding a
  received field.
- All external paths are treated as data, not executable commands.
- Unknown protocol fields are ignored for forward compatibility.
- Unsupported schema versions and malformed required fields are rejected with
  diagnostics rather than partially applied.
- Network or tracker failures must not crash, pause, or invalidate the game.

## First-version event set

The initial protocol includes at least:

- `game_connected`;
- `run_started` and `run_ended`;
- `battle_started` and `battle_ended`;
- `player_sent_out`;
- `player_state_changed`;
- `player_level_up`;
- `player_move_changed`;
- `player_held_item_changed`;
- `healing_inventory_changed`;
- `enemy_sent_out`;
- `enemy_withdrawn`;
- `enemy_move_used`; and
- `connection_diagnostic` in development mode.

The initial request set includes at least:

- `current_state`;
- `pokemon_search`;
- `pokemon_lookup`;
- `fusion_preview`;
- `debug_inspect_pokemon`; and
- `debug_run_diagnostics`.

Exact payload schemas should be specified and covered by fixtures before Ruby
or C# feature implementation begins.

## First-version acceptance criteria

The first tracker release is complete when:

1. A user can extract the normal Ironmon release and start the tracker by
   double-clicking its executable without installing .NET or using a browser.
2. Game and tracker can start in either order, reconnect, and recover current
   state without restarting the run.
3. Sending the player into battle initializes a complete player card.
4. HP, status, level, moves, PP, held item, and healing capacity remain current.
5. Enemy appearance automatically opens a legal enemy card while Player and
   Enemy remain manually selectable.
6. Enemy moves appear only after player-assisted or enemy-use discovery and are
   ordered as the four newest applicable moves.
7. Discoveries survive tracker restarts and return on later encounters in the
   same run.
8. Enemy stat boxes cycle and persist their empty/plus/minus annotations.
9. Post-run searches regenerate complete data from a versioned run recipe
   without storing generated Pokemon records.
10. Debug mode reproduces current inspector information and exposes connection
    diagnostics without being available in ordinary release play.
11. Closing or never starting the tracker has no gameplay consequence.

## Deferred features

The following are outside the first version unless separately designed:

- permanent historical battle timelines;
- video-stream overlays and click-through capture modes;
- cloud synchronization;
- remote-network access;
- automatic game or tracker startup;
- support for non-Windows platforms;
- full completed-run action replay.
