# Step 4.0 game-path coverage audit

Status: **Complete**

Audited against Ironmon `0.6.4` and Pokemon Infinite Fusion 2 version 6.8.0.
This is a gap audit, not a claim that Milestone 4 is complete.

## Audit method

The source review traced every base-game Pokemon acquisition and battle entry
point to its last shared boundary. A temporary scanner then loaded the compiled
game data in the bundled runtime and inspected script commands on every event
page and common event.

The runtime scan covered:

- 113 compiled maps;
- 2,661 map events;
- 4,375 event pages; and
- 250 common events.

It found 26 scripted single-wild calls, two scripted triple-wild calls, 112
scripted ordinary-trainer calls, eight move-tutor battles, seven scaled-level
trainer battles, eight direct gift calls, and two NPC trades. No compiled event
directly calls a Pokemon-object wild battle, custom trainer battle, rematch
battle, Safari battle, Bug Contest battle, roaming battle, or Battle Frontier
entry point; those families are reached through engine callbacks or Ruby-owned
systems instead.

The temporary scanner and report were removed after the inventory completed.

## Coverage matrix

| Path family | Final boundary | Audit result |
| --- | --- | --- |
| Encounter-table wild Pokemon | `PokemonEncounters#setup`, followed by the ordinary wild battle boundary | Covered |
| Single scripted/static wild battle | `pbWildBattle` | Covered |
| Scripted double, triple, 1v2, and 1v3 species battles | Size-specific wrappers in `004_Encounter_Hooks.rb` | Covered |
| Single, 1v2, and 1v3 Pokemon-object battles | `prepare_wild_pokemon` in the corresponding specific wrapper | Covered |
| Pokemon-object double wild battle | Base `pbWildDoubleBattleSpecific` | **Gap** |
| Static and visible-overworld Pokemon | `OverworldPokemonEvent#setup_pokemon`, with a mapped-object guard at battle time | Covered |
| Safari, Bug Contest, and roaming wild battles | Mapped by the outer `pbWildBattle`; object creation reaches `onWildPokemonCreate` | Covered by shared boundaries; focused regression still required |
| Ordinary, placeholder, double, and triple trainers | `GameData::Trainer` mapping plus `onTrainerPartyLoad` final policy/scaling checks | Covered |
| Dynamic rival, Wally, custom, and rematch parties | Cloned battle-party mapping followed by `onTrainerPartyLoad` | Covered |
| Gym Leaders | Ordinary trainer boundary plus deterministic six-member expansion | Covered |
| Move-tutor and scaled-level trainers | Delegate to `pbTrainerBattle` | Covered |
| Standard visible gifts | `tryRandomizeGiftPokemon`, then pivot acquisition interception | Covered |
| Standard catches and direct storage | battle peer, `promptCaughtPokemonAction`, `pbStorePokemon`, and storage boundaries | Covered |
| NPC trades and ordinary trades | generated progression input plus `pbStartTrade` acquisition resolution | Covered |
| Starter | mapped starter selection plus explicit starter acquisition context | Covered |
| PC Porygon and Mystery Gift Pokemon | `pbAddPokemonSilent`, which enters pivot handling without wild-policy mapping | **Gap / policy decision required** |
| Day Care returns, unfusion outputs, eggs, and rentals | Existing-owned, transformation-owned, or explicitly excluded acquisition paths | Covered by documented ownership/exclusion rules |
| Battle Frontier and organized challenge opponents | `pbGenerateBattleTrainer` and `pbOrganizedBattleEx`, outside `onTrainerPartyLoad` | **Gap / availability decision required** |

## Confirmed findings

### 1. Silent permanent gifts can bypass the wild policy

`pbAddPokemonSilent` correctly sends a permanent non-egg Pokemon through the
pivot acquisition transaction, but it deliberately does not call the ordinary
gift randomizer. Most callers require that behavior: starters are already
mapped, Day Care returns an existing Pokemon, and unfusion returns
transformation-owned components.

Two independent permanent-gift callers are different:

- the PC glitch creates a new level-1 Porygon; and
- Mystery Gift can supply a new Pokemon object.

Those Pokemon can enter the pivot prompt with their authored species even when
the selected wild policy would not allow that species. A blanket change to
`pbAddPokemonSilent` would break valid return/transformation paths, so these
callers need explicit acquisition contexts or an explicit exclusion rule.

Severity: **current-path policy gap**. The PC path is available through the
ordinary Pokemon Center PC system. Mystery Gift availability depends on the
player having claimable external gift data.

### 2. Pokemon-object double wild battles have no mapping wrapper

The base game defines `pbWildDoubleBattleSpecific(pokemon1, pokemon2, ...)`, but
Ironmon currently wraps only the single, 1v2, and 1v3 Pokemon-object variants.
Passing unmarked Pokemon objects to the double-specific function therefore
scales them through `onWildPokemonCreate` but does not apply the wild species
policy first.

The compiled Hoenn event scan found no direct caller, so this is not a confirmed
current-map failure. It remains an engine compatibility bypass and should be
covered before Milestone 4 closes.

Severity: **dormant compatibility gap**.

### 3. Organized Battle Frontier opponents bypass trainer boundaries

Battle Frontier opponents are constructed from separate challenge datasets and
started through `pbOrganizedBattleEx`. That path does not trigger
`onTrainerPartyLoad`, so the configured trainer fusion policy, Ironmon's 1.6
level scaler, and Gym Leader expansion do not run. Tracker battle startup still
occurs through the shared battle method, but it observes the unmodified facility
party.

No installed compiled Hoenn event directly enters the Battle Frontier system.
Milestone 4 must nevertheless choose one stable behavior: integrate generated
facility opponents, or prevent facility entry while Ironmon is active and
document the exclusion.

Severity: **dormant special-battle gap with an unresolved availability rule**.

## Existing evidence retained

- Step 1.5 validated all nine wild/trainer policy combinations, standard and
  visible-overworld wild Pokemon, gifts, starters, placeholders, custom/rematch
  parties, and Wally's story fusion.
- Step 1.6 validated exactly-once scaling for wild, ordinary, dynamic, custom,
  rematch, double, and triple parties.
- Step 1.8 exercised representative wild, visible-overworld, gift, starter,
  trainer, placeholder, dynamic/rematch, and Gym Leader paths together.
- Milestone 2 validated the permanent-acquisition transaction, storage
  restrictions, progression gifts/trades, eggs, and transformation ownership.
- Milestone 3 validated generated Pokemon data after direct acquisition,
  pivoting, transformations, save/load, and reseeding.

## Required follow-up matrix

- [x] Disable the PC Porygon encounter and Pokemon Mystery Gift claims during
  Ironmon while retaining item Mystery Gifts and all non-Ironmon behavior.
- [x] Wrap `pbWildDoubleBattleSpecific`; validate that both Pokemon receive
  distinct stable mapping subslots before the native double battle is called.
- [x] Exercise Safari, Bug Contest, and roaming override routing in the bundled
  runtime; each received the once-mapped species before its native battle path.
- [x] Block Battle Frontier and organized challenge battles during Ironmon while
  retaining normal-mode delegation.
- [x] Retain the existing embedded-runtime evidence for ordinary, multi-trainer,
  rematch, Wally, rival, Gym Leader, move-tutor, and scaled-level boundaries;
  the Step 4.0 changes do not alter those paths.
- [x] Canonical, distribution, and installed source hashes match; the game and
  tracker pass clean startup, and all temporary validators and reports are
  removed. Package extraction remains a release-build check, not a manual
  validation queue.

## Audit result

Standard Hoenn event paths are broadly routed through existing shared
boundaries, and the compiled map inventory found no additional bespoke battle
function. The three decisions raised by this audit have been implemented and
validated in the bundled runtime. Alternate wild override routing and all
remaining objective validation items are also closed; no manual validation
queue remains.
