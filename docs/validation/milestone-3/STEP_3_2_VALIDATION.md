# Step 3.2 validation

## Embedded-runtime generator pass

The automated catalogue suite ran after normal game-data initialization inside
Pokemon Infinite Fusion 2's embedded Ruby runtime.

- [x] All 582 eligible normal-species and mechanical-form source identities
  preserved their exact original BST.
- [x] Every generated normal stat was between 5 and 255 inclusive.
- [x] Repeated resolution returned the same vector for every source identity.
- [x] A second seed produced at least one different generated vector.
- [x] The source-stat fingerprint was `1e25ae036f296389`.
- [x] Two component values of 5 produced a standard fusion value of 4 through
  the game's separately floored formula.
- [x] A real standard fusion matched the result calculated from its generated
  displayed body and head components.
- [x] Shedinja received generated base HP above 1.
- [x] The game remained running after the validation suite completed.
- [x] The temporary runtime validator and its output file were removed.

## Tracker pass

- [x] All 33 tracker unit tests pass.
- [x] The Windows tracker application builds with no warnings or errors.
- [x] The protocol supports nullable base-stat generator metadata so archived
  pre-Step-3.2 recipes remain readable.
- [x] Debug inspection carries original and generated final stat vectors, BST
  values, and generator metadata.
- [x] Post-run lookup reconstructs the final original and generated vectors
  from the compact run recipe without a persisted mapping.
- [x] The Pokemon inspector's Stats subtab labels head/body dominance for
  fusions without embedding
  either component's stat table.
- [x] Debug and post-run stat displays provide table, generated-bar, and
  delta-colored-bar modes.
- [x] Completed lost or won recipes are no longer blocked merely because the
  loaded save contains another active run.
- [x] Losses and draws complete the tracker run even when the base game marks
  the encounter as safe to lose.

## Cumulative follow-up validation

- [x] Start a new Ironmon run and confirm the starter choices use generated
  base stats in battle and in the tracker.
- [x] Save and reload the run, then confirm the same species vectors remain.
- [x] Use F7 and confirm at least one inspected species receives a different
  vector under the new seed.
- [x] Confirm a Pokemon with Wonder Guard has 1 actual maximum HP, while
  Shedinja without Wonder Guard uses its generated HP normally.
- [x] Inspect both a normal Pokemon and a fusion on the tracker Stats subtab and
  confirm the layout is readable at the normal window size.
- [x] Complete or fail a test run and confirm post-run lookup agrees with the
  active-run values.

The Step 3.5 cumulative embedded-runtime audit closed save/load, F7, normal and
fusion generated-stat consistency, and direct-versus-pivot fusion ownership.
The Wonder Guard behavior was already exercised in the Step 3.1 contextual-
eligibility runtime pass. The later shared Pokemon-information and 0.6.2
tracker validation passes exercised normal/fusion Stats presentation and
active/completed reconstruction through the shared runtime APIs.

## Release packaging

- [x] Canonical source, distribution, and installed game copies contain the
  same 26 Ruby files byte-for-byte.
- [x] The synchronized 0.4.0 scripts loaded successfully in Infinite Fusion's
  embedded runtime and the exact smoke-test process was stopped afterward.
- [x] The self-contained tracker builds without warnings and all 33 automated
  tests pass.
- [x] The release archive contains no temporary validation files.
- [x] Two consecutive `Ironmon-v0.4.0-base-stats.zip` builds produced SHA-256
  `8804b74a63193d27aadef343fd5ad1b16d0cfa5a3db41f4304dd2538acc77acf`.
