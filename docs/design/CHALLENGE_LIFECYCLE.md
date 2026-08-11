# Challenge lifecycle and statistics

Status: **Steps 5.1-5.4 implemented**

This document defines when an Ironmon attempt starts and ends, how a failed run
is enforced, how checkpoint resets preserve attempt history, and which
statistics are authoritative. The game-owned attempt lifecycle, per-save
ledger, active duration, manual F7 carry-over, failure enforcement, automatic
reset, authoritative statistics collection, tracker presentation, and
completed-run selection are implemented.

## Attempt identity and results

An attempt begins when Ironmon generates its seed and opens starter selection.
The player has already received information that could influence a reroll at
that point, so resetting before selecting a starter still consumes the attempt.

Every attempt has one per-save-slot number and one stable run ID. Its result is
one of:

- `active`: generated and not yet completed;
- `lost`: ended by battle loss or draw;
- `won`: ended by Hall of Fame completion; or
- `abandoned`: replaced by F7 while still active.

The transition away from `active` is idempotent. Later callbacks cannot replace
or duplicate the result.

Battle decisions 2 and 5 end the run. This includes story and rival battles
marked `canLose`, because safe base-game continuation does not weaken the
Ironmon challenge rule. Wild capture and ordinary victory do not end the run.

## Failure enforcement

When automatic reset is disabled, the completed battle performs its native
cleanup and the map then enters a failed-run lock. Movement, new battles, and
Pokemon acquisitions cannot continue. F7 remains available, and the external
tracker remains usable for completed-run lookup.

When automatic reset is enabled, failure completion queues the existing
checkpoint-reset mechanism immediately. It displays no failure prompt and asks
for no confirmation. The reset begins only after battle cleanup has returned to
a safe map-scene boundary.

If a checkpoint cannot be read, loaded, generated, or saved, automatic reset
stops. The failed run remains locked and displays the existing actionable reset
error. A win never triggers automatic reset.

Automatic reset is a persistent pre-run Boolean setting. Its default and legacy
migration value is disabled, and F7 preserves the selection.

## Per-slot attempt ledger

Attempt history belongs to the save slot because checkpoints and run state are
already slot-scoped. The tracker archive remains the installation-wide history.

The save stores:

- next/current attempt number;
- attempts started;
- attempts lost;
- attempts won;
- attempts abandoned;
- current attempt statistics; and
- the last completed attempt summary.

Checkpoint loading must not roll these values back. Manual and automatic reset
carry the current ledger across the pre-starter checkpoint load, complete the
old attempt first when necessary, then start the next numbered attempt.

Duration is accumulated active game time. Saving stores the accumulated value;
loading resumes from it. Time while the application is closed is excluded.

## Core attempt statistics

Each attempt records:

- attempt number, run ID, seed, and final result;
- active duration;
- battles completed;
- highest level reached by a player-owned usable Pokemon; and
- badges earned.

Aggregate started/lost/won/abandoned totals are retained per save slot. The
tracker archives the full completed summary; the game shows only the current
attempt number in starter/reset notices and diagnostic output.

## Item and healing statistics

`total_item_healing` is the HP actually restored by a player resource. It
includes Bag use and player-owned held-item activation in and out of battle.

`wasted_item_healing` is the requested HP restoration minus the actual HP gain
when restoration is capped at maximum HP. A failed use that consumes nothing
adds neither an item use nor wasted healing.

`items_used` counts every successfully consumed player resource and retains a
breakdown by item ID and source:

- `Bag`: deliberate Bag use, including Poke Balls; and
- `Held`: berries, Focus Sash, gems, seeds, White Herb, Air Balloon, and other
  activated held items, including deliberate move consumption such as Fling.

Enemy-owned items, key-item activation, item transfers, Knock Off, and scripted
inventory removal are excluded. If a player-owned item is stolen and later
consumed, it remains a consumed player resource and is counted once.

## Trainer-opponent statistics

An encountered trainer Pokemon is recorded when its displayed battler first
appears in one trainer battle. Switching the same Pokemon out and back in does
not count it twice. A later rematch is a new battle and counts again.

The attempt records:

- encounter frequency by displayed species identity, retaining both its
  protocol identifier and authoritative display name;
- the number of distinct displayed trainer species; and
- every species tied for the highest encounter frequency.

A defeated trainer Pokemon is recorded only when that opposing Pokemon actually
faints. Its displayed generated base-stat total at that moment contributes to:

- defeated count;
- exact total and derived average;
- minimum BST and every species tied at that value; and
- maximum BST and every species tied at that value.

Hidden party members that never appear and opponents that survive the battle do
not contribute to these statistics. Normal and fused Pokemon use the same
displayed-species generated base-stat resolver.

## Tracker behavior

Run completion is game-owned and continues to emit the existing deterministic
completed recipe. The protocol adds versioned attempt statistics without making
them mandatory for historical recipes.

When a completion is received or recovered, the tracker archives it, navigates
to Lookup, and selects that run. An immediate `run_started` from automatic reset
updates the background live state but does not steal navigation focus from the
completed attempt. Manual navigation remains authoritative afterward.

The tracker displays the complete statistics. Debug diagnostics and run-start
metadata include the attempt number, while ordinary gameplay receives no new
statistics screen.
