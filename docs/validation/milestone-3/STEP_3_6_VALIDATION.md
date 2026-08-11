# Step 3.6 milestone integration and regression validation

Status: **Complete**

Validated against release `0.6.4` in Pokemon Infinite Fusion 2's bundled game
runtime.

## Cumulative boundary

Step 3.6 combines the four Milestone 3 generators without changing their
schemas or deterministic rules. The immediately preceding Step 3.5 audit
remains the exhaustive fusion record: 174,348 custom-sprite fusion identities
and 574 complete-fusion Body/Head evolution mappings passed. This pass adds the
eligible normal catalogue, repeated reseeds, reconstruction, inspection, and
diagnostic coverage needed to close the milestone.

## Embedded-runtime matrix

- [x] Prepared ability, base-stat, evolution, and move-access randomization
  together for three fixed run seeds.
- [x] Validated all 576 eligible normal species for registered unique ability
  slots, six bounded integer stats with preserved BST, valid unique move-access
  channels, and the generated evolution graph.
- [x] Validated all 287 normal conceptual evolution branches together.
- [x] Cleared every seed-scoped generator and tracker lookup cache and reproduced
  the inspected data exactly for each seed.
- [x] Marshalled and reloaded run metadata and Pokemon data, rebuilt the caches,
  and reproduced the same results for each seed.
- [x] Confirmed successive seeds changed the sampled aggregate generated data.
- [x] Verified live Debug Pokemon overview, ability, and stat inspection for a
  normal Pokemon and fusion `B1H1`.
- [x] Verified active generated lookup overview, abilities, stats, moves, and
  evolutions for a normal Pokemon and fusion `B1H1`.
- [x] Verified structured diagnostics include runtime/configuration plus species,
  ability, base-stat, evolution, move-access, and player-fusion recipes.
- [x] Verified the diagnostic log line identifies Ironmon 0.6.4 and all four
  Pokemon-data generator versions.

The embedded pass completed in 2.839 seconds. The temporary validator and report
were removed afterward.

## Compatibility, exclusions, and exceptions

- Generator schemas and rules are unchanged from 0.6.3, so current compatible
  saves reconstruct their existing results.
- Runs without evolution-generator metadata retain native evolutions by design;
  current generated evolutions begin on a new run or F7 reset.
- Triple and special fusions are deliberately excluded from the standard
  displayed-body/head randomizers and retain native behavior.
- Live Pokemon inspection is debug-authorized and requires an active run.
  Completed-run generated lookup reconstructs results from the recorded recipe.
- Current moves and learned-move history are instance-owned and therefore are
  not expected to match species-owned access lists after transformations.

## Result

All Step 3.6 acceptance criteria pass in the authoritative bundled runtime.
Milestone 3 is complete.
