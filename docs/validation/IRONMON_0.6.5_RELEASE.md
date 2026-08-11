# Ironmon 0.6.5 release validation

## Package

- Archive: `release/Ironmon-v0.6.5-challenge-lifecycle.zip`
- SHA-256: `661fc320cc62e20f41a5f9fde6c03a2c746e55b4ebf04e73f54d2ad8fa3c147b`
- Size: 72,088,200 bytes
- Entries: 460

## Milestone 5 regression

- [x] Ordinary loss and draw decisions complete and lock the attempt, including
  the decision boundary used by safe-to-lose base-game battles.
- [x] Wins and manual-reset abandonment remain distinct and idempotent.
- [x] Attempt numbering, active duration, per-slot totals, save normalization,
  and checkpoint-ledger restoration remain exact across repeated attempts.
- [x] Missing checkpoints reject automatic reset with an actionable error;
  valid automatic reset scheduling occurs only once per failed attempt.
- [x] Manual failure lock remains active, shows its notice once, and rejects
  subsequent gameplay actions without changing completed identity.
- [x] Bag and consumed Held items, actual and wasted healing, progress, trainer
  encounter ties, and defeated-trainer BST count, average, minimum, and maximum
  retain exact values without duplicate callbacks.
- [x] Trainer-frequency entries preserve the authoritative display name beside
  each species identifier, including the most-encountered summary.
- [x] Completed statistics survive save normalization and recipe archival.
- [x] Immediate next-attempt state retains the completed recipe for tracker
  recovery during the automatic-reset navigation race.
- [x] The first new enemy releases completed-run Lookup navigation and opens
  Enemy; the following player move menu restores normal Player navigation.

The focused cumulative validator passed all 44 assertions inside Pokemon
Infinite Fusion's bundled runtime. The temporary validator and report were
removed before packaging.

An additional focused bundled-runtime validator passed all 9 trainer-name
assertions, covering fusion-style identifiers, normal species, duplicate
encounter suppression, most-encountered derivation, normalization, and in-place
migration of legacy statistics without names. Its temporary validator and
report were also removed.

## Build and startup validation

- [x] All 36 canonical, distribution, and installed Ruby scripts match by
  SHA-256.
- [x] Runtime and tracker metadata report 0.6.5; the tracker executable has
  file version `0.6.5.0`.
- [x] All 45 tracker tests pass in Release configuration.
- [x] The self-contained Windows tracker publishes with zero warnings and zero
  errors.
- [x] Two release builds produced the identical SHA-256 checksum.
- [x] The archive contains the runtime and tracker executable, with no PDB or
  temporary validation entries.
- [x] The packaged tracker and game both remained running through the
  concurrent ten-second hidden startup checkpoint.
- [x] Only the exact processes started by the smoke test were stopped; no game
  or tracker process was already running or remained afterward.
- [x] Repository whitespace validation passes.

## Result

Milestone 5 is complete. Ironmon 0.6.5 is ready for installation on Pokemon
Infinite Fusion 2 version 6.8.0.
