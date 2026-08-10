# Ironmon 0.6.2

This patch release strengthens the shared tracker information model, prevents
oversized lookup responses, improves diagnostics, and adds foreground-safe
keyboard and controller navigation.

## Shared Pokemon information

- Gives live Debug Pokemon, active-run Debug Lookup, and completed-run Lookup
  the same Overview, Abilities, Stats, Moves, and Evolutions information.
- Preserves live slot diagnostics while sharing occurrence, fusion-material,
  generator, move-access, and evolution details across every entry point.
- Places the evolution graph before the asynchronously loaded valid-target
  lists.
- Restructures completed-run recipes and run diagnostics into focused nested
  protocol models instead of an increasingly flat payload.

## Lookup and protocol reliability

- Pages large occurrence and fusion-material searches instead of including
  unbounded result sets in one response.
- Records the request and response context for automatic protocol diagnostics.
- Returns stable protocol errors for unavailable inspection targets and keeps
  the tracker connected after recoverable lookup failures.
- Avoids mutating frozen runtime lookup data when a fusion has no material
  results.
- Treats missing generator sections in early completed-run archives as
  unavailable data instead of surfacing a debugger-only deserialization
  exception.

## Background shortcuts

- Replaces the focused `P`/`E`/`L`/`D` shortcuts with `Ctrl+1` through
  `Ctrl+4` for Player, Enemy, Lookup, and Debug.
- Accepts those shortcuts while the connected Infinite Fusion executable owns
  the foreground window without focusing the tracker.
- Supports XInput controllers by holding both triggers and flicking the right
  stick left for Player, right for Enemy, up for Lookup, or down for Debug.
- Ignores background Debug requests unless the game authorized development
  access.

## Compatibility

- Retains the deterministic species, ability, base-stat, move-access, and
  evolution mappings introduced by earlier releases.
- The tracker application and Ironmon runtime now both report version 0.6.2.

Ironmon 0.6.2 targets Pokemon Infinite Fusion 2 version 6.8.0.
