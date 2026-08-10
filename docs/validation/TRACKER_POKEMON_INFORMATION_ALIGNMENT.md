# Tracker Pokemon information alignment

## Result

The three Pokemon-information entry points now use the same shared card and the
same five information tabs:

- completed-run Lookup searches an archived run;
- Debug Pokemon starts from the selected current player or enemy and enriches
  the shared tabs with live instance diagnostics; and
- Debug Lookup searches the active authorized run.

## Shared information

- [x] Overview, Abilities, Stats, Moves, and Evolutions tabs in all three entry
  points.
- [x] Identity, sprite, and generated typing.
- [x] Original and generated stats with the same visualization modes.
- [x] Complete original/generated and fusion component/final slot diagnostics,
  including eligibility, source, and restricted-replacement information, in
  both lookup flows and Debug Pokemon.
- [x] Evolution candidate lists and the one-step evolution graph.
- [x] Authored wild and trainer occurrences.
- [x] Wild and trainer occurrences use independent bounded 50-row pages, so
  combinatorial self-fusion slot pairs cannot exceed the one-message framing
  limit.
- [x] Fusion components, reverse, materials, and fusion exploration.
- [x] Fusion materials use bounded 50-row pages in both completed-run and
  active Debug lookup, preventing large collision sets from exceeding the
  one-message framing limit.
- [x] A fusion with no reverse-index material entry returns an empty material
  page without mutating the frozen reverse index.
- [x] Game-side protocol error text is capped at 2,000 characters so Ruby
  object inspection cannot create an oversized failed response.
- [x] The game replaces any still-oversized serialized response with a bounded
  `response_too_large` failure before it reaches the socket.
- [x] Complete generated move-access channels.
- [x] Ability, base-stat, move-access, and normal/fusion evolution generator
  diagnostics in their corresponding tabs.
- [x] Lookup and live-inspector requests identify one section. Hidden tabs are
  neither generated nor transferred, and an opened lookup tab is cached for
  the current Pokemon.
- [x] Clickable related Pokemon; complete lookup surfaces retain Back/Forward
  history.

## Validation

- [x] Debug Pokemon resolves the selected live shortcut through the same active
  run `PokemonLookupSnapshot` endpoint as Debug Lookup.
- [x] The live shortcut automatically follows a changed player or enemy species
  and falls back to an available target when the selected battler disappears.
- [x] The Abilities tab is present in both lookup flows and shows reconstructed
  slot diagnostics plus generated abilities with clickable detail panels.
- [x] Debug Pokemon uses that same Abilities tab and adds the current live slot
  and active-state marker.
- [x] Bundled-runtime reconstruction produced complete normal slots and all
  final-fusion, body-generated, and head-generated groups.
- [x] Overview shows one displayed-component section, using icon-bearing
  relation buttons.
- [x] Debug player/enemy selector labels contain a valid middle-dot separator.
- [x] Active-run lookup reuses prepared generators and does not construct
  dynamic fusion species merely to compare occurrence targets.
- [x] Bundled-runtime normal-only fusion occurrence benchmark measured about
  265 ms for the one-time table-index build and 3.1 ms for subsequent fusion
  targets, versus about 283 ms for every target before table caching. The
  original isolated cold path was approximately 1.7 seconds before active-run
  generator reuse.
- [x] Fusion-material reconstruction measured roughly 800 ms for each new
  fusion before optimization. The compact reverse index measured about 307 ms
  once and 0.3 ms for the next fusion while preserving deterministic mapping.
- [x] End-to-end active-run fusion lookup measured about 662 ms for the single
  cold index build and about 98 ms for the next previously unseen fusion.
- [x] The tracker Release build completed with zero warnings and zero errors.
- [x] All 41 tracker tests passed.
