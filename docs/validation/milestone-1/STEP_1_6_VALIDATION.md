# Step 1.6 validation

## Automated embedded-Ruby checks

- [x] The game starts without a script error.
- [x] Ironmon forces Hard Mode while leaving Hard AI behavior active.
- [x] Gameplay Options omits Difficulty and Battle type during Ironmon.
- [x] Gameplay Options omits Download data during Ironmon.
- [x] Optional Challenge Options omits Level caps, No reviving, and No heals
  (overworld) during Ironmon.
- [x] Direct attempts to alter any locked setting retain its forced value.
- [x] Level scaling maps 10 to 16 and caps 80 at 100.
- [x] Wild Pokemon objects are scaled exactly once.
- [x] Ordinary trainer construction suppresses the built-in 1.2 multiplier and
  applies the Ironmon 1.6 multiplier exactly once.
- [x] Dynamic, custom, rematch, double, and triple battle parties share the
  validated battle-boundary scaler and are scaled
  exactly once.
- [x] The default battle format is locked to 1v1, while an explicit double or
  triple encounter retains its requested format.
- [x] Loading an Ironmon save calls the validated locked-setting enforcement
  path.
- [x] Loading an older Ironmon save with sprite downloading enabled changes it
  to disabled, including the F7 checkpoint-loading path.
- [x] Direct assignments cannot re-enable sprite downloading while Ironmon is
  active.

The checks ran inside the game's embedded Ruby runtime against installed game
data. The temporary test hook was removed afterward. Representative results:
level 10 became 16, level 80 became 100, a stored level 5 trainer became level
8, the default battle setting was `[1, 1]`, and trainer AI skill was 100.

## In-game acceptance pass

- [x] Gameplay Options has no Difficulty or Battle type control.
- [x] Optional Challenge Options has no Level caps, No reviving, or No heals
  (overworld) control.
- [x] Wild Pokemon levels visibly follow the 1.6 multiplier.
- [x] Trainer Pokemon levels visibly follow the 1.6 multiplier without the
  built-in 1.2 multiplier stacking on top.
- [x] A base level 80 encounter is capped at level 100 (embedded-runtime check).
- [x] Trainer AI uses Hard Mode skill level 100 (embedded-runtime check).
- [x] The default format is 1v1, while natural or scripted double/triple battles
  retain their requested format.
- [x] Save/load and F7 restore all locked settings and retain 1.6 scaling.

Step 1.6 is complete. All automated and in-game acceptance checks passed.
