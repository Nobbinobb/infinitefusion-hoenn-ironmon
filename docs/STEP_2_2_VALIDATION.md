# Step 2.2 validation

## Embedded-Ruby checks

- [x] The game loads the acquisition and storage hooks without a startup error.
- [x] A first acquisition installs exactly one usable Pokemon.
- [x] A later acquisition replaces the current Pokemon transactionally and
  leaves no copy of the previous Pokemon in the party.
- [x] An invalid candidate leaves the original party unchanged and retains a
  distinct pending transaction for safe recovery.
- [x] Extra usable Pokemon in a legacy party are moved to the save-backed
  quarantine rather than deleted or sent to ordinary storage.
- [x] Eggs and explicitly temporary rental Pokemon do not count as usable and
  are recorded in the exclusion audit.
- [x] Lower-level PC copy, move-to-party, and move-to-box methods reject usable
  Pokemon.
- [x] Quarantine and exclusion records survive save serialization.

The checks ran inside Infinite Fusion's embedded Ruby runtime after normal game
data initialization. The temporary runtime hook was removed after the pass.

## Interception audit

- [x] Permanent wild catches are intercepted at the caught-Pokemon action
  boundary before the battle peer can place them in party or storage.
- [x] Visible/static catches use the same battle boundary.
- [x] Normal, silent, party-only, foreign, and direct-store gift helpers enter
  the same transaction boundary.
- [x] Trades retain the game's direct outgoing-to-incoming replacement and
  receive a completed stable acquisition identifier.
- [x] A pending transaction blocks another acquisition or trade.
- [x] PC withdraw, deposit, drag/place, swap, copy, and move-to-party paths are
  blocked for usable Pokemon while Eggs remain movable.
- [x] The canonical source, copy-ready distribution, and local installation
  match.

Step 2.2 uses a transactional Swap fallback so the intermediate build remains
playable. Step 2.3 replaces that fallback with the complete non-cancellable
blind action interface.
