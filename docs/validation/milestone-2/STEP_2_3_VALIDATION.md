# Step 2.3 validation

## Embedded-Ruby checks

- [x] No current Pokemon plus a normal candidate exposes only Take.
- [x] No current Pokemon plus a fusion exposes Take, Reverse and Take, and
  Unfuse and Take One.
- [x] A normal current Pokemon plus a normal candidate exposes Swap and Fuse.
- [x] A fused current Pokemon plus a normal candidate exposes only Swap.
- [x] Any current Pokemon plus a fusion exposes Swap, Swap and Reverse, Swap and
  Unfuse.
- [x] Swap, Fuse, Swap and Reverse, and Swap and Unfuse each finish with exactly
  one usable Pokemon and clear the pending acquisition.
- [x] Swap permanently removes the previous Pokemon from the party.
- [x] Pivot-created fusions are marked as player-created with no later
  transformation right.
- [x] Reversed caught fusions are marked as processed with no later
  transformation right.
- [x] Unfusion keeps one normal component and does not store the discarded
  component.

The checks ran inside Infinite Fusion's embedded Ruby runtime after normal game
data initialization. The temporary runtime hook and its output were removed
after the clean pass.

## Interface and transaction audit

- [x] The interface receives only the legal action labels and does not open a
  party, storage, fusion-preview, or summary screen.
- [x] Pressing the cancel button returns no action and immediately reopens the
  forced choice instead of selecting or abandoning an outcome.
- [x] The candidate remains only in save-backed pending state until a result is
  prepared successfully.
- [x] Every transformed result is built from clones; the current party remains
  unchanged during result preparation.
- [x] The final party replacement and pending-state completion share the Step
  2.2 rollback boundary.
- [x] Failures preserve the current Pokemon, report the error, and return to the
  same legal choices.
- [x] The canonical source, copy-ready distribution, and local installation
  match.

Step 2.3 uses the normal input orientation as a temporary fusion result and the
body as a temporary unfusion component so this intermediate build is playable.
Step 2.4 replaces fusion species selection with the deterministic custom-sprite
mapping. Step 2.5 replaces caught-fusion component selection with the configured
seeded-random or player-choice behavior.
