# Ironmon configuration

Ironmon stores an `Ironmon::Configuration` object in `PokemonGlobalMetadata`.
It is serialized automatically with the rest of the save data.

## Schema version 2

The wild and trainer settings use separate stable identifiers:

- `:mixed`
- `:custom_fusions_only`
- `:normal_only`

Both settings default to `:mixed`.

The caught-fusion unfusion setting uses two stable identifiers:

- `:random_component` (default)
- `:player_choice`

Random Component commits the unfusion and then reveals one seed-derived
component. Player Choice shows only the two component identities and allows one
to be selected without opening either summary.

Invalid or missing values fall back to their documented defaults rather than
preventing a save from loading.

Proof-of-concept saves do not contain an `ironmon_configuration` value. The
first Ironmon activity after loading creates a schema-version-2 configuration
with all defaults. Schema-version-1 objects and hash-shaped development values
are also migrated, with their existing wild and trainer policies preserved.

Before an F7 reset, Ironmon snapshots the current configuration. It restores
that snapshot after loading the pre-starter checkpoint and before generating
the new run mappings. The new run receives a different seed while retaining the
same policy and unfusion-setting identifiers. See
`../design/PIVOT_SYSTEM.md` for the complete pivot and determinism rules.

## Selection flow

The configuration screen keeps wild and trainer selections in a local draft.
Policy descriptions are shown while selecting each value. Back from the screen
discards the draft; only confirming the final summary stores the configuration
and allows run generation to begin. F7 resets bypass this screen.
