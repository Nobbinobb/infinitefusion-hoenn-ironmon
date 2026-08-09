# Step 3.4 Part 2 validation

## Review boundary

This part generates and reconstructs the complete normal-species evolution
graph for current Step 3.4 runs. It records only deterministic generator
metadata in the save. Native runtime evolution checks and destinations remain
unchanged until Part 3.

## Embedded-runtime graph pass

The exhaustive suite ran after normal game-data initialization inside Pokemon
Infinite Fusion 2's bundled Ruby runtime.

- [x] All 269 native evolution sources received a complete generated branch
  set containing all 287 conceptual branches.
- [x] Every generated target is a normal base species outside the source's
  native family and differs from the original destination.
- [x] Every generated target has a BST strictly greater than its source.
- [x] Preferred bounds independently matched `ceil(reference BST * 90%)`,
  `floor(reference BST * 115%)`, and the strict-strength-adjusted minimum.
- [x] All 287 branches for seed `123456` found targets inside their preferred
  ranges; the current catalogue required no closest-BST fallback.
- [x] First-stage sources target only intermediate or terminal species, while
  intermediate sources target only finals or standalones.
- [x] Targets and target native families are unique within each source's
  complete branch set.
- [x] The generated graph contains no cycle and every generated path contains
  at most two evolution steps.
- [x] The complete-source solver handled a controlled constrained assignment
  where one branch had only one valid family and another branch had to yield
  that family rather than fail greedily.
- [x] A controlled out-of-range catalogue selected the closest valid stronger
  BST without relaxing stage, family, or strength rules.
- [x] A controlled single-bucket catalogue redistributed an unavailable
  intermediate category to the available terminal category.
- [x] Across 1,000 seeds and every current dual-bucket branch, an intermediate
  original destination selected the intermediate bucket `60.1855%` of the
  time, while a terminal original destination selected it `39.8891%` of the
  time.
- [x] Candidate ordering is based on independent seed/branch/target hashes,
  not candidate counts or BST. A controlled two-candidate sample split exactly
  `50.0000%` across 2,000 seeds.
- [x] Rebuilding seed `123456` produced the same graph and fingerprint
  `eae534d97c7e6f3d` regardless of whether a single source was queried first.
- [x] Seed `654321` produced a different graph with fingerprint
  `8953b532f8cf9c52`.
- [x] Native evolution destinations and method entries were identical before
  and after all graph-generation tests.

## Metadata, legacy, and reconstruction pass

- [x] New-run preparation records generator schema and rules versions; source,
  taxonomy, method, and normal-target fingerprints; and the dependent
  base-stat generator schema and source fingerprint.
- [x] The complete graph is generated eagerly during preparation but is not
  persisted in `PokemonGlobalMetadata` or the save.
- [x] A marshalled metadata round trip reconstructed seed `123456` to the same
  graph fingerprint.
- [x] Re-preparation with the F7 test seed `654321` eagerly produced the
  different expected graph.
- [x] Independent mismatches in all eight metadata fields were rejected with
  the exact incompatible catalogue or version named in the error.
- [x] A pre-Step-3.4 metadata set with all evolution fields absent retained
  native evolution mode and did not activate the generated graph.
- [x] Graph cache suspension and reconstruction did not change any mapping.

## Performance and startup pass

- [x] The initial complete cold generation measurement was approximately
  960 ms for all 287 branches.
- [x] Deterministic seed and catalogue hash prefixes are now calculated once
  per generator, branch, and bucket instead of being rebuilt for every target
  candidate.
- [x] Structurally valid stronger targets are cached once per source rather
  than rescanned separately for each of its conceptual branches.
- [x] Seven optimized cold passes ranged from approximately 121-142 ms, with
  a 127 ms median and 128 ms average.
- [x] Optimized seed `123456` retained graph fingerprint `eae534d97c7e6f3d`,
  and seed `654321` retained `8953b532f8cf9c52`; all 287 mappings and rules
  therefore remained unchanged.
- [x] The average cached lookup across 1,000 calls measured less than
  0.0001 ms.
- [x] Canonical source was synchronized into the distribution and installed
  game through `tools/Build-Distribution.ps1` before validation.
- [x] All 30 canonical Ruby scripts matched their distribution and installed
  copies byte-for-byte.
- [x] With the validator removed, the game loaded normally and remained
  running at the ten-second smoke-test checkpoint.
- [x] Only the exact hidden smoke-test process was stopped; no pre-existing
  Infinite Fusion process was present.
- [x] The temporary runtime validator and its output report were removed.
- [x] Repository whitespace validation passed.

## Player validation

Part 2 does not route gameplay evolution checks to the generated graph, so it
introduces no player-visible evolution behavior. Manual level, item,
friendship, conflict, and state-continuity validation begins in Part 3.

Part 3 runtime integration is intentionally outside this review boundary and
must not begin until Part 2 is accepted.
