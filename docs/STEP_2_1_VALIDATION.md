# Step 2.1 validation

## Embedded-Ruby checks

- [x] The game loads all Ironmon scripts without a syntax or startup error.
- [x] A schema-version-1 configuration migrates to schema version 2 while
  preserving both fusion policies and defaulting unfusion to Random Component.
- [x] Configuration and pivot state survive Ruby save serialization.
- [x] Stable acquisition identifiers are unique within a run.
- [x] A serialized pending pivot remains pending and can be completed once.
- [x] Completed identifiers cannot be reopened or confused with pending state.
- [x] Caught-fusion origin and transformation-right markers survive Pokemon
  serialization, and processing removes the transformation right.
- [x] Starting a new generated run replaces the pivot state and resets its
  acquisition sequence.

The automated check ran inside Infinite Fusion's embedded Ruby runtime. It
exercised the same `Marshal` serialization used by save-backed game objects.
The temporary runtime hook was removed after the pass.

## Source-path checks

- [x] The configuration screen exposes Random Component and Player Choice and
  includes the selection in its final confirmation.
- [x] Configuration snapshots include the unfusion setting, so the existing F7
  restore path retains it.
- [x] Run generation assigns the new seed before creating a clean pivot state,
  so acquisition identifiers and later deterministic choices are run-scoped.
- [x] Diagnostic records include the selected unfusion setting.
- [x] Canonical source, copy-ready distribution, and local installation match.

Step 2.1 is complete. Acquisition interception and the one-Pokemon party rule
begin in Step 2.2.
