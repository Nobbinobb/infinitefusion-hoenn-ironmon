# Infinite Fusion item and item-randomization audit

Audit date: 2026-08-16
Audited installation: Infinite Fusion 2, game version declared as 6.8.0
Scope: registered item data, ground pickups, scripted gifts, shops, trainer-held items, TM rewards, and current Ironmon integration

## Executive summary

Infinite Fusion does not independently roll an item at each pickup. At new-game initialization it builds two saved, run-specific permutations:

- one permutation over 476 ordinary items; and
- one permutation over 125 TM items.

Every eligible occurrence of the same authored item therefore resolves to the same replacement for the whole save. Trainer-held items are the exception: each trainer Pokémon samples independently, with replacement, from a fixed 143-item list when the trainer party is constructed.

The ordinary pool is deliberately broad. It preserves neither pocket, use, price, rarity, nor progression value. A Potion can map to a sellable treasure, held item, Mail, evolution item, or zero-price item. Machines, key items, nine invalid items, and four explicit exceptions are excluded.

Ironmon now uses its own deterministic slot generator for new attempts. Ground
items and found non-HM TMs share a full-chaos pool, TM gifts use a TM-only
pool, ordinary gifts remain protected, standard Poke Marts retain only their
existing balls and Repel-family items, and trainer held items remain authored.
Legacy attempts retain their saved base-game mappings.

Item pool rules version 2 removes all Mail, all Apricorns, Exp. Share, and all
nine permanent HM tools in addition to the four version 1 bans. Bans
apply to results, and a banned authored ground source randomizes away. This is
why the Trick House pickup authored as Shears or Lantern no longer appears as
either tool in a generated Lookup result. Converted HM rewards remain
protected so field progression is unchanged.

| Introduced in rules | Ban group | Items | Count |
|---:|---|---|---:|
| 1 | Core exclusions | DNA Splicers, Super Splicers, DNA Reverser, Dynamite | 4 |
| 2 | Mail | Air Mail, Bloom Mail, Brick Mail, Bubble Mail, Flame Mail, Grass Mail, Heart Mail, Mosaic Mail, Snow Mail, Space Mail, Steel Mail, Tunnel Mail | 12 |
| 2 | Apricorns | Black Apricorn, Blue Apricorn, Green Apricorn, Pink Apricorn, Red Apricorn, White Apricorn, Yellow Apricorn | 7 |
| 2 | Experience | Exp. Share | 1 |
| 2 | Permanent HM tools | Shears, Teleporter, Surfboard, Lever, Jetpack, Pickaxe, Scuba Gear, Lantern, Climbing Gear | 9 |
|  | **Total inherited by rules version 3** |  | **33** |

Current rules version 3 retains that 580-item roster and replaces equal
per-item selection with deterministic integer tickets. The audited
classification is exclusive:

| Category | Items | Weight per item | Total tickets | Expected among 146 randomized slots |
|---|---:|---:|---:|---:|
| HP recovery | 15 | 32 | 480 | 13.34 |
| Status and PP recovery | 26 | 20 | 520 | 14.45 |
| General utility and treasure | 122 | 16 | 1,952 | 54.25 |
| Evolution items | 21 | 16 | 336 | 9.34 |
| Poke Balls | 41 | 12 | 492 | 13.67 |
| TMs | 124 | 8 | 992 | 27.57 |
| Battle consumables | 50 | 6 | 300 | 8.34 |
| Held combat items | 181 | 1 | 181 | 5.03 |
| **Total** | **580** |  | **5,253** | **146.00** |

These expected values are audit projections, not quotas or guarantees. Every
physical slot still rolls independently with replacement from the complete
weighted pool. Existing rules version 1 and 2 attempts retain their original
uniform selection.

## Registered item inventory

The authoritative bundled runtime reports 721 unique registered items.

| Bag pocket | Registered | In ordinary shuffle |
|---|---:|---:|
| Items | 266 | 259 |
| Medicine | 62 | 59 |
| Poké Balls | 42 | 40 |
| TMs & HMs | 135 | 0 |
| Berries | 66 | 65 |
| Mail | 12 | 12 |
| Battle Items | 41 | 41 |
| Key Items | 97 | 0 |
| **Total** | **721** | **476** |

Of the 135 machines, 125 are TMs and enter the separate TM permutation. The remaining 10 machines do not enter either base permutation. The complete runtime-derived catalog is in `generated/ITEM_RANDOMIZER_POOL.csv`.

The base registrations contain an important naming anomaly: `HM06` reports
`is_TM? == true` and `is_HM? == false`, so it is one of those 125 base TM-pool
entries despite its HM identifier. The Ironmon policy protects the complete
`HM01`-`HM11` identifier roster explicitly, leaving 124 current non-HM TM gift
results rather than inheriting that misclassification.

The corrected external Ironmon area catalog identifies 141 real ground-pickup
events across the supported installation: 82 ordinary visible pickups, 42
hidden pickups, 14 special presents, and 3 special tile pickups. Six additional
events named like hidden items contain only “nothing here” dialogue and no item
grant; they are decoys and are excluded. This is an event count, not the total
number of all possible item acquisitions; shops, NPC-only gifts, berry
harvests, fishing rewards, mining, Mystery Gifts, and other direct Bag writes
are separate paths.

The current deterministic rules produce 580 eligible ground results, 5,253
ground-selection tickets, and 124 uniformly selected TM-gift results in the
audited installation. Pool membership, category assignments, weights,
projections, and exact ban rows are regenerated in
`generated/ITEM_RANDOMIZATION_GENERATED.csv` by the release pipeline rather
than maintained as hand-edited counts.

## Initialization and persistence

Map 295's Intro event calls `pbShuffleItems` and `pbShuffleTMs` before game-mode selection. This initializes both maps even when the player ultimately chooses a non-randomized mode. The maps live on `PokemonGlobalMetadata` as `randomItemsHash` and `randomTMsHash`, so they are serialized with the save.

Ironmon generates a new Ironmon seed and later calls both shuffle functions again. The resulting item maps use Ruby's ambient random-number generator, not the stored Ironmon seed or Ironmon's deterministic generator classes. Ironmon therefore archives the full maps because the seed alone cannot reconstruct them.

## Ordinary-item algorithm

`itemCanBeRandomized` accepts an item only when all of these are true:

1. it is not a TM, HM, or TR-style machine;
2. it is not a key item;
3. it is not in `INVALID_ITEMS`; and
4. it is not in `RANDOM_ITEM_EXCEPTIONS`.

`pbShuffleItems` deduplicates the accepted IDs, clones the list, and repeatedly samples and removes one remaining result. The result is a bijection over all 476 eligible IDs. Fixed points are allowed, so an item can map to itself.

Consequences:

- repeated occurrences of the same authored item always produce the same result;
- the algorithm shuffles registered item identities, not physical pickup locations;
- items that never appear as authored rewards can still become results;
- campaign frequency is inherited from the authored source item (for example, if Potion is common, whatever Potion maps to becomes common); and
- there is no price, function, pocket, rarity, healing-power, or progression balancing.

### Ordinary exclusions

The invalid list contains Cover Fossil, Plume Fossil, Accuracy Up, Damage Up, Ancient Stone, Full Odd Keystone, Devolution Spray, Invisiball, and Debug Candy.

The explicit exception list contains DNA Splicers, Poké Ball, Dynamite, and Pinkan Berry. These source items remain unchanged and cannot be ordinary-shuffle results.

`NON_RANDOMIZE_ITEMS` is a separate resolution-time list: Cell Battery, Magnet Stone, TM94, and Dynamite. Cell Battery and Magnet Stone are still members of the 476-item generation pool, so other sources can map to them, but occurrences authored as Cell Battery or Magnet Stone refuse their generated mapping. This makes the effective observed behavior no longer a strict permutation and can duplicate those outcomes.

## TM algorithm

`pbShuffleTMs` creates a second without-replacement permutation over the 125 items for which `is_TM?` is true. It does not include HMs or any possible TR-style machine. Fixed points are allowed.

At resolution, a TM source consults `randomTMsHash` only when the global TM switch is on. TM94 is explicitly protected by `NON_RANDOMIZE_ITEMS`, so its authored occurrences remain TM94 even though TM94 participates in the generated TM map and can be the result of another TM.

The unused helper `getRandomGivenTM` references `RANDOM_TM_EXCEPTIONS`, but no definition of that constant exists in the audited scripts. Current pickup/gift paths do not call that helper; activating it would raise an error.

## Acquisition channels

| Channel | Entry point | Behavior |
|---|---|---|
| Visible/hidden ground pickup | `pbItemBall` | Resolves through the ordinary or TM map when the master switch, channel switch, and mapped mode permit it. Quantity is preserved. |
| Scripted NPC/event gift | `pbReceiveItem` | Resolves non-TMs through the ordinary map and TMs through the TM map. The UI itself warns that randomized gifts may make quests impossible. |
| Standard shop | `pbPokemonMart` | Replaces each stock ID directly through the ordinary map. A result is rejected when its price is zero or it is Rare Candy; that stock slot then keeps its original item. |
| Trainer-held item | Trainer party construction | Ignores the authored held item and independently samples one of 143 fixed held items for every trainer Pokémon. Sampling is with replacement and is not a saved source-to-result map. |
| Fishing item reward | `pbItemBall(..., false)` | Explicitly bypasses randomization. |
| HM distribution helper | `pbItemBall(..., false)` | Explicitly bypasses base randomization. Ironmon separately converts HM rewards to tools. |
| Berry harvest, mining, Mystery Gift, held-item transfers, direct Bag writes | `PokemonBag#pbStoreItem` or storage helper | Bypasses item randomization unless the caller randomized the item itself. |

Non-money shops and specialized systems need individual review because only calls routed through the standard `pbPokemonMart` wrapper receive its stock substitution.

## Settings behavior and defects

The item-options screen exposes Found items, Found TMs, Given items, Given TMs, Shop items, and Trainer Held items, underneath a master Items switch.

There is an important coupling defect: changing **Found items** also sets `SWITCH_RANDOM_ITEMS_MAPPED` to the same value. If Found items is off but Given items is on, `SWITCH_RANDOM_ITEMS` is true but mapped mode is false, so `pbGetRandomItem` returns the original gifts. The screen claims given-item randomization is enabled, but it is ineffective. Ironmon avoids this exact defect by explicitly forcing mapped mode on.

The `RandomizerItemOptionsScene` class is duplicated verbatim in the same file. Ruby reopens the same class, so the second definitions replace the first with equivalent behavior; it is maintenance duplication rather than a current functional difference.

The dynamic-randomization switch and helper are effectively dead. `getDynamicRandomItem` immediately returns the original item, and its former implementation is commented out.

## Shop-specific effects

Shop randomization is mapped, but it is not fully consistent with pickup randomization:

- it directly reads the ordinary map rather than calling the general resolver;
- it falls back to the authored stock item when the mapped result has zero price;
- it falls back when the mapped result is Rare Candy;
- original TMs, HMs, key items, or other IDs absent from the ordinary map remain unchanged; and
- the same source item can therefore resolve differently in a shop and on the ground.

The only configured shop-result exclusion is Rare Candy. High-price treasures and other unusual but positive-price items are allowed, including items worth 10,000 or more. Among the 476 ordinary-pool items, 30 have price zero and 31 have prices of at least 10,000.

## Historical Ironmon integration

Earlier Ironmon attempts enabled the base item channels and rebuilt the two
ambient-RNG permutations. Those attempts retain their exact archived item and
TM maps for compatibility. New attempts disable those base switches and use
the deterministic Ironmon generator described above. HM wrappers still run
before item randomization: actual HM rewards become permanent field tools and
pass `canRandom = false`, preventing progression loss.

### Risks identified in the former base integration

1. **Trainer-held items are ambient-RNG results.** They are not derived from trainer identity, party slot, or Ironmon seed and can change whenever a party is reconstructed.
2. **The item maps are not seed-reconstructable.** Exact archival handles completed runs, but seed-only diagnostics or regeneration cannot reproduce them.
3. **NPC gifts are randomized wholesale.** Key items stay safe, but ordinary quest commodities and required non-key items may not; the base UI explicitly acknowledges this risk.
4. **The ordinary pool is progression-blind.** Healing availability, capture resources, evolution items, sell value, and battle-item strength can swing sharply.
5. **Base resolver inconsistencies carry into Ironmon.** Resolution-only exclusions, shop fallbacks, TM94 behavior, and bypassing acquisition paths remain.
6. **The game version is part of the effective ruleset.** Adding or removing registered items changes the pool and therefore changes every ambient shuffle's possible mapping.

## Adopted Ironmon boundary

Ironmon addressed the audit findings with these rules:

1. Generate ground rewards by stable physical slot from the Ironmon seed and a
   versioned, fingerprinted pool rather than by authored item identity.
2. Keep full chaos for eligible ground items and non-HM TMs while applying the
   explicit versioned result bans and per-item integer weights documented
   above.
3. Protect ordinary gifts; randomize non-HM TM gifts within the TM-only pool.
4. Preserve each standard Poke Mart's authored order but filter it to existing
   balls and Repel-family items.
5. Preserve base-game trainer held items.
6. Convert actual HM rewards to protected permanent tools before randomization,
   while randomizing away ground slots authored directly as banned tools.
7. Regenerate the exact pool, ban, slot, TM-gift, and Mart audit as part of the
   release pipeline.

## Primary implementation references

- Base pools and permutations: `Data/Scripts/025-Randomizer/randomizer.rb`
- Resolution and exclusion lists: `Data/Scripts/025-Randomizer/RandomizerUtils.rb`
- Item settings: `Data/Scripts/025-Randomizer/RandomizerSettings.rb`
- Ground and gift entry points: `Data/Scripts/012_Overworld/001_Overworld.rb`
- Standard shop substitution: `Data/Scripts/016_UI/020_UI_PokeMart.rb`
- Trainer-held assignment: `Data/Scripts/010_Data/002_PBS data/013_Trainer.rb`
- Item classification: `Data/Scripts/010_Data/002_PBS data/006_Item.rb`
- Ironmon preset: `Ironmon/src/002_Randomization.rb`
- Ironmon deterministic item generator: `Ironmon/src/003_Item_Randomization_Generation.rb`
- Ironmon item hooks: `Ironmon/src/004_Item_Randomization_Hooks.rb`
- Ironmon HM protection: `Ironmon/src/017_HM_Tools.rb`
- Ironmon completed-run item-map archival: `Ironmon/src/019_Tracker_Post_Run.rb`
