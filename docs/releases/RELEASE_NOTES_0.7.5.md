# Ironmon 0.7.5

Ironmon 0.7.5 reorganizes the tracker around four permanent primary views and
adds aggregate attacking-type coverage for the complete eligible trainer
populations.

## Tracker navigation

- Keeps Player, Enemy, Lookup, and Archive visible as the four primary views.
- Makes Lookup active-run only, with Trainers, Encounters, Items, and Type
  Coverage tabs.
- Moves completed-run selection, world and Pokemon lookup, run statistics,
  move-access analysis, and evolution analysis into Archive.
- Reuses the same world-lookup component for active and archived runs while
  preserving their distinct disclosure and reconstruction rules.
- Moves diagnostic tools beside the always-available Diagnostic Access screen.
  Ctrl+4 and the matching controller shortcut now open Archive.

## Type coverage

- Shows every standard attacking type. Every selected type uses its type color,
  while a separate marker identifies types available from the current Pokemon's
  Physical or Special moves.
- Groups the complete eligible defensive populations into 0x, 1/4x, 1/2x, 1x,
  2x, and 4x according to the best selected attacking type.
- Shows only each bucket's current count and percentage, without a denominator.
- Normal Only and Custom Fusions Only use their corresponding complete pools.
  Mixed shows the raw combined count but weights percentages as 50% of the
  Normal pool percentage plus 50% of the Fusion pool percentage.
- Retains manual choices while the same player Pokemon refreshes, provides a
  Current Moves reset, and resets the selection when the player Pokemon changes.

## Information boundary and compatibility

- Coverage uses a release-precalculated aggregate dataset containing only
  defensive type profiles and separate Normal/Fusion counts.
- The active run supplies only its trainer policy and pool compatibility
  metadata. No coverage request contains a run seed, generated mapping, trainer
  slot, future roster, or individual species identity.
- A mismatched or older game disables only Type Coverage with a clear status;
  its other compatible tracker features remain available.

## Catch assistance

- Makes low native catch rates less punishing in ordinary Ironmon wild battles,
  with a diminishing bonus as the native catch rate improves.
- Scales the bonus with missing HP and reaches its full effect at half HP,
  preserving the value of weakening a wild Pokemon without requiring players to
  risk reducing it to critical health.
- Leaves Safari encounters and explicit scripted catch-rate calculations
  unchanged.
- Shows the current ordinary Poke Ball success chance on the Enemy page during
  wild battles, including HP, status, and caught-off-guard modifiers.

## Tracker interface refinements

- Keeps the diagnostic Access and Tools selector compact regardless of the
  selected page or available diagnostic capabilities.

## Release data

- The release pipeline regenerates both `area_catalog.dat` and the aggregate
  type-coverage dataset in Infinite Fusion's bundled runtime before publishing.
- Failed, missing, invalid, or stale generation aborts the release. The package
  verifies the fresh area catalog and exact embedded coverage resource.
- Player archives exclude release-data generators, audits, temporary runtime
  hooks, diagnostic-token generators, development scripts, and private keys.
