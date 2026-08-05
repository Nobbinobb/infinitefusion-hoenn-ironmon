# Ironmon 0.2.2

This patch release replaces the permanent utility Pokemon with targeted story
progression support.

- The Slave pivot action and utility party slot have been removed.
- Mandatory NPC trades generate a short-lived outgoing Pokemon and preserve the
  run's current Pokemon. The received Pokemon still enters the forced pivot.
- Wally receives a generated normal Pokemon without requiring a second party
  member or a demonstration catch. This remains safe when the current Pokemon
  is fainted.
- Temporary progression Pokemon are removed after their story transaction and
  cannot become reserve party members.
- HM01 through HM10 rewards become the corresponding permanent field tools
  before item randomization.
- Existing Ironmon saves transactionally exchange owned HMs for field tools
  during loading.
- Legacy utility Pokemon are removed from the party and preserved in pivot
  quarantine during migration.

The release retains the deterministic custom-sprite fusion and reversal fixes
from 0.2.1 and targets Pokemon Infinite Fusion 2 version 6.8.0.
