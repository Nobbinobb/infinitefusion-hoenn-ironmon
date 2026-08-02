# Step 1.8 validation

## Automated embedded-Ruby regression

- [x] The game starts without a script error.
- [x] All nine wild/trainer fusion-policy combinations generate successfully.
- [x] Normal Only contains no fusions; Custom Fusions Only contains only
  validated custom-sprite fusions; Mixed contains both categories.
- [x] Wild and trainer namespaces remain independent in every combination.
- [x] Repeated inputs and serialized save metadata retain their mappings.
- [x] Repeated reset generation changes the seed and mappings while retaining
  both configured policies.
- [x] Representative wild, visible-overworld, gift, starter, trainer,
  placeholder, dynamic/rematch, and Gym Leader paths obey their policy.
- [x] Level scaling remains exactly 1.6 with a level-100 cap and Hard AI.
- [x] Gym Leaders expand to six; ordinary trainers remain unchanged.
- [x] Diagnostic lines contain version, context, seed, policies, pool metadata,
  and mapping counts.

The comprehensive regression ran inside the game's embedded Ruby runtime
against installed game data. All nine policy combinations passed against the
176,556-entry validated custom fusion pool. Representative encounter, gift,
starter, dynamic trainer, ordinary trainer, Gym Leader, serialization, repeated
reset, difficulty, and diagnostic checks passed in 7.48 seconds. The temporary
regression hook was removed afterward.

## Prior in-game coverage incorporated into this regression

- [x] The scripted opening encounter follows the wild policy.
- [x] Visible overworld encounters match their battle species and reveal their
  colored body material after discovery.
- [x] Wally accepts a policy-generated fused gift and completes his fusion.
- [x] Save/load and repeated F7 resets work without a startup/reset crash.
- [x] Trainer levels, locked challenge settings, and Gym Leader expansion match
  the agreed rules.

## Final Milestone 1 in-game pass

- [x] Start a fresh run and confirm the selected policies appear in
  `Ironmon.log` with a nonzero seed and mapping counts.
- [x] Complete the opening, one wild encounter, one ordinary trainer, and the
  first Gym Leader without a script error.
- [x] Save, reload, and use F7 once; confirm the run restarts at starter
  selection with a different seed in `Ironmon.log`.
- [x] Confirm the copy-ready archive installs into a clean game directory using
  `docs/INSTALLATION.md`.
- [x] A fresh Ironmon selection skips the base game's redundant difficulty
  question and enters the run with Hard Mode enforced.

The release archive was extracted into an empty verification directory. It
contained `README.md`, `INSTALLATION.md`, and only the expected Ruby files under
`Data/Scripts/997_Ironmon`; every installed Ruby hash matched canonical source.
The installation guide in the archive also matched the repository copy.

The live diagnostic log recorded a new run with seed `1468156362`, a subsequent
load with the same seed, and a successful F7 reset with seed `1926154748` while
retaining both Mixed policies, the 176,556-entry custom pool, and populated wild
and trainer mappings.

Step 1.8 and Milestone 1 are complete. Automated regression, clean-package
verification, diagnostic load/reset records, and the final in-game acceptance
pass all succeeded.
