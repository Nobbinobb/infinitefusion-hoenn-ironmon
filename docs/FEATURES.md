# Ironmon feature list

This guide describes the player-facing features in the current source tree.
Published downloads may contain fewer features; check the [release notes](releases/)
for the version you use. The [mechanics manual](IRONMON_MECHANICS.html) is the
reference for exact rules, formulas, and edge cases.

For download and setup instructions, start with the [repository README](../README.md).

## Contents

- [Run setup and lifecycle](#run-setup-and-lifecycle)
- [Seeded randomization](#seeded-randomization)
- [Pivoting and fusion choices](#pivoting-and-fusion-choices)
- [Progression safeguards](#progression-safeguards)
- [Starter selection](#starter-selection)
- [Live tracker views](#live-tracker-views)
- [World lookup and species information](#world-lookup-and-species-information)
- [Run archives and seed sharing](#run-archives-and-seed-sharing)
- [Achievements and artwork](#achievements-and-artwork)
- [History maintenance and local data](#history-maintenance-and-local-data)
- [Controls, features, and speed](#controls-features-and-speed)
- [Cosmetic wardrobe](#cosmetic-wardrobe)
- [Installation and updates](#installation-and-updates)
- [Diagnostic and development tools](#diagnostic-and-development-tools)

## Run setup and lifecycle

### Starting and continuing runs

- The tracker starts the game automatically and reuses a game already running
  from the same installation.
- The **Play** header action creates or continues Ironmon runs from the title
  screen or main menu. Ironmon is available on a fresh installation.
- New runs support **File A–H**, reusable rule profiles, and a review step.
  Replacing an occupied normal or Ironmon save requires confirmation.
- Continue supports valid Ironmon saves, including **Auto 1** and **Auto 2**.
- Wild, trainer, and starter policies are configured independently. Unfusion
  supports either a random retained component or a player choice.
- A run keeps the rules copied into its save. Editing a reusable profile changes
  future runs without rewriting existing attempts.

### Results, resets, and reconnection

- Attempts record distinct **active**, **lost**, **won**, and **abandoned** results.
  Starting starter selection already counts as an attempt.
- A battle loss or draw ends the challenge. Automatic loss resets are enabled
  by default and can be disabled; a failed run then stays locked until reset.
  The two opening battles always reset after a loss or draw.
- **F7** resets to the starter checkpoint with a fresh seed while retaining the
  run rules. Replacing an active attempt records it as abandoned.
- Resets and seed imports preserve the previous state if the transition fails.
- The current Alpha goal is victory in the normal Hoenn Gym battle against
  **Wattson**. It records a win, archives the result, and qualifies for a
  commemorative achievement. Wins do not trigger automatic loss resets.
- Ironmon requires a compatible tracker connection. Losing that connection
  pauses the game and attempt timer; reconnecting resumes the same attempt.
  Ordinary non-Ironmon play remains available without the tracker.

Details: [Challenge lifecycle](IRONMON_MECHANICS.html#challenge-lifecycle)
and [run configuration](guides/CONFIGURATION.md).

## Seeded randomization

### Reproducible runs and generation profiles

- A run seed determines its generated Pokémon data and supported randomized rewards.
- Each attempt is pinned to an immutable generation profile so compatible
  updates and later sprite additions do not silently change an existing run.
- Completed-run recipes retain the inputs needed to reconstruct that run's
  generated data for later lookup.

### Encounters and trainers

- **Mixed**, **Custom fusions only**, and **Normal only** policies apply to the
  supported wild, trainer, and starter slots.
- Generation covers starters, wild encounters, supported gifts, trainers, and
  evolution results, using validated normal and custom-fusion pools.
- Eligible encounters can produce derived fusions, including combinations from
  different encounter tables on the same map. Fishing has separate rules.
- Boss-trainer parties and early Hoenn encounters include challenge adjustments.
- The rival takes the original counter-slot starter. After the opening victory,
  the trainer policy determines its persistent keep, swap, unfuse, or fusion choice.
- Ordinary wild captures receive assistance that increases as the target is
  damaged. Ball and status effects continue through the normal capture rules.

### Abilities, stats, moves, and evolutions

- Abilities are generated with rules for slots, forms, eligibility, and fusion
  inheritance.
- Eligible normal species and mechanical forms preserve their base-stat total
  while redistributing it across the six stats. Fusions use component-aware
  calculations, with seeded inheritance weights for new generation profiles.
- Move-access randomization covers level-up and evolution moves, Egg moves,
  TM compatibility, ordinary tutors, and the specialized Fusion Move Tutor.
  Native learning and teaching actions use the generated access data.
- Evolution targets are generated while retaining the supported progression
  structure, branches, forms, and evolution methods.
- The tracker can show evolution branches and whether generated species are
  obtainable in the selected run.

### Items and rewards

- Ground rewards and supported non-HM TM gifts use deterministic randomized
  selections, with item weights and eligibility rules.
- HM rewards become permanent field tools before item randomization.
- Standard shops have challenge-specific stock rules. Ordinary scripted gifts
  and trainer held items retain their authored behavior unless explicitly covered
  by a separate rule.
- A seed-determined Pokémon Center recovery gift can be claimed once per attempt.

Details: [Encounters](IRONMON_MECHANICS.html#encounters),
[Pokémon data](IRONMON_MECHANICS.html#data-model),
[abilities](IRONMON_MECHANICS.html#abilities),
[base stats](IRONMON_MECHANICS.html#base-stats),
[move access](IRONMON_MECHANICS.html#move-access),
[evolutions](IRONMON_MECHANICS.html#evolutions), and
[items](IRONMON_MECHANICS.html#items).

## Pivoting and fusion choices

- Ironmon keeps one usable challenge Pokémon. Obtaining another Pokémon goes
  through the pivot decision rather than building an ordinary party.
- Supported choices include taking or swapping to the new Pokémon, fusing two
  normal Pokémon, reversing a newly obtained fusion, or unfusing it and keeping
  one component. Available actions depend on both Pokémon and their history.
- Choices commit before concealed results are revealed. Unfusion uses the saved
  random-component or player-choice rule.
- Fusion move selection shows a spread hint and one current calculated stat.
  Nature inheritance is seeded; the player still selects the retained moves.
- Versioned reverse pairing can relax its BST tolerance when needed while
  preserving disjoint components. Existing profiles retain their original rules.
- Fusion choices preserve their information boundary: hidden identities and
  generated outcomes are not disclosed merely to populate a tracker view.
- Story helpers, fusion materials, unselected candidates, and retirement copies
  are kept separate from the usable challenge Pokémon and its displayed history.

Details: [Single-Pokémon pivot system](IRONMON_MECHANICS.html#pivot-system).

## Progression safeguards

- Required story gifts and trades can use temporary progression Pokémon rather
  than consuming the player's only usable Pokémon.
- The Wally sequence preserves the player's Pokémon and uses the run's generation
  rules for the story encounter.
- Permanent field tools replace HM progression, so field moves do not require
  keeping a spare party Pokémon. Existing owned HMs are converted on load.
- Tool conversion happens before reward randomization, preserving required
  progression tools.
- The optional Repel counter displays remaining steps without changing duration.

Details: [Progression safeguards](IRONMON_MECHANICS.html#progression).

## Starter selection

- The starter presentation is shared between the game and tracker, with a stable,
  deterministic **Random Pick** for the selection scene.
- Manual mode reveals candidates as their balls are opened. Unopened candidates
  keep their identity, sprite, and base-stat total hidden.
- Automatic selection reveals all three candidates and normally takes the Random
  Pick. It is enabled by default and can be changed for the next selection scene.
- A saved maximum starter BST applies to manual and automatic choices when its
  enforcement switch is enabled. An empty value or **0** means unlimited.
- **Favorite Clause** recognizes configured normal species, including either
  component of a fusion. In automatic mode, eligible favorites can provide an
  alternative to a non-favorite Random Pick.
- Eligible revealed tracker cards support selection with confirmation. Hidden,
  stale, or otherwise ineligible choices are rejected by the game.

Details: [Starter selection](IRONMON_MECHANICS.html#tracker-starter-selection).

## Live tracker views

### Player and opponent information

- The four main tabs are **Player**, **Enemy**, **Lookup**, and **Archive**.
- Player information updates for HP, status, levels, stats and stat stages,
  types, nature effects, ability, held items, moves, and PP.
- Temporary **Imposter** and **Transform** state is reflected during battle.
- Opponent views reveal only permitted information and applicable move discoveries.
  Manual stat annotations let the player record their own observations.
- Wild opponents show the game's current ordinary Poké Ball capture percentage.
- Owned Pokémon inspection includes recorded IVs where available.

### Battle support

- The defensive overview explains visible type matchups, physical and special
  damage factors, protections, and relevant adjustments.
- Conditional and nonstandard move-power displays account for supported move
  rules and current battle conditions.
- Healing inventory shows usable item counts and combined healing capacity.
- Items can be selected from the tracker through normal battle usability,
  targeting, and timing checks.
- The **in-game item quick selector** groups available items into categories and
  remembers each category's selection. Its navigation and use actions can be rebound.
- The optional **Run shortcut** selects the normal Run command from an eligible
  wild battle's top-level command menu. Normal escape restrictions still apply.

Details: [Player view](IRONMON_MECHANICS.html#tracker-player-view),
[Enemy view](IRONMON_MECHANICS.html#tracker-enemy-view), and
[battle item support](IRONMON_MECHANICS.html#tracker-battle-item-view).

## World lookup and species information

- World lookup covers **encounters**, **trainers**, **ground items**, and
  **type coverage**, with per-run discovery progress.
- Encounter information distinguishes authored slots and derived fusion
  possibilities while preserving concealed entries.
- Trainer lookup shows disclosed party information and battle details.
- Story-team previews use the relevant persistent partners. Encounter and
  obtainability sources are filtered by the map's actual encounter triggers.
- Pokémon search and detail views provide information allowed by the current
  live, completed-run, or diagnostic context.
- Evolution graphs support branch expansion and configurable layout. Obtainability
  information identifies species reachable under the selected run's generated data.
- Expensive data preparation can run in the background. **Calculate lookup data**
  allows explicit preparation when the active view is not ready yet.
- Concealed information is filtered by the game before transmission. Opening
  another view does not itself grant access to hidden results.

Details: [World lookup](IRONMON_MECHANICS.html#tracker-area-progress-lookup)
and [Pokémon lookup](IRONMON_MECHANICS.html#tracker-post-run-search-and-lookup).

## Run archives and seed sharing

- Archive groups save-slot histories and completed runs, with results and totals
  for wins, losses, and abandoned attempts.
- Run statistics cover active time, battles, opponents, levels, badges, items,
  healing, and wasted healing.
- Recorded Pokémon history shows the last Pokémon and earlier retained stages,
  their highest levels, and whether they came from starter selection, a pivot,
  fusion, or evolution. Selecting a stage opens its archived species details.
- The displayed timeline follows saved checkpoints and removes discarded
  continuations after a reload where the saved history boundary is known.
- Recorded sprites are reused. Missing artwork can be resolved through the
  connected game and cached for later offline viewing.
- Completed recipes support world and Pokémon inspection using that run's
  preserved generation profile.
- **Seeded runs** exports an active attempt's seed token on request; completed
  runs can export their token from Archive.
- Import validates compatibility and requires confirmation before replacing an
  active attempt. Manual and automatic resets generate fresh seeds rather than
  replaying an imported seed.

Details: [Archive](IRONMON_MECHANICS.html#tracker-archive-view)
and [shared seeds](IRONMON_MECHANICS.html#tracker-shareable-seeded-runs).

## Achievements and artwork

- The **Achievements** page shows earned names, requirements, unlock dates,
  collection progress, search, and sorting.
- Locked names, requirements, and artwork stay hidden. Recorded progress can
  contribute to later tiers when their prerequisites become active.
- Earned achievements persist across attempts, resets, and save slots. Compatible
  definition and artwork updates preserve unlocks and their original dates.
- Achievement progress uses recorded gameplay from the collection feature's
  starting point. A seed import does not grant achievements or recreate
  unrecorded actions.
- Earned icons with detail artwork open a larger image when clicked.
- Compact in-game notifications show the earned icon and title. They are queued
  individually and adapt to the game window's display size.
- **Achievement pop-ups** can be disabled without disabling achievement tracking.
  Notices suppressed while disabled do not replay when the switch is enabled again.

Details: [Achievements and collection](IRONMON_MECHANICS.html#tracker-achievements).

## History maintenance and local data

- Discoveries, archived runs, gameplay collection, and earned achievements are
  stored outside the release directory under `%LocalAppData%\IronmonTracker`.
  User preferences also persist outside the installation.
- With the game closed, **Settings > Maintenance** can review and export history
  that no shipped achievement uses, including eligible completed-run recipes.
- Review rehearses export and compaction on a temporary database copy. Its
  conservative **disk space freed** estimate accounts for the archive remaining
  on the same drive and leaves the live profile unchanged.
- Export creates a compressed, encrypted `.irmhistory` file and verifies it before
  removing eligible source history. It reports measured disk savings afterward.
- Archive protection is automatic; the player does not choose or receive a password.
- Achievement evidence, permanent unlocks, shared identities, live checkpoints,
  and Pokémon configuration chains remain available in the active profile.
- Import restores an archive into its original tracker profile, preserves newer
  activity, and prevents the same archive from counting progress twice.
- Exported history is unavailable until restored. Future achievements may need
  it, so keep the archive; deleting it permanently discards the detached history.

Details: [History maintenance](IRONMON_MECHANICS.html#tracker-history-maintenance)
and [persistence](IRONMON_MECHANICS.html#tracker-persistence).

## Controls, features, and speed

### Input profiles

- Keyboard and supported controllers have saved profiles. Mapped Xbox and
  PlayStation devices and generic joysticks are supported.
- Each action has **two independent binding slots**, with press-and-release
  capture, conflict warnings, clearing, and profile-default restoration.
- Bindings cover movement, field actions, menus, battle actions, item quick
  selection, tracker navigation, and system shortcuts.
- Context-specific actions can be configured independently. Longer combinations
  suppress shorter component bindings, and conflicting simultaneous actions are
  checked before saving.
- Tracker game-control input applies while the connected game is in the foreground.

### Feature switches

**Settings > Features** groups optional assistance and session preferences.
Existing saved preferences are retained; these are the defaults for new settings.

| Feature | Default | Purpose |
| --- | --- | --- |
| Achievement pop-ups | On | Show earned achievement notices in the game. |
| Repel step counter | On | Show remaining Repel steps. |
| Speed indicator | On | Show the active accelerated Toggle speed in the overworld. |
| Item quick select | On | Enable the in-game battle selector and its shortcuts. |
| Quick run hotkey | On | Enable the wild-battle Run shortcut. |
| Favorite Clause | On | Use saved favorite species during starter selection. |
| Enforce BST limit | On | Apply the saved nonzero starter BST ceiling. |
| Autoselect starter | On | Use automatic selection for the next starter scene. |
| Automatic reset after a loss | On | Reset failed attempts after battle cleanup, subject to opening-battle rules. |
| Start tracker minimized | Off | Start with the tracker window minimized while services remain active. |

Turning a switch off preserves its saved configuration, favorites, and bindings.
Display switches do not change Repel duration, game speed, or achievement progress.

### Speed profiles

- **Toggle** mode has separate overworld and battle cycles. The default is
  **1× → 2× → 3× → 1×** in both contexts.
- **Settings > Speed** supports multipliers from **2× through 10×**, custom order,
  duplicate values, and grouped stages. Normal speed remains first.
- Links between overworld and battle groups define context transitions. Each
  group remembers its selected member and has a configured first-visit entry.
- The editor supports **Undo**, **Redo**, and explicit **Save changes**. Every
  accelerated group needs its opposite-context connection before saving.
- **Hold** mode keeps the game's independent overworld and battle speed sliders.
- The optional overworld speed indicator shows the active multiplier and cycle
  position. Multipliers are targets; demanding scenes can achieve less acceleration.

Details: [Controls](IRONMON_MECHANICS.html#tracker-controls),
[feature switches](IRONMON_MECHANICS.html#tracker-feature-toggles), and
[speed](IRONMON_MECHANICS.html#game-speed-up).

## Cosmetic wardrobe

- A shared profile retains wardrobe points and permanent cosmetic unlocks across runs.
- Default outfits and starting hairstyles are available, with one additional free
  confirmed appearance selection across the profile.
- The wardrobe supports outfits, hairstyles, two accessory layers, skin tones,
  base hair colors, and dye controls, including bicycle dye.
- Preview views include a detailed portrait, multiple overworld directions, and
  the bicycle. Browsers can show all pieces or only obtained pieces.
- Locked pieces can be previewed and purchased with points. Colors and dye are free.
- Purchases retain ownership; confirming applies the equipped appearance. Each
  save slot keeps its own confirmed appearance through loading and resets.
- Named outfit profiles save reusable cosmetic selections and dye settings.
- Each newly earned achievement grants 100 points once. Preexisting unlocks do
  not receive retroactive rewards. Current permanent prices are five times the
  original wardrobe prices; existing balances and ownership are retained.

Details: [Cosmetic wardrobe](IRONMON_MECHANICS.html#shared-cosmetic-wardrobe).

## Installation and updates

- Standalone **Setup** downloads and installs the required game and mod, or reuses
  a supported installation, without requiring a separate Git installation.
- Available package types include the bundled-runtime tracker and the smaller
  package for an installed Windows x64 .NET 10 Desktop Runtime.
- Setup checks runtime availability, offers WebView2 when needed, and can install
  sprite sheets and a desktop shortcut.
- Advanced release selection uses available signed metadata and matching game revisions.
- Mutable test releases are explicitly labeled in Setup's advanced selector.
  Automatic updates remain stable-only; replacement test builds use new internal
  sequences and require a fresh Setup review or complete ZIP reinstallation.
- **Settings > General > Custom sprite library** can review and synchronize sprite
  sheets later, with the game closed.
- Startup notifications and **Check for updates** provide signed release updates.
  Compatible updates preserve player data and the generation data needed by existing runs.

Details: [Installation, updates, and recovery](guides/INSTALLATION.md).

## Diagnostic and development tools

- Diagnostic information and development actions use separate, explicit capabilities.
  Normal tracker access and shared seed tokens do not grant those capabilities.
- Authorized tools can inspect permitted Pokémon, run, and protocol information.
- Supported development actions include healing, level and ability changes, move
  replacement, item grants, direct evolution, Pokémon swaps, and NoFail / AutoRevive.
  Each action requires its own grant and is checked by the game.
- Battle-time mutation restrictions and current-Pokémon checks protect against stale
  actions. Losing or expiring access removes the corresponding authority.

Details: [Controlled diagnostic access](IRONMON_MECHANICS.html#tracker-debug-mode)
and [development guidance](guides/DEVELOPMENT.md).
