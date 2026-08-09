# Step 3.4 Part 1 validation

## Review boundary

This part adds the audited native evolution, taxonomy, conceptual-branch,
effective-method, and normal-target catalogs. It does not generate or install
randomized destinations, add save metadata, or alter an evolution check.

## Embedded-runtime catalog pass

The automated suite ran after normal game-data initialization inside Pokemon
Infinite Fusion 2's bundled Ruby runtime.

- [x] All 576 registered normal base species were audited exactly once.
- [x] The current game data contains no separately registered mechanical-form
  records; the catalog retains form-aware identity and mechanical-difference
  handling for future records.
- [x] The native graph contains 269 sources, 303 method entries, and 287
  conceptual branches.
- [x] Stage classification produced 196 first-stage, 73 intermediate, 214
  final, and 93 standalone species.
- [x] Native connected-family membership was built for every audited species,
  and the directed native graph passed cycle validation.
- [x] All 303 native method entries were accounted for by their conceptual
  branches and effective triggers.
- [x] The 12 methods used by current data are `Level` (222), `Item` (59),
  `HasMove` (7), `LevelDay` (4), `LevelNight` (3), `TradeItem` (2), and one
  each of `AttackGreater`, `AtkDefEqual`, `DefenseGreater`, `DayHoldItem`,
  `Ninjask`, and `Shedinja`.
- [x] All nine currently used converted entries were converted: seven
  `HasMove` entries use their source role's replacement level, while two
  `TradeItem` entries become held-item level-up triggers with the same item.
- [x] All 60 registered evolution methods received a policy: 43 preserve, 16
  convert, and the invalid `None` method remains outside usable branches.
- [x] The diagnostic catalog reports all 48 registered methods unused by the
  current native evolution data.
- [x] Current data contains no naturally identical effective triggers. An
  isolated `Location` plus `Region` fixture confirmed that both converted
  entries merge into one level-25 trigger while retaining both originals.
- [x] An injected unknown used method failed with its source and destination;
  an injected malformed branch also failed explicitly.
- [x] The normal base-species target catalog contains all 576 valid identities
  with native role, family, and BST data.
- [x] Catalog rebuilding produced identical fingerprints:
  - source: `dc8aeb3996ab777c`;
  - taxonomy: `1cccc43b38942361`;
  - methods: `8844fb3fa681715d`; and
  - normal targets: `ffcf0c68b63b611b`.
- [x] Native outgoing destinations and method entries were byte-for-byte
  equivalent before and after catalog creation and rejection tests.

## Distribution and startup pass

- [x] The canonical source was synchronized through
  `tools/Build-Distribution.ps1` into the distribution and installed game.
- [x] Canonical, distribution, and installed copies have the same SHA-256:
  `935bcbba7b22c5d93d41b3e9be2b52f7cfa0429b461308e54ac309e3db3a5560`.
- [x] With the isolated validator removed, the game loaded normally and
  remained running at the ten-second smoke-test checkpoint.
- [x] Only the exact hidden process started by the smoke test was stopped; no
  pre-existing Infinite Fusion process was present.
- [x] The temporary runtime validator and its output report were removed.
- [x] Repository whitespace validation passed.

## Player validation

No player behavior is introduced in Part 1. Manual evolution testing begins
when generated normal destinations are connected to runtime checks in Part 3.

Part 2 normal graph generation is intentionally outside this review boundary
and must not begin until Part 1 is accepted.
