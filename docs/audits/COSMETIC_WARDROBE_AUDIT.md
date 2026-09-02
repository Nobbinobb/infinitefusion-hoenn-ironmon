# Cosmetic wardrobe audit

`tools/generation/Generate-Cosmetic-Audit.ps1` runs in the bundled game runtime
and writes `docs/audits/generated/COSMETICS_GENERATED.json`. The release builder
regenerates it and compares the previous canonical report. Runtime gameplay
never depends on that report: the wardrobe derives its catalog from the
installed game's metadata and graphic files whenever it opens.

The generator and runtime share `src/cosmetics/Catalog.rb`, including the price
bands and zero/missing-price fallbacks. The report tracks stable category/ID
keys, original names/descriptions, authors, acquisition metadata, point prices,
hair color variants, required movement/trainer sprites, asset fingerprints,
unavailable entries, orphaned asset directories, malformed rows, and changes.
Its totals distinguish authored entries from presently usable entries.

Release generation must complete and produce a valid report. Individual
missing assets, new/removed IDs, malformed optional metadata, or price drift
are reported rather than hard-coded as expected counts. Existing ownership
survives missing entries. No audit fingerprint participates in seeded-run
compatibility, and no personal profile is shipped in the distribution.

## Behavior boundaries

- One initial confirmed selection per shared profile, not per game/save/seed.
- Preview is detached from the live player; only final selected pieces are free.
- Owned outfits, hairstyles, and two accessory layers can be mixed freely.
- Hairstyle variants, skin tones, and dyes are controls, not extra purchases.
- Trainer victories pay once per actual battle; badges and Hall of Fame pay
  once per attempt. Reward identities distinguish repeated imports of one seed.
- Points/ownership are shared; confirmed appearance is slot-specific and is
  restored separately from the gameplay checkpoint.
- The profile uses checksummed alternating snapshots and a cross-process lock.
  Corruption fails closed for spending/claiming, not for starting gameplay.
- Nurse healing, type-outfit item rewards, team disguise benefits, breeder egg
  acceleration, and Zoroark/trumpet encounter effects are disabled only in
  Ironmon. Other modes keep the base game behavior.

## Validation

`tools/Test-Cosmetics.ps1` synchronizes the distribution and launches the hidden
game runtime. All profile writes are directed to isolated temporary test
directories within `data/cosmetics-validation`; it never reads or changes the
player's shared profile. It exercises price boundaries, ownership, purchases,
duplicate rewards, corruption handling, slot appearance restoration, isolation
from ordinary game unlock inventories, the actual bedroom map patch, and the
real wardrobe renderer in portrait, four-direction, and bicycle modes. Screenshots and the result
summary remain in that ignored validation directory for visual inspection.
The full `Test-GameRuntime.ps1` suite includes this focused pass.

Manual checks before shipping: complete the bedroom intro with no hat; claim
once then start another game; preview/cancel and purchase/cancel; change both
accessories and colors; reset with an already selected starter; switch save
slots; import a seed; verify trainer/badge awards across save reloads; and check
that Classic Mode's original wardrobe and outfit effects still behave normally.
