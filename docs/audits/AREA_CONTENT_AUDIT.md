# Area Content Audit

Generated from the installed Infinite Fusion Hoenn data on 2026-08-13.

## Counting rules

- Audited **113 physical map files**; **35 maps** have encounter tables.
- Trainer count = physical map events containing a direct `pbTrainerBattle` or `pbDoubleTrainerBattle` call. Raw call count is retained separately.
- Encounter slots are authored rows. Base, time, and weather slots are separated because variants may be mutually exclusive; the total is inventory, not simultaneous availability.
- `ground_items_standard` counts visible, non-hidden `pbItemBall` pickups using a character graphic?the normal item-on-the-ground measure. Visible presents and tile-based meteorites are classified separately as special pickups.
- `hidden_items_itemfinder` counts invisible `pbItemBall` events whose names begin with `Item`, matching the game Itemfinder lookup. Custom hidden-named and other inferred candidates remain separate.
- The by-map CSV is authoritative. The by-name CSV only sums maps sharing an editor-visible name.

## Totals

- **108 trainer events** (110 direct battle calls)
- **715 encounter slots** across 286 tables
- **87 normal visible ground-item events**
- **17 special visible pickups** (14 presents and 3 tile pickups)
- **39 Itemfinder-compatible hidden items**
- **7 custom hidden-named events** and **42 total inferred invisible item-ball candidates**

## Maps with audited content

| ID | Area | Trainers | Slots total (base/time/weather) | Ground normal/special | Hidden Itemfinder/custom/candidates |
|---:|---|---:|---:|---:|---:|
| 5 | Route 101 | 0 | 17 (0/12/5) | 0/0 | 0/0/0 |
| 6 | Slateport City | 0 | 14 (12/2/0) | 0/0 | 1/0/1 |
| 7 | Petalburg Town | 0 | 18 (9/0/9) | 1/0 | 0/0/0 |
| 10 | Route 102 | 4 | 28 (16/0/12) | 1/0 | 0/0/0 |
| 11 | Route 103 | 7 | 30 (10/11/9) | 2/0 | 0/0/0 |
| 12 | Route 104 (South) | 3 | 28 (10/2/16) | 1/0 | 3/0/3 |
| 13 | Littleroot Interiors | 0 | 0 (0/0/0) | 0/2 | 0/0/0 |
| 18 | Professor Birch's Lab | 0 | 0 (0/0/0) | 0/1 | 0/0/0 |
| 19 | Route 104 (South) | 1 | 0 (0/0/0) | 0/0 | 0/0/0 |
| 20 | Route 104 (North) | 3 | 41 (24/0/17) | 5/0 | 1/0/1 |
| 21 | EVENT_TEMPLATES | 1 | 0 (0/0/0) | 0/0 | 0/0/0 |
| 27 | Happy Birthday! | 0 | 0 (0/0/0) | 0/1 | 0/0/0 |
| 28 | Rusturf Tunnel | 2 | 5 (5/0/0) | 2/0 | 1/0/1 |
| 30 | Petalburg Woods | 3 | 39 (10/21/8) | 3/0 | 4/0/4 |
| 31 | Route 116 | 10 | 22 (10/5/7) | 6/0 | 3/1/3 |
| 32 | Granite Cave 1F | 0 | 4 (4/0/0) | 1/0 | 0/0/0 |
| 34 | Granite Cave B1F | 0 | 5 (5/0/0) | 1/0 | 0/0/0 |
| 35 | Granite Cave B2F | 0 | 8 (8/0/0) | 3/0 | 0/0/2 |
| 37 | Route 107 | 0 | 22 (11/2/9) | 0/0 | 0/0/0 |
| 38 | Route 108 | 0 | 26 (12/3/11) | 2/1 | 4/0/4 |
| 39 | Route 109 | 8 | 22 (13/3/6) | 4/0 | 7/0/7 |
| 41 | quest_route 104N | 3 | 0 (0/0/0) | 5/0 | 1/0/1 |
| 42 | Orre Desert | 1 | 0 (0/0/0) | 0/0 | 0/0/0 |
| 47 | Rustboro City | 0 | 0 (0/0/0) | 4/0 | 1/0/1 |
| 49 | Route 106 | 2 | 26 (15/2/9) | 1/0 | 3/0/3 |
| 50 | Route 105 | 0 | 23 (12/2/9) | 0/0 | 0/0/0 |
| 51 | Dewford Town | 0 | 23 (12/2/9) | 0/0 | 0/0/0 |
| 58 | Seaside Cottage | 0 | 0 (0/0/0) | 0/1 | 0/0/0 |
| 61 | Petalburg Gym | 0 | 0 (0/0/0) | 0/1 | 0/0/0 |
| 62 | Hidden Clearing | 0 | 40 (11/21/8) | 1/0 | 0/0/0 |
| 65 | Route 115 | 0 | 39 (21/8/10) | 7/3 | 0/0/1 |
| 69 | Hidden Cove | 0 | 32 (20/2/10) | 2/0 | 0/0/0 |
| 70 | Altering Cave | 0 | 2 (2/0/0) | 0/0 | 0/0/0 |
| 71 | Route 110 | 12 | 49 (21/16/12) | 9/0 | 5/0/5 |
| 72 | testing | 0 | 3 (3/0/0) | 0/0 | 0/0/0 |
| 73 | Mauville City | 4 | 0 (0/0/0) | 3/0 | 0/2/0 |
| 74 | Route 117 | 7 | 36 (19/6/11) | 2/0 | 1/0/1 |
| 75 | Mauville City Interiors | 0 | 0 (0/0/0) | 0/0 | 1/0/1 |
| 76 | Route 118 | 4 | 45 (21/7/17) | 2/0 | 2/2/2 |
| 77 | Route 111 (South) | 6 | 22 (13/3/6) | 3/0 | 0/0/0 |
| 78 | Rustboro Gym | 4 | 4 (4/0/0) | 0/1 | 0/0/0 |
| 79 | Dewford Gym | 1 | 3 (3/0/0) | 0/1 | 0/0/0 |
| 80 | Dewford Gym | 6 | 4 (4/0/0) | 0/0 | 0/0/0 |
| 82 | Seashore House | 3 | 0 (0/0/0) | 0/0 | 0/0/0 |
| 83 | Verdanturf Town | 1 | 0 (0/0/0) | 1/0 | 0/0/0 |
| 84 | Verdanturf Interiors | 0 | 0 (0/0/0) | 0/1 | 0/0/0 |
| 88 | Stern's Shipyard | 0 | 0 (0/0/0) | 0/0 | 1/0/1 |
| 90 | Magma Camp | 0 | 0 (0/0/0) | 0/1 | 0/0/0 |
| 93 | QUEST_TEMPLATES | 1 | 0 (0/0/0) | 0/0 | 0/0/0 |
| 94 | Clothing Boutique | 0 | 0 (0/0/0) | 0/1 | 0/0/0 |
| 97 | Illusion Grove | 0 | 12 (12/0/0) | 4/0 | 0/0/0 |
| 99 | Trick House | 0 | 0 (0/0/0) | 1/0 | 0/2/0 |
| 101 | Pokémon Day Care | 0 | 0 (0/0/0) | 0/1 | 0/0/0 |
| 102 | Mauville Gym | 6 | 7 (7/0/0) | 0/1 | 0/0/0 |
| 104 | Route 111 | 0 | 11 (6/5/0) | 0/0 | 0/0/0 |
| 106 | New Mauville | 0 | 0 (0/0/0) | 1/0 | 0/0/0 |
| 107 | Trick House 2 | 2 | 0 (0/0/0) | 3/0 | 0/0/0 |
| 108 | Trick House 1 | 3 | 0 (0/0/0) | 4/0 | 0/0/0 |
| 109 | Cliffside Sanctuary | 0 | 0 (0/0/0) | 1/0 | 0/0/0 |
| 111 | Cliffside Sanctuary | 0 | 0 (0/0/0) | 1/0 | 0/0/0 |
| 112 | Underwater | 0 | 5 (5/0/0) | 0/0 | 0/0/0 |

## Deliverables

- `AREA_CONTENT_AUDIT_BY_MAP.csv`: physical-map audit with trainer, encounter, visible-item, and hidden-item counts.
- `AREA_CONTENT_AUDIT_BY_NAME.csv`: same-name convenience aggregation.
- `AREA_ENCOUNTER_SLOTS.csv`: every authored encounter slot.
- `AREA_GROUND_ITEMS.csv`: every visible pickup with coordinates, item alternatives, pickup classification, event name, and visual evidence.
- `AREA_HIDDEN_ITEM_CANDIDATES.csv`: hidden-item evidence and Itemfinder compatibility.
