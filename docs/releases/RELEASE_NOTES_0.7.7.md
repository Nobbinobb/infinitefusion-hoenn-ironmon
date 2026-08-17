# Ironmon 0.7.7

Ironmon 0.7.7 adds shareable seeded runs through the external tracker while
keeping generated active-run information behind an explicit export action.

## Shareable seeded runs

- Create a recognized tracker seed token from the connected active attempt or
  from the selected completed run in Archive.
- Copy actions show adjacent success feedback, and tokens can be saved as
  `.ironmon-seed` files through the native Windows file picker.
- Paste or load a token, validate its signed type and claims locally, review its
  seed and version context, and explicitly confirm the destructive import.
- Ordinary seed-token integrity material is shared between trackers and is
  separate from diagnostic access. It identifies tracker-generated data but
  does not make a token an official or maintainer-approved challenge.

## Compatibility and transaction safety

- Tokens bind the seed, complete run configuration, game-data mode, game and
  Ironmon versions, and a canonical fingerprint of every seeded generator and
  source pool needed to reconstruct the world.
- The game independently validates the normalized request, active attempt,
  checkpoint, versions, configuration, and local compatibility before queueing.
- Import loads the starter checkpoint and generates, verifies, and saves the
  replacement attempt transactionally. Failure restores the previous live
  attempt instead of committing a partial transition.
- F7 remains the ordinary fresh-seed reset. Reproducing a shared run again
  requires importing its token again.

## Validation

- The bundled game runtime performs repeated imported-preset generations and
  compares representative starter, species, ability, base-stat, move,
  evolution, player-fusion, item, Gym, and progression results across opposite
  lookup orders.
- The normal release pipeline now runs all tracker token tests and bundled-game
  deterministic-import checks before it builds the player archive.

## Compatibility

Ironmon 0.7.7 targets Pokemon Infinite Fusion 2 version 6.8.0. Existing 0.7
saves remain compatible. The tracker and game scripts should be updated
together. A seed token must match the installed release, configuration mode,
game data, and custom-fusion pool; incompatible tokens are rejected rather than
silently producing a different world.
