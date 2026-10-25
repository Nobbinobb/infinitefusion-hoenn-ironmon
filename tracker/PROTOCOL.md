# Ironmon Tracker protocol v1

This document records the implemented protocol through the 0.8.15 tracker UI,
required tracker sessions, run management, native fusion closure, and run-aware
evolution additions. Later parts extend
the payload catalog without changing the common envelope or transport.

## Transport

- IPv4 loopback only: `127.0.0.1:38521`.
- The tracker listens and the game initiates the connection.
- One UTF-8 JSON object followed by `\n` forms each message.
- The connection remains open for duplex events, requests, and responses.
- One message may contain at most 1,048,576 characters on the tracker and
  1,048,576 buffered bytes in the game bridge.

The game performs connection creation on a background Ruby thread. Established
socket reads and writes occur on the game thread only after a zero-timeout
`IO.select` readiness check. A missing tracker leaves classic play and the main menu available. Ironmon
loads require a negotiated session; active Ironmon pauses and reconnects
without unwinding its battle or event stack.

## Connection sequence

1. The game sends `game_connected` as the first message.
2. The tracker validates schema version 1 and sends `tracker_connected`, echoing
   the advertised management protocol, process session, and installation root.
3. The tracker sends a `current_state` request with a unique `request_id`.
4. The game returns a successful response with the same `request_id`.
5. Both sides retain the connection until either process closes it.

The game retries after disconnection. Restarting the tracker triggers the same
handshake and state-recovery sequence without restarting the game.

## Game handshake

```json
{
  "schema_version": 1,
  "type": "event",
  "event": "game_connected",
  "run_id": null,
  "battle_id": null,
  "sequence": 0,
  "sent_at": "2026-08-06T20:05:45.253Z",
  "payload": {
    "game_version": "6.8.2",
    "ironmon_version": "0.8.15",
    "management_protocol_version": 1,
    "session_id": "4820:1791835545.253:123456789",
    "ironmon_active": false,
    "control_overrides_supported": true,
    "debug_available": true,
    "supported_diagnostic_capabilities": ["pokemon.current_player", "run.seed"],
    "game_root": "C:/Games/InfiniteFusion2",
    "run_id": null,
    "battle_id": null
  }
}
```

`run_id` is present after an Ironmon run begins. `battle_id` is present while a
battle is active.
`control_overrides_supported` advertises the transient input override described
below. An absent or false value means the game does not support that override.

## Tracker handshake

```json
{
  "schema_version": 1,
  "type": "event",
  "event": "tracker_connected",
  "sequence": 0,
  "sent_at": "2026-08-06T20:05:45.300Z",
  "payload": {
    "tracker_version": "0.8.15",
    "management_protocol_version": 1,
    "session_id": "4820:1791835545.253:123456789",
    "game_root": "C:/Games/InfiniteFusion2",
    "automatic_reset": true,
    "debug_requested": false,
    "auto_select_starter": false,
    "maximum_starter_base_stat_total": 525,
    "favorite_species_ids": ["BULBASAUR:0"],
    "diagnostic_capabilities": ["run.seed"]
  }
}
```

`supported_diagnostic_capabilities` is the bounded set of named information
capabilities implemented by this game version. `diagnostic_capabilities` is
the sorted intersection of that set and the tracker's current effective signed
grant. The tracker never sends the token, signature, note, expiration, or key
material to the game.

`debug_requested` and `debug_available` remain as the 0.7.3 development
compatibility pair. When both are true, the connection receives the local
unrestricted developer override. Neither field is required for a valid named
release-token grant.
`auto_select_starter` supplies the tracker-owned persisted starter setting on
every connection or reconnection.
`maximum_starter_base_stat_total` is the optional inclusive generated-BST
ceiling. It is null when disabled and applies only with autoselect.
`favorite_species_ids` contains the tracker-owned normal-species favorites used
by the Favorite Clause.

The tracker sends `update_settings` when any of these preferences changes while connected:

```json
{
  "auto_select_starter": true,
  "automatic_reset": false,
  "maximum_starter_base_stat_total": 525,
  "favorite_species_ids": ["BULBASAUR:0", "SQUIRTLE:0"]
}
```

The game applies and echoes the complete settings payload. When the tracker is
not connected, the preference remains local and is supplied by the next
handshake.

For older games without control override support, the foreground controller
reset shortcut sends a `reset_run` request with an empty payload. The game
responds with `accepted`, queues the request until a safe map scene boundary,
and then opens the same confirmation flow as F7. Keyboard input is not
synthesized by the tracker.

### Transient control state

A supporting tracker sends `controls_state` events while the connected game
has focus. Their payload contains the managed logical actions and the subset
currently held:

```json
{
  "enabled": true,
  "managed": ["up", "confirm", "interact", "menu", "reset"],
  "pressed": ["up"]
}
```

The game suppresses native input for managed actions and derives triggers,
repeats, releases, and movement from the received state. `interact` and `menu`
apply in the field; `confirm` and `cancel` apply in menus and dialogs.
`battle_run` applies only to the guarded wild-battle Run context. The `reset`
action reaches the existing F7 reset guard. Tracker navigation shortcuts remain
local to the tracker.

These events have no request ID or response. Valid state refreshes a one-second
lease. Disabling the override with `{"enabled": false}`, disconnecting, or
allowing the lease to expire restores native input. Unknown actions or malformed
action arrays cannot renew the lease. The game never saves this transient state
to its native control configuration.

### Explicit seeded-run export

Seed tokens are never part of `current_state`, `run_started`, diagnostic
snapshots, or automatically rendered active-run views. After the user chooses
Create token, the tracker sends `export_seeded_run` with an empty payload and
the active `run_id`. The game accepts only the matching active attempt with no
pending reset or import, then returns `SeededRunExportPayload`: the shared
reproduction recipe containing the seed, complete configuration, game and
Ironmon versions, data mode, the immutable generation-profile ID pinned when
the attempt began, and generator compatibility manifests.

The tracker calculates the canonical compatibility fingerprint and creates an
`ironmon-seed+jwt` compact token with the public ordinary-tracker HS256
integrity material. This material is deliberately separate from diagnostic
access and is neither secret nor maintainer authority. A token is described as
recognized tracker-generated data, never as an official challenge.

The selected completed-run archive recipe implements the same reproduction
contract, so the tracker can create an equivalent token locally without an
`export_seeded_run` request or a game connection. Archive-only results,
statistics, observations, and item-mapping fields are not token claims. Both
active and archived export surfaces can copy the token with adjacent success
feedback or save it as an `.ironmon-seed` UTF-8 text file.

### Seeded-run import lifecycle

The tracker sends `import_seeded_run` only for a token it has already decoded
and normalized. Its payload contains exactly `token_id`, `seed`, `game_version`,
`ironmon_version`, `data_mode`, `configuration`, and
`compatibility_fingerprint`. The configuration contains exactly
`schema_version`, `wild_policy`, `trainer_policy`, `unfusion_setting`, and
`automatic_reset`.

The historical `automatic_reset` recipe field is retained for seed identity and
compatibility. From 0.8.10, loss-reset behavior follows the live tracker
preference independently of the imported recipe.

The game independently validates the complete payload, active run identity,
version and data-mode boundary, configuration values, seed range, and lowercase
SHA-256 compatibility fingerprint. The correlated response has the same
`SeededRunImportStatusPayload` shape used by later status events:

```json
{
  "token_id": "01K2SEEDTOKEN00000000000000",
  "status": "accepted",
  "message": "Seeded-run import was accepted for guarded queueing."
}
```

An accepted response means that the checkpoint was readable and the request is
eligible to queue; it does not mean the active attempt has been replaced. The
game then emits `seeded_run_import_status` with `queued` after the correlated
response. At the next safe map-scene boundary it snapshots the live save,
stages abandonment in a copied ledger, loads the starter checkpoint, generates
the imported seed and configuration, verifies the generated compatibility
again, and saves the new attempt. Only after all those operations succeed does
it publish the old `run_completed` event and the new `run_started` event,
followed by a `started` status.

Validation failures return `rejected` without queueing. A queued transaction
that cannot load, generate, validate, or save emits `failed` against the source
run and restores the live snapshot. Ordinary F7 reset remains a separate
new-seed operation, and either transition rejects while the other is pending.

## Current-state recovery

Request:

```json
{
  "schema_version": 1,
  "type": "request",
  "command": "current_state",
  "request_id": "bc77caf578f946b69081dc72e3757bf4",
  "sent_at": "2026-08-06T20:05:45.301Z",
  "payload": {}
}
```

Recovery response before a player Pokemon has been sent out:

```json
{
  "schema_version": 1,
  "type": "response",
  "request_id": "bc77caf578f946b69081dc72e3757bf4",
  "run_id": null,
  "battle_id": null,
  "sent_at": "2026-08-06T20:05:45.320Z",
  "success": true,
  "payload": {
    "ironmon_active": false,
    "run_id": null,
    "battle_id": null,
    "sequence": 0
  }
}
```

During battle, `battle` contains the active battle identifier. Once a player
Pokemon has actually been sent out, `player` contains the same complete player
snapshot used by the live events below. `enemies` contains the currently active
legal enemy snapshots and remains an empty array outside battle.
`starter_selection` contains the active three-slot starter view while the bag
scene is open, including only details that have been revealed in the game.

The nested player `healing` object retains `item_count`, `potential_hp`, and
`percentage`, and also includes an `items` array containing every carried item
whose game data has a nonzero battle-use type. Each entry contains `id`, `name`,
`description`, `quantity`, `category`, and `requires_move`. Category is one of
`healing`, `pp_restore`, `status`, `combat_stat`, or `other`.

The tracker selects an item with `use_battle_item`:

```json
{
  "item_id": "MAXETHER",
  "move_index": 2,
  "target_position": 1
}
```

`move_index` is required only for PP items. `target_position` identifies the
selected opponent for opponent-targeted items and is otherwise ignored. The
response contains `accepted` and a user-facing `message`. Acceptance means the
game queued the native item choice. It is available from the main command menu,
Fight move selection, native Bag item list and item-action prompt, and Pokémon
party list and party-action prompt. A submenu request cancels that layer through
its normal return value, performs its native cleanup without selecting the
highlighted move, item, or party member, and then enters the same native Bag
action path. Non-cancellable nested screens such as Summary reject the request
until closed. The native battle checks, turn consumption, item consumption, and
effect handlers remain authoritative.

An active 0.8.15 run also includes optional aggregate type-coverage context:

```json
{
  "type_coverage": {
    "trainer_policy": "mixed",
    "normal_pool_size": 576,
    "normal_pool_fingerprint": "5ab45fb7fa469aa5",
    "fusion_pool_schema_version": 1,
    "fusion_pool_size": 176654,
    "fusion_pool_fingerprint": "68e863503c00d52f"
  }
}
```

The context contains only the selected trainer category policy and release-pool
compatibility metadata. The legacy-named `fusion_pool_schema_version` field is
the pinned generation profile's `custom_fusion_eligibility` algorithm version;
the packed component keeps its independently versioned storage schema. The
context contains no run seed, generated species mapping, trainer slot, future
roster, or species identity. This object is optional; when absent, the tracker
disables type coverage. The required run-management session handshake is
validated separately.

## Starter selection

`starter_selection_changed` sends a complete starter-selection snapshot when
the three-candidate scene opens, when another starter is revealed, and when the
scene closes. A newly opened selection contains three hidden choices and one
stable random pick:

```json
{
  "active": true,
  "random_pick_index": 1,
  "maximum_base_stat_total": 525,
  "choices": [
    { "index": 0, "revealed": false },
    { "index": 1, "revealed": false },
    { "index": 2, "revealed": false }
  ]
}
```

After the player reveals a candidate, that choice additionally contains
`species_id`, `species_name`, `sprite_path`, `base_stat_total`, `bst_eligible`,
and `favorite`.
The last field reports whether the normal species or either half of a fusion is
in `favorite_species_ids`. Hidden
choices never transmit those fields. Closing the scene sends
`{"active":false,"choices":[]}` and removes `starter_selection` from later
current-state recovery responses.

When `auto_select_starter` is enabled, the opening snapshot reveals all three
choices immediately. The game ignores player selection input for two seconds,
opens the choice at `random_pick_index`, displays it for 0.75 seconds, and
returns that Pokemon without a confirmation prompt.

When a maximum BST is configured, candidates above it are removed before the
Random Pick and Favorite Clause are applied. If none remain, the game abandons
the attempt and starts the normal checkpoint reset after closing the scene.

If the Random Pick is not a favorite while one or more favorites are present,
automatic completion pauses after the delay. The game displays a named command
for the Random Pick and every favorite candidate, and the player selects from
that complete list. A favorite Random Pick is still selected automatically.

The settings page uses the public `favorite_pokemon_search` command for
autocomplete suggestions. The request uses the active connection rather than a
completed-run recipe:

```json
{
  "schema_version": 1,
  "type": "request",
  "command": "favorite_pokemon_search",
  "request_id": "ef63cbe80641442785b59eb7c2682dce",
  "run_id": "run-seed-1652146642",
  "sent_at": "2026-08-12T21:15:10.100Z",
  "payload": {
    "query": "squirt",
    "offset": 0,
    "limit": 8,
    "normal_only": true
  }
}
```

The game enforces normal-only matching even if a client omits or changes
`normal_only`, so fusion identifiers cannot enter the saved favorites list:

```json
{
  "schema_version": 1,
  "type": "response",
  "request_id": "ef63cbe80641442785b59eb7c2682dce",
  "run_id": "run-seed-1652146642",
  "sent_at": "2026-08-12T21:15:10.110Z",
  "success": true,
  "payload": {
    "matches": [
      {
        "species_id": "SQUIRTLE:0",
        "species_name": "Squirtle"
      }
    ],
    "total": 1
  }
}
```

## Battle lifecycle

`battle_started` is emitted immediately before the game begins its normal
battle flow. Its payload is:

```json
{
  "battle_id": "battle-run-123-456789"
}
```

`battle_ended` uses the same payload after the complete battle flow returns.
The tracker clears the active battle but retains the last player card.

## Player state

`player_sent_out` is emitted after the game's real send-out operation. Party
position alone never initializes the production player view.

```json
{
  "pokemon_id": "184467",
  "species_id": "ESPEON:0",
  "nickname": "Espeon",
  "species_name": "Espeon",
  "sprite_path": "Graphics/Battlers/196.png",
  "level": 5,
  "current_hp": 24,
  "maximum_hp": 24,
  "status": "NONE",
  "confused": false,
  "types": ["PSYCHIC"],
  "ability": "Synchronize",
  "held_item": null,
  "attack": 14,
  "defense": 16,
  "special_attack": 21,
  "special_defense": 18,
  "speed": 15,
  "stat_stages": {
    "attack": 2,
    "defense": 0,
    "special_attack": 0,
    "special_defense": 0,
    "speed": -1,
    "accuracy": -1,
    "evasion": 0
  },
  "base_stat_total": 525,
  "nature": "Hardy",
  "moves": [
    {
      "id": "PSYCHIC",
      "name": "Psychic",
      "type": "PSYCHIC",
      "current_pp": 10,
      "total_pp": 10,
      "power": 90,
      "accuracy": 100
    }
  ],
  "level_up_moves": [],
  "healing": {
    "item_count": 1,
    "potential_hp": 20,
    "percentage": 83.3
  }
}
```

After initialization, `player_state_changed` sends the same complete payload
when any serialized value changes. The game checks at most ten times per
second and sends nothing when the snapshot is unchanged. This covers HP,
status, temporary battle confusion, level, calculated stats, types, ability,
held item, moves, PP, sprite, nature, and healing inventory without creating
per-frame network traffic. Confusion may appear alongside a persistent status
and becomes false when its counter expires, it is cured, or battle ends.
Tracking continues outside battle after the first real send-out, so bag item
changes update healing capacity before the next encounter.

Power zero represents a status move. Accuracy zero represents the game's
always-hit value. The tracker applies the move user's current `accuracy` stage
and the selected target's `evasion` stage to the displayed move accuracy.

## Healing inventory

The game counts carried, non-revival HP-healing items and healing berries. It
includes fixed healing, quarter-maximum-HP Sitrus Berries, and maximum-HP Max
Potions and Full Restores. Rage Candy Bars count only when the current game
rules make them an HP item. Status-only medicine and held items are excluded.

`potential_hp` is the combined nominal restoration against the active
Pokemon's current maximum HP. `percentage` is
`potential_hp / maximum_hp * 100` and may exceed 100.

## Enemy state

`enemy_sent_out` is emitted for trainer send-outs and for wild opponents once
they become active. `enemy_state_changed` uses the same payload when legally
visible identity data changes.

```json
{
  "enemy_id": "enemy-987654",
  "position": 1,
  "species_id": "BELLOSSOM:0",
  "species_name": "Bellossom",
  "sprite_path": "Graphics/Battlers/182.png",
  "level": 25,
  "types": ["GRASS"],
  "base_stat_total": 490,
  "stat_stages": {
    "attack": 0,
    "defense": 1,
    "special_attack": 0,
    "special_defense": 0,
    "speed": 0,
    "accuracy": 0,
    "evasion": 1
  }
}
```

The live payload deliberately omits HP, calculated stats, ability, held item,
nature, and undiscovered moves. The tracker supports multiple positions but
the first UI displays the lowest active opposing position.

## Fusion marker

Player and enemy snapshots also include `fusion`, a Boolean derived from the
game's ordinary or triple-fusion classification of the displayed species.
The live cards use this flag to show Active fusion or Opposing fusion in the
existing small heading above the Pokemon's name. The sprite component does
not consume this flag, and no sprite space is reserved for a marker.
Missing values from older game versions default to false and retain the
ordinary Pokemon headings.
This identity field does not reveal concealed abilities, items, or fusion
material obtainability.

## Defensive overview

Player and enemy snapshots optionally include `defensive_overview`, serialized
from `DefensiveOverview`. This is distinct from the player's numeric `defense`
stat. The same optional object travels in state-change events and recovered
current state; there is no separate defense request. A missing or null object
produces an unavailable state rather than fabricated defenses.

| Field | Meaning |
| --- | --- |
| `in_battle`, `limited_information` | Whether battle context is available and whether concealed enemy state is excluded. |
| `type_matchups` | Incoming type identifiers/names, `base_multiplier`, and `multiplier` after known immunity/chart overrides. The optional `physical_min`, `physical_max`, `special_min`, and `special_max` fields contain combined relative factors, including conditional ranges but excluding Defense and Special Defense stat stages. |
| `protections` | Compact effect objects with `label`, nullable `active`, and public `moves` catalogs. Duplicate labels are merged. |
| `recovery` | Compact effect objects whose `label` is a passive trigger or active countdown, such as `Rain`, `Switch out`, or `Next turn end`; `healing_amounts` contains HP, cure, duration, or stage-restoration outcomes with applicable chance/one-use qualifiers. Repeated values represent independent contributions and must be preserved. The existing wire name is retained for compatibility. |
| `modifiers`, `status_protections`, `move_protections`, `other_protections` | Supporting rule identifiers, nullable activity, types, affected moves, and known source metadata from game catalogs. These are not rendered directly by the compact view; summaries and prose conditions are not generated. |
| `ability_name`, `ability_description`, `ability_suppressed` | Individually known ability metadata retained in the payload, not displayed in this view. |

Compact effects omit source names and prose. An `active` value of `false` hides
the entry; `null` is conditional rather than confirmed. For recovery, `true`
means the known effect is available subject to its trigger and healing rules;
it does not disclose current enemy HP/status or guarantee recovery. Known Heal Block
removes only the HP recovery it actually blocks; status cures, Regenerator, and
pending switch-in wishes retain their native exceptions.
Hydration's Rain entry includes heavy rain and remains listed regardless of
current weather or concealed status; suppressed or unrevealed abilities add nothing.
Missing `moves` or `healing_amounts` deserialize to
empty lists. Protection move lists and recovery amounts are collapsed by
default and disclosed only on click. The UI groups identical recovery strings
with a contribution count instead of deduplicating or summing rounded heals.

Recovery includes passive abilities/held items and active field, battler, or
position states. Unused healing moves do not contribute. Wish's public counter
selects `Next turn end` or `Turn end`; no WishAmount is read. Its outcome uses
the original max-HP basis rather than the current recipient's max HP. Leech Seed
uses the public recipient index, including zero, and a symbolic drained-HP amount;
source HP, hidden items/status, and unrevealed drain-changing abilities are never
consulted. Consumed states disappear on the next normal snapshot update.

All 18 type rows, including neutral ones, remain in the payload. The UI omits
combined neutral results and falls back to `multiplier` when combined factors
are absent. Supporting `adjustments` and `exceptions` on type rows do not create
source disclosures; the current producer leaves `exceptions` empty. These
relative factors are not exact HP-damage predictions.

The game loads audited definitions from the release-generated
`Data/Ironmon/defense_presentation.json` catalog. Generation resolves public
names and affected-move lists using flags, functions, categories, and audited
pure predicates on detached move objects. Ruby evaluates conditions and combines factors
against the permitted live context. Catalog provenance is not checked against
the running game, so an older catalog remains usable after a game update.
Missing or unreadable data fails only this optional overview, not gameplay.
The UTF-8 catalog is parsed through the native JSON parser, never evaluated as
Ruby. Recovery definitions support multiple typed events, independent blockers,
probabilities, single-use outcomes, and pending-state counters. Legacy recovery
tuples remain readable; there is no game-version or provenance-hash gate.

The game builds enemy defenses only from visible types/effects and the ability
last revealed for that individual. Hidden held items, HP, status, and actual
unrevealed abilities are excluded before serialization. Public move catalogs
describe affected moves, not the enemy's moveset. This extends the existing
schema-v1 snapshots without adding a command or changing transport framing.

## Move discovery

`enemy_move_used` is emitted after an opposing Pokemon uses an observable move:

```json
{
  "enemy_id": "enemy-987654",
  "species_id": "BELLOSSOM:0",
  "enemy_level": 25,
  "move": {
    "id": "MEGADRAIN",
    "name": "Mega Drain",
    "learned_level": 20,
    "learn_order": 36,
    "source": "level_up",
    "origin": "enemy_use",
    "type": "GRASS",
    "power": 40,
    "accuracy": 100,
    "total_pp": 15,
    "pp_after_use": 14
  }
}
```

The game detects this through the battle callback and confirms it through the
periodic enemy-state poll. A signature of enemy, move, and remaining PP avoids
duplicate events while allowing repeated uses of the same move to update PP.
The most recently used regular move is also included as optional `last_move`
data in `enemy_sent_out`, `enemy_state_changed`, and recovered enemy snapshots.
This lets discovery recover through the same state path as the enemy card.

The game derives `learned_level` and `learn_order` from the active generated
learnset. Moves without an applicable level-up entry use source `unknown` and
remain visible when directly observed from an enemy. Player snapshots include
applicable current entries in `level_up_moves` with origin `player_initial`;
subsequent snapshots preserve earlier discoveries even after a move is
forgotten.

The tracker stores all discoveries per run and species/form. For the current
enemy level it selects level-up entries not above that level, orders by learned
level and learn order, and displays the newest four. Repeated enemy use updates
observed remaining PP without deleting the player-assisted discovery origin.
Direct observations with an unknown learn source remain visible as a safe
fallback when the game cannot resolve a learnset entry.

Every live event reselects the run identifier from its envelope. This covers
the common startup order where the tracker connects at the title screen before
an existing Ironmon save supplies its run identifier.

When a save does not yet contain a tracker run identifier, the game derives one
from its persisted Ironmon randomization seed as `run-seed-<seed>`. Reloading
the same save therefore restores the same tracker-owned knowledge, while a new
randomized run receives a different identifier.

## Tracker-owned Part 5 state

Remembered moves and the six manual stat annotations are written atomically to:

```text
%LocalAppData%/IronmonTracker/runs/<run-id>.json
```

Annotations are keyed by run, species/form, and stat. Left-click cycles
`empty -> plus -> minus -> empty`; right-click cycles in reverse. Neither
annotations nor remembered discoveries are written into the game save.

## Adjustment Part A metadata

Player snapshots additionally carry:

- `ability_details` with stable ID, localized name, and description;
- `nature_adjustments` for the five nature-adjustable stats;
- `learnset_progress` with `learned_moves`, `maximum_moves`, and nullable
  `next_move_level`;
- consistently ordered `evolutions` containing only compact level, item, or
  other requirement data without the destination species; and
- move `category` and localized `description` fields.

The learned-move count intersects the Pokémon's recorded learned-move history
with its unique level-up learnset. The maximum is the complete unique level-up
learnset. The next level is the earliest learnset entry above the current
level, or null when the learnset is complete.

An opposing ability activation emits `enemy_ability_revealed`. The same legal
ability is retained as optional `last_ability` enemy-state data for recovery.
The tracker remembers multiple abilities per run and species/form, together
with the highest visible enemy level. Player-owned abilities also contribute
to this knowledge.

`player_move_menu_opened` is emitted when the battle first enters the player's
move-selection menu after the latest player or opponent send-out. It contains
only the stable `pokemon_id`. The UI consumes this as navigation intent. An
opponent replacement re-enables the event even when the player's active Pokémon
did not change; repeated visits without another send-out do not emit it again.

Move effectiveness is calculated in the tracker from move and target types.
It deliberately excludes hidden or conditional ability effects.

## Run lifecycle

Starting or resetting an Ironmon run assigns a new persisted `run_id`, resets
its event sequence, and emits `run_started` when connected. Its payload uses
the same current-state shape, including optional type-coverage context. If an
event is missed during a connection interruption, the next handshake and
`current_state` response recover the active run. Ironmon stays paused until a
compatible tracker session reconnects.

When a run ends, the game persists its result in the save metadata and emits
`run_completed` with a compact deterministic recipe:

```json
{
  "run_id": "run-seed-918273645",
  "seed": 918273645,
  "result": "lost",
  "generation_profile_id": "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
  "game_version": "6.8.2",
  "ironmon_version": "0.8.15",
  "configuration": {
    "schema_version": 1,
    "wild_policy": "mixed",
    "trainer_policy": "mixed",
    "unfusion_setting": "random_component",
    "automatic_reset": false
  },
  "data_mode": "classic",
  "species_generator": {
    "version": 1,
    "pool_fingerprint": "..."
  },
  "ability_generator": {
    "version": 1,
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
        "version": 1,
        "size": 174346,
        "fingerprint": "..."
      }
    }
  },
  "move_access_generator": {
    "version": 1,
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
    "version": 1,
    "pool_size": 174346,
    "pool_fingerprint": "..."
  },
  "item_generator": {
    "version": 1,
    "rules_version": 1,
    "ground_pool_size": 580,
    "ground_total_weight": 5253,
    "ground_pool_fingerprint": "...",
    "tm_pool_size": 124,
    "tm_pool_fingerprint": "...",
    "result_bans": ["DNASPLICERS", "SUPERSPLICERS", "DNAREVERSER", "DYNAMITE", "AIRMAIL", "BLOOMMAIL", "BRICKMAIL", "BUBBLEMAIL", "FLAMEMAIL", "GRASSMAIL", "HEARTMAIL", "MOSAICMAIL", "SNOWMAIL", "SPACEMAIL", "STEELMAIL", "TUNNELMAIL", "BLACKAPRICORN", "BLUEAPRICORN", "GREENAPRICORN", "PINKAPRICORN", "REDAPRICORN", "WHITEAPRICORN", "YELLOWAPRICORN", "EXPSHARE", "MACHETE", "TELEPORTER", "SURFBOARD", "LEVER", "JETPACK", "PICKAXE", "SCUBAGEAR", "LANTERN", "CLIMBINGGEAR"],
    "result_ban_fingerprint": "...",
    "shop_policy_version": 1
  },
  "item_mappings": {
    "POTION": "HYPERPOTION"
  },
  "tm_mappings": {
    "TM01": "TM02"
  }
}
```

The same optional `completed_run` value is included in `current_state`, so a
tracker started after the loss can still archive the recipe. The tracker writes
recipes atomically under
`%LocalAppData%/IronmonTracker/runs/<run-id>/recipe.json`. It never persists
the reconstructed lookup response.

Recipes use `item_generator` to reconstruct physical-slot rewards from the run
seed and the pinned version-1 pool manifest. Its ground-pool fingerprint
includes the exclusive category and integer weight of every eligible result.
TM gifts select uniformly from their TM-only pool. Missing or incompatible
item metadata is rejected; archived lookup never substitutes the currently
loaded run's mapping or authored item behavior.

## Immutable generation-profile format

`generation_profile.json` contains `profile_id` and `manifest`. Manifest schema
1 contains `schema_version`, `algorithms`, and `components`. Each algorithm is
`{name, version}`; each component is `{name, schema_version, sha256, byte_length}`.
Names are stable lowercase identifiers. Versions and byte lengths are positive
integers; component digests are lowercase SHA-256 hashes of the exact bytes.
Algorithm and component names must be unique. The required algorithm families
are defined by `Ironmon::GenerationProfile::ALGORITHM_FAMILIES` and
`GenerationAlgorithmNames.All`; supported implementation versions belong to
their generators, independently of component formats and cache schemas.

Before hashing, sort both descriptor arrays by name in ordinal order and sort
object keys ordinally at every depth. Serialize the normalized manifest as
compact UTF-8 JSON, without a BOM or trailing newline. `profile_id` is its
lowercase SHA-256 digest; release versions are not part of this identity.
The shared Ruby/C# generation-profile tests verify canonical-byte parity.

Current packages retain `base_catalog` (normal game data), `custom_fusion_pool`
(packed eligibility), `custom_sprites` (reviewed sprite catalog), `area_catalog`
(authored area sources), and `obtainability_sources` (semantic acquisition and
resource sources). Historical eligibility-version-1 packages omit
`custom_sprites`; the runtime validates the component set against the pinned
eligibility implementation. Derived indexes are rebuildable caches, not source
components or profile identity.

The `custom_fusion_pool` component uses this schema-1 binary layout:

| Offset | Bytes | Encoding |
| ---: | ---: | --- |
| 0 | 8 | ASCII `IFCFPOOL` |
| 8 | 2 | Unsigned little-endian schema version |
| 10 | 2 | Unsigned little-endian normal-species count, `N` |
| 12 | 4 | Unsigned little-endian eligible-fusion count |
| 16 | 4 | Unsigned little-endian bitset byte length |
| 20 | remaining | Eligibility bitset |

For one-based body/head identifiers, bit index is
`(body - 1) * N + head - 1`: body is the outer row axis, head is the inner axis,
and bits within each byte are least-significant first. Bitset length is
`ceil(N * N / 8)`, with unused padding bits zero. Readers reject unknown schemas,
invalid dimensions, odd eligibility counts, extra or missing bytes, and counts
that disagree with set bits. Enumeration follows increasing bit index, retaining
the runtime pool's deterministic order.

Starting an attempt validates and retains its immutable package under
`Data/Ironmon/generation_profiles/<profile_id>/` before pinning the ID to the
attempt and save metadata. Active loads and archived requests use that package;
an archived request restores the active profile afterward. Reset selects the
installed current package for the replacement attempt. Missing, damaged, or
unsupported historical inputs return `generation_profile_unavailable`; they
never fall back to current catalogs or acquire today's profile implicitly.
Future deterministic behavior changes must preserve the implementations and
packages needed by supported pinned profiles rather than rewriting old runs.

## Deterministic post-run lookup

`pokemon_sprites` accepts `species_ids`, a list of one through eight canonical
species/form identifiers, and returns a `sprites` object mapping each requested
identity to a game-relative image path or null. This public presentation request
does not require a generation recipe, diagnostic access, or generated lookup
preparation. It selects stable standard artwork without consuming gameplay RNG
or changing current sprite substitutions. The game materializes missing sheet
images and writes `history-<species>-<form>.json` under its tracker sprite cache;
the tracker reuses those paths offline when older ownership records have no
captured image. Available recorded sprite paths take precedence over this
species-artwork fallback.

`pokemon_search` accepts a non-empty text query, zero-based `offset`, a `limit`
from 1 through 50, an optional `normal_only` restriction, and a completed-run
recipe. The request envelope and recipe must carry the same run ID. A
successful response contains the requested page of localized names and stable
species/form IDs plus the complete match count:

```json
{
  "matches": [
    { "species_id": "CHARMANDER:0", "species_name": "Charmander" }
  ],
  "total": 1
}
```

`pokemon_lookup` accepts the selected `species_id` and the same recipe. The
tracker also sends a fixed compatibility level of 100 for game processes that
loaded the earlier Part 6 protocol; it is not a user-facing filter and does not
limit the returned learnset. The response includes:

- localized identity and a game-relative sprite path;
- types, all six original and generated base stats, their differences, both
  BST values, and whether that run enabled base-stat randomization;
- every generated normal and hidden ability slot;
- generated move access grouped into `learnset`, `egg_moves`, `machine_moves`,
  and `tutor_moves`, including acquisition source labels and localized move
  details;
- current evolution requirements, destinations, direct pre-evolutions, and
  sprites;
- authored wild route, encounter-table, and slot occurrences with source
  species and slot percentage when available;
- authored trainer and party-slot occurrences with source species;
- displayed body and head components for a fusion;
- the fusion's deterministic Ironmon reverse.

Evolution destinations, previous evolutions, displayed components, reverse
fusions, and fusion materials use stable species identifiers and can be
selected as the subject of another lookup.

Overview leaves `fusion_materials` empty so reconstructing reverse mappings
cannot delay the initial Pokemon page. For a fusion, the tracker requests the
first 10-row page separately through `fusion_material_search` after Overview
has rendered. Later pages use the same command and remain independently cached.

The Evolutions section deliberately leaves `generated_predecessors` empty in
its initial `pokemon_lookup` response. The tracker requests predecessors only
when the generated graph is opened. `evolution_predecessor_search` accepts the
selected `species_id`, completed-run recipe, zero-based `offset`, and a `limit`
from 1 through 50. The tracker normally uses pages of 8. Its response contains
the page's predecessor nodes, echoed offset and limit, a `continuation` value
of `complete`, `available`, or `unknown`, and `next_offset` unless complete.
`available` proves that another result is already known; `unknown` permits a
later page without forcing an exhaustive scan before returning the current
page. Evolution-section payloads include `current_stage_level`, and every
generated target or predecessor includes `stage_level` plus a
`component_side` of `normal`, `head`, or `body`. These stable values let the
tracker progressively assemble bounded neighborhoods without deriving rows
from response order. The local expansion-depth preference applies independently
to the original node and every later branch selection. Already completed nodes
are reused. The tracker can therefore limit the visible canvas to the selected
node's configured neighborhood without discarding loaded nodes, while an
all-loaded view requires no new protocol requests. The view scope remains a
presentation choice when another branch is selected. Closing the graph stops
the tracker from requesting further pages.

Graph row presentation is also tracker-local. Nodes within each stable
`stage_level` are ordered by generated BST and divided according to the local
nodes-per-row preference. A compact view may represent a connection to a
currently hidden BST range through a local range portal; this does not alter or
aggregate the directed relationships received from the game.

The machine list identifies the TM or TR item that teaches each compatible
move. The tutor list includes only moves supported by a currently available
ordinary tutor or by the Fusion Tutor's regular or legendary catalog. It also
reports the supported ordinary-tutor count beside the Pokemon's full abstract
tutor compatibility count. The legacy top-level `learnset` field mirrors the
new grouped level-up list for older tracker clients.

Normal Only wild fusion outcomes list both ordered encounter slots and both
source species. Their displayed percentage is conditional on the separate
wild-fusion event triggering because that trigger rate is not part of the
completed-run recipe.

Occurrence lookup reconstructs mappings from the selected recipe and its pinned
generation profile. It does not depend on the currently loaded save's source
maps, so an archived run remains queryable while another compatible run is
active.

`fusion_preview` accepts two normal species identifiers and the same recipe.
It returns the two ordered results, first-species body plus second-species head
and the reversed orientation. Those results are one global reverse pair: wild
or player-created instances of either species resolve the other species when
reversed. The pair's allowed BST separation is derived from the widest legal
material interval produced by the continuous fusion formula, not from its
40-BST lower-bound breakpoint. Identical materials produce one distinct result.
The tracker uses normal-only paged search to select the second material; it
does not enumerate every possible fusion containing the selected species.

Evolution relationships use the selected run's version-1 deterministic
evolution recipe and pinned profile. A fusion's displayed body and head are its
actual species-owned components; encounter or pivot input Pokemon do not replace
those components.

Before any post-run command returns generated information, the game verifies
that the run is complete, that its recipe contains a lowercase SHA-256
generation-profile ID, and that the requested species, ability, base-stat,
move-access, and player-fusion generators plus their recorded source pools and
catalogs still match. A missing profile identity returns `invalid_recipe`.
The runtime selects the content-addressed package named by that identity for
the complete request, including its historical base data, custom-fusion pool,
area catalog, and obtainability sources. A missing, damaged, or internally
inconsistent package returns `generation_profile_unavailable` instead of
falling back to current data. Other mismatches return a structured error such
as `generator_unavailable`,
`incompatible_species_pool`, `incompatible_ability_pool`,
`incompatible_base_stats`, `incompatible_move_access`, or
`incompatible_fusion_pool`. A completed lost or won run remains available for
lookup while another Ironmon run is active when its immutable profile is
available. A recipe that itself declares an active or missing result remains
rejected.

## Authorized diagnostic inspection

Protected active-run requests use named capabilities. The tracker checks the
negotiated grant before sending a request, and the game independently checks
the same requirements before reading hidden runtime state. Failure returns
`debug_forbidden` and no protected data.

The request requirements are:

| Request | Required capabilities |
| --- | --- |
| current player inspection | `pokemon.current_player` plus an authorized surface on the selected page |
| current enemy inspection | `pokemon.current_enemies` plus an authorized surface on the selected page |
| active-run Pokemon search | `pokemon.all_active` |
| active-run Pokemon lookup | `pokemon.all_active` plus the selected information capability |
| evolution candidates | `evolution.candidates` plus either a represented current target or `pokemon.all_active` |
| evolution predecessors | `evolution.results` plus either a represented current target or `pokemon.all_active` |
| fusion material pairs | `fusion.material_pairs` plus either a represented current target or `pokemon.all_active` |
| wild reverse occurrences | `world.wild_encounters` plus either a represented current target or `pokemon.all_active` |
| trainer reverse occurrences | `world.trainer_parties` plus either a represented current target or `pokemon.all_active` |
| fusion preview | `fusion.preview_results` |
| development state | any `development.*` capability |
| development mutation | the exact capability mapped to its action |

The selected exact Pokemon information capability is `pokemon.overview`,
`pokemon.abilities`, `pokemon.base_stats`, `pokemon.move_access`, or
`evolution.results`. Overview also accepts an independently authorized wild,
trainer, material-pair, or preview surface, and Evolutions also accepts
`evolution.candidates`; the game omits the exact Overview or evolution fields
when only a tool surface is granted. The `pokemon.all_active` catalog grant
implies both current-Pokemon availability grants before negotiation.

Candidate, predecessor, material, and reverse-occurrence requests optionally carry a
`target` and `enemy_position`. When present, only `player` or `enemy` is valid,
and the game resolves that live target and replaces the supplied `species_id`
before lookup. Without a target, the request is arbitrary and therefore
requires `pokemon.all_active` at both tracker and game boundaries.

Active area detail uses `world.wild_encounters`, `world.trainer_parties`, or
`world.items` for its selected category. Without that grant, ordinary
discovery filtering remains in force. Completed-run reconstruction is
unchanged.

Encounter area detail is game-paged. `area_lookup_detail` carries a zero-based
`offset` and a `limit` from 1 through 50; the tracker uses pages of 10. The game
returns only that encounter slice together with echoed `offset` and `limit` and
the route-wide `total_count`. Species generation, fusion resolution, sprite
lookup, serialization, and transport therefore apply only to the requested
page. Trainer and item detail retain their complete category payloads and use
tracker-local paging.

`debug_inspect_pokemon` accepts a target of `player` or `enemy`. Enemy targets
include `enemy_position`. The game resolves the actual current Pokemon and
returns a live inspector section together with the capability-filtered lookup
section for that resolved species:

- identity, nickname, level, gender, sprite, item, species, and form;
- normal/fusion kind and displayed fusion components;
- active ability index, slot, ID, and name;
- ability generator schema, pool-rules version, pool size, and fingerprint;
- original and generated normal and hidden slots;
- final fusion slots and body/head generated slots;
- ability eligibility; and
- final-slot source and restricted-source replacement information;
- original and generated final base stats, per-stat differences, and both BST
  values; and
- base-stat generator metadata, with body/head dominance labels for standard
  fusions but without duplicated component stat tables; and
- the same Learnset, Egg, TM, and supported Tutor move-access groups used by
  completed-run and active Debug Lookup.

Arbitrary species inspection is not implemented by this request because the
game's ordinary Pokemon constructor consumes random values. The debug contract
will not use that mutating path merely to fabricate an inspection target.

`debug_run_diagnostics` has an empty payload. Runtime version and identity
context accompany any authorized group. `run.seed`, `run.configuration`,
`run.generator_manifests`, and `evolution.generator_details` independently
control the seed, configuration, non-evolution manifests and mapping counts,
and evolution manifest. Omitted groups are not serialized by the game.

Exact evolution targets and graph edges are serialized only with
`evolution.results`. Candidate pages use their separate request and require
only `evolution.candidates` plus the relevant Pokemon availability scope, so
candidate-only and result-only access work independently.

The authorized arbitrary active-run lookup uses `debug_pokemon_search`,
`debug_pokemon_lookup`, and `debug_fusion_preview`. Their result contracts match
the completed-run lookup equivalents, but their request payloads omit the
completion recipe and the game resolves them from the currently loaded run.
These commands are unavailable through the normal post-run API while a run
remains active.

`debug_development_state` accepts `include_catalogs` and `include_evolutions`
(both default to true) and returns only option catalogs covered by the effective
development grants. Its player identity, level,
ability identifier, and moves describe the single current usable party Pokemon.
The ability and move collections are complete searchable game catalogs; the
evolution and devolution collections list every valid direct branch for the
current Pokemon.

The tracker first requests state without evolutions, then prepares fusion
directions through its shared native assignment index before sending the
evolution request. For an authorized fusion development request, the initial
request also sets `use_native_evolution_assignments` and the response includes
internal `fusion_assignments` generation inputs. This explicit request works
before normal background lookup starts without changing its readiness flag;
other state requests retain their existing disclosure rules. The internal
recipe is not displayed by Development or retained in its combined response.
This preparation runs outside the correlated game request
timeout and never builds the complete fusion predecessor index on the game
thread. The second request carries `pokemon_id`, `species_id`,
`use_native_evolution_assignments`, `evolution_assignments`, and
`predecessor_assignments`. The game binds these arrays to both the current
individual and species before formatting options. Empty arrays are complete
results; missing arrays for a fusion fail immediately with
`evolution_assignments_unavailable`, while a replaced or evolved source fails
with `pokemon_changed`. Mutation commands still validate the selected direct
edge independently. Older peers may omit these optional fields.

`debug_development_action` carries an `action` plus only the input used by that
action: `enabled`, `level`, `ability_id`, `move_ids`, `item_id` and
`quantity`, or `species_id`. The action-to-capability mapping is:

| Action | Capability |
| --- | --- |
| `set_auto_revive` | `development.auto_revive` |
| `full_heal` | `development.full_heal` |
| `set_level` | `development.level` |
| `set_ability` | `development.change_ability` |
| `set_moves` | `development.change_moves` |
| `give_item` | `development.give_item` |
| `evolve`, `devolve` | `development.evolution` |
| `swap_pokemon` | `development.swap_pokemon` |

Only `set_auto_revive` is accepted during battle. Every persistent Pokemon or
inventory mutation is rejected while a battle ID is active. Evolution actions
must traverse one explicitly selected direct generated edge in the requested
direction. Ability changes may select any identifier in the game catalog. Swap
creates a fresh Pokemon at the replaced Pokemon's level and resets its moves to
the newest four level-up moves available at that level. Every successful action
returns the refreshed player state without repeating the option catalogs. The
development-state request can load the invariant catalogs separately from the
Pokemon-specific evolution directions so the page renders before predecessor
preparation completes.

### Live grant replacement

Activation, replacement, removal, and expiration send an uncorrelated
`diagnostic_access_changed` event whose payload contains the complete new
`diagnostic_capabilities` list. The game validates the list as a single bounded
replacement and clears its previous grants before applying it. A non-array,
empty identifier, identifier longer than 128 characters, unknown identifier,
duplicate identifier, or list over 128 entries produces an empty grant. The
event does not carry token metadata.

### 0.7.3 compatibility

These pairs describe historical diagnostic payload compatibility. Ironmon
0.8.10 also requires the negotiated run-management session; an older tracker
without that support cannot authorize Ironmon loading or play.

- A 0.7.4 tracker connected to a 0.7.3 game receives no supported named
  capabilities. Signed-token access is therefore unavailable, while the old
  `debug_requested && debug_available` developer pair still works.
- A 0.7.3 tracker connected to a 0.7.4 game omits
  `diagnostic_capabilities`, which the game treats as an empty named grant. Its
  legacy development pair still works.
- A 0.7.4 Release tracker without a valid token sends an empty grant even when
  the game implements named capabilities.
- Unknown optional fields remain ignored, so ordinary tracking and completed-
  run lookup retain the v1 compatibility policy.

## Milestone 5 attempt statistics

`current_state` and `run_started` optionally include `attempt_statistics`.
Completed recipes optionally include the same versioned payload as
`statistics`. Schema version 1 contains attempt number, optional `save_slot`,
seed, result, active seconds, per-save result totals, completed battles,
highest player level, badges earned, actual and wasted item healing, item
counts grouped by `Bag` or `Held`, trainer species frequencies with display
names keyed by species identifier, and defeated-trainer BST aggregates. New
0.7.8 snapshots include `save_slot`; older snapshots omit it and remain valid.

Recipes archived before these fields existed remain valid. The tracker shows
their statistics as unavailable rather than creating zero values. The game
also retains the last completed recipe in the save ledger while the next
attempt is active. A reconnect after immediate automatic reset can therefore
recover, archive, navigate to Lookup, and select the missed completion.

## Failure behavior

- A client that does not send `game_connected` within five seconds is closed.
- Malformed JSON, unsupported schemas, missing required fields, and invalid
  first messages close only that connection; the listener remains available.
- Unknown game commands return a structured `unknown_command` error.
- Port-binding failures appear as tracker connection errors rather than
  crashing the desktop window.
- Game-side connection errors are rate-limited and never escape into gameplay.

## Tracker-owned title actions

Handshakes advertise `management_protocol_version: 1`. The game adds a
process-scoped `session_id`; the tracker echoes it and `game_root`. A socket
alone does not authorize Ironmon. `tracker_heartbeat` carries that session
every second independently of correlated requests. Eight seconds without a
matching heartbeat revokes the connection. Buffered input is read before the
timeout check to avoid false timeouts after lengthy game-owned work.

`game_management` returns `protocol_version`, `state`, `menu_id`, and `slots`.
The native main-menu chooser reports `main_menu`; the animated title loop
awaiting player input reports `title_screen`. Both expose saves and accept run
actions. Gameplay and startup dialogs report `gameplay`. Slots contain the game's actual names, `manual`,
`occupied`, `ironmon`, `loadable`, SHA-256 `revision` (or `empty`), trainer,
location, badge count, attempt number, and saved rules. Filesystem paths are
never accepted from the client.

`start_ironmon` requires current `menu_id`, manual `slot`, exact `revision`,
explicit `overwrite: true` for occupied files, and `rules` containing only
`wild_policy`, `trainer_policy`, and `unfusion_setting`. `continue_ironmon`
uses the same menu/slot/revision checks, requires a valid Ironmon save, and
includes autosaves. Both return `accepted` when queued. The response is flushed
before unwinding the native chooser or title loop; the game checks again before action and
reports creation/loading failures using its normal message UI.

Handshake and `update_settings` carry `automatic_reset` (default true). This live
preference changes loss handling independently of saved rules and historical
seed recipes. Existing shared-seed commands remain separate from title actions.

## Earned achievement notices

Both handshakes advertise `achievement_protocol_version: 1`. The tracker sends
`achievement_unlocks` only to a runtime that advertises this version. Its
`unlocks` array contains earned `id`, `number`, `name`, `condition`, and
`unlocked_at` values. No dormant definitions or locked progress are transmitted.

The game may send `achievement_poll` with an empty payload to recover pending
notices after reconnecting. After a notice has actually been displayed, it sends
`achievement_displayed` with an `ids` array of opaque UUIDs. Delivery and display
acknowledgements are idempotent; an interrupted display remains pending.
Notification failure does not reject already committed gameplay data.

## Desktop achievement notifications

`achievement_overlay_version: 1` is optional alongside achievement delivery
version 3. Both peers must advertise it; older peers retain native game toasts.
The game sends `achievement_overlay_start` with a unique `presentation_id`
and an earned `achievement_id`. The tracker opens artwork only after checking
that identity in the authenticated earned ledger. No images cross this channel:
the renderer reads the installed encrypted packs in memory.

The tracker responds with `achievement_overlay_ready` containing the matching
presentation identity, `ready` and `visible`. Preparation does not advance time.
`achievement_overlay_frame` carries `elapsed` seconds from the game clock;
health/visibility acknowledgements are bounded to ten per second unless changed.
The game accumulates time only while visible and sends
`achievement_overlay_stop` at completion, interruption,
or fallback. Stale identities never stop a newer presentation. Renderer errors or
a missing acknowledgement for 0.75 seconds restart a full native notification.
Only the existing `achievement_displayed` event acknowledges the earned unlock.
Frame/health traffic never enters persistent collection data or diagnostics.
The optional `attempt.completed.completion_milestone` and
`completion_trainer_class_ids` describe a game-authoritative challenge endpoint.
The additive lifetime summary `attempt_completion_milestone_joint` retains those
fields with the result, independently of earned or locked achievement rules.
An absent milestone does not qualify as evidence for a content-limited reward.
Availability is explicitly retired in the encrypted definition and has no
automatic release-version expiry. Retired earned nodes remain readable.

### Configurable toggle speed

`tracker_connected.toggle_speed` carries the committed profile. Both lanes default to `[1,2,3]`; 2 connects to 2 and 3 to 3. `update_settings.toggle_speed` replaces the profile atomically after validation and is echoed in the accepted settings. Omission or null in a settings update retains the live profile, allowing unrelated settings to change independently. A handshake without this field restores defaults.

The profile contains `lanes.overworld` and `lanes.battle`, each an ordered array of `{id, nodes:[{id,value}]}` groups, plus `links:[{id,overworld,battle}]`. Link endpoints are stage IDs selecting first-visit entries. Every accelerated group has exactly one opposite group; its connection covers all members. Normal 1 is first, standalone, implicitly connected, and cannot be an explicit endpoint. Other positions are integers 2–10 in arbitrary order, including duplicates. IDs distinguish equal multipliers and retain per-group member memory. Each lane permits at most 128 positions; identifiers are 1–64 ASCII letters, digits, underscores or hyphens. Group IDs, stage IDs, and wire IDs are individually unique across the profile. Invalid profiles leave the active profile unchanged. Context transitions restore the destination group's last selected member, otherwise its entry endpoint. Hold-mode sliders are unaffected.

### Feature switches

Tracker handshakes and `update_settings` carry `features`, an object with Boolean
`achievement_popups`, `repel_counter`, `speed_indicator`, `battle_quick_items`,
`quick_run`, `favorite_clause`, and `enforce_bst_limit`. Missing fields default to
true; an absent/null object in an update preserves the current switches. Responses
return the normalized complete object. Favorites and maximum starter BST remain
stored separately and are echoed unchanged while their feature is disabled.
Suppressed earned notices use the existing achievement acknowledgement, retaining
unlocks without creating a later notification backlog. These local preferences
are not added to saved run rules or seed recipes.

## Game-owned wardrobe sessions

Both handshakes negotiate `wardrobe_protocol_version: 1`. The tracker has no
opening operation: only the game's Change outfit or bedroom entry constructs a
session. The game emits `wardrobe_state` with `open: true`, the process
`game_session_id`, random `wardrobe_id`, monotonically increasing `revision`,
shared `profile_revision`, slot `appearance_key`, point balance, permanent
`owned_keys`, draft `appearance`, `equipped_appearance`, `initial_selection`,
`loaded_profile_id`, and named `outfit_profiles`. Opening and recovery include
`catalog`; ordinary action responses retain the previously supplied catalogue.
A closed event has `open: false` and records whether the look was confirmed.

`wardrobe_state` is also a read-only recovery request. It can return a closed
session and never opens the game wardrobe. `wardrobe_action` requires both
session identities, the exact editing revision and a fresh 32-character lowercase
hex `operation_id`. Allowed actions are `change`, `purchase`, `confirm`, `cancel`,
`save_profile`, `replace_profile`, `rename_profile`, `delete_profile`, and
`load_profile`. Shared-profile mutations also require the expected
`profile_revision`, checked inside the profile's cross-process transaction lock.
Repeated identical operations in the current session return their original
response. Reusing an operation ID for different data, stale sessions or revisions,
invalid assets, locked profile pieces, duplicate names, and insufficient points
are rejected. Purchases use the canonical game catalogue price. A lost response
is reconciled by reading state; the tracker never blindly retries a mutation.

A change contains the full appearance, including explicit null `hat` and `hat2`
slots, integer skin tone 1–6, installed hair `version_style`, and all five integer
hue fields in 0–359. Preview changes and profile loads never equip the player.
Confirmation atomically persists the per-slot look and one-time free claim when
applicable, then equips it. Cancel retains permanent purchases and profile edits.
Saved outfits contain all appearance fields except skin tone and use stable
32-character IDs and unique 1–48 character names.

`wardrobe_preview` requires the current session and revision. It accepts native
`mode` (`portrait`, `front`, `back`, `left`, `right`, `bike`), optional candidate
`piece_key` and `accessory_slot`, optional saved `profile_id`, `head_only`, and
`dye_field`. It returns a cropped native `png_base64`, pixel `width`/`height`,
matching wardrobe identity and revision, installed `base_colors`, 36 native
10-degree `dye_colors`, and six `skin_colors`. Images use actual layered game
assets and native hue transforms without modifying the draft. Thumbnails load
only for the current catalogue page; obsolete work is cancelled and image caches
are bounded. The tracker accepts PNGs at most 512 pixels per dimension and
131,072 bytes. The game pauses gameplay and its attempt timer while continuing
native input and transport updates. Native Back cancels independently of tracker
control forwarding. A reconnect republishes the same session and draft.

### Future achievement wardrobe rewards

Both handshakes independently negotiate `wardrobe_achievement_rewards_version: 1`.
The collection transaction queues only newly earned identities in a separate
encrypted consumer outbox. Existing achievement checkpoints are never backfilled.
The outbox is independent of the existing achievement-popup pending queue and
survives tracker restarts and notification acknowledgements.

On compatible connection startup, achievement polls and committed collection
batches, the tracker sends `wardrobe_achievement_rewards` with `version: 1`, the
current game `session_id`, and `ids` containing at most 4,096 earned UUIDs. The
game validates the entire batch, atomically adds 100 points for each previously
uncredited identity in its shared cosmetic ledger, and sends
`wardrobe_achievement_rewards_acknowledged` with the same `session_id` and `ids`.
Only that durable game acknowledgement removes identities from the tracker
outbox. Repeated deliveries are harmless even after reload or a lost response.
Transient cosmetic-profile failures retain the pending delivery for retry.
The tracker sends no reward channel to older games and keeps pending rewards.
Reward IDs disclose only achievements already earned; locked metadata remains
protected. The point rate comes from game policy, never from an event-supplied
currency amount. Active wardrobe snapshots refresh after successful credits.
