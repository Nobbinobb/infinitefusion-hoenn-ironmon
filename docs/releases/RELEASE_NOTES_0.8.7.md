# Ironmon 0.8.7

Ironmon 0.8.7 shortens the Devon rescue and delivery sequence, adds trainer
battle details, speeds up prepared Pokemon lookups, and improves tracker
navigation and scrolling.

## Early Hoenn progression

- Shorten the Rusturf Tunnel rescue dialogue after recovering the Devon Parts
  and rescuing Peeko.
- Move Mr. Stone's delivery handoff outside in Rustboro, skipping the office
  visit and walk downstairs while retaining Exp. All, the Letter, delivery
  quests, and later story progression. Existing pending office visits receive
  the same shortened handoff, and rewards cannot repeat.

## Trainer and Pokemon lookup

- Expand trainer party members to inspect battle moves and ability information,
  including move types, categories, and effect descriptions. Preserve live
  diagnostic permissions and explicitly identify unavailable historical sets.
- Open the existing Pokemon explorer directly from a trainer to inspect the
  species' abilities, stats, move access, and evolutions.
- Restore missing battle details in older trainer records when reconstructed
  party slots, species, and levels match the recorded party.
- Speed up prepared Pokemon lookups and occurrence searches through cache
  reuse and reduced redundant preparation work.
- Fix scrolling below the Pokemon detail tabs when entering from an area
  trainer. Show a single arrow-style return link to the trainer, preserving
  the trainer selection. Normal Pokemon lookup keeps its Return to Results
  link and search state.

## Tracker layout and archives

- Reduce tracker header space and streamline archive navigation.
- Restore the original tracker type colors.
- Expand already prepared archived runs immediately and reuse their completed
  preparation instead of starting it again.

## Compatibility and installation

Ironmon 0.8.7 targets Pokemon Infinite Fusion 2 version 6.8.2. Install the game
scripts and tracker from the same 0.8.7 archive. The standard Windows x64 package
includes .NET; the smaller runtime-required package needs the Windows x64
.NET 10 Runtime. Both provide the same player features and license notices.

Back up saves before updating and retain existing generation-profile data.
Compatible saved runs continue using their original pinned data and algorithms;
a missing or damaged profile is reported rather than regenerated differently.
