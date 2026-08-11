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
- foreground-safe `Ctrl+1` through `Ctrl+4` keyboard switching;
- foreground-safe controller switching by holding both triggers and flicking
  the right stick left for Player, right for Enemy, up for Lookup, or down for
  Debug; and
- a Debug tab only when development access is authorized.

Global shortcuts are accepted only while the connected Infinite Fusion
executable owns the foreground window. They change the tracker view without
activating or focusing the tracker window. The controller chord uses inputs
that Infinite Fusion does not bind by default.

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
  "schema_version": 1,
  "run_id": "run-123",
  "seed": 918273645,
  "result": "lost",
  "game_version": "6.8.0",
  "ironmon_version": "0.6.4",
  "configuration": {
    "schema_version": 2,
    "wild_policy": "mixed",
    "trainer_policy": "mixed",
    "unfusion_setting": "random_component"
  },
  "species_generator": {
    "version": 3,
    "pool_fingerprint": "..."
  },
  "ability_generator": {
    "version": 3,
    "pool_size": 250,
    "pool_fingerprint": "..."
  },
  "base_stat_generator": {
    "version": 1,
    "source_fingerprint": "..."
  },
  "evolution_generator": {
    "version": 1,
    "rules_version": 1,
    "source_fingerprint": "...",
    "taxonomy_fingerprint": "...",
    "method_fingerprint": "...",
    "target_fingerprint": "...",
    "base_stat_generator": {
      "version": 1,
      "source_fingerprint": "..."
    },
    "fusion": {
      "version": 1,
      "rules_version": 1,
      "target_pool": {
        "version": 2,
        "size": 500,
        "fingerprint": "..."
      }
    }
  },
  "move_access_generator": {
    "version": 6,
    "pool_fingerprint": "...",
    "contextual_restriction_fingerprint": "...",
    "level_up_source_fingerprint": "...",
    "egg_source_fingerprint": "...",
    "tm": { "roster_fingerprint": "...", "source_fingerprint": "..." },
    "tr": { "roster_fingerprint": "...", "source_fingerprint": "..." },
    "tutor": { "catalog_fingerprint": "...", "source_fingerprint": "..." },
    "fusion_tutor": { "catalog_fingerprint": "...", "source_fingerprint": "..." }
  },
  "player_fusion_generator": {
    "version": 2,
    "pool_size": 500,
    "pool_fingerprint": "..."
  }
}
```

The nested recipe is a versioned protocol contract. Flat pre-schema recipes
are intentionally not migrated during early development and are ignored by the
archive loader. Their shape is recognized before typed deserialization so they
do not produce handled startup exceptions. Deleting those old run folders is
the supported cleanup path.

Post-run lookup has no arbitrary expiration. It remains reproducible for as
long as Ironmon retains the requested generator implementation and compatible
source data. Generator algorithms are schema-versioned, and older deterministic
implementations should remain callable after a current algorithm changes.

Fingerprints detect incompatible species, ability, base-stat, move-access,
tutor, or custom-fusion source data and pools. The game must report an explicit
incompatibility instead of silently producing a different result.

Generated facts such as mappings, ability slots, base stats, move-access
channels, tutor offerings, and deterministic teams can be recalculated.
Historical actions such as damage, consumed items, exact final PP, player move
choices, encounter order, and manual tracker annotations are not reconstructable
from a seed and are not presented as generated lookup data.

## Post-run search and lookup

The tracker provides a name-based search UI, but requests use stable internal
species/form identifiers. Names alone are unsuitable identifiers because of
localization, nicknames, forms, and fusions.

The search flow is:

1. The tracker sends `pokemon_search` with a text query and run ID.
2. The game returns a page of matching names, stable identifiers, and the
   complete match count.
3. The user selects a match.
4. The tracker sends `pokemon_lookup` with the selected ID, run ID, and the
   currently selected information section.
5. The game reconstructs and returns only that section. Overview loads first;
   Abilities, Stats, Moves, and Evolutions load when selected and are cached
   independently for that Pokemon.

Search results use pages of 20. Before Step 3.4 is enabled, post-run evolution
destinations and direct previous evolutions are explicitly the current natural
graph. Step 3.4 replaces them with the generated operational graph for runs
that declare compatible evolution metadata. Target rows include icons, names,
and generated BSTs and can be selected for another lookup. Fusion results
expose their displayed body and head components, their seeded Ironmon reverse,
and the ordered normal-material pairs that produce them. Material pairs use
50-row pages so a fusion with many deterministic source collisions cannot
exceed the protocol framing limit. Encounter and pivot inputs do not replace a
fusion's displayed components.

Lookup also reconstructs every authored wild and trainer slot which maps to
the selected species. Wild rows show data mode, route, encounter type, slot,
source species, and the authored table percentage. Trainer rows show trainer
identity, party slot, and source species. Normal Only fusion encounters show
their ordered pair of route slots and the pair probability conditional on the
fusion event triggering. Wild and trainer occurrences use independent 50-row
pages. This bounds self-fusion and other high-collision Overview responses
without discarding their complete occurrence totals.

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

The three Pokemon-information entry points use one shared tabbed information
card with Overview, Abilities, Stats, Moves, and Evolutions:

- completed-run Lookup searches the selected archived run recipe;
- Debug Pokemon selects the current player or enemy and supplies its live
  instance diagnostics to the shared card; and
- Debug Lookup searches any Pokemon in the active debug run.

Debug Pokemon obtains both a live `DebugPokemonInspectorSnapshot` and the same
active-run `PokemonLookupSnapshot` used by Debug Lookup, then merges them in the
same shared pages. The Abilities tab exists in all three entry points and
reconstructs complete original, generated, component, and final-fusion slot
diagnostics, including eligibility, source component, source ability, and
restricted replacements. It also shows each generated ability's detail panel.
Debug Pokemon merges in the live current slot and marks it active. The Stats
and Overview pages are enriched with live data in the same way. Ability
generator metadata belongs to Abilities, base-stat generator metadata belongs
to Stats, move-access generator metadata belongs to Moves, and normal/fusion
evolution generator metadata belongs to Evolutions. Every entry point shows
those diagnostics in the same tabs. Related Pokemon remain clickable and open
through the appropriate lookup flow.

Both debug payloads follow the selected shared page. A player, enemy, starter,
or encounter refresh therefore computes Overview plus only the currently
visible live section; it does not generate or transfer hidden-tab stats,
abilities, moves, or evolutions. Switching pages requests that page once and
retains it until the represented Pokemon changes.

`PokemonLookupSnapshot` always contains an `identity` object and exactly one
optional section object (`overview`, `abilities`, `stats`, `moves`, or
`evolutions`). `DebugPokemonInspectorSnapshot` follows the same pattern for
live identity plus its live Abilities or Stats enrichment. Run diagnostics are
grouped into runtime identity, configuration, generator manifests, and mapping
counts, and reuse the same generator-manifest types as completed-run recipes.

Step 3.4 adds Evolutions to authorized Debug and completed-run lookup. A normal
Pokemon receives one clickable valid-candidate list; a fusion receives
separate Head and Body candidate lists when applicable. Candidate lists use
50-row server-side pages. Normal lists hide the name filter when their full
candidate set contains fewer than 50 Pokemon; fusion Head and Body lists always
show it. Fusion candidates occupy one area with Head and Body tabs instead of
two stacked lists. Candidate lists show every preferred
90%-115% candidate, or only the closest fallback candidates when the preferred
set is empty. Every candidate shares at least one type with the normal source,
or with the evolving Head/Body component for a fusion. Rows show only target
icon, name, and generated BST. The actual
selected destinations are not identified by these lists. A clickable graph
shows one step in each direction for a normal Pokemon.
A fusion graph omits incoming predecessors and shows the selected fusion with
its immediate Head/Body targets. Nodes show icon, name, and generated BST. One
edge per conceptual target lists every effective method and the component side
where applicable. Complete generated evolutions remain unavailable during
ordinary live play. The tracker does not invent placeholder generated values.

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
Whenever a protocol or connection failure occurs, the tracker atomically
replaces
`%LocalAppData%\IronmonTracker\diagnostics\latest-protocol-error.json` with a
self-contained snapshot of the complete exception and bounded incoming,
outgoing, and lifecycle history. Failed request responses and response-payload
validation failures use the same capture path. Clearing in-memory history or
resetting the game does not delete this recovery file.
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
- Game-provided error text is capped at 2,000 characters before serialization,
  preventing an exception that embeds a large inspected object from exceeding
  the protocol framing limit.
- The game measures every serialized outbound message before queueing it. An
  oversized request response is replaced with a bounded `response_too_large`
  failure instead of disconnecting the tracker.
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
