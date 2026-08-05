# Step 1.2 validation

## Automated checks

- [x] Canonical source, distribution, and installed runtime copies match.
- [x] The game starts without a script error.
- [x] Missing and invalid configuration values resolve to Mixed defaults.
- [x] Wild and trainer policy identifiers are stored separately.

The configuration checks ran in the game's embedded Ruby runtime. They also
verified that separate policy values survive a metadata Marshal round trip.

## In-game acceptance pass

- [x] Configuration survives saving and loading.
- [x] F7 changes the run seed without changing either policy.
- [x] A proof-of-concept Ironmon save loads with Mixed defaults.

Step 1.2 passed its acceptance criteria on 2026-08-02. The temporary F6
acceptance helper was removed after validation.
