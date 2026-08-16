# Ironmon 0.7.6

Ironmon 0.7.6 turns the Player view's healing summary into a complete
battle-item inventory while preserving its existing heal count and potential
HP percentage.

## Tracker battle items

- Click **Heals** to review every carried item that the game marks usable in
  battle.
- Items are grouped into Heals, PP heal, Status, Battle stats, and Other.
- Each stack shows its localized name, description, and current quantity.
- Single-move PP items provide an explicit move choice; all-PP items use one
  ordinary Use action.
- During battle, tracker item selection is enabled from the command menu, Fight
  move selection, the native Bag item list or item prompt, and the Pokémon party
  list or party prompt. The open submenu closes cleanly without choosing its
  highlighted move, item, or party member. The tracker item then consumes the
  normal turn and uses the game's native eligibility, targeting, consumption,
  and effect handling.
- Stale, duplicate, mistimed, missing-item, and invalid-move requests are
  rejected without consuming anything.

## Compatibility

Ironmon 0.7.6 targets Pokemon Infinite Fusion 2 version 6.8.0. Existing 0.7
saves remain compatible. The tracker and game scripts should be updated
together because battle-item inventory and use extend the local protocol.
