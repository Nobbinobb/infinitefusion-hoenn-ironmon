# Ironmon configuration

Ironmon stores an `Ironmon::Configuration` object in `PokemonGlobalMetadata`.
It is serialized automatically with the rest of the save data.

## Schema version 1

The wild and trainer settings use separate stable identifiers:

- `:mixed`
- `:custom_fusions_only`
- `:normal_only`

Both settings default to `:mixed`. Invalid or missing values fall back to their
documented defaults rather than preventing a save from loading.

Proof-of-concept saves do not contain an `ironmon_configuration` value. The
first Ironmon activity after loading creates a schema-version-1 configuration
with both defaults. Hash-shaped development values are also migrated.

Before an F7 reset, Ironmon snapshots the current configuration. It restores
that snapshot after loading the pre-starter checkpoint and before generating
the new run mappings. The new run receives a different seed while retaining the
same policy identifiers.
