# Ironmon 0.9.0

Ironmon 0.9.0 is a substantial update from 0.8.15. It changes fusion generation
and the opening rival sequence, adds independent starter rules, brings the
wardrobe into the tracker, and introduces persistent gameplay collection,
achievements, richer Pokémon history, configurable speed cycles, and more
accurate world lookup.

## Fusion generation and concealed choices

- **New fusion stat inheritance for new runs.** Body- and head-dominant stat
  groups use seeded 90/10, 80/20, and two-thirds/one-third contributions instead
  of the same fixed weighting for every stat. Fusion strength calculations in
  the tracker and game use the same versioned formula.
- **More resilient reverse-fusion pairing.** The new generator can retry the
  complete pairing with progressively wider BST tolerances when a tight pairing
  fails. Shared components remain forbidden; strength tolerance increases in
  five-BST steps, bounded by the pool's full strength range.
- **Useful concealed fusion clues.** Move selection shows a stat-spread
  description and one deterministic, current calculated stat. The other stat
  values remain concealed.
- **Automatic nature inheritance.** A fusion inherits one material's nature
  through a seeded choice. The old nature-selection prompt is removed. Native
  move selection and generated ability assignment are retained.
- Existing runs retain their recorded generation-profile algorithms. The new
  formula and pairing rules do not silently regenerate old runs or archives.

## Independent starter rules and the rival's story

- **Starter Pokémon now have their own run-setting policy:** normal Pokémon,
  custom fusions, or mixed offers, independently of wild and trainer policies.
  The selected policy is preserved in presets, run recipes, and shared seed data.
- The rival takes the actual starter from the original counter slot, preserving
  the slot order even when randomized types no longer form a type triangle.
- After the first rival victory, the trainer policy determines whether the
  rival keeps its partner, swaps to the remaining offer, unfuses, or fuses the
  two normal offers. The resulting partner persists through the later story.
- The lab sequence now handles those branches with Birch's assistance, native
  fusion animation, and movement around the player, furniture, and doorway.
  Rival fusion does not replace the player's party or register its Pokémon as
  the player's catches.
- Rival fusion assignments and additional evolution work are prepared through
  the tracker. Large result imports are processed in smaller game updates to
  reduce long pauses after the opening battle.
- A loss or draw in the Route 101 rescue or first rival battle always ends the
  attempt and queues a safe reset, including when automatic loss reset is off.
  The failed attempt is recorded and Archive opens without delaying reset for
  completed-run lookup calculations.
- Earlier configuration-version-1 runs and seed data remain supported, retaining
  their original wild-controlled starter policy and rival plan.

## Progression, rewards, and battle safeguards

- **The first Alpha endpoint is recorded as a win.** Winning Wattson's normal
  Hoenn Gym battle completes the attempt, updates win statistics and achievement
  evidence, and saves the completed state after battle and event cleanup.
  Duplicate battle reports cannot award the win again.
- **A Pokémon Center recovery gift is available once per attempt.** Its item is
  selected deterministically from the run's seed, so repeating the interaction
  does not reroll or repeatedly award the gift.
- Direct scripted trainer rematches now obey the same challenge restriction as
  menu rematches, including rival and Tiana rematch paths. One-time story battles
  remain available.
- Move-forgetting selection waits for Confirm/Cancel to be released before
  accepting a new input. Move-learning protection also prevents accelerated
  frames from carrying a held action straight into an unintended choice.

## Tracker wardrobe and cosmetic rewards

- **Wardrobe editing now happens in a game-opened tracker dialog.** Use the
  game's Change outfit command or bedroom wardrobe. Gameplay and the attempt
  timer pause while editing; Apply or Cancel returns to the game.
- Browse searchable outfits, hairstyles, and two accessory layers, with filters
  for all, owned, or unowned pieces. Locked pieces can be previewed before purchase.
- Native previews cover the detailed portrait, front/back/side views, and bicycle.
  Skin tone, installed hair colors, and hair/clothes/accessory/bicycle dyes are
  editable, including precise hue values.
- **Named outfit profiles** can be saved, applied, renamed, replaced, and deleted.
  Each save slot keeps its confirmed appearance through loading and resets.
- Purchases are permanent even if an equipped preview is cancelled. Reconnection
  restores the current draft and ownership; physical Back can leave the wardrobe
  if the tracker disconnects.
- **Permanent unlock prices are five times their previous values.** Existing
  point balances and owned pieces are retained without rescaling or extra charges.
  Colors and dye remain free, and the profile-wide additional free selection is
  retained until confirmed.
- **Each newly earned achievement grants 100 wardrobe points.** Rewards are
  queued durably and credited once, including after a disconnect or lost
  acknowledgement. Achievement pop-ups do not need to be enabled. Achievements
  already earned before this reward system was introduced are not paid retroactively.

## Persistent achievements and artwork

- A new **Achievements** page provides earned names, requirements, unlock dates,
  collection progress, search, and sorting. Progress and earned unlocks persist
  across attempts, resets, and save slots.
- Locked names, conditions, and artwork stay concealed. Recorded evidence can
  contribute to later stages as their prerequisites become active.
- Earned achievements have dedicated icons. Clicking supported earned icons
  opens their larger detail artwork. Artwork is shipped in protected embedded
  packs rather than loose image folders.
- Compact in-game notifications show the earned icon and title, queue unlocks
  individually, and follow the game window's position and display size.
- Compatible achievement-definition and artwork updates retain earned identities
  and original unlock dates. Collection begins when the new recorder is used;
  importing a seed does not recreate older, unrecorded gameplay.

## Gameplay collection and history maintenance

- The tracker now records gameplay evidence in a persistent SQLite collection,
  covering Pokémon ownership and history, battle activity, resources, items,
  progression, and cosmetic events. Journaled delivery and duplicate protection
  support recovery across reconnects and interrupted sessions.
- **Settings > Maintenance** reviews and exports unrelated history with the game
  closed. A closed game also permits maintenance after a connection-error state.
- Review rehearses the real export, removal, and compaction on an owned temporary
  database copy. The displayed estimate is conservative **net disk space freed**,
  including the archive remaining on the same drive, rather than expanded JSON
  record size. The live database, WAL, and recipes remain unchanged during review.
- Export creates a compressed, encrypted `.irmhistory` archive and verifies it
  before removing eligible source records. Protection is automatic: the player
  does not choose or receive a password.
- Achievement evidence, earned unlocks, shared identities, live checkpoints, and
  Pokémon configuration chains remain in the active profile. Completion reports
  measured disk savings, including additional space used when an export grows.
- Import restores an archive to its originating profile, merges it with newer
  evidence, and prevents duplicate counting. Keep exported files: future
  achievements may need detached history, and deleting the archive loses it.
- Reconnecting the game discards a stale maintenance review and requires a fresh
  measurement before changing history.

## Pokémon history, IVs, and archive inspection

- **Owned-Pokémon IV inspection** adds recorded individual values to Pokémon
  lookup, including effective values and Hyper Training where that information
  is available. When several owned individuals share a species, their records
  can be inspected separately.
- **Archive Pokémon history** retains the last Pokémon and earlier recorded
  starter, pivot, fusion, and evolution stages, with their highest recorded
  levels. Selecting a stage opens its archived species information.
- Saved checkpoint boundaries trim discarded continuations after a reload,
  keeping the retained Pokémon timeline aligned with the saved attempt.
- Archived sprites are reused offline. Missing history sprites can be requested
  from the connected game in bounded batches and cached for later viewing.
- Losing opening-rival runs retain the context needed for later trainer-team
  inspection. Generating that preview is deferred until explicitly requested.

## Trainer, encounter, and obtainability lookup

- Trainer catalog generation now follows additional scripted story-battle paths
  and links multiple event identities to the correct trainer entry.
- Rival, Wally, and other supported story teams can use their actual persistent
  story Pokémon when preparing trainer lookup, rather than treating every member
  as a fresh generic trainer randomization.
- Trainer battle completion and item completion better follow their actual event
  identities, completion switches, and self-switches.
- Encounter and obtainability calculations filter tables by the encounter methods
  the map can actually trigger, including land, cave, water, fishing, weather, and
  time variants. Unused encounter data no longer creates false available sources.
- Early manual lookup preparation and authorized evolution inspection can prepare
  required fusion assignments before normal background lookup has started.
- Additional cooperative checkpoints reduce long game-thread work during fusion
  closure, encounter expansion, and completed-run lookup preparation. Existing
  hidden-information restrictions remain in force.

## Battle quick items, speed, and settings

- **An in-game battle item selector** browses items and categories, remembers the
  selected item in each category between battles, and uses normal battle
  usability and targeting checks. Its controls are configurable in the tracker.
- Five new battle bindings cover previous/next item, previous/next category, and
  use. Keyboard defaults use Ctrl+Shift with arrows and C; mapped controllers use
  both triggers with the D-pad and confirm button.
- **Settings > Speed** edits independent overworld and battle Toggle cycles,
  using multipliers from 2× to 10×, custom order, repeated values, grouped stages,
  and links between contexts. Groups remember their selected member and define
  their first-entry speed. Normal speed remains first.
- The speed editor includes Undo, Redo, and explicit Save changes. Hold mode
  retains the game's separate overworld/battle sliders. A new optional overworld
  indicator shows the active multiplier and cycle position; the Repel display
  uses the shared overlay styling.
- Settings now have **General, Controls, Features, Speed, and Maintenance** pages.
  Features groups achievement pop-ups, Repel/speed indicators, item quick select,
  quick Run, Favorite Clause, BST enforcement, starter autoselection, automatic
  loss reset, and minimized startup.
- Turning assistance off preserves its saved favorites, values, item selections,
  and bindings. Disabled quick-item/Run bindings remain visible but cannot be
  edited until their feature is enabled again.
- A blank or zero starter BST ceiling means unlimited. Favorite Clause and BST
  enforcement can be switched independently of their saved settings. Saved-state
  feedback no longer shifts the Settings header as it appears.

## Installation, documentation, and technical changes

- Setup detects the required installed Windows x64 .NET Desktop Runtime before
  enabling the smaller runtime-required package and explains missing prerequisites.
  The standard package includes its runtime; WebView2 remains a separate prerequisite.
- Durable journal replacement and verified directory promotion retry short Windows
  sharing locks. Persistent locks still retain recovery data, and a changed source
  tree is rejected before a retry can promote it.
- Tracker projects are organized under Tracker, Setup, Updater, and Shared, with
  consistent namespaces and build-resource paths. Gameplay collection uses a
  dedicated SQLite layer with generated typed queries and reusable commands.
- Native `dotnet test` Quick/Full profiles replace the old PowerShell regression
  launchers. Routine PR checks cover important C# tests, bundled Ruby smoke tests,
  sprite contracts, and metadata; release preparation runs the full package gate.
  Runtime coverage now includes rival fusion, wardrobe transport/previews, and
  tracker UTF-8 text handling.
- The sprite-maintenance Python dependency is updated to Pillow 13.0.0.
- The README is shorter and links to a [full feature guide](../FEATURES.md).
  Installation/development guidance and the mechanics manual are refreshed, and
  the README explicitly discloses AI-generated code and artwork used by the mod.

## Mutable test release and compatibility

Ironmon 0.9.0 targets **Pokémon Infinite Fusion 2: Hoenn 6.8.2** on Windows x64.
Keep game scripts and tracker from the same build together. Existing runs retain
versioned generation data; creating a new run uses the new generation algorithms.

During testing, this is a **mutable GitHub prerelease**. Replacement builds keep
version 0.9.0 while advancing their signed internal build sequence and recovery
helper identity. Normal tracker update checks continue to offer stable releases only.

Download `Ironmon-Setup-win-x64.exe` from this release, expand **Advanced:
Ironmon version**, refresh the list, and explicitly select **0.9.0** labeled
**Test release**. Setup's default still installs the latest stable release. For a
replacement test build, rerun Setup and repeat selection and review, or reinstall
the complete matching ZIP. Changed program files may need confirmation and backup;
a different required game revision may require a fresh test folder.

The standard ZIP includes .NET; the smaller runtime-required ZIP needs the Windows
x64 .NET 10 Desktop Runtime. `SHA256SUMS.txt` lists the current download and metadata
hashes. Previously downloaded files do not change automatically. After testing is
accepted, the exact tested assets can be frozen and promoted to stable without
rebuilding them.
