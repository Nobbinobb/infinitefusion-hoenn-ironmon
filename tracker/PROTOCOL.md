# Ironmon Tracker protocol v1

This document records the implemented connection and player-tracking subset
through Part 4. Later parts extend the payload catalog without changing the
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
    "ironmon_version": "0.3.3",
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
snapshot used by the live events below. Enemy data remains absent until Part 5.

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

## Run lifecycle

Starting or resetting an Ironmon run assigns a new persisted `run_id`, resets
its event sequence, and emits `run_started` when connected. Its payload uses
the same Part 3 current-state shape. If the tracker is absent at that moment,
the next handshake and `current_state` response recover the active run.

## Failure behavior

- A client that does not send `game_connected` within five seconds is closed.
- Malformed JSON, unsupported schemas, missing required fields, and invalid
  first messages close only that connection; the listener remains available.
- Unknown game commands return a structured `unknown_command` error.
- Port-binding failures appear as tracker connection errors rather than
  crashing the desktop window.
- Game-side connection errors are rate-limited and never escape into gameplay.
