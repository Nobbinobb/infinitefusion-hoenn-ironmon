# Compact defense coverage audit

Date: 2026-08-31. The original read-only review is preserved below as a baseline; the subsequent authorized implementation is recorded separately here.

## Subsequent implementation

Implemented locally, not released. The baseline omission tables below describe the state before this change, not outstanding work:

- Added the passive recovery abilities, held healing/cure items, active terrain/Aqua Ring/Ingrain healing, Early Bird, linked Leech Seed recovery, and pending Wish/Healing Wish/Lunar Dance states. Unused healing moves remain excluded by the user's explicit defensive-state boundary.
- Added recoil/contact protection, active guards/Substitute/Endure, Sturdy/Sash/Band survival, intact Disguise, known global Damp, hazard avoidance, Dig/Dive weather protection, and the compact conditional benefits listed below. Conditional move reductions contribute to the relevant type/category range without claiming all moves share them.
- Recovery data now contains multiple typed events and per-effect eligibility. Identical contributions retain a count; native per-contribution rounding is not replaced by a summed fraction. Wish uses its public countdown and original-HP basis, never the hidden stored amount. Linked drains never read source HP or hidden source abilities/items/status.
- The compiler and consumer use native JSON parsing instead of Ruby evaluation. Legacy tuples remain supported. The generated CSV includes nested/scoped and state entries so partial source coverage is visible.
- Immediate action capabilities, retaliation, item replenishment, PP restoration, and forecasts of future stat-trigger effects remain outside this view. Already-realized defensive stage changes continue to affect the current damage factors. Generation-disabled mechanics listed below stay excluded.

See `tests/runtime/Defense-Coverage.rb` and `tracker/tests/Ironmon.Tracker.App.Tests/Defense/DefenseViewTests.cs` for the new state, privacy, coverage, and stacking checks. The canonical mechanics manual and protocol describe current behavior.

Implementation verification: the bundled runtime passed **1,665 assertions** across all **233 abilities and 721 items**; all **8** tests in the dedicated app-test project passed. Browser checks using runtime-produced snapshots confirmed initially collapsed recovery, click expansion/collapse, consumed-state removal without leaving the view, a four-contribution recovery count, the three affected Grassy Terrain moves, and public pending recovery on the enemy view. These are presentation checks, not an exhaustive combat-interaction proof. The test command disabled only the unavailable online NuGet vulnerability lookup; project settings were unchanged.

The reviewed/generated recursive comparison has zero differences, including full numeric precision. Runtime, distribution, and source catalog copies share SHA-256 `A6A7B3F961A9D1134F68A9DAB5664D19B45E3DDD64C9C9AE91B1A7B7C8B783C4`. The generated CSV now contains 233 base ability rows, 721 base item rows, and 56 nested/scoped/state rows. The baseline counts and hashes below are historical.

## Original finding

The compact view does **not** yet cover all defenses that fit its design. Recovery is particularly incomplete: only four ability definitions and two item definitions currently supply recovery. Absorbed-hit healing, switch-out recovery, poison recovery, probabilistic status cures, healing/cure consumables, and active field/move recovery are missing.

This is not a generator dropping reviewed entries. Every field/value in the reviewed JSON survived in the generated JSON. The missing effects are absent from the reviewed definitions, and the runtime's recovery model cannot accurately express several of them yet. Some omissions were already outside the original weakness audit's scope, rather than being lost solely during the compact redesign.

Ability, item, and move names below identify maintenance sources. They are **not proposed source labels in the UI**. Proposed UI text remains defense/trigger only, with compact outcomes behind a click. The accepted fusion heading is outside this review and remains unchanged.

## Compared artifacts and confidence

- Original inventory: `WEAKNESS_MODIFIERS_AUDIT.md`, especially sections 1, 4, 5, 7, and 9. Section 7 explicitly excludes a complete ordinary-recovery inventory.
- Reviewed definitions: `DEFENSE_PRESENTATION_RULES.json`.
- Compiler: `tools/generation/Export-DefensePresentation.rb`, notably `resolve_moves`, `run`, and CSV construction at lines 43-52.
- Generated catalog: `data/defense_presentation.json`; report: `docs/audits/generated/DEFENSE_PRESENTATION_GENERATED.csv`.
- Runtime consumers: `src/tracker/defense/{Overview,Conditions,Ability_Rules,Item_And_Field_Rules,State_Rules,Rule,Compact_Effects,Damage_Profiles}.rb`.
- UI: `PokemonDefenseView` and `DefenseRuleList` under `tracker/src/Ironmon.Tracker.App/Components/Common`.
- Installed battle source listed at the end of this document; installed mechanics generation is **5**, with `MORE_TYPE_EFFECTS` enabled. Modern-game descriptions are not substituted for this code.

Read-only verification results:

| Check | Result |
| --- | --- |
| Recursive comparison of every reviewed JSON field/value against generated JSON | Zero missing or changed values; generated additions such as resolved names are allowed |
| Generated, `dist/Data/Ironmon`, and installed `Data/Ironmon` catalog copies | Identical SHA-256: `F7DFB66DFA4DE89DE58BB5257AF9202880BC51403C80801D1AA5ECD0174CC35A` |
| Stored provenance hashes against installed types/moves/abilities/items catalogs | All four match; this is audit evidence, not a proposed runtime compatibility gate |
| Generated CSV inventory | 233 abilities: 73 modeled, 160 unmodeled; 721 items: 30 modeled, 691 unmodeled |

**`modeled=true` means only that a definition exists. It does not mean all effects of that ability/item are represented.** Dry Skin and Sturdy are examples of partial coverage. Conversely, most unmodeled entries are not defensive. These counts are not completeness percentages. The CSV also has no individual field/move-effect coverage rows.

This audit traces source and data, not newly executed combat scenarios. Existing tests were not rerun because no executable behavior changed. It is a candidate coverage inventory, not proof of every interaction or of every effect's availability in every Ironmon battle format.

## What recovery already shows

| Definition | Trigger | Expanded outcome | Coverage |
| --- | --- | --- | --- |
| Dry Skin | Rain | +1/8 HP per turn | Rain recovery only; absorbed Water recovery missing |
| Ice Body | Hail | +1/16 HP per turn | Weather recovery represented |
| Rain Dish | Rain | +1/16 HP per turn | Weather recovery represented |
| Hydration | Rain | Heals status conditions | Major status only, not confusion; independent of Heal Block |
| Leftovers | Turn end | +1/16 HP per turn | Represented while known item is active |
| Black Sludge | Turn end | +1/16 HP per turn | Represented for a Poison-type holder with active item |

Weather labels describe available triggers even when that weather is not currently present. Follow that capability convention for new conditional recovery: do not hide a Poison trigger just because the player is not poisoned yet, or consult hidden enemy status to decide whether to show it. Actual ongoing effects, such as Aqua Ring, should appear only while present.

## Priority 1: missing recovery that fits the compact design

### Abilities and ongoing effects

| Maintenance source | Proposed trigger -> expanded outcome | Current omission and implementation constraint | Native evidence |
| --- | --- | --- | --- |
| Water Absorb; Dry Skin | Water move -> +1/4 HP | The Water immunity exists, healing does not. Applies to qualifying incoming Water moves, not self-use. Healing may be blocked while immunity remains. Dry Skin needs a second recovery trigger alongside Rain. | ABILITIES 681-693; HELPERS 547-568 |
| Volt Absorb | Electric move -> +1/4 HP | Electric immunity exists, healing does not. Same eligibility and independent immunity as above. Do not describe this as per-turn recovery. | ABILITIES 681-685; HELPERS 547-568 |
| Poison Heal | Poisoned -> +1/8 HP per turn | Entire ability absent. Handles ordinary and bad poison. Also prevents poison residual damage even when Heal Block prevents recovery; do not label it Poison protection, since poisoning still occurs. | ROUND 339-363 |
| Regenerator | Switch out -> +1/3 HP | Entire ability absent. Not a per-turn effect. Handler skips battle-end use and directly calls `pbRecoverHP`, without `canHeal?`; the present blanket Heal Block filter must not be inherited. | ABILITIES 2381-2387; HP 19-29 |
| Natural Cure | Switch out -> Heals status conditions | Entire ability absent. Clears major status, not confusion. No HP healing or Heal Block dependency. | ABILITIES 2374-2379 |
| Shed Skin | Turn end -> 30% chance to heal status conditions | Entire ability absent. Installed probability is exactly 30%, not 1/3. Major status only. | ABILITIES 1887-1910 |
| Healer on a known ally | Turn end -> 30% chance to heal status conditions | Entire ability absent. Benefits the bearer's allies, not the bearer. One 30% roll precedes iteration over its allies. Requires existing knowledge-filtered ally handling; never read a hidden partner ability. | ABILITIES 1835-1860; `Ability_Rules.rb:16` |
| Cheek Pouch | Berry consumed -> +1/3 HP | Entire ability absent. Triggered through item consumption, including supported forced consumption; it is not restricted to healing berries. HP recovery uses `canHeal?`. | ABILITY_ITEM 193-210 |
| Grassy Terrain, while target is affected | Turn end -> +1/16 HP per turn | Terrain currently supplies only the possible incoming Grass boost. Add recovery only while terrain is active and the recipient is affected by it. | ROUND 281-285; BATTLER 538-541 |
| Active Aqua Ring | Turn end -> +1/16 HP per turn | Public effect already captured but has no presentation definition. Big Root changes the recovery; do not always show the base amount. | ROUND 292-299 |
| Active Ingrain | Turn end -> +1/16 HP per turn | Already contributes Forced switching protection, but not healing. Big Root changes the recovery. | ROUND 301-308 |

`Turn end` is deliberately reused for ongoing field/move healing: showing Aqua Ring or Ingrain as a source explanation is unnecessary. Multiple contributions must survive grouping. Where compatible, an aggregate or a compact contribution count can convey stacking without exposing sources.

### Held recovery and cure items

All entries here are missing. They belong only on the player side under the current privacy policy. Show a one-use qualifier in the expanded outcome for consumables; a held cure is not a permanent immunity.

| Maintenance source | Proposed trigger -> expanded outcome | Native evidence / qualifications |
| --- | --- | --- |
| Sitrus Berry | HP <= 1/2 -> +1/4 HP, once | ITEMS 174-187; ABILITY_ITEM 127-136. Normal consumption respects Unnerve. |
| Oran Berry | HP <= 1/2 -> +10 HP, once | ITEMS 145-159; ABILITY_ITEM 127-136. Needs flat-HP outcome support. |
| Berry Juice | HP <= 1/2 -> +20 HP, once | ITEMS 61-76. Does not use the berry eligibility helper; do not apply Unnerve indiscriminately. |
| Figy, Wiki, Mago, Aguav, Iapapa Berries | HP <= 1/2 -> +1/8 HP, once | HELPERS 481-509; ITEMS `HPHealItem` registrations. These are the installed generation-5 values. Disliked flavor can cause confusion; a compact warning or exclusion is needed for an incompatible known player nature. Do not import modern 1/3 healing or 1/4-HP activation. |
| Enigma Berry | Super-effective hit -> +1/4 HP, once | ITEMS 1032-1040, 1171-1189. Uses the hit's type effectiveness and excludes substitute/disguise hits. |
| Shell Bell | Damage dealt -> +1/8 damage dealt as HP | ITEMS 1274-1285. Uses accumulated target HP loss for the move, not the holder's max HP. |
| Chesto / Pecha / Rawst / Cheri / Aspear Berry | Sleep / Poison / Burn / Paralysis / Freeze -> Heals condition, once | ITEMS 211-247, 320-363. Separate trigger per relevant condition. |
| Persim Berry | Confusion -> Heals confusion, once | ITEMS 334-349. Confusion is not a major status. |
| Lum Berry | Status condition -> Heals status conditions and confusion, once | ITEMS 251-283. Can also trigger on confusion alone; the chosen label must cover both. |
| Mental Herb | Infatuation / Taunt / Encore / Torment / Disable / Heal Block -> Removes effect, once | ITEMS 286-318. Six compact triggers, or a single grouped condition disclosure. Not permanent protection. Cure can itself remove Heal Block. |
| White Herb | Stat drop -> Restores lowered stages, once | ITEMS 1322-1342. Restores negative stages to zero; does not restore raw stat swaps or remove positive stages. |

Big Root belongs in the outcome calculation for supported drain, Aqua Ring, and Ingrain recovery, not as a standalone heal. The actual formula floors a 1.3 multiplier after the underlying quantity is calculated (ROUND 294-307; HP 32-47). Berry eligibility, suppression, one-use lifecycle, and chance should be data-driven generic rules, rather than item/ability-name branches in Ruby.

## Priority 2: useful candidates requiring additional context

These still admit concise outcomes, but more public state or an explicitly separate action-capability model is needed.

| Candidate | Compact presentation | Missing support / boundary |
| --- | --- | --- |
| Active Leech Seed recovery on its actual recipient | Turn end -> Restores drained HP | ROUND 310-325; HP 32-47. Need target-to-recipient linkage. Amount depends on the seeded battler's actual HP loss, then Big Root; Liquid Ooze can reverse recovery. Never publish enemy HP-derived amounts. A boolean effect reader cannot represent a recipient index, including valid index zero. |
| Pending Wish | Next turn end -> Restores stored HP | MOVES_B 2508-2527; ROUND 249-257. Need position countdown and ownership, not just battler flags. Amount is stored from the user's HP maximum when cast, not necessarily half the eventual recipient's maximum. Only expose an exact amount when its basis is permitted. |
| Pending Healing Wish / Lunar Dance | Switch in -> Full HP; heals status conditions | SWITCHING 337-352; MOVES_B 2804-2851. Position effects are not currently captured. They are consumed on entry. PP restoration from Lunar Dance remains outside defense. These handlers directly heal rather than using the usual `canHeal?` gate. |
| Early Bird | Sleep -> Half sleep duration | STATUSES 320-323. Compact status-recovery benefit, but not an immediate cure or 50% per-turn chance. Native duration division floors, including self-induced sleep. |

Known healing moves are a separate capability question, not automatically ongoing recovery. The installed move classes include half-HP healing/Roost, Rest, weather healing, Shore Up, drains, Refresh, party status cures, Heal Pulse, Floral Healing, Pollen Puff, Purify, Swallow, and Strength Sap (MOVES_A 352-450; MOVES_B 2476-2717; MOVES_C 516-565, 1583-1603, 1810-1830, 1943-1990, 2287-2366). They could be presented compactly only when a relevant known move is available, with its action/cost/target distinction preserved.

Do not add a generic Rain, Sun, or Turn end heal just because a Pokémon knows a weather-dependent healing move. Rest's sleep cost, Swallow's consumed defensive stages, Strength Sap's target dependence, and ally-only healing must not be silently dropped. Current defense input does not inspect a moveset or action eligibility. This audit did not regenerate an exhaustive installed move-to-function recovery catalog; listed functions are source-confirmed candidates, not a promise every Pokémon can obtain/use them. Keep these as a separate follow-up scope from the passive recovery omissions above.

## Compact protection omissions and partial coverage

| Candidate | Suitable compact defense | Current gap / source |
| --- | --- | --- |
| Rock Head | Recoil, optionally expanding to relevant moves | Missing definition. Covers the shared recoil-move path, not every self-inflicted HP cost. GENERIC 510-527. |
| Protective Pads | Contact effects | Missing item definition. Prevents effects gated through `affectedByContactEffect?`; does not make incoming contact attacks harmless. BATTLER 621-627. |
| Long Reach | Contact effects | Missing ability definition. The user's moves do not count as contact through `pbContactMove?`; do not describe this as incoming contact immunity. USAGE 34-39. |
| Poison Heal | Poison damage | Missing alongside recovery. Preventing poison damage is distinct from preventing Poison status, and persists while Heal Block stops healing. ROUND 339-363. |
| Active Protect/Detect, King's Shield, Spiky Shield, Baneful Bunker | Guarded moves, expanding to the actual covered move list | Their public flags are captured, but the only generic `effects` rule is Ingrain. Requires flag/category-filtered catalogs and bypass exclusions, rather than claiming universal protection. SUCCESS 329-384. |
| Active Substitute | Status moves, expanding only to the covered list; a compact damage-shield indication separately | Public flag captured but unused. Source exceptions must be honored. A plain Status moves immunity would overstate the coverage. SUCCESS 471-478; USAGE 158-188; MOVE_BASE 134-140. |
| Sturdy; Focus Sash; Focus Band; active Endure | Lethal hit -> Survives at 1 HP, with Full HP / 10% / once qualifiers where applicable | Sturdy currently shows only One-hit KOs; the items and Endure have no compact survival output. Focus Band is restricted to the full-HP branch in this installation. This needs a concise survival outcome, not a type resistance. USAGE 190-211. |
| Intact Disguise | First hit -> Damage absorbed | Missing. Use actual Mimikyu-fusion/form and ability predicates; do not assume standard modern recoil/suppression behavior. USAGE 159-189. |
| Known Damp anywhere on the field | Explosions, with existing move list | Present only for the inspected holder. Native suppression checks globally, so a known other bearer should also contribute. Add generic global-effect scope with the same hidden-ability boundary. MOVES_B 2726-2738; MOVES_C 2373-2385. |
| Airborne hazard avoidance | Spikes; Toxic Spikes; Sticky Web | Ground immunity alone does not communicate these hazard protections. Derive from known grounding/airborne state, not from all Ground-immune abilities. SWITCHING 372-398. |
| Grounded Poison type absorbing Toxic Spikes; other poison-prevention paths | Toxic Spikes | Currently only generic Poison protection is shown. Grounded Poison types also remove the hazard. Distinguish immunity from hazard removal if exposing the latter. SWITCHING 384-395. |
| Dig/Dive during their protected phase | Sandstorm; Hail | Native weather-chip predicates exempt these phases; current weather protections only cover types/abilities/items. Need public phase context. BATTLER 561-577. |

The move list for One-hit KOs is also absent from Sturdy's current definition, even though the engine has dedicated OHKO function classes. This can use the same generated affected-move mechanism as Sound/Powder after mapping current functions; no explanatory prose is needed.

Some additional benefits are compact in principle but **do not fit the current immunity-or-move-list protection semantics**: Heatproof's reduced burn damage, Grassy Terrain's reduction for Earthquake/Magnitude/Bulldoze, Sand Veil/Snow Cloak/Tangled Feet/Wonder Skin/Bright Powder/Lax Incense accuracy effects, Unaware's ignored attacking stages, and Contrary's reversed stage changes. These must not be hidden behind misleading bare immunity labels or merged into all moves of a type. A future compact numeric/conditional outcome would be needed. Evidence: ROUND 367-373; MOVES_A 1097-1102, 2360-2372; MOVES_B 423-426; ABILITIES 826-860; ITEMS 433-439; CALC 237-253; STAGES 12-70.

Current defense-stage factors already show the realized defensive result of Stamina, Water Compaction, boosts/debuffs, etc. Their future triggering potential is not shown. Retaliation (Rough Skin, Iron Barbs, Liquid Ooze, status-on-contact), berry replenishment (Harvest/Pickup/Recycle), targeting, and move denial are not passive HP/status recovery or immunity. Do not inflate the view by reintroducing their descriptions. In particular, Liquid Ooze does not prevent the drain attack's damage.

## Omissions that should remain omissions for this installation

- **Overcoat and Safety Goggles are not missing general Powder protection.** Both relevant native branches require generation 6 or later, disabled here. Grass's separate Powder protection is enabled (BATTLER 585-610; SUCCESS 446-468).
- **Oblivious is not missing current Taunt protection/cure.** The relevant cure branch is generation 6+, while infatuation protection is already modeled (ABILITIES 303-328).
- **Sweet Veil should not gain an assumed ally rule from a modern description.** The local registration in the ally-handler section incorrectly registers into `StatusImmunityAbility`, not `StatusImmunityAllyAbility`; current data matches the self-only registered behavior (ABILITIES 192-204).
- Immunity abilities that also immediately cure their matching status are already represented as the corresponding protection. Duplicating every such cure in Recovery would usually add noise; Natural Cure, Shed Skin, Hydration, and Healer are distinct because they cure without permanent status immunity.
- Neutral combined type rows, source names, the Details & conditions block, and prose-only descriptions remain intentionally excluded.

## Data and renderer changes required before implementation

1. **Multiple recovery events per definition.** Dry Skin must retain both Rain and Water move triggers; Mental Herb needs multiple condition outcomes. Keep facts in reviewed/generated data.
2. **Typed outcomes and timing.** Represent max-HP fractions, flat HP, damage-relative recovery, status cures, volatile cures, and restored stages separately. Add per-turn/per-event, probability, and one-use information. `Compact_Effects.rb:17-23` currently treats arrays as HP and every HP array as per-turn; encoding new HP heals as arbitrary text would bypass Heal Block handling and should not be used as a workaround.
3. **Per-effect healing eligibility.** Most HP recovery uses `canHeal?`, but Regenerator and the switch-in wishes do not. Major-status cures do not heal HP. Poison-damage protection must survive suppression of its healing by Heal Block. Item activity and berry consumption eligibility are separate checks.
4. **Capability condition versus current activation.** Keep labels such as Rain or Poisoned visible for a known available capability, while requiring actual presence for ongoing field/move effects. A hidden enemy condition must never drive row presence or numeric output.
5. **Scope and public context.** Healer is ally-only; Damp is global. Extend the allowlist only for needed public state. `Overview.rb:66` currently converts numeric effects into `> 0` booleans, which is unsuitable for indexed recipients or stored healing amounts. Never derive recipient HP or status from concealed data.
6. **Preserve stacking.** `DefenseRuleList.razor.cs:21-34` groups by label and deduplicates identical outcome strings. Once multiple independent Turn end heals exist, two +1/16 contributions must not collapse into one. Do not blindly sum effects with different eligibility, timing, rounding, or chance.
7. **Measure coverage per effect, not per source ID.** Maintain explicit included/partial/excluded decisions and reasons for recovery, protection, survival, and other relevant categories. Export those decisions alongside the existing source inventory. New unclassified entries should be review information, not a game/version startup blocker.

Suggested implementation order: passive ability recovery first; then active field healing and player consumables; then compact protection/survival additions; finally decide whether known healing-move capabilities belong in this view at all. The existing release-time generation workflow and tolerance for older informational data should stay unchanged.

## Validation scenarios for a later implementation

- Absorb abilities show both type immunity and healing; Heal Block removes only the healing. Dry Skin retains two independent triggers.
- Poison Heal describes poisoning as a recovery trigger, prevents poison chip without claiming status immunity, and handles Heal Block separately.
- Regenerator under Heal Block still exposes switch-out recovery; Hydration/Natural Cure/Shed Skin/Healer do not become HP healing or confusion cures.
- Healer benefits the correct known ally; unrevealed abilities never affect either side's rows. Natural Cure and Regenerator do not imply that switching is currently legal.
- Berry thresholds/fractions follow this installed generation, flat heals remain flat, one-use effects are identifiable, and Unnerve does not suppress Berry Juice.
- Leftovers, Grassy Terrain, Aqua Ring, and Ingrain can coexist without losing identical contributions. Big Root uses native quantity/rounding semantics.
- Wish and drain output remain invariant when only concealed enemy HP/status changes. Valid Leech Seed recipient index zero is handled correctly.
- Protection move lists exclude bypasses and unavailable modern branches. Full-HP survival is distinct from OHKO protection and from resisting a type.
- UI disclosures remain initially collapsed, concise, source-free, and keyboard accessible; no explanatory block returns.

## Native source index

Paths and line numbers refer to the installed source reviewed on the audit date. Search the named handler/method after future source changes. Source files are read-only evidence, not files to patch for presentation work.

- **ABILITIES:** [ability handlers](<C:/Program Files (x86)/InfiniteFusion2/Data/Scripts/011_Battle/003_BattleHandlers_Abilities.rb>).
- **ITEMS:** [item handlers](<C:/Program Files (x86)/InfiniteFusion2/Data/Scripts/011_Battle/004_BattleHandlers_Items.rb>).
- **HELPERS:** [ability and berry helpers](<C:/Program Files (x86)/InfiniteFusion2/Data/Scripts/011_Battle/002_BattleHandlers.rb>).
- **ROUND:** [end-of-round processing](<C:/Program Files (x86)/InfiniteFusion2/Data/Scripts/011_Battle/003_Battle/012_Battle_Phase_EndOfRound.rb>).
- **SWITCHING:** [entry healing and hazards](<C:/Program Files (x86)/InfiniteFusion2/Data/Scripts/011_Battle/003_Battle/006_Battle_Action_Switching.rb>).
- **ABILITY_ITEM:** [item activation and consumption](<C:/Program Files (x86)/InfiniteFusion2/Data/Scripts/011_Battle/001_Battler/006_Battler_AbilityAndItem.rb>).
- **BATTLER:** [healing, weather, terrain, and contact predicates](<C:/Program Files (x86)/InfiniteFusion2/Data/Scripts/011_Battle/001_Battler/001_PokeBattle_Battler.rb>).
- **HP:** [HP changes and drain recovery](<C:/Program Files (x86)/InfiniteFusion2/Data/Scripts/011_Battle/001_Battler/003_Battler_ChangeSelf.rb>).
- **STATUSES:** [status application and sleep duration](<C:/Program Files (x86)/InfiniteFusion2/Data/Scripts/011_Battle/001_Battler/004_Battler_Statuses.rb>).
- **STAGES:** [stage-change rules](<C:/Program Files (x86)/InfiniteFusion2/Data/Scripts/011_Battle/001_Battler/005_Battler_StatStages.rb>).
- **USAGE:** [contact, damage absorption, and survival](<C:/Program Files (x86)/InfiniteFusion2/Data/Scripts/011_Battle/002_Move/002_Move_Usage.rb>).
- **SUCCESS:** [guard and immunity checks](<C:/Program Files (x86)/InfiniteFusion2/Data/Scripts/011_Battle/001_Battler/009_Battler_UseMove_SuccessChecks.rb>).
- **MOVE_BASE:** [move flags and substitute bypass](<C:/Program Files (x86)/InfiniteFusion2/Data/Scripts/011_Battle/002_Move/001_PokeBattle_Move.rb>).
- **CALC:** [attack calculation and stage handling](<C:/Program Files (x86)/InfiniteFusion2/Data/Scripts/011_Battle/002_Move/003_Move_Usage_Calculations.rb>).
- **GENERIC:** [healing and recoil move classes](<C:/Program Files (x86)/InfiniteFusion2/Data/Scripts/011_Battle/002_Move/004_Move_Effects_Generic.rb>).
- **MOVES_A:** [move effects 000-07F](<C:/Program Files (x86)/InfiniteFusion2/Data/Scripts/011_Battle/002_Move/005_Move_Effects_000-07F.rb>).
- **MOVES_B:** [move effects 080-0FF](<C:/Program Files (x86)/InfiniteFusion2/Data/Scripts/011_Battle/002_Move/006_Move_Effects_080-0FF.rb>).
- **MOVES_C:** [move effects 100-17F](<C:/Program Files (x86)/InfiniteFusion2/Data/Scripts/011_Battle/002_Move/007_Move_Effects_100-17F.rb>).
