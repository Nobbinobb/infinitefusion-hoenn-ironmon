# Ironmon 0.4.0

This release completes Milestone 3 Step 3.2 with deterministic base-stat
randomization and expanded tracker inspection.

## Base-stat randomization

- Preserves each eligible normal Pokemon or mechanical form's original BST.
- Redistributes that total deterministically within the inclusive 5-255 range.
- Generates assignments on demand without storing per-species mappings.
- Treats Pumpkaboo and Gourgeist sizes as distinct mechanical stat identities.
- Gives Shedinja normal generated base HP while Wonder Guard remains
  responsible for actual 1-HP behavior.
- Keeps pre-0.4.0 runs on their original stats when generator metadata is
  absent.

## Fusion stats

- Calculates standard fusion stats from the generated stats of the displayed
  body and head.
- Uses Infinite Fusion's exact separately floored dominant-component formula.
- Does not clamp or rescale final fusion values, allowing an effective minimum
  of 4 and a final BST that differs from the original fusion BST.
- Leaves special and triple fusions outside the standard range unchanged.

## Tracker presentation

- Groups Overview, Abilities, and Stats beneath one Pokemon Debug page.
- Adds original/generated stat comparison, generated bars, and delta-segment
  bars to Debug inspection and completed-run lookup.
- Reconstructs generated post-run stats from compact run metadata without a
  stored mapping.
- Clearly identifies archived runs that predate base-stat randomization.
- Allows completed historical lookup while another run or save is loaded.
- Records safe-to-lose rival battles as Ironmon losses even when the base game
  does not black out the player.

This cumulative release retains all previous Ironmon functionality and targets
Pokemon Infinite Fusion 2 version 6.8.0.
