# Ironmon Tracker protocol v1

This document records the implemented protocol through the 0.7.5 type-coverage
context additions. Later parts extend the payload catalog without changing the
common envelope or transport.

## Transport

- IPv4 loopback only: `127.0.0.1:38521`.
- The tracker listens and the game initiates the connection.
- One UTF-8 JSON object followed by `\n` forms each message.
- The connection remains open for duplex events, requests, and responses.
- One message may contain at most 1,048,576 characters on the tracker and
  1,048,576 buffered bytes in the game bridge.

The game performs connection creation on a background Ruby thread. Established
socket reads and writes occur on the game thread only after a zero-timeout
`IO.select` readiness check. A missing tracker therefore never blocks gameplay.

## Connection sequence

1. The game sends `game_connected` as the first message.
2. The tracker validates schema version 1 and sends `tracker_connected`.
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
    "game_version": "6.8.0",
    "ironmon_version": "0.7.5",
    "ironmon_active": false,
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

## Tracker handshake

```json
{
  "schema_version": 1,
  "type": "event",
  "event": "tracker_connected",
  "sequence": 0,
  "sent_at": "2026-08-06T20:05:45.300Z",
  "payload": {
    "tracker_version": "0.1.0.0",
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

The tracker sends `update_settings` when that setting changes while connected:

```json
{
  "auto_select_starter": true,
  "maximum_starter_base_stat_total": 525,
  "favorite_species_ids": ["BULBASAUR:0", "SQUIRTLE:0"]
}
```

The game applies and echoes the complete settings payload. When the tracker is
not connected, the preference remains local and is supplied by the next
handshake.

The foreground controller shortcut sends a `reset_run` request with an empty
payload. The game responds with `accepted`, queues the request until a safe map
scene boundary, and then opens the same confirmation flow as F7. Keyboard input
is not synthesized by the tracker.

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

An active 0.7.5 run also includes optional aggregate type-coverage context:

```json
{
  "type_coverage": {
    "trainer_policy": "mixed",
    "normal_pool_size": 576,
    "normal_pool_fingerprint": "5ab45fb7fa469aa5",
    "fusion_pool_schema_version": 2,
    "fusion_pool_size": 174348,
    "fusion_pool_fingerprint": "a71c6c1c12491b47"
  }
}
```

The context contains only the selected trainer category policy and release-pool
compatibility metadata. It contains no run seed, generated species mapping,
trainer slot, future roster, or species identity. Older game versions omit the
whole optional object and remain protocol-compatible; the tracker disables only
type coverage for that connection.

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
    "speed": -1
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
always-hit value.

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
    "speed": 0
  }
}
```

The live payload deliberately omits HP, calculated stats, ability, held item,
nature, and undiscovered moves. The tracker supports multiple positions but
the first UI displays the lowest active opposing position.

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

`player_move_menu_opened` is emitted once per player Pokémon send-out when the
battle first enters that Pokémon's move-selection menu. It contains only the
stable `pokemon_id`. The UI consumes this as navigation intent; repeated visits
to the same menu do not emit another event.

Move effectiveness is calculated in the tracker from move and target types.
It deliberately excludes hidden or conditional ability effects.

## Run lifecycle

Starting or resetting an Ironmon run assigns a new persisted `run_id`, resets
its event sequence, and emits `run_started` when connected. Its payload uses
the same current-state shape, including optional type-coverage context. If the tracker is absent at that moment,
the next handshake and `current_state` response recover the active run.

When a run ends, the game persists its result in the save metadata and emits
`run_completed` with a compact deterministic recipe:

```json
{
  "run_id": "run-seed-918273645",
  "seed": 918273645,
  "result": "lost",
  "game_version": "6.8.0",
  "ironmon_version": "0.7.5",
  "configuration": {
    "schema_version": 2,
    "wild_policy": "mixed",
    "trainer_policy": "mixed",
    "unfusion_setting": "random_component"
  },
  "data_mode": "classic",
  "species_generator_version": 2,
  "ability_generator_version": 3,
  "base_stat_generator_version": 1,
  "move_access_generator_version": 6,
  "player_fusion_generator_version": 3,
  "species_pool_fingerprint": "...",
  "ability_pool_fingerprint": "...",
  "base_stat_source_fingerprint": "...",
  "move_pool_fingerprint": "...",
  "move_contextual_restriction_fingerprint": "...",
  "move_source_fingerprint": "...",
  "egg_move_source_fingerprint": "...",
  "tm_roster_fingerprint": "...",
  "tm_source_fingerprint": "...",
  "tr_roster_fingerprint": "...",
  "tr_source_fingerprint": "...",
  "tutor_catalog_fingerprint": "...",
  "tutor_source_fingerprint": "...",
  "fusion_tutor_catalog_fingerprint": "...",
  "fusion_tutor_source_fingerprint": "...",
  "item_mappings": {
    "POTION": "HYPERPOTION"
  },
  "tm_mappings": {
    "TM01": "TM02"
  },
  "move_access_metrics": {
    "schema_version": 1,
    "encounters": [],
    "machine_acquisitions": [],
    "tutor_visits": [],
    "move_acquisitions": [],
    "move_uses": []
  },
  "fusion_pool_fingerprint": "..."
}
```

The same optional `completed_run` value is included in `current_state`, so a
tracker started after the loss can still archive the recipe. The tracker writes
recipes atomically under
`%LocalAppData%/IronmonTracker/runs/<run-id>/recipe.json`. It never persists
the reconstructed lookup response.

`item_mappings` and `tm_mappings` preserve the exact mapped shuffles because
Infinite Fusion generates them from runtime RNG rather than from the Ironmon
run seed. Historical recipes which omit these fields use already persisted
area-entry details and leave unknown archived item identities concealed rather
than consulting the currently loaded run.

New Step 3.3 runs include `move_access_metrics`. The game records only species,
levels, party compatibility, acquisitions, and move uses that were actually
encountered or observed. It reconstructs channel totals, overlap, the effective
initial four, tutor mismatch, and fusion-union growth when the run ends. The
tracker archives that completed snapshot with the recipe and does not expose it
through active-run views. Recipes created before metric schema 1 omit the field
and remain valid.

## Deterministic post-run lookup

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
- the fusion's deterministic Ironmon reverse; and
- every ordered normal-material pair that maps to that fusion in the run.

Evolution destinations, previous evolutions, displayed components, reverse
fusions, and fusion materials use stable species identifiers and can be
selected as the subject of another lookup.

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

Schema-version-1 runs reconstruct occurrences from their loaded saved source
maps. Because those legacy maps are not derivable from the compact recipe,
their occurrences are available only while that run's save is loaded.

`fusion_preview` accepts two normal species identifiers and the same recipe.
It returns the two ordered results, first-species body plus second-species head
and the reversed orientation. Identical materials produce one distinct result.
The tracker uses normal-only paged search to select the second material; it
does not enumerate every possible fusion containing the selected species.

Until the planned evolution generator is implemented in Ironmon 0.6.0, the
evolution relationships describe the game's current natural evolution graph.
They must not be presented as seeded randomized targets. A fusion's displayed
body and head are its actual species-owned components; encounter or pivot input
Pokemon do not replace those components.

Before any post-run command returns generated information, the game verifies
that the run is complete and that the requested species, ability, base-stat,
move-access, and player-fusion generators plus their recorded source pools and
catalogs still match. Recipes created before Step 3.3 have no move metadata and
retain native move-access lookup. A mismatch returns a structured
error such as `generator_unavailable`, `incompatible_species_pool`,
`incompatible_ability_pool`, `incompatible_base_stats`, or
`incompatible_move_access`, or `incompatible_fusion_pool`. A completed lost or
won run remains available for
lookup while another Ironmon run is active. A recipe that itself declares an
active or missing result remains rejected.

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
| fusion material pairs | `fusion.material_pairs` plus either a represented current target or `pokemon.all_active` |
| wild reverse occurrences | `world.wild_encounters` plus either a represented current target or `pokemon.all_active` |
| trainer reverse occurrences | `world.trainer_parties` plus either a represented current target or `pokemon.all_active` |
| fusion preview | `fusion.preview_results` |

The selected exact Pokemon information capability is `pokemon.overview`,
`pokemon.abilities`, `pokemon.base_stats`, `pokemon.move_access`, or
`evolution.results`. Overview also accepts an independently authorized wild,
trainer, material-pair, or preview surface, and Evolutions also accepts
`evolution.candidates`; the game omits the exact Overview or evolution fields
when only a tool surface is granted. The `pokemon.all_active` catalog grant
implies both current-Pokemon availability grants before negotiation.

Candidate, material, and reverse-occurrence requests optionally carry a
`target` and `enemy_position`. When present, only `player` or `enemy` is valid,
and the game resolves that live target and replaces the supplied `species_id`
before lookup. Without a target, the request is arbitrary and therefore
requires `pokemon.all_active` at both tracker and game boundaries.

Active area detail uses `world.wild_encounters`, `world.trainer_parties`, or
`world.items` for its selected category. Without that grant, ordinary
discovery filtering remains in force. Completed-run reconstruction is
unchanged.

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

### Live grant replacement

Activation, replacement, removal, and expiration send an uncorrelated
`diagnostic_access_changed` event whose payload contains the complete new
`diagnostic_capabilities` list. The game validates the list as a single bounded
replacement and clears its previous grants before applying it. A non-array,
empty identifier, identifier longer than 128 characters, unknown identifier,
duplicate identifier, or list over 128 entries produces an empty grant. The
event does not carry token metadata.

### 0.7.3 compatibility

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
`statistics`. Schema version 1 contains attempt number, seed, result, active
seconds, per-save result totals, completed battles, highest player level,
badges earned, actual and wasted item healing, item counts grouped by `Bag` or
`Held`, trainer species frequencies with display names keyed by species
identifier, and defeated-trainer BST aggregates.

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
