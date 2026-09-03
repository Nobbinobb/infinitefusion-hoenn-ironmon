# Ironmon 0.8.5

Ironmon 0.8.5 improves wardrobe access, makes temporary transformed battle
state accurate in the tracker, aligns derived wild fusions with the encounters
that can actually coexist in the overworld, and refreshes the mechanics manual.

## Wardrobe

- Permanently unlock both male and female default outfits for every profile,
  including existing profiles loaded after the update.
- Add an explicit All items or Obtained only control to every normal wardrobe
  category browser while keeping locked pieces available for preview and
  purchase in the complete view.
- Preserve shared points and permanent ownership while keeping confirmed
  equipped appearance specific to each save slot.

## Imposter and Transform tracking

- Project the copied visible species, sprite, fusion identity, ability, types,
  base-stat total, non-HP battle stats, stat stages, and temporary 5-PP move set
  onto the live Player card after Imposter or Transform activates.
- Keep nickname, level, HP, status, gender, held item, nature, learnset,
  evolutions, and PP-restoring item targets attached to the original Pokemon.
- Continue showing only the original nature in its chip and suppress nature
  stat highlights while the displayed battle stats are copied.
- Keep persistent move and ability knowledge under the species that owns it,
  while recording the copied ability for the transformed species.
- Clear the temporary projection when the Pokemon leaves its transformed state.

## Derived wild fusions

- Restrict the 10% same-table and 5% cross-table standard fusion rolls to
  grass, cave, and surf-water encounters that can participate in visible
  overworld encounters.
- Exclude fishing and special encounters from both two-source rolls. Their
  authored rows may still map directly to a fusion when the selected wild
  policy permits fused results.
- Suppress Infinite Fusion's separate single-encounter random fusion roll
  during active Ironmon runs. With visible overworld encounters enabled,
  ordinary derived fusions now require two eligible visible participants;
  explicit forced story fusions remain supported.
- Apply the same eligibility boundary to tracker area totals, concealed derived
  rows, occurrence lookup, and obtainability material indexes.

## Mechanics reference

- Reorganize the mechanics manual with a descriptive contents index, clearer
  ownership overview, corrected wardrobe and attempt-ledger hierarchy, and
  improved navigation for screen and print layouts.
- Add captions and accessible headers to every table, convert attempt results
  and item weights into tables, and state the exact fusion-stat formula.
- Break dense tracker, area-lookup, evolution-graph, and obtainability sections
  into focused subsections while preserving their implemented behavior.
- Clarify the fishing boundary and the distinction between policy-mapped
  fusions and derived two-source encounters.

## Compatibility

Ironmon 0.8.5 targets Pokemon Infinite Fusion 2 version 6.8.2. Install the game
scripts and tracker from the same 0.8.5 archive. Both Windows x64 packages
contain the same features; the standard package includes .NET, while the
smaller runtime-required package needs the Windows x64 .NET 10 Runtime.
