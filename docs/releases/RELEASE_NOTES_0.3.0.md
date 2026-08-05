# Ironmon 0.3.0

This cumulative release implements Milestone 3 Step 3.1.

- Normal and hidden ability slots receive deterministic, seed-specific
  assignments.
- Results are generated on demand without persistent species or fusion mapping
  tables.
- Each species retains its original number of defined normal and hidden slots.
- Within-species duplicate assignments are prevented.
- Mechanically species-bound abilities are excluded, while abilities that are
  merely powerful or detrimental remain eligible.
- Fusions derive their ability slots from the generated slots of their displayed
  body and head components.
- Trainer-forced and special hidden-ability acquisition paths cannot bypass
  generated species slots.
- Ability Capsule behavior uses the generated normal slots.
- Evolution preserves the current slot where possible and applies documented
  fallbacks when the evolved species lacks that slot.
- Temporary battle ability effects remain instance-local and do not change the
  generated assignment.
- The Pokemon debug menu includes an Ironmon ability inspector for validation.
- Diagnostics record the ability-generator version, pool size, and pool
  fingerprint.

The release retains the complete `0.2.3` pivot system and targets Pokemon
Infinite Fusion 2 version `6.8.0`.
