# Ironmon 0.6.5

This cumulative release completes Milestone 5 with a game-owned challenge
lifecycle, enforceable failed runs, authoritative attempt statistics, and
completed-run tracker presentation.

## Attempts and failure handling

- Numbers every generated attempt per save slot and records `lost`, `won`, or
  `abandoned` exactly once.
- Counts only active game time and preserves the ledger through saves,
  checkpoint restoration, manual F7, and automatic reset.
- Treats battle losses and draws as failure even when the base game allows
  continuation, then blocks movement, interaction, new battles, and Pokemon
  acquisition while retaining F7 and tracker access.
- Adds an optional pre-run automatic reset setting, disabled by default. It
  begins only after native battle cleanup and leaves the run locked with an
  actionable error if checkpoint reset cannot start.

## Statistics and tracker

- Records battles, highest player level, badges, Bag and consumed held items,
  actual and wasted item healing, named trainer species frequency, and the average,
  lowest, and highest generated BST of defeated trainer Pokemon.
- Preserves held-item ownership through battle transfers so a player resource
  counts exactly once even if it is stolen before consumption.
- Archives versioned statistics with the completed recipe and displays summary,
  item-source, trainer-frequency, BST, and save-history sections in Lookup.
- Automatically selects each new completion in Lookup, including reconnect
  recovery after immediate automatic reset. Duplicate recovery remains
  idempotent, and historical recipes explicitly show unavailable statistics.
- Opens Enemy for the first encounter after completed-run Lookup navigation,
  then returns to Player when the player's move menu opens as usual.

## Compatibility

- Existing saves migrate with automatic reset disabled and begin numbering on
  their next generated attempt.
- Existing completed recipes remain readable without fabricated statistics.
- Deterministic Pokemon generator schemas and rules remain unchanged from
  `0.6.4`.
- Targets Pokemon Infinite Fusion 2 version 6.8.0.
