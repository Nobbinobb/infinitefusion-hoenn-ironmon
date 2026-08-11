# Ironmon Tracker protocol v1

This document records the implemented protocol through the Step 3.3 move-access
inspection additions. Later parts extend the payload catalog without changing
the common envelope or transport.

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
    "ironmon_version": "0.6.4",
    "ironmon_active": false,
    "debug_available": true,
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
    "debug_requested": false
  }
}
```

`debug_requested` records the `--debug` command-line request. It does not grant
debug access; the game remains authoritative through `debug_available`.

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
  "base_stat_total": 490
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
the same Part 3 current-state shape. If the tracker is absent at that moment,
the next handshake and `current_state` response recover the active run.

When a run ends, the game persists its result in the save metadata and emits
`run_completed` with a compact deterministic recipe:

```json
{
  "run_id": "run-seed-918273645",
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
  "data_mode": "classic",
  "species_generator_version": 2,
  "ability_generator_version": 3,
  "base_stat_generator_version": 1,
  "move_access_generator_version": 6,
  "player_fusion_generator_version": 2,
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

## Authorized debug inspection

Debug requests require both authorization signals established during the
handshake:

- the tracker must send `debug_requested: true`, which only occurs when it was
  launched with `--debug`; and
- the game must send `debug_available: true`, which currently requires its
  development `$DEBUG` mode.

The tracker refuses to send debug requests unless both signals are present.
The game independently enforces the same pair before resolving either command.
Failure returns `debug_forbidden` and no inspector data.

`debug_inspect_pokemon` accepts a target of `player`, `enemy`, or `party`.
Enemy targets include `enemy_position`; party targets include the zero-based
`party_index`. The game resolves the actual current Pokemon and returns:

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

`debug_run_diagnostics` has an empty payload and returns the game and Ironmon
versions, protocol version, run and battle IDs, seed, configuration policies,
fusion-pool metadata, ability-generator metadata, base-stat generator metadata,
move-access source metadata, and wild/trainer mapping counts.

The authorized active-run lookup uses `debug_pokemon_search`,
`debug_pokemon_lookup`, and `debug_fusion_preview`. Their result contracts match
the completed-run lookup equivalents, but their request payloads omit the
completion recipe and the game resolves them from the currently loaded run.
All three commands independently require the same dual debug authorization and
are unavailable through the normal post-run API while a run remains active.

## Failure behavior

- A client that does not send `game_connected` within five seconds is closed.
- Malformed JSON, unsupported schemas, missing required fields, and invalid
  first messages close only that connection; the listener remains available.
- Unknown game commands return a structured `unknown_command` error.
- Port-binding failures appear as tracker connection errors rather than
  crashing the desktop window.
- Game-side connection errors are rate-limited and never escape into gameplay.
