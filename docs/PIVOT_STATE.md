# Pivot state persistence

Milestone 2 stores its run-specific data in one `Ironmon::PivotState` object on
`PokemonGlobalMetadata`. Normal save serialization therefore preserves the
whole pivot transaction without relying on runtime-only variables.

## State contents

- `pending_pivot` holds at most one transaction and always has a stable
  acquisition identifier plus the `:pending` status.
- `completed_acquisition_ids` prevents a resolved acquisition from being
  reopened after loading.
- `fusion_mappings` stores deterministic unordered-pair results.
- `discovered_fusion_mappings` records which mapped results the player may see.
- `next_acquisition_sequence` produces acquisition identifiers namespaced by
  the current run seed.
- `quarantined_pokemon` safely removes extra usable Pokemon found in legacy
  parties without sending them to ordinary PC storage or deleting save data.
- `excluded_acquisition_log` records excluded Egg and temporary-Pokemon paths
  for later coverage audits.

The transaction API refuses to start a second pending pivot, reopen a completed
identifier, or complete an identifier other than the pending one. Migration
also removes any legacy pending record already listed as completed.

Old saves have no pivot state. Their first Ironmon access creates a clean
schema-version-2 state. Schema-version-1 state migrates by adding empty
quarantine and exclusion collections. A new run and F7 both create a fresh
state after the new seed is assigned, while the separate configuration snapshot
preserves all three player settings.

## Pokemon markers

Pokemon objects have two serialized fields:

- `ironmon_fusion_origin`: `:player_created`, `:caught`, or
  `:processed_caught` for marked fusions.
- `ironmon_transformation_right`: `:caught_pivot` only while a caught fusion
  has its immediate pivot opportunity, otherwise `:none`.

Because these are ordinary Pokemon instance variables, party movement, box
storage, cloning, and save serialization retain them. Unmarked legacy Pokemon
have no transformation right and cannot accidentally gain one.

Temporary Pokemon may additionally carry `ironmon_party_exclusion`. These
marked instances and Eggs do not count toward the one-usable-Pokemon limit.
