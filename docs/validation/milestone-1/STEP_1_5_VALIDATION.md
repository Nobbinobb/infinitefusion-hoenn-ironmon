# Step 1.5 validation

## Automated embedded-Ruby checks

- [x] The game starts without a script error.
- [x] The normal pool contains all 576 valid base species.
- [x] The custom-only policy selects exclusively from 176,556 validated custom
  fusions.
- [x] The normal-only policy never selects a fusion.
- [x] Mixed produces both categories and chooses the category before species.
- [x] Identical seeds and inputs reproduce identical mappings regardless of
  generation order.
- [x] Wild and trainer namespaces produce independent mappings.
- [x] Repeated inputs retain their mapping after the in-memory cache is reset.
- [x] All 137 trainer party records receive policy-valid generated species.
- [x] Dynamic custom/rematch parties are mapped from clones at the battle
  boundary, including story-created fusions.
- [x] Wally's fused-gift check is redirected only for Ironmon; other modes keep
  the original rejection behavior.
- [x] Fused NPC inputs flatten to a valid two-base story fusion before the
  trainer policy is applied in battle.
- [x] Static and dynamic overworld encounters share the mapped setup path, and
  their Pokemon objects are not mapped again at battle time.
- [x] Existing Step 1.4 metadata falls back to its saved legacy mapping.

The checks ran inside the game's embedded Ruby runtime against the installed
game data. The temporary test hook was removed afterward.

## In-game acceptance pass

- [x] Wild encounters and starters follow the selected wild policy.
- [x] Dynamically spawned visible overworld encounters follow the wild policy
  in both their map sprite and battle species.
- [x] A previously seen fusion uses its colored body overworld sprite instead
  of the fusion silhouette; unseen fusions remain silhouettes.
- [x] Trainer parties and resolved placeholders follow the trainer policy.
- [x] Wild and trainer policies visibly operate independently.
- [x] The scripted opening encounter follows the wild policy.
- [x] Wally accepts a fused gift in Custom Fusions Only and completes the
  required gift/fusion sequence.
- [x] Saving/loading retains mappings, and F7 changes them while retaining both
  policies.

Step 1.5 is complete. All automated and in-game acceptance checks passed.

## Schema version 2 follow-up

- [ ] Two encounter-table slots containing the same source species receive
  independent results, while each slot remains stable across revisits and
  save/load.
- [ ] Two trainers containing the same source species receive independent
  results, and duplicate source species in separate party slots do as well.
- [ ] Static, gift, starter, custom-trainer, and rematch slots remain stable.
- [ ] Schema-version-1 saves retain their old mappings until F7 creates a
  schema-version-2 run.

Partial acceptance used Custom Fusions Only for wild Pokemon and Normal Only
for trainers. The rival was normal, all starters and ordinary wild encounters
were custom-sprite fusions, and the two policies visibly differed. Dynamically
spawned visible overworld encounters exposed a missing subclass hook; that hook
has been corrected and passed its in-game retest. That retest also exposed the
base game's permanent fusion-silhouette behavior and an ordinary-trainer policy
bypass. Both boundary fixes passed their in-game checks: ordinary trainers
contained only normal Pokemon under Normal Only, and previously seen overworld
fusions appeared using their colored body material. Wally accepted a fused
Pokemon as his gift and completed the required fusion correctly by selecting
the appropriate base component from it. The scripted opening encounter was
fused under Custom Fusions Only, and save/load plus F7 reset behavior preserved
both configured policies and produced the expected persistent or regenerated
mappings.
