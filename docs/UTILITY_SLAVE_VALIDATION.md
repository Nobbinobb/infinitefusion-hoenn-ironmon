# Utility slave validation

## Pivot and party checks

- [x] Slave is offered for normal and fused candidates whenever one current
  usable Pokemon exists.
- [x] Slave is not offered without a current usable Pokemon, preventing a
  non-combat-only party.
- [x] Committing Slave preserves the current Pokemon, marks a cloned candidate,
  clears the pending pivot, and leaves exactly one usable Pokemon.
- [x] Choosing Slave again replaces and permanently discards the previous
  utility Pokemon rather than accumulating reserve party members.
- [x] The utility marker survives normal save serialization.

## Allowed uses and restrictions

- [x] Hidden-move lookup can find a move known by the utility Pokemon.
- [x] Wally's required gift accepts the utility Pokemon and rejects the current
  battler while Ironmon is active.
- [x] Original Wally gift behavior is unchanged outside Ironmon.
- [x] Able-party and able-Pokemon counts exclude the utility Pokemon.
- [x] Last-usable-Pokemon removal checks cannot count the utility Pokemon as a
  surviving battler.
- [x] The real battle constructor preserves party indexes but replaces the
  utility entry with an empty slot, making it unavailable for battle or
  switching.
- [x] Pickup and Honey Gather ignore the utility Pokemon.

The matrix ran inside Infinite Fusion's embedded Ruby runtime after normal game
data initialization. The temporary runtime hook and output were removed after
the clean pass.
