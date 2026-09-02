# Ironmon 0.8.4

Ironmon 0.8.4 makes generated runs reproducible across compatible updates,
adds a shared cosmetic wardrobe, and reduces the impact of background lookup
preparation on gameplay.

## Immutable generation profiles

- Pin every new attempt to a content-addressed generation package containing
  the exact normal-species data, custom-fusion pool, area catalog,
  obtainability sources, and deterministic algorithm versions it started with.
- Keep active and completed runs on their original package instead of silently
  migrating them to current game data or newly installed custom sprites.
- Generate aggregate type coverage from the same finalized profile used by the
  game and tracker, fixing false custom-build mismatch warnings after the
  versioning change.
- Validate profile fingerprints in Ruby and C#, retain historical packages in
  normal in-place updates, and report missing or damaged packages explicitly.
- Replace the previous per-generator saved-run migration path with one
  authoritative profile identity. Saves or archive recipes from before this
  baseline that lack valid pinned metadata are not rewritten; start a new run.

## Wardrobe and appearance

- Add an Ironmon wardrobe with a shared local point balance and permanent
  cosmetic unlocks, while keeping the equipped appearance specific to each save
  slot.
- Offer one profile-wide free initial outfit choice, then allow previews and
  permanent purchases for outfits, hairstyles, accessories, colors, and dyes.
- Award cosmetic points silently for trainer victories and major milestones,
  with save-persisted duplicate protection.
- Restore each slot's confirmed appearance through loads, resets, and seeded-run
  imports. A new male or female character now begins from that character's own
  appearance unless a compatible shared outfit has actually been confirmed.
- Disable base-game gameplay bonuses attached to cosmetic items during Ironmon.

## Lookup performance and presentation

- Break late player-fusion preparation into finer cooperative checkpoints,
  pause it during unsafe movement states, and bound native worker concurrency so
  the post-rival background calculation leaves more capacity for the game.
- Reuse profile-specific custom-fusion catalogs in the tracker and retain exact
  obtainability and evolution results without exposing concealed identities.
- Show a single neutral `1x` defense value when physical and special results are
  equal, and exclude general Defense/Special Defense stat stages from the type
  resistance multiplier display.
- Order body stats as Speed, Attack, Defense and head stats as HP, Special
  Attack, Special Defense.

## Startup, sprites, and battle scaling

- Skip Infinite Fusion's legacy randomization pass when starting Ironmon because
  Ironmon's own deterministic generators replace it. Other game modes retain the
  original setup.
- Synchronize custom-fusion and normal-species sprite sheets incrementally using
  server change metadata, preserve retry behavior, and remember only confirmed
  HTTP 404 responses as unavailable.
- Detect transparent cached sprites and fall back to a usable generated or base
  sprite instead of keeping an invisible cache entry.
- Keep player-level-matched special challenges at the player's level rather than
  applying Ironmon's additional 50% level multiplier a second time.

## Compatibility

Ironmon 0.8.4 targets Pokemon Infinite Fusion 2 version 6.8.2. Install the game
scripts and tracker from the same 0.8.4 archive. Both Windows x64 packages
contain the same features; the standard package includes .NET, while the smaller
runtime-required package needs the Windows x64 .NET 10 Runtime.
