# Step 1.3 validation

## Automated checks

- [x] Canonical source, distribution, and installed runtime copies match.
- [x] The game starts without a script error.
- [x] Configuration edits remain local until final confirmation.
- [x] Confirmation returns one complete configuration to the mode selector.
- [x] F7 reset code does not invoke the configuration screen.

The configuration-screen checks ran in the game's embedded Ruby runtime. They
simulated all nine policy combinations as well as each Back path.

## In-game acceptance pass

- [x] All nine wild/trainer policy combinations can be selected.
- [x] Back from a policy chooser leaves that policy unchanged.
- [x] Back from the main configuration screen discards all draft changes.
- [x] Back from the final summary returns to configuration without generation.
- [x] Final confirmation generates the run exactly once.
- [x] F7 resets directly to starter selection without showing configuration.

Step 1.3 passed its acceptance criteria on 2026-08-02. All nine combinations
were exercised by the embedded-runtime test; the remaining screen behavior was
confirmed in-game.
