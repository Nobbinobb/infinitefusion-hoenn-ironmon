# Ironmon 0.7.3

Ironmon 0.7.3 refines the early run and adds live battle-stage and starter-filtering support.

- Fully heals the selected starter after winning the Route 101 rescue encounter.
- Removes the default PC Potion in Ironmon and grants its deterministic randomized replacement after the successful starter rescue encounter instead.
- Fully heals the active Pokemon after the first rival victory, once per run.
- Replaces the Oldale Route 102 handoff's DNA Splicers with one Poke Ball during Ironmon runs and updates its dialogue.
- Shows live Attack, Defense, Special Attack, Special Defense, and Speed stages for player and enemy Pokemon, including multiplier tooltips.
- Adds a persisted inclusive maximum-BST ceiling for starter autoselect.
- Filters Random Pick and Favorite Clause candidates through that ceiling and automatically abandons and resets attempts with no eligible starter.
- Adds an edge-triggered LT + RT + Start controller shortcut that invokes the existing F7 confirmation and checkpoint-reset flow while the connected game is foreground.
- Fixes the fusion move picker so its source names and icons show the two actual randomized Pokemon instead of the mapped fusion's unrelated component species.
- Constrains fusion-gamble results around the generated normal-fusion strength and lets every type contributed by either consumed Pokemon qualify the randomized result.
- Fixes archived trainer, encounter, and item lookup by retaining version-2 fusion reconstruction, preserving exact future item shuffles, and preferring tracker-owned historical area snapshots.
