# Ironmon 0.8.3

Ironmon 0.8.3 adds live defensive overviews, brings derived wild fusions and
encounter lookup into agreement, and improves early Repel access and sprite
downloads.

## Defensive overview

- Click a Pokemon's type text on the Player or Enemy card to view its defenses.
  Combined damage factors account for known abilities, items, field effects,
  and defensive states; neutral results are omitted.
- Show compact protection labels, with affected moves available on click and
  no source names or explanatory panels.
- Show passive recovery and currently active healing states. Wish appears only
  after use, follows its countdown, and disappears when consumed. Knowing an
  immediate healing move such as Milk Drink does not add a defensive state.
- Keep recovery outcomes collapsed behind their trigger, preserve independent
  stacked heals, and apply native healing blockers and exceptions.
- Include compact survival, recoil/contact protection, hazard avoidance,
  active guards and substitutes, and conditional defenses.
- Filter enemy information in the game before transmission. Unrevealed
  abilities, held items, HP, and status do not become inferred tracker facts.
- Generate the informational defense catalog during release builds. Older data
  remains usable after game updates; an outdated catalog does not block play.
- Identify fusions in the existing small card heading without taking sprite
  space or adding a marker beside the types.

## Wild fusions and lookup

- Preserve each overworld spawn's original encounter context and keep derived
  fusions consistent with the deterministic player-fusion mapper.
- For standard encounters with an eligible normal first result, use mutually
  exclusive 10% same-table and 5% cross-environment fusion rolls. Eligible pairs
  of normal overworld Pokemon use a 36% fusion roll. Failed partner searches
  remain ordinary encounters rather than falling back to another category.
- Group lookup by encounter environment, track derived fusion progress, and
  use the current encounter mode without revealing concealed species.
- Prepare shared run lookup data in the background, with a Calculate lookup
  data action to prioritize it. Bound fusion page work and reuse tracker mapping
  for location searches.
- Repair blocked strength pairings through deterministic chains while retaining
  strength and component constraints. Failed preparation remains a terminal
  error rather than publishing a partial mapping or repeatedly retrying it.

## Early-game and sprite downloads

- Sell basic Repels at the main Petalburg counter and later towns before the
  base game's two-badge unlock. Oldale and stronger Repels retain their existing
  unlocks; generated item pools are unchanged.
- Remember confirmed sprite-sheet HTTP 404 responses per installation and URL.
  Normal downloads skip them without counting them as installed. Other failures
  remain retryable, and Recheck unavailable files explicitly retries the 404s.
- Add dedicated desktop component tests to the release gate, along with defense
  and seeded encounter-lookup runtime validation.

## Compatibility

Ironmon 0.8.3 targets Pokemon Infinite Fusion 2 version 6.8.2. Existing saves,
seeded-run tokens, completed-run recipes, and tracker persistence remain
supported when their recorded generator metadata matches the installed data.
Install game scripts and tracker from the same 0.8.3 archive. Both Windows x64
packages contain the same features; the standard package includes .NET, while
the smaller runtime-required package needs the Windows x64 .NET 10 Runtime.
