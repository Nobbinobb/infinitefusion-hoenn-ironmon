# Ironmon 0.6.1

This patch release improves the 0.6.0 evolution release's tracker integration,
runtime responsiveness, starter handoff, and offline sprite behavior.

## Tracker information alignment

- Gives Debug Pokemon, Debug Lookup, and completed-run Lookup the same Overview,
  Abilities, Stats, Moves, and Evolutions pages.
- Restores live ability-slot information while adding the same reconstructed
  slot diagnostics and generator metadata to every entry point.
- Keeps authored occurrences, fusion relationships, generated stats, move
  access, and evolution analysis in their corresponding shared pages.
- Removes duplicated component information and corrects malformed compact UI
  labels.

## Lookup performance

- Generates and transfers only the selected information page. Other pages load
  when opened and are cached independently for the represented Pokemon.
- Removes duplicate eager move and evolution reconstruction from live player
  and enemy inspection.
- Reuses active-run generators, caches wild-fusion table targets, and builds one
  compact reverse material index instead of rescanning every material pair for
  each fusion.

## Gameplay fixes

- Preserves experience earned by the Hoenn starter during the initial rescue
  battle when the starter is handed back through the story event.
- Warns before starting that Ironmon expects the spritepack to be installed.

## Compatibility

- Retains the deterministic species, ability, base-stat, move-access, and
  evolution mappings introduced by earlier releases.
- Existing Ironmon saves remain supported; the sprite-download setting is
  normalized safely when they load.

Ironmon 0.6.1 targets Pokemon Infinite Fusion 2 version 6.8.0.
