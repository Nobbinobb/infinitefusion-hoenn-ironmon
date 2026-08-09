# Ironmon 0.6.0

This release completes Milestone 3 Step 3.4 with deterministic normal and
complete-fusion evolution randomization, authorized tracker inspection, and
completed-run evolution analysis.

## Evolution generation

- Preserves each native conceptual branch and activation opportunity while
  replacing its destination deterministically from the run seed.
- Selects strictly stronger targets using the original destination's inclusive
  90%-115% BST range and closest-valid fallback.
- Allows first stages to reach intermediate, final, or standalone Pokemon while
  preventing backward-stage and intermediate-to-intermediate edges.
- Excludes the source's native family and requires at least one shared source
  type.
- Replaces Eevee's Normal filter on stone branches with Water, Electric, Fire,
  Psychic, Dark, Grass, Ice, or Fairy as appropriate.

## Methods and runtime behavior

- Preserves reachable native level, friendship, held-item, and item methods.
- Converts location, region, party, move, Beauty, and trade requirements to the
  documented level or item alternatives.
- Resolves simultaneous eligible branches deterministically without changing a
  Pokemon's level or proportional experience progress.
- Preserves after-evolution effects, including the Nincada/Shedinja duplicate
  path, while preventing evolution from restoring transformation rights.
- Supports level-up, item, trade, forced, party-menu, and story-owned evolution
  consumers.

## Complete-fusion evolution

- Treats Head and Body branches independently but assigns every simultaneously
  available branch together.
- Replaces the entire displayed fusion with a stronger validated custom-sprite
  fusion rather than naturally evolving one component.
- Uses the evolving component's type, including Eevee's stone override, and
  excludes both source component families.
- Generates fusion mappings on demand without query-order dependence; reversed
  orientation remains an independent fusion.

## Tracker and analysis

- Adds authorized Debug and completed-run candidate lists with paging, name
  filtering, icons, names, and generated BSTs.
- Uses one tabbed Head/Body candidate area for fusions and hides unnecessary
  filters on short normal lists.
- Adds clickable one-step normal and fusion evolution graphs with effective
  method labels and Head/Body edge badges.
- Records offered, completed, cancelled, fallback, forced, fusion-side, BST, and
  duplicate evolution outcomes for local completed-run analysis.
- Keeps generated targets hidden throughout ordinary active play.

## Compatibility

- Runs created before 0.6.0 retain native evolutions unless a new run or F7
  reset records current evolution metadata.
- Exact development saves using evolution rules version 1 or 2 migrate to rules
  version 3 while preserving the seed and already evolved Pokemon.
- Retains all ability, base-stat, and move-access randomization from 0.5.0.

Ironmon 0.6.0 targets Pokemon Infinite Fusion 2 version 6.8.0.
