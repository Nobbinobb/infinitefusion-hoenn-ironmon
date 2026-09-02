# Weakness, immunity, and defensive modifier audit

Date: 2026-08-31. Target: this installed Infinite Fusion game and the local Ironmon source/tracker.

## Scope and confidence

This document records the original source audit and the subsequent tracker implementation boundary. The audit inventories the implemented rules that change defensive types, type effectiveness, immunity to incoming moves, or situational damage mitigation. It also covers the ways those protections can be removed, and separates ordinary damage scaling from type weaknesses. The tracker changes presentation only, not battle mechanics.

The follow-up [compact defense coverage audit](COMPACT_DEFENSE_COVERAGE_AUDIT.md) records the original gaps and their subsequent local implementation. Passive capabilities and active/pending defensive states are represented; unused move capabilities remain outside the view.

The authoritative paths inspected are the battle's actual move execution, effectiveness calculation, ability/item handlers, battler state, weather/terrain, form/fusion rules, and Ironmon's presentation. AI scoring and move descriptions are not treated as the battle result. Searches for replacement implementations covered `Data/Scripts` and `Ironmon/src`.

**Original audit validation:** source tracing, cross-checking call sites, and read-only decoding of the installed catalogs: **30 types, 680 moves, 233 abilities, and 721 items**, deduplicated by ID. Catalog decoding consumed each complete file and used the game's XOR format; that audit phase did not execute game code, load saves, rebuild runtime scripts, or change gameplay. Subsequent implementation validation is described in section 9. A catalog entry or effect implementation does not prove availability in the current run or a particular generated pool; species/encounter data was not enumerated. Examples below are calculations from the inspected rules, not measured combat damage.

The active setting is **`MECHANICS_GENERATION = 5`**, but individual mechanics include later additions and custom behavior. Do not substitute a modern Pokémon rules list for these sources. `MORE_TYPE_EFFECTS` and the newer critical-hit rules are enabled; generation-6/7 branches are generally not. Sources: SETTINGS 170; BATTLE_SETTINGS 4–42.

Source labels and line numbers used below resolve through the [source index](#source-index).

## The distinction a weaknesses display must preserve

| Layer | Examples | Meaning |
| --- | --- | --- |
| Current defensive types | Color Change, Soak, Roost, Forest's Curse | Rebuild the defensive type chart. |
| Move-specific effectiveness | Freeze-Dry, Flying Press, Foresight, Strong Winds | Change the matchup for this move or field state. |
| Immunity / failure | Levitate, Water Absorb, Bulletproof, Protect | The hit is blocked; this need not change the stored type multiplier. |
| Damage modifier | Thick Fat, Filter, Reflect, resist berries | Damage is reduced or increased while the hit can still count as super effective. |
| Stats / survival / recovery | Eviolite, Stamina, Sturdy, healing | Change damage or survival, but do not change the type chart. |

The engine stores a neutral type multiplier as **8**, not 1. Each distinct defensive type contributes 0, 1, 2, or 4 internally; up to three contributions are multiplied. Divide by 8 for the familiar multiplier. Three types can therefore produce **1/8× and 8×**, as well as the usual values. The same type is not counted twice. Sources: TYPE; CALC 32–81; BATTLER 352–378.

Do not promise an exact HP-damage ratio by multiplying all labels together. Some modifiers affect base power, some Attack or Defense, and some final damage. The formula rounds intermediate values and adds 2 before final multipliers. A “Fire power ×0.5” modifier is not exactly the same operation as halving the final HP loss. Source: CALC 227–267.

## Findings that materially affect correctness

| Finding | Actual implementation and impact | Source |
| --- | --- | --- |
| Resist berries activate on neutral hits | The shared helper rejects resisted non-Normal hits, rather than requiring a super-effective hit. A matching berry therefore halves both neutral and super-effective damage. Chilan also works on resisted Normal hits. | HELPERS 581–587; TYPE `resistant?` |
| Resist berries do not consult Unnerve | The damage helper does not call `canConsumeBerry?`; later consumption also has no Unnerve check. Unnerve blocks ordinary berry paths, but not this path. | HELPERS 581–587; USE 800–809; ABILITY_ITEM 127–134 |
| Prism Armor and Shadow Shield are bypassed by Mold Breaker in this implementation | Although registered as `DamageCalcTargetAbilityNonIgnorable`, their invocation is inside the outer `if !@battle.moldBreaker` block. Treating their registration name as their actual behavior would be wrong. | CALC 285–305; ABILITIES 1215–1230 |
| Iron Ball has a whole-matchup override | Ground against a Flying target with an active Iron Ball returns neutral before examining any other type. A Ground/Flying target and a Bug/Flying target both return 1×; ordinary grounding instead retains the other type's contribution. | CALC 60–64 |
| Grass Pelt boosts the selected defensive stat for every damage category | Its handler checks only Grassy Terrain, with no physical-move or grounding requirement. The shared multiplier can therefore also reduce special damage. | ABILITIES 1171–1177; CALC 257–265 |
| Fluffy checks the raw contact flag | Fire final damage ×2 and contact final damage ÷2 stack. It uses `contactMove?`, not the Long Reach-aware `pbContactMove?(user)`. A nominal contact move can still receive the reduction with Long Reach. | ABILITIES 1158–1163; USAGE 37–40 |
| Strong Winds expiry is disabled | The branch ending Strong Winds when Delta Stream disappears is commented out. Existing Strong Winds can remain after its setter leaves; Cloud Nine/Air Lock can still suppress its effective weather. | BATTLE 672–674, 706–727 |
| Grassy Terrain's attack reduction is field-wide in these handlers | Earthquake and Magnitude halve final damage; Bulldoze halves base power. None checks the target's grounding here. Other terrain effects use grounding checks. | MOVES_A 1097–1102, 2360–2372; MOVES_B 423–426; CALC 351–360 |
| Dazzling / Queenly Majesty lack a Mold Breaker check in their execution path | Their global priority blocking loop checks active abilities but not `moldBreaker`; the handlers do not check it either. Do not fold these into the ordinary bypassable immunity-handler group. | USE 283–299; ABILITIES 566–579 |
| Tracker effectiveness is intentionally type-only | It has no ability, item, weather, terrain, move ID, or protection input. It cannot represent the effective defensive outcome described by this audit. This is a capability boundary, not evidence that the game ignores those mechanics. | TRACKER_RULES 38–61; TRACKER_PRESENTATION 15–20 |
| Tracker collapses 8× and 1/8× to neutral | The calculator multiplies any supplied distinct types, but its result mapping only recognizes 0, 1/4, 1/2, 2, and 4; other values become neutral. Bug/Steel + Forest's Curse is 8× weak to Fire. Fire/Flying + Forest's Curse takes 1/8× from Grass. | TRACKER_RULES 42–61; SNAPSHOTS 123–129 |
| Composite defensive type IDs are not standard-type arrays | The installed type catalog has ten composite entries with authored matchup lists. The tracker treats unrecognized defensive IDs as neutral. Splitting the displayed names would also be wrong: e.g. the single `GRASSSTEEL` entry lists Fire at 2×, not the 4× of separate Grass and Steel types. | TYPE_CATALOG; TYPE 54–60; TRACKER_RULES |
| Tracker move type resolution is incomplete for battle overrides | Selected dynamic moves use `pbBaseType`; the final `pbCalcType` layer containing Electrify and Ion Deluge is not used there. Ordinary move types also do not automatically incorporate the attacker's conversion ability. | MOVE_PRESENTATION 115–123; CALC 5–26 |

These are source-level findings. The table distinguishes actual code behavior from assumptions a future feature could otherwise make; it does not silently fix the game to match another generation.

## 1. Abilities that directly block or modify incoming attacks

### Type and move immunities

Unless stated otherwise, these require an active ability. Ordinary immunity handlers are bypassed by Mold Breaker/Teravolt/Turboblaze and by moves which set the same battle flag. A type immunity and an ability immunity are separate checks; removing one does not necessarily remove the other. Sources: USAGE 81–90; SUCCESS 399–445.

| Ability | Protection and conditions | Source |
| --- | --- | --- |
| Levitate | Ground damaging moves miss while airborne. Gravity, Ingrain, Smack Down, and an active Iron Ball ground the holder. Mold Breaker removes Levitate's contribution, but not another independent airborne source. | BATTLER 526–536; SUCCESS 419–444 |
| Flash Fire | Blocks incoming Fire-type moves from another battler. Sets a flag that later boosts the holder's Fire attacks. | ABILITIES 600–624 |
| Water Absorb | Blocks Water; attempts to heal 1/4 maximum HP. Immunity still works when healing cannot occur. | ABILITIES 687–691; HELPERS 547–568 |
| Dry Skin | Same Water immunity/healing handler as Water Absorb; separately increases incoming Fire base power by 25%. Weather also affects its HP at round end. | ABILITIES 693, 1134–1138; end-of-round handlers |
| Storm Drain | Blocks Water and attempts to raise Special Attack by one stage. Also redirects eligible single-target Water moves. | ABILITIES 659–663; TARGETING |
| Volt Absorb | Blocks Electric; attempts to heal 1/4 maximum HP. | ABILITIES 681–685; HELPERS 547–568 |
| Lightning Rod | Blocks Electric and attempts to raise Special Attack by one stage. Also redirects eligible single-target Electric moves. | ABILITIES 626–630; TARGETING |
| Motor Drive | Blocks Electric and attempts to raise Speed by one stage. | ABILITIES 632–636 |
| Sap Sipper | Blocks Grass and attempts to raise Attack by one stage. | ABILITIES 638–642 |
| Wonder Guard | Blocks typed damaging moves unless their stored matchup is super effective. Does not provide universal protection from status, hazards, weather, or typeless damage. | ABILITIES 695–708 |
| Bulletproof | Blocks moves with the bomb/ball flag, regardless of attack type. Use the move's actual `bombMove?` flag, not its name. | ABILITIES 585–598 |
| Soundproof | Blocks moves with the sound flag, regardless of type. Use `soundMove?`. | ABILITIES 644–657 |
| Telepathy | Blocks damaging moves from an ally; does not block an opponent's attacks or self-targeting. | ABILITIES 665–679 |
| Dazzling, Queenly Majesty | Opposing positive-priority moves fail when the protected side is targeted. Global blocking path; see the Mold Breaker finding above. | ABILITIES 566–579; USE 283–299 |
| Damp | Prevents the implemented explosive moves, including the Self-Destruct/Explosion and Mind Blown paths. This is a move-use failure, not Normal/Fire type immunity. | MOVES_B Self-Destruct/Explosion; MOVES_C 2377 onward |
| Magic Bounce | Reflects eligible status moves. Does not provide immunity to damaging attacks of that type. Can prevent a reflectable type/ability-changing status move. | SUCCESS 385–402 |

The absorb/stat-boost helpers ignore self-targeting and still block the move when HP or the relevant stat cannot rise. They are called before the damaging-move type-immunity rejection. For example, an ability may absorb an attack despite a separate chart immunity. Sources: HELPERS 523–568; SUCCESS 399–417.

**Exact installed flag-based lists** (MOVE_CATALOG; MOVE_BASE 112–128):

- **Soundproof — 25 sound-flagged moves:** Bug Buzz, Snarl, Parting Shot, Clanging Scales, Disarming Voice, Chatter, Grass Whistle, Boomburst, Hyper Voice, Uproar, Relic Song, Round, Snore, Echoed Voice, Confide, Growl, Heal Bell, Noble Roar, Perish Song, Roar, Screech, Sing, Supersonic, Metal Sound, Sparkling Aria. This is the flag inventory; global/team-targeting moves can use separate ability checks rather than the ordinary targeted-move handler.
- **Bulletproof — 24 bomb-flagged moves:** Pollen Puff, Zap Cannon, Electro Ball, Focus Blast, Aura Sphere, Searing Shot, Beak Blast, Shadow Ball, Energy Ball, Seed Bomb, Bullet Seed, Mud Bomb, Ice Ball, Egg Bomb, Weather Ball, Barrage, Sludge Bomb, Acid Spray, Mist Ball, Rock Wrecker, Rock Blast, Magnet Bomb, Gyro Ball, Octazooka. These are the actual installed flags, including less obvious entries; type conversion does not remove the flag.

### Damage reductions and vulnerabilities

Here “power”, “Defense”, and “final” identify different stages of the formula. Unless listed as a chart effect, these do **not** remove the engine's super-effective classification.

| Ability | Actual modifier | Condition / caveat | Source |
| --- | --- | --- | --- |
| Thick Fat | Fire and Ice **power ×0.5** | Both types, independent of damage category. | ABILITIES 1199–1203 |
| Heatproof | Fire **power ×0.5** | Burn residual damage has a separate Heatproof rule. | ABILITIES 1179–1183; end-of-round status |
| Water Bubble | Fire **final ×0.5** | Also prevents burns and boosts outgoing Water Attack ×2. | ABILITIES 1205–1209, 1105–1109 |
| Dry Skin | Fire **power ×1.25** | Plus Water immunity above. | ABILITIES 1134–1138 |
| Fluffy | Fire **final ×2**; contact **final ×0.5** | Both apply to a Fire contact move, cancelling nominally. Raw contact flag, including the Long Reach caveat. | ABILITIES 1158–1163 |
| Filter, Solid Rock | Super-effective **final ×0.75** | A 2× matchup has a nominal 1.5× combined factor; a 4× matchup has 3×. Still super effective. | ABILITIES 1140–1148 |
| Prism Armor | Same **final ×0.75** | Current call site also skips it under Mold Breaker. | ABILITIES 1215–1221; CALC 285–305 |
| Multiscale | **Final ×0.5** at full HP | Reevaluate between hits; chip damage can remove it. | ABILITIES 1193–1197 |
| Shadow Shield | **Final ×0.5** at full HP | Current call site also skips it under Mold Breaker. | ABILITIES 1223–1230; CALC 285–305 |
| Fur Coat | **Defense ×2** | Physical moves, plus function `122` (Psyshock/Psystrike/Secret Sword). | ABILITIES 1165–1169 |
| Marvel Scale | **Defense ×1.5** | Holder has a major status and incoming move is physical. | ABILITIES 1185–1191 |
| Grass Pelt | **Defense multiplier ×1.5** | Grassy Terrain; code does not restrict category or grounding. | ABILITIES 1171–1177 |
| Flower Gift | **Defense multiplier ×1.5** against special moves | Effective Sun/Harsh Sun; available from holder and allied handlers. Damage handler does not require Cherrim, unlike form-changing code. | ABILITIES 1150–1156, 1235–1241 |
| Friend Guard | Ally's incoming **final ×0.75** | Holder protects its ally, not itself. Each eligible allied handler can contribute. | ABILITIES 1243–1247; CALC 300–304 |
| Battle Armor, Shell Armor | Prevent critical hits | Critical-hit protection, not type resistance. Bypassable through the critical calculation's Mold Breaker check. | ABILITIES 1269–1275; CALC 167–210 |
| Unaware | Ignores attacker's offensive stat stages | Does not ignore base stats, move power, types, items, or the other damage multipliers. Offensive Unaware separately ignores the defender's stages. | CALC 242–253 |

### Changes after a hit, or to survival

| Effect | Relevance and limitation | Source |
| --- | --- | --- |
| Color Change | After the move, changes the target to the attack's type when the handler's damage/substitute checks permit. It does not retroactively resist the triggering hit. | ABILITIES 1712–1724 |
| Stamina | Raises Defense one stage after a hit. Changes later physical damage, potentially later hits, not the current type matchup. | ABILITIES 1556–1560 |
| Water Compaction | Raises Defense two stages after a Water hit. **Does not make Water harmless.** | ABILITIES 1580–1585 |
| Weak Armor | Physical hit lowers Defense one stage and raises Speed (one stage at current generation setting). Can worsen later physical damage. | ABILITIES 1587–1600 |
| Sturdy | Survives a qualifying full-HP lethal hit at 1 HP; also blocks one-hit-KO moves in their own path. Does not resist every move. | USAGE 197–210; MOVES_A one-hit-KO effects |
| Disguise | Absorbs a hit for a Mimikyu-containing Pokémon in form 0 with Disguise; substitute is checked first. Its code uses ability identity plus species/form checks, not the generic active-ability helper. | USAGE 158–178, 190–193 |
| Magic Guard | Prevents indirect damage where the `takesIndirectDamage?` gate is used, including entry hazards. Does not reduce ordinary direct attacks. | BATTLER 545–560; SWITCHING 354–381 |
| Overcoat / weather-protecting abilities | Can prevent weather chip. Powder protection from Overcoat is behind the disabled generation-6 branch; it is not currently a general powder immunity. | BATTLER weather-damage predicates; SUCCESS 446–468 |

Healing, Regenerator, weather healing, Poison Heal, contact retaliation, and status immunities affect survival or future states rather than the incoming type multiplier. HP restoration can reactivate full-HP defenses; residual damage can disable them. Water Compaction, Justified, Rattled, Weakness Policy, Absorb Bulb, Cell Battery, and Luminous Moss must not be mistaken for an immunity merely because they react to a particular attack.

## 2. Abilities and forms that change the defensive type chart

| Ability / system | Actual behavior | Source |
| --- | --- | --- |
| Protean | Changes the user to the resolved move type before the attack, subject to the existing move-use checks. No once-per-entry limit appears here. Changes later weaknesses and current STAB. | USE 366–382 |
| Color Change | Replaces types after a qualifying incoming move. | ABILITIES 1712–1724 |
| Multitype + Plates | The Pokémon getters replace each naturally Normal type slot with the held Plate type. Ironmon explicitly adapts this for randomized Multitype; non-Normal slots remain. Do not assume the entire Pokémon becomes a single type. | POKEMON 611–625; IRONMON_ABILITIES 73–90 |
| RKS System + Memories | Silvally-specific form selection from the held Memory. Ironmon restricts this contextual ability to Silvally. | FORMS 505–539; ABILITY_POOL 13–21 |
| Forecast | Castform-specific form changes according to effective weather; suppression/removal can revert its form. | CHANGE 177–202; ABILITY_POOL 13–21 |
| Zen Mode | Darmanitan-specific low-HP form branch; recalculate types/stats for the resulting form. The code tests ability identity here. | CHANGE 219–236 |
| Imposter / Transform | Copies target types, ability, non-HP battle stats, stages and moves through the transformation path. Can acquire a very different defensive profile. | ABILITIES 2250–2265; CHANGE 277–311 |
| Other species/form changes | Evolution, fusion/reversal/unfusion, Mega/form transitions, and species changes can replace types and/or abilities. Recompute from the resulting authoritative battler; do not assume every form transition changes type. | CHANGE 144 onward; INITIALIZE 37–85; FUSION 156–164 |
| Relic Song | Custom Meloetta A/P component/species transformation, including fusions, after a successful use; Sheer Force can prevent that transformation path. | MOVES_A 26–65 |

Ironmon's eligible abilities are not all freely assignable: Forecast, Zen Mode, RKS System and other contextual abilities have exact-species restrictions; Disguise and Shields Down have component restrictions. The presence of a name in those restrictions alone does not prove its battle mechanic exists. Source: ABILITY_POOL 13–30.

Fusion typing is calculated from the **head's first type** (with the Normal/Flying head exception selecting Flying) and the **body's second type**, substituting the body's first type when the second duplicates the selected head type. Reversing components can therefore change weaknesses even without an ability. This is not the union of all four component types. Source: FUSION 156–164.

### Special composite type entries

The following are **single type IDs**, distinct from the normal two defensive types plus an extra type from Forest's Curse/Trick-or-Treat. Their weakness lists contribute 2×, resistance lists 1/2×, immunity lists 0×, and unlisted types 1×. Do not infer multiplied factors from the slash-separated display name. These rows describe the catalog, not current-run availability. Source: TYPE_CATALOG; TYPE `effectiveness`.

| Single type ID | Weak to (2×) | Resists (1/2×) | Immune to |
| --- | --- | --- | --- |
| `ICEFIREELECTRIC` | Fighting, Ground, Water | Ice, Flying, Steel, Electric, Bug, Grass, Fairy, Fire | — |
| `FIREWATERELECTRIC` | Ground, Rock | Bug, Ice, Fairy, Steel, Fire, Flying | — |
| `WATERGROUNDFLYING` | Grass, Ice | Steel, Fire, Poison, Fighting, Bug | Electric, Ground |
| `GHOSTSTEELWATER` | Electric, Ground | Water, Flying, Rock, Bug, Steel, Psychic, Ice, Dragon, Fairy | Normal, Fighting, Poison |
| `FIREWATERGRASS` | Rock, Flying, Poison | Fire, Ice, Fairy, Steel, Water, Grass | — |
| `GRASSSTEEL` | Fighting, Fire | Normal, Rock, Ghost, Steel, Grass, Psychic, Dragon, Dark, Fairy, Water, Electric | Poison |
| `BUGSTEELPSYCHIC` | Fire | Psychic, Normal, Steel, Grass, Ice, Dragon, Fairy, Fighting | Poison |
| `ICEROCKSTEEL` | Ground, Fire, Water, Rock | Psychic, Normal, Grass, Ice, Dragon, Fairy | Poison |
| `PSYCHICFLYING` | Ghost, Dark, Rock, Electric, Ice | Fighting, Psychic, Grass | Ground |
| `GROUNDELECTRICPSYCHIC` | Water, Grass, Ice, Ground, Bug, Ghost, Dark | Poison, Rock, Flying, Steel, Fighting, Psychic | Electric |

`QMARKS` and `QMARK` have empty defensive matchup lists. None of the installed defensive lists names a composite ID as an attacking weakness/resistance/immunity, so a composite **attacking** type is chart-neutral; that does not make the same ID neutral when **defending**. There is no `SHADOW` entry in this decoded 30-type catalog, despite the generic Shadow calculation branch in the engine. The composite entries are not marked pseudo-types, so code which filters only `pseudo_type` does not necessarily exclude them.

Literal type checks also do not split these names: `pbHasType?(:FLYING)` does not recognize a lone `WATERGROUNDFLYING` ID as Flying. Consequently its authored Ground immunity is not the ordinary Flying contribution which Gravity, Strong Winds or Thousand Arrows special-case. The same caution applies to literal Water checks in Freeze-Dry and literal Rock checks in Sandstorm. Sources: BATTLER 371–374; CALC 32–81; MOVES_C 741–744, 1015–1018.

## 3. Moves that directly alter weaknesses or immunity

### Replacing, adding, removing, or copying types

| Move | Effect in this implementation | Source |
| --- | --- | --- |
| Conversion | At the active generation setting, picks randomly among the user's move types not already held; not restricted to the first move. Replaces existing types. | MOVES_A 1643–1675 |
| Conversion 2 | Replaces the user's types with a random different type that resists or is immune to the target's last-used move type. | MOVES_A 1677–1719 |
| Camouflage | Replaces types from terrain first, otherwise battle environment. Environments cover Grass, Water, Rock/Ground, Bug, Ice, Fire, Ghost, Flying, Dragon and Psychic; fallback Normal. | MOVES_A 1721–1792 |
| Soak | Replaces the target's types with Water. | MOVES_A 1795–1812 |
| Reflect Type | Copies the target's current types, including the extra type through `pbChangeTypes(target)`. | MOVES_A 1815–1849; CHANGE 122–139 |
| Transform | Copies the target's battle identity attributes as described above. | MOVES_A 2084–2115; CHANGE 277–311 |
| Trick-or-Treat | Sets the extra type slot to Ghost, retaining base types. | MOVES_C 1341–1358 |
| Forest's Curse | Sets the extra type slot to Grass, retaining base types. It replaces an existing extra Ghost type; the two effects do not create four types. | MOVES_C 1360–1377 |
| Roost | Removes Flying until round end when its healing effect executes. A pure Flying Pokémon becomes Normal for that period. Other airborne sources still matter. | MOVES_B 2493–2503; BATTLER 352–368 |
| Burn Up | Removes Fire through a battler flag; a pure Fire Pokémon can become typeless. A subsequent type replacement clears the flag. | MOVES_C 2005–2023; BATTLER 352–368; CHANGE 137–138 |
| Relic Song | Can change Meloetta component/form typing; species-aware rather than a generic type assignment. | MOVES_A 26–65 |

Type-changing move checks generally reject Multitype/RKS System using `canChangeType?`. This is an ability-ID check rather than a generic “currently active ability” check. Base types, Roost/Burn Up masks, and the extra type slot are handled separately. The third type is appended **after** Roost/Burn Up remove base types, so state order matters. Sources: BATTLER 352–368, 522–524; CHANGE 122–139.

### Overriding the type matchup

| Move / effect | Result | Source |
| --- | --- | --- |
| Foresight, Odor Sleuth | Remove Ghost's type immunity to Normal/Fighting; other type contributions remain. Also affect evasion handling. | MOVES_B 1004–1015; CALC 38–42 |
| Miracle Eye | Removes Dark's Psychic immunity; other type contributions remain. | MOVES_B 1019–1030; CALC 43–47 |
| Freeze-Dry | Forces the Water defensive contribution to 2×. The override checks defensive Water, not that the resolved attack is still Ice; conversion effects require care. | MOVES_C 1012–1019 |
| Flying Press | Multiplies the primary move matchup by a separate Flying matchup per defending type. The added Flying calculation is a raw type-chart lookup, not a second call through all custom overrides. | MOVES_C 1380–1399 |
| Thousand Arrows | Its shared grounding class permits Ground to hit Flying, bypasses airborne hit rejection, then grounds a successfully affected target. It neutralizes the Flying component and still applies other types. | MOVES_C 733–763; SUCCESS 419–444 |
| Smack Down | Shares that grounding function with Thousand Arrows. The initial Rock hit still uses its Rock matchup; later Ground hits benefit from grounding. | MOVES_C 733–763 |
| Sky Drop | Returns ineffective against a Flying-type target in its own type-modifier method. | MOVES_B 2260–2263 |
| Synchronoise | Fails if user and target share no current type; sharing a type does not remove other immunities. | MOVES_C 912–930 |
| Struggle | Typeless battle move; do not assign it ordinary Normal-vs-Ghost immunity. | GENERIC `PokeBattle_Struggle` |
| Shadow attacks | Special calculation: 2× against non-Shadow Pokémon, 1/2× against Shadow Pokémon, rather than ordinary defensive type multiplication. Does not imply these moves are obtainable in Ironmon. | CALC 69–74 |

Scrappy provides the Foresight-style Ghost immunity override from the **attacker**. Ring Target provides chart-immunity removal from the **defender's item**. Strong Winds neutralizes the Flying type's weaknesses. None of these automatically removes every independent ability immunity. Source: CALC 32–57.

### Becoming airborne or grounded

| Source | Effect / duration | Source |
| --- | --- | --- |
| Flying type | Airborne and chart-immune to Ground unless overridden. | BATTLER 526–536; CALC |
| Levitate | Ability-based airborne source. | BATTLER 532 |
| Air Balloon | Item-based airborne source; its on-hit handler consumes the balloon. | BATTLER 533; ITEMS 1014–1021 |
| Magnet Rise | Airborne for five turns. | MOVES_C 668–688 |
| Telekinesis | Airborne for three turns; also changes accuracy handling. | MOVES_C 692–722 |
| Iron Ball | Grounds while item is active, with the whole-matchup exception described above. | BATTLER 527; CALC 61–64 |
| Ingrain | Grounds the user while the effect remains. | BATTLER 528; MOVES_B 2604 onward |
| Smack Down / Thousand Arrows | Persistently ground the affected target; cancel Magnet Rise/Telekinesis and certain airborne charge states after a successful hit. | MOVES_C 733–763 |
| Gravity | Grounds battlers for five turns; interrupts/prohibits relevant airborne moves. | BATTLER 530; MOVES_C 630–665 |
| Roost | Removes Flying for the turn; does not erase Levitate/Air Balloon/Magnet Rise/Telekinesis. | BATTLER 352–368, 526–536 |

Grounding checks take precedence over airborne sources. Ring Target can remove the Flying chart immunity while a separate Levitate, Air Balloon, Magnet Rise or Telekinesis immunity still blocks the hit. Mold Breaker does not negate Air Balloon. Thousand Arrows is a move-specific escape from the airborne checks. Sources: BATTLER 526–536; CALC 32–64; SUCCESS 419–444.

## 4. Held items

### Direct immunity and resistance items

| Item | Effect | Source |
| --- | --- | --- |
| Air Balloon | Ground immunity while airborne; lost through its on-hit consumption handler. | BATTLER 533; ITEMS 1014–1021 |
| Iron Ball | Grounds; additionally forces Ground-vs-Flying to neutral across the whole matchup. | BATTLER 527; CALC 61–64 |
| Ring Target | Changes otherwise immune defensive type contributions to neutral. Does not suppress abilities. | CALC 34–37 |
| Resist berries | Matching incoming attack final damage ×0.5, then consumption. Current neutral-hit/Unnerve behavior differs from a simple “super-effective only” assumption. | ITEMS 817–970; HELPERS 581–587; USE 800–809 |
| Plates + Multitype | Can change naturally Normal defensive slots; Plate effects on outgoing power are separate. | POKEMON 611–625; IRONMON_ABILITIES 73–90 |
| Memories + RKS System | Change Silvally's form/type; not generic defensive type changers for all holders. | FORMS 505–539 |

The complete resist-berry mapping in the damage handlers is:

| Incoming type | Berry | Incoming type | Berry |
| --- | --- | --- | --- |
| Normal | Chilan | Fire | Occa |
| Water | Passho | Electric | Wacan |
| Grass | Rindo | Ice | Yache |
| Fighting | Chople | Poison | Kebia |
| Ground | Shuca | Flying | Coba |
| Psychic | Payapa | Bug | Tanga |
| Rock | Charti | Ghost | Kasib |
| Dragon | Haban | Dark | Colbur |
| Steel | Babiri | Fairy | Roseli |

Roseli is registered twice with the same effect; this is not two simultaneous reductions. The helper changes final damage, not `typeMod`, so other super-effective-triggered mechanics still see the chart result. No Ripen multiplier or Unnerve gate occurs in this helper.

### Defensive stats and survival items

| Item | Actual local behavior | Source |
| --- | --- | --- |
| Eviolite | Selected defensive stat ×1.5 if the Pokémon's species/form evolution data contains an evolution. In Ironmon, check the actual generated evolution provider rather than assuming the vanilla species list. | ITEMS 868–878 |
| Assault Vest | Selected defensive stat ×1.5 for special-category moves; also restricts choosing status moves. | ITEMS 811–815; SUCCESS 105–115 |
| Deep Sea Scale | Selected defensive stat ×2 for special moves when `isSpecies?(:CLAMPERL)` passes. | ITEMS 860–866 |
| Metal Powder | Selected defensive stat ×1.5 for a Ditto-containing holder that is not transformed. The handler does not restrict damage category. | ITEMS 898–904 |
| Soul Dew | At these settings, selected defensive stat ×1.5 for special moves against Latias/Latios-containing holders, unless the clause disables it. | ITEMS 942–950; BATTLE_SETTINGS 32 |
| Focus Sash | Full-HP lethal-hit survival at 1 HP; consumed. | USAGE 197–214 |
| Focus Band | Chance-based survival. In this code it is nested inside the same `damage == totalhp` branch, limiting its protection to that full-HP path. | USAGE 201–214 |
| Kee / Maranga / Ganlon / Apicot berries, Luminous Moss | Raise defensive stats through their own trigger conditions; do not alter the triggering move's type effectiveness. | ITEMS target-hit and stat-berry handlers |
| Safety Goggles | Weather protection exists; powder immunity in the move-success path requires generation ≥6 and is currently disabled. | BATTLER weather-damage predicates; SUCCESS 454–468 |
| Light Clay, weather rocks, Terrain Extender | Extend the relevant screen/weather/terrain effect; no independent resistance multiplier. | ITEMS duration handlers |

Held-item activity is disabled by **Embargo, Magic Room, or Klutz**. Changing/removing the item through **Knock Off, Thief, Covet, Trick, Switcheroo, Bestow, Fling, Bug Bite, Pluck, Incinerate**, consumption or transfer can remove a defense; **Recycle, Harvest, Pickup, Symbiosis** can restore or transfer one subject to their rules. Do not assume the persistent Pokémon's original held item is still the live battler's item. Source: BATTLER 454–466; MOVES_B 3182–3650; ABILITIES/ITEMS item-handling hooks.

## 5. Weather, terrain, screens, and other field effects

| Field condition | Effect relevant to weaknesses | Setters / removers and caveats | Source |
| --- | --- | --- | --- |
| Sun | Fire final ×1.5, Water final ×0.5. | Sunny Day, Drought; affects all relevant attacks, not just those originally super effective. | CALC 392–413 |
| Rain | Water final ×1.5, Fire final ×0.5. | Rain Dance, Drizzle. | CALC 392–413 |
| Harsh Sun | Water damaging moves fail entirely; Fire still benefits from sun. | Desolate Land; effective-weather suppression matters. | USE 343–364 |
| Heavy Rain | Fire damaging moves fail entirely; Water still benefits from rain. | Primordial Sea. | USE 343–364 |
| Strong Winds | Flying's super-effective contributions become neutral: normally Electric, Ice, Rock. Other type contributions remain. | Delta Stream / field weather; expiry caveat above. | CALC 48–52; BATTLE 706–727 |
| Sandstorm | Rock target's selected defensive stat ×1.5 against special moves, excluding function `122`. | Sandstorm, Sand Stream. Weather chip is separate. | CALC 410–413 |
| Hail | No direct Ice Defense boost in this implementation. Can enable Aurora Veil and change Weather Ball, Forecast, accuracy and residual effects. | Hail, Snow Warning; do not import Snow mechanics. | CALC weather branch; MOVES_C Aurora Veil |
| Electric Terrain | Electric power ×1.5 if attacker is affected by terrain. | Electric Terrain, Electric Surge. | CALC 351–360 |
| Grassy Terrain | Grass power ×1.5 if attacker is affected; Earthquake/Magnitude/Bulldoze have separate halving rules. | Grassy Terrain, Grassy Surge; also Grass Pelt and healing. | CALC 351–360; MOVES_A 1097–1102, 2360–2372; MOVES_B Magnitude |
| Misty Terrain | Dragon power ×0.5 if **target** is affected by terrain. | Misty Terrain, Misty Surge; status prevention is separate. | CALC 359–360 |
| Psychic Terrain | Psychic power ×1.5 if attacker is affected; opposing positive-priority moves fail against affected targets. | Psychic Terrain, Psychic Surge. | CALC 357–358; SUCCESS 301–306 |
| Mud Sport | Electric power ÷3. | At generation 5, tied to a battler flag rather than a five-turn field timer. | MOVES_B 649–676; CALC 326–338 |
| Water Sport | Fire power ÷3. | Same generation-dependent persistence as Mud Sport. | MOVES_B 681–708; CALC 339–350 |
| Reflect | Physical final damage ×0.5 in singles, ×2/3 when the defending side has multiple battlers. | Critical hits, Infiltrator, and screen-bypassing moves ignore the reduction. | CALC 428–452 |
| Light Screen | Same reduction for special moves. | Same bypass rules. | CALC 428–452 |
| Aurora Veil | Same reduction for both categories. Does not stack with Reflect/Light Screen in this `if/elsif` chain. | Hail required to set; duration and bypass rules remain separate. | CALC 428–452; MOVES_C Aurora Veil |
| Wonder Room | Swaps the base Defense and Special Defense getters. | Not a type-chart inversion. | BATTLER 86–98; MOVES_C 933–952 |

`affectedByTerrain?` means **not airborne and not semi-invulnerable**. The attacker's grounding controls offensive terrain boosts, whereas the target's grounding controls Misty Terrain's Dragon reduction and Psychic Terrain's protection. Source: BATTLER 539–543.

**Cloud Nine and Air Lock** make effective weather `None`; this disables weather-dependent matchup/damage effects without deleting stored weather. **Dark Aura / Fairy Aura** boost the corresponding attack's base power by 4/3 globally; **Aura Break** changes that aura multiplier to 2/3. These affect incoming damage without changing the type chart. Sources: BATTLE 672–674; CALC 272–280.

**Brick Break / Psychic Fangs** bypass and remove Reflect, Light Screen and Aurora Veil through their shared move effect. **Defog** can remove screens/hazards under its local generation rules. **Infiltrator** bypasses screen reduction without removing the screen. Sources: MOVES_C 149–178; MOVES_A Defog; CALC 428–452.

## 6. Opponent effects that defeat, strengthen, or redirect a defense

### Ability suppression, replacement, and copying

| Source | Defensive consequence | Source |
| --- | --- | --- |
| Mold Breaker, Teravolt, Turboblaze | Set the battle ability-bypass flag for move execution. Negate ordinary ability immunities and defensive handlers; do not erase actual defensive types or held items. Exceptions/implementation findings above still apply. | BATTLER 518–520; USE 234; CALC 285–305 |
| Sunsteel Strike, Moongeist Beam | Shared effect sets the same bypass flag on normal move use. | MOVES_C 2027–2034 |
| Photon Geyser | Inherits that ability bypass and chooses physical/special using the attacker's battle values. | MOVES_C 2038 onward |
| Gastro Acid | Suppresses eligible target ability; can remove immunity/resistance until reset. | MOVES_A 2063–2081; BATTLER 388–393 |
| Simple Beam, Worry Seed | Replace eligible target ability with Simple / Insomnia. Losing the previous defensive ability can change weaknesses to actual attacks. | MOVES_A 1852–1918 |
| Role Play, Entrainment, Skill Swap | Copy/transfer/swap eligible abilities. Restrictions differ; notably Skill Swap rejects Wonder Guard, while Entrainment uses a different blacklist. | MOVES_A 1921–2060 |
| Trace | Copies an eligible opposing ability on its switch-in check. Reevaluate resulting defenses. | ABILITY_ITEM 70–96 |
| Mummy | Contact can replace the attacker's eligible ability. A player's attack can therefore cost it Levitate or another protection before the next incoming hit. | ABILITIES 1507–1530 |
| Receiver, Power of Alchemy | Acquire an eligible fainted ally's ability. | ABILITIES 2393–2410 |
| Imposter / Transform | Copy the target's ability along with other battle attributes. | CHANGE 277–311 |

Ability suppression is not universal. `unstoppableAbility?` / `ungainableAbility?` and move-specific blacklists must be respected. `abilityActive?` checks fainting and Gastro Acid; do not invent a Neutralizing Gas rule in this installation. Sources: BATTLER 388–451; MOVES_A ability-changing classes.

### Attacker effects that specifically change a matchup or its payoff

| Source | Effect | Source |
| --- | --- | --- |
| Scrappy | Removes Ghost's chart immunity contribution. Does not disable unrelated ability immunities. | CALC 38–42 |
| Tinted Lens | Final damage ×2 when stored matchup is resisted. A 1/2× resistance is nominally offset; 1/4× becomes 1/2×. Does not make an immune hit connect. | ABILITIES 1077–1081; SUCCESS type immunity |
| Neuroforce | Super-effective final damage ×1.25. | ABILITIES 971–977 |
| Expert Belt | Super-effective final damage ×1.2. | ITEMS 529–535 |
| Adaptability / STAB | Matching attack type gets final ×2 with Adaptability, otherwise ×1.5. This is offense, not the defender acquiring a weakness. | CALC 417–426 |
| Aerilate / Pixilate / Refrigerate / Galvanize | Convert Normal moves to Flying/Fairy/Ice/Electric, respectively, and power ×1.2 when the conversion flag applies. This can change which immunity/resistance is relevant. | ABILITIES 714–758, 868–874 |
| Normalize | Converts the attack to Normal; the generation-7 power flag is disabled here. It is not part of the listed `-ate` damage-handler copies. | ABILITIES 738–745, 868–874 |
| Liquid Voice | Changes sound moves to Water. | ABILITIES 732–736 |
| Electrify | Target's attacks become Electric for the rest of the turn. | CALC 21–24; MOVES_C 1402 onward |
| Ion Deluge | Converts Normal attacks to Electric for the turn. Final electric conversion clears the earlier conversion power-boost flag. | CALC 16–24; MOVES_C 1420 onward |
| Weather Ball / Hidden Power / Natural Gift | Resolve attack type from weather / hidden-power rules / berry. | MOVES_B 104–128, 242–251, 434–505 |
| Judgment / Techno Blast / Multi-Attack | Resolve attack type from active Plate / Drive / Memory. These items do not generically change the defender's type. | MOVES_B 713–780 |
| Revelation Dance | Uses the user's first current type. | MOVES_C 2159–2166 |
| Grass Pledge / Fire Pledge / Water Pledge combinations | The combination can supply an override attack type as well as changing power and field effects. Resolve that actual type before checking immunities. | GENERIC 630–675; MOVES_C 89–128 |

Move-specific `pbBaseType` overrides can replace the ordinary ability-conversion method rather than call it. Evaluate the actual move method and then the final `pbCalcType` layer; do not apply every conversion independently and assume they all stack. Sources: CALC 5–26; MOVES_B dynamic-type classes; MOVES_C 2162–2165.

### Other outgoing damage scaling (separate from weaknesses)

The damage handlers also register **Analytic, Blaze, Defeatist, Flare Boost, Flash Fire, Flower Gift, Guts, Huge Power, Pure Power, Hustle, Iron Fist, Mega Launcher, Minus, Plus, Overgrow, Reckless, Rivalry, Sand Force, Sheer Force, Slow Start, Solar Power, Sniper, Stakeout, Steelworker, Strong Jaw, Swarm, Technician, Torrent, Tough Claws, Toxic Boost, Water Bubble**, and allied **Battery / Flower Gift**. Their HP/status/weather/category/contact/stat conditions can change incoming damage. They do not independently edit the defensive chart. The dedicated matchup/conversion handlers are listed above. Source: ABILITIES 868–1129.

Moves which call another move — for example **Nature Power, Metronome, Assist, Sleep Talk, Copycat, Mirror Move, Me First, Instruct** — inherit the called move's relevant rules. **Mimic and Sketch** can change access to those moves. **Snatch** can steal eligible defensive setup; **Follow Me, Rage Powder, Spotlight, Ally Switch**, Lightning Rod and Storm Drain can change which battler is actually hit. These are not new type-chart modifiers, but any per-attack prediction must resolve the actual move, user and target first. Sources: MOVES_A Mimic/Sketch; MOVES_B move-calling/Snatch classes; MOVES_C redirection/Instruct classes; TARGETING.

Ordinary type-boosting held items, Plates/incenses, Gems, Choice Band/Specs, Life Orb, Muscle Band, Wise Glasses, species-specific offensive items, Charge, Helping Hand, Me First, critical hits, Parental Bond's second hit, spread-move reduction, damage variance, and the attacker's burn also alter damage. Their contribution belongs in a damage estimate, not an unconditional weakness label. At the current settings Gems boost power ×1.5; the normal critical multiplier is ×1.5. Sources: ITEMS 442–802; HELPERS 570–579; CALC 315–456.

## 7. Temporary protection, stat changes, and special damage paths

| Mechanic | How it changes the practical outcome | Source |
| --- | --- | --- |
| Protect, Detect, King's Shield, Spiky Shield, Baneful Bunker | Per-turn protection with move-flag/category restrictions. Not universal type immunity. | SUCCESS 329–379; MOVES_B/MOVES_C protection classes |
| Wide Guard, Quick Guard, Mat Block, Crafty Shield | Team protection conditioned on spread targeting, priority, damaging/status category. Current Wide Guard branch protects against damaging spread moves; the later status-move extension is disabled. | SUCCESS 307–384 |
| Feint, Shadow Force, Phantom Force, Hyperspace Hole, Hyperspace Fury | Bypass/remove applicable protection through their flags/effect classes. Protection is distinct from chart and ability immunities. | MOVES_B Feint/Shadow Force; MOVES_C Hyperspace/Phantom Force |
| Substitute | Redirects damage to the substitute and blocks many status effects. The generic sound/Infiltrator bypass requires generation ≥6 and is disabled here; individual moves can still explicitly override `ignoresSubstitute?`. | USAGE 158–188; SUCCESS 471–478; MOVE_BASE 134–140 |
| Fly, Bounce, Dig, Dive, Sky Drop, Shadow Force, Phantom Force | Temporary semi-invulnerability; a hit can miss even when its type matchup is favorable. | SUCCESS 484–533 |
| No Guard, Lock-On, Mind Reader, Future Sight's execution path | Can bypass relevant hit/accuracy or semi-invulnerability checks; do not erase the earlier type/ability immunity checks. | SUCCESS 488–509 |
| Gust, Twister, Thunder, Hurricane, Sky Uppercut, Smack Down, Thousand Arrows | Have airborne-target hit exceptions. Gust/Twister also have damage changes against relevant airborne charge states. | MOVES_A airborne-hit overrides; MOVES_C 727–763 |
| Earthquake, Magnitude, Fissure / Surf, Whirlpool | Relevant underground / underwater hit exceptions; some also double damage against Dig / Dive. Do not confuse a charging move's vulnerability with Ground/Water defensive typing. | MOVES_A Dig/Dive-hit overrides; MOVES_B Magnitude |
| Minimize | Makes selected trampling moves stronger, with generation-dependent accuracy exceptions. This changes vulnerability to specific moves, not a whole attack type. | CALC 95, 453–456; `tramplesMinimize?` overrides |
| Defense / Special Defense stages | Buffs, debuffs, Haze/Clear Smog, Psych Up, Guard Swap/Heart Swap, Topsy-Turvy, Baton Pass, and switching can change the stats used for incoming damage. | CALC 237–253; MOVES_A stat classes; MOVES_C Topsy-Turvy |
| Guard Split / Power Trick / Wonder Room | Change the underlying defensive values used by the formula rather than type matchup. | MOVES_A 1459–1508; MOVES_C 933–952; BATTLER 86–98 |
| Psyshock, Psystrike, Secret Sword | Special-category attacks using Defense, with category-based interactions still relevant. Fur Coat explicitly includes their shared function. | MOVES_C 901–909; ABILITIES 1165–1169 |
| Chip Away, Sacred Sword, Darkest Lariat | Ignore defensive stat stages through their Defense getter. Do not ignore type resistance or ordinary defensive item/ability multipliers. | MOVES_B 1033–1047 |
| Spectral Thief | Steals the target's positive stat stages before damage calculation, potentially removing a Defense boost for the triggering hit. | MOVES_C 1887–1917 |
| Pollen Puff | Heals an ally instead of taking the normal damaging-move path; an opponent remains an attack target. Move flags and immunity hooks still matter. | MOVES_C 2324–2366 |
| Fixed-damage moves | Sonic Boom, Dragon Rage, Seismic Toss, Night Shade, Psywave, Super Fang, Endeavor, Counter, Mirror Coat, Metal Burst, Bide, Final Gambit and Nature's Madness use their fixed/formula paths. The ordinary power/stat/final multiplier pipeline is skipped; hit/type/ability checks can still reject the move. | GENERIC 393–403; individual fixed-damage subclasses |
| One-hit-KO moves | Dedicated success/damage rules, including Sturdy. A numeric type weakness is not a KO probability. | MOVES_A one-hit-KO classes |
| Endure, False Swipe / Hold Back, Focus Sash / Band, Sturdy | Limit lethal HP loss under their own conditions; do not reclassify the hit as resisted. | USAGE 190–221 |
| Stealth Rock | Uses current defensive types in a raw Rock chart lookup, multiplied by max HP / 8. Does not invoke the normal move damage ability/item reduction pipeline. Magic Guard's indirect-damage gate can prevent it. | SWITCHING 354–369 |
| Spikes / Toxic Spikes | Grounding controls exposure; Poison types can absorb Toxic Spikes. These are not ordinary Ground/Poison damaging moves. | SWITCHING 372–394 |

Status-specific and stat-control abilities such as **Simple, Contrary, Clear Body, White Smoke, Full Metal Body, Big Pecks, Flower Veil, Intimidate, Defiant, Competitive, Moody, Download, Beast Boost**, and status immunity/infliction effects belong to the evolving battle-stat/state layer. Ordinary HP recovery, accuracy/evasion, Speed, targeting, PP denial, flinching, and move-disable mechanics are not enumerated move-by-move as “weakness modifiers”; otherwise this audit would become a complete damage, status, and action-availability audit. The type/immunity-specific and dedicated defensive paths are inventoried above.

## 8. Version boundaries and things not to assume

- **No general Snow defense boost:** this installation's weather calculation handles Hail, not modern Snow's Ice Defense mechanic.
- **No automatic Overcoat/Safety Goggles powder protection:** those success-check branches require generation ≥6. Grass powder immunity is enabled through the separate `MORE_TYPE_EFFECTS` setting.
- **No Dark immunity to Prankster assumed:** the relevant success-check branch requires generation ≥7 and is disabled here.
- **No general sound/Infiltrator bypass of Substitute:** the generic bypass requires generation ≥6. Infiltrator's separate screen bypass is active.
- **No Ice-type immunity to Sheer Cold:** that special failure check requires generation ≥7; the current one-hit-KO path does not apply it.
- **No universal super-effective-only berry rule:** use the actual shared helper described above.
- **No type resistance from Water Compaction or Weakness Policy:** they react after damage rather than blocking it.
- **No generic modern ability catalog:** Tar Shot is absent from the move catalog. Ice Face, Libero, Mimicry, Neutralizing Gas, Purifying Salt, Well-Baked Body, Earth Eater, Tera Shell and Ripen are absent from the ability catalog; no corresponding battle implementations were found in the scanned sources. Ironmon contains an Ice Face eligibility restriction, which does not make Ice Face an installed ability. A restriction is insufficient evidence of implemented behavior.
- **No ordinary modern Terastallization or inverse-chart path was identified in the audited calculation.** Reassess if the game is updated or additional scripts are installed.
- **No badge-based defensive boost in normal progression:** the configured Defense/Sp. Def badge thresholds are 999 at the current generation setting. The generic code branch exists but is not normally reachable.

Catalog fingerprints (SHA-256, recorded after read-only inspection):

| File | SHA-256 |
| --- | --- |
| `types.dat` | `5CC760CC5F251018C9A3ABA076E1BE9BB80458C1B76452C7E9622E21AEB6DBF8` |
| `moves.dat` | `673627CD48F3CA75D65283DD2653C4847E57EFD490EE8473BAE9A75C62A9305A` |
| `abilities.dat` | `7B32D666BBF138424CA4A559BDD494BE3D1D26A5F1B6DCE96856E6110A204DBD` |
| `items.dat` | `CD5A0F38691383AA817D550DDCDDEC5A6EB562931318030F0F054138C2C04A92` |

## 9. Tracker implications and implemented boundary

At the original audit boundary, the live snapshot used `battler.pbTypes(true)` for visible types and the local calculator applied an 18-type static chart. The new defensive overview now has a separate game-derived `defensive_overview` payload; it does not repurpose the static move-indicator or aggregate type-coverage calculator. Sources for the original boundary: SNAPSHOTS 123–129, 149, 193; TRACKER_RULES; TRACKER_PRESENTATION.

Additional boundaries:

1. **A type-only function cannot handle move exceptions.** Freeze-Dry, Flying Press, Sky Drop, Synchronoise, grounding, and fixed damage need move-specific context, even without revealing an enemy ability.
2. **The original player-card fields are not defensive authority.** The new overview reads the current player's battler ability and item in battle so Skill Swap/Mummy/Transform and live item changes can alter its defenses. Outside battle it reads the individual Pokémon. Original field source: SNAPSHOTS 131–154.
3. **Outside-battle types use individual getters.** The overview reads `type1` and `type2`, rather than the species-based `pokemon.types` path, preserving the individual Multitype adaptation. Original sources: POKEMON 611–641; IRONMON_ABILITIES 73–90.
4. **Do not collapse arbitrary damage factors into the current enum.** Apart from 1/8× and 8× chart values, real reductions can yield nominal values such as 1.5×, 3×, 0.375× or 0.75×. Chart category, immunity, and damage-factor layers need separate representation.
5. **Preserve hidden information.** An unexplained “immune” or adjusted multiplier can reveal a concealed enemy ability/item just as clearly as its name. Unknown enemy mechanics must remain unknown; use the player's known live state, public field effects, and legitimately revealed opponent information.
6. **Do not call mutating damage methods just to draw a weakness panel.** Resist-berry calculation starts animations and sets consumption state; other handlers also have side effects. The implemented overview derives presentation data without invoking those handlers.

The implemented view is opened by clicking type text on either live card. It combines applicable known factors with each matchup, hides combined neutral results, and uses separate physical/special values or ranges where needed. Original chart values remain separate internally for activation rules such as Filter and resist berries. Full damage predictions still require move power, category, stats, HP, field, targeting, and hit sequence.

`Compact_Effects.rb` projects only suitable rules into source-free protection labels and recovery triggers. Protection labels such as Leech Seed, Powder, and Hail are deduplicated; only affected-move catalogs expand. Recovery labels such as Hail, Rain, and Turn end expand on click to show their separate `healing_amounts`. The view has no ability/source names, type-row explanations, or Details & conditions block. Defense and Special Defense stat stages remain separate card state and do not alter the displayed type factors. Prose-only effects remain outside the compact view; supporting rule data in the payload is not a promise that every audited mechanic has a visible entry.

Hydration extends this compact subset with **Rain → Heals status conditions**. The installed `EORHealingAbility` handler in `Data/Scripts/011_Battle/003_BattleHandlers_Abilities.rb` (Hydration registration) cures major status at turn end during Rain or HeavyRain. Its cure does not restore HP, does not cure confusion, and is not blocked by Heal Block. The presentation describes the available trigger without inspecting concealed enemy status or requiring rain right now; suppressed or unrevealed abilities do not contribute entries.

Implementation entry points are `src/tracker/defense/Catalog.rb`, `Overview.rb`, `Conditions.rb`, `Damage_Profiles.rb`, and `Compact_Effects.rb`, the optional live snapshot field, and the tracker components `PokemonDefenseView` and `DefenseRuleList`. Ruby reads semantic rules from the release catalog; it does not duplicate per-ability lists or derive mechanics by matching English descriptions or serialized rule IDs. The [mechanics manual](../IRONMON_MECHANICS.html#tracker-defense-view) defines the current UI; the [protocol reference](../../tracker/PROTOCOL.md#defensive-overview) defines the payload and privacy boundary.

Subsequent validation used `tools/Test-DefenseOverview.ps1` in the bundled game runtime, including script synchronization, real battler objects, installed move flags, suppression, combined factors, compact effects, and concealed-state invariance. The dedicated app tests use production components, and browser checks verified the protection/recovery disclosures. These checks validate presentation and privacy, not every combat interaction listed by the original audit.

### Defense catalog generation

- **Reviewed input:** `docs/audits/DEFENSE_PRESENTATION_RULES.json` stores the compact subset of this audit as data: ability/item effects, factors, condition expressions, protection labels, recovery fractions, move flags, chart overrides, and public field rules. Change this input when an audit changes the modeled mechanics.
- **Generator:** `tools/generation/Generate-Defense-Presentation.ps1` runs `Export-DefensePresentation.rb` in the bundled game runtime without loading a save. It resolves current public names and affected moves from installed move flags and records source catalog hashes for provenance. It compiles reviewed classifications; it does not infer arbitrary new mechanics from ability descriptions or automatically re-audit changed battle handlers.
- **Generated outputs:** `data/defense_presentation.json` is the runtime catalog; `docs/audits/generated/DEFENSE_PRESENTATION_GENERATED.csv` inventories abilities/items plus nested, scoped, field, and state effects, with conditions and move counts. Structured cells contain JSON. `modeled` still means a definition exists, not a completeness score. Both outputs are reproducible ignored artifacts; do not edit them by hand.
- **Recovery definitions:** `recovery` is a list of events, each with a trigger and typed outcomes. HP fractions, flat HP, damage-relative HP, stored healing, full HP, cures, and duration/stage outcomes are distinct. Timing, chance, single-use flags, berry eligibility, drain boosts, and Heal Block exceptions live in data. `position_recovery` and `linked_recovery` describe public pending counters and recipient links. Legacy `[trigger, fraction-or-label]` pairs remain supported. The wire field remains `healing_amounts`, including independent identical contributions.
- **Additional defenses:** nested `additional` rules, generic global/ally scope, state conditions, and semantic protection implications represent the expanded coverage. Generated move filters use audited flags, functions, categories, and pure predicates, not live battle handlers. Source names and descriptive prose remain outside the compact view.
- **Data decoding:** both generation and runtime use the native UTF-8 JSON parser, not the game's permissive Ruby-evaluating JSON shim. No executable per-ability recovery code is generated or loaded.
- **Release:** `tools/Build-TrackerRelease.ps1` regenerates both outputs alongside the other audits, promotes successful temporary outputs, and verifies that both player packages contain the resulting catalog. Only the runtime catalog is shipped, not maintainer audit inputs or tools.
- **Ordinary build and use:** `tools/Build-Distribution.ps1` reuses the existing catalog, generating it only when absent, and copies it to `Data/Ironmon/defense_presentation.json`. Ruby loads and caches it. Source hashes and audit dates never gate play, builds, or the view after a game update; stale information is acceptable until refreshed. The schema check concerns the data format only. Missing or unreadable data makes only the optional overview unavailable.

For a manual refresh, run from the Ironmon project directory:

```powershell
& ./tools/generation/Generate-Defense-Presentation.ps1
& ./tools/Test-DefenseOverview.ps1
```

The test tool synchronizes the catalog and canonical scripts before exercising the actual bundled runtime. Generation and testing do not create a release. Restart the game after refreshing to load the replacement catalog.

## 10. Source-derived combat verification scenarios

These scenarios were proposed by the original source audit. They are not a claim that every combat case below has been executed; later presentation checks are described separately in section 9:

| Scenario | Source-derived expectation |
| --- | --- |
| Fire into a Fire-weak Thick Fat holder | Super-effective classification remains; Fire power is halved. |
| Water into Dry Skin at full HP | Immune even though no HP can be restored. |
| Ground into Flying/Steel: no item, Iron Ball, then Gravity without Iron Ball | Immune; 1× through the Iron Ball early return; 2× through ordinary grounding. |
| Ground into Levitate plus Air Balloon, with Mold Breaker | Air Balloon still blocks it. |
| Thousand Arrows into Flying/Steel | Flying contributes neutral, Steel contributes 2×; no assumption of whole-hit neutralization. |
| Freeze-Dry into Water/Ground | Both contributions are 2×, giving 4×. |
| Fire into Bug/Steel after Forest's Curse | 8× chart matchup; the new defensive overview retains this factor rather than using the original indicator enum's neutral fallback. |
| Neutral Fire hit into an Occa holder, with and without Unnerve | Both go through the current berry reduction path. |
| Mold Breaker into full-HP Shadow Shield / a Prism Armor weakness | Their handlers are skipped by the outer guard. |
| Special attack into Grass Pelt under Grassy Terrain | Current handler increases the selected defensive stat. |
| Fire contact move into Fluffy, with Long Reach | Current raw contact flag still cancels the nominal Fire vulnerability factor. |
| Delta Stream setter leaves the field | The shown expiry path does not clear Strong Winds. |
| Skill Swap changes the player's Levitate to another ability | Battler defense changes; persistent snapshot ability alone is insufficient. |
| Two hits into Multiscale / after an Air Balloon or berry triggers | Reevaluate HP/item/protection between hits. |

## Source index

Paths identify the local files inspected. Line numbers refer to the source snapshot on the audit date; search by method/handler name if later edits shift them.

- **SETTINGS:** [game settings](<C:/Program Files (x86)/InfiniteFusion2/Data/Scripts/001_Settings.rb:170>).
- **BATTLE_SETTINGS:** [battle settings](<C:/Program Files (x86)/InfiniteFusion2/Data/Scripts/002_BattleSettings.rb:1>).
- **TYPE:** [type and effectiveness primitives](<C:/Program Files (x86)/InfiniteFusion2/Data/Scripts/010_Data/002_PBS data/003_Type.rb:64>).
- **CALC:** [move type and damage calculation](<C:/Program Files (x86)/InfiniteFusion2/Data/Scripts/011_Battle/002_Move/003_Move_Usage_Calculations.rb:1>).
- **MOVE_BASE:** [move flags and predicates](<C:/Program Files (x86)/InfiniteFusion2/Data/Scripts/011_Battle/002_Move/001_PokeBattle_Move.rb:95>).
- **USAGE:** [immunity, damage absorption, and survival](<C:/Program Files (x86)/InfiniteFusion2/Data/Scripts/011_Battle/002_Move/002_Move_Usage.rb:81>).
- **GENERIC:** [generic move classes](<C:/Program Files (x86)/InfiniteFusion2/Data/Scripts/011_Battle/002_Move/004_Move_Effects_Generic.rb:393>).
- **MOVES_A:** [move effects 000–07F](<C:/Program Files (x86)/InfiniteFusion2/Data/Scripts/011_Battle/002_Move/005_Move_Effects_000-07F.rb:1>).
- **MOVES_B:** [move effects 080–0FF](<C:/Program Files (x86)/InfiniteFusion2/Data/Scripts/011_Battle/002_Move/006_Move_Effects_080-0FF.rb:1>).
- **MOVES_C:** [move effects 100–17F](<C:/Program Files (x86)/InfiniteFusion2/Data/Scripts/011_Battle/002_Move/007_Move_Effects_100-17F.rb:1>).
- **ABILITIES:** [ability handlers](<C:/Program Files (x86)/InfiniteFusion2/Data/Scripts/011_Battle/003_BattleHandlers_Abilities.rb:566>).
- **ITEMS:** [item handlers](<C:/Program Files (x86)/InfiniteFusion2/Data/Scripts/011_Battle/004_BattleHandlers_Items.rb:805>).
- **HELPERS:** [shared ability and berry helpers](<C:/Program Files (x86)/InfiniteFusion2/Data/Scripts/011_Battle/002_BattleHandlers.rb:523>).
- **BATTLER:** [active types, ability/item activity, airborne state](<C:/Program Files (x86)/InfiniteFusion2/Data/Scripts/011_Battle/001_Battler/001_PokeBattle_Battler.rb:352>).
- **INITIALIZE:** [battler initialization and reset](<C:/Program Files (x86)/InfiniteFusion2/Data/Scripts/011_Battle/001_Battler/002_Battler_Initialize.rb:37>).
- **CHANGE:** [type, form, and transformation changes](<C:/Program Files (x86)/InfiniteFusion2/Data/Scripts/011_Battle/001_Battler/003_Battler_ChangeSelf.rb:122>).
- **ABILITY_ITEM:** [ability transitions and berry eligibility](<C:/Program Files (x86)/InfiniteFusion2/Data/Scripts/011_Battle/001_Battler/006_Battler_AbilityAndItem.rb:1>).
- **USE:** [move execution and between-hit effects](<C:/Program Files (x86)/InfiniteFusion2/Data/Scripts/011_Battle/001_Battler/007_Battler_UseMove.rb:234>).
- **TARGETING:** [target selection and redirection](<C:/Program Files (x86)/InfiniteFusion2/Data/Scripts/011_Battle/001_Battler/008_Battler_UseMove_Targeting.rb:1>).
- **SUCCESS:** [move success, protection, and immunity checks](<C:/Program Files (x86)/InfiniteFusion2/Data/Scripts/011_Battle/001_Battler/009_Battler_UseMove_SuccessChecks.rb:294>).
- **BATTLE:** [effective weather and weather lifecycle](<C:/Program Files (x86)/InfiniteFusion2/Data/Scripts/011_Battle/003_Battle/002_PokeBattle_Battle.rb:672>).
- **SWITCHING:** [switch-in and entry hazard effects](<C:/Program Files (x86)/InfiniteFusion2/Data/Scripts/011_Battle/003_Battle/006_Battle_Action_Switching.rb:354>).
- **POKEMON:** [persistent Pokémon types and Plate getters](<C:/Program Files (x86)/InfiniteFusion2/Data/Scripts/014_Pokemon/001_Pokemon.rb:611>).
- **FORMS:** [species form handlers](<C:/Program Files (x86)/InfiniteFusion2/Data/Scripts/014_Pokemon/001_Pokemon-related/001_FormHandlers.rb:505>).
- **FUSION:** [fusion type calculation](<C:/Program Files (x86)/InfiniteFusion2/Data/Scripts/052_InfiniteFusion/Fusion/Data/FusedSpecies.rb:156>).
- **IRONMON_ABILITIES:** [randomized ability integration](<C:/Program Files (x86)/InfiniteFusion2/Ironmon/src/randomization/abilities/Runtime_Integration.rb:73>).
- **ABILITY_POOL:** [contextual ability eligibility](<C:/Program Files (x86)/InfiniteFusion2/Ironmon/src/randomization/abilities/Generator.rb:13>).
- **SNAPSHOTS:** [live tracker snapshots](<C:/Program Files (x86)/InfiniteFusion2/Ironmon/src/tracker/live/Snapshots.rb:123>).
- **MOVE_PRESENTATION:** [game-side move power/type presentation](<C:/Program Files (x86)/InfiniteFusion2/Ironmon/src/tracker/live/Move_Power_Presentation.rb:115>).
- **TRACKER_RULES:** [tracker type-effectiveness calculator](<C:/Program Files (x86)/InfiniteFusion2/Ironmon/tracker/src/Ironmon.Tracker.Core/Moves/TypeEffectivenessRules.cs:38>).
- **TRACKER_PRESENTATION:** [tracker move-row presentation](<C:/Program Files (x86)/InfiniteFusion2/Ironmon/tracker/src/Ironmon.Tracker.App/Components/Common/MovePresentation.cs:15>).
- **MECHANICS:** [canonical mechanics manual](<C:/Program Files (x86)/InfiniteFusion2/Ironmon/docs/IRONMON_MECHANICS.html:1268>), consulted for the existing type-only presentation and hidden-information boundary; unchanged by this audit.
- **TYPE_CATALOG:** [installed type data](<C:/Program Files (x86)/InfiniteFusion2/Data/types.dat>), decoded read-only for all 30 entries.
- **MOVE_CATALOG:** [installed move data](<C:/Program Files (x86)/InfiniteFusion2/Data/moves.dat>), decoded read-only for all 680 entries, move-function mappings and flags.
- **ABILITY_CATALOG / ITEM_CATALOG:** [installed ability data](<C:/Program Files (x86)/InfiniteFusion2/Data/abilities.dat>) and [installed item data](<C:/Program Files (x86)/InfiniteFusion2/Data/items.dat>), decoded read-only for identity/presence checks.
