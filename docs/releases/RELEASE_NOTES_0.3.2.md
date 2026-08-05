# Ironmon 0.3.2

This save-compatible patch optimizes Milestone 3 Step 3.1 ability lookups.

- Replaces repeated full metadata validation in every ability read with a
  validated in-memory readiness flag established during run setup and load.
- Suspends randomized reads while a save is loading, then enables them only
  after the saved generator metadata has been validated.
- Caches complete fusion normal and hidden slot results per seed and displayed
  fusion identity.
- Reuses the universal candidate pool for ordinary species and caches the small
  augmented pools needed by contextual species.
- Resolves a Pokemon's active ability from one generated slot lookup instead of
  repeatedly entering the public species ability methods.

The generator schema, pool fingerprint, and deterministic assignments are
unchanged from 0.3.1, so existing 0.3.1 Ironmon runs remain compatible.

The release retains all previous functionality and targets Pokemon Infinite
Fusion 2 version 6.8.0.
