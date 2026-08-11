# Ironmon 0.6.3 release validation

## Package

- Archive: `release/Ironmon-v0.6.3-fusion-integration.zip`
- SHA-256: `14734850b1f7cc7a42649c5169dc71e0a7e36a3358e35ff07dbdd8d4273e93bb`
- Size: 72,073,274 bytes
- Entries: 457

## Fusion integration audit

- [x] Directly encountered and pivot-generated copies of fusion `B132H494`
  exposed identical generated abilities, base stats, move access, specialized
  Fusion Tutor access, and evolution branches despite different personal IDs
  and instance data.
- [x] Keep, reverse, unfuse, player-created fusion, and complete-fusion
  evolution retained valid data and the documented transformation locks.
- [x] Cache clearing and marshalled metadata/Pokemon reload reproduced the same
  fusion data.
- [x] A simulated F7 seed changed generated data and restored same-species
  agreement under the new seed.
- [x] Incompatible ability, base-stat, move-access, and evolution metadata each
  failed explicitly.
- [x] All 174,348 custom-sprite fusion identities passed generated ability,
  base-stat, level-up, Egg, TM, TR, and ordinary-tutor validation.
- [x] All 287 supported conceptual evolution branches passed as both Body and
  Head branches, for 574 complete-fusion branch validations.
- [x] Triple and special fusion boundaries remain outside all four standard-
  fusion randomizers.
- [x] The final bundled-runtime audit completed in 176.025 seconds with no
  production-code failure.

## Build and startup validation

- [x] The canonical Ruby source was synchronized into the distribution and the
  local Infinite Fusion installation.
- [x] All 33 canonical, distribution, and installed Ruby files match byte for
  byte.
- [x] The tracker Release build completed with zero warnings and zero errors.
- [x] All 41 tracker tests passed.
- [x] The runtime and tracker report version 0.6.3; the tracker executable has
  file version `0.6.3.0`.
- [x] Two consecutive release builds produced the identical SHA-256 checksum.
- [x] The final game and packaged tracker both remained running through a
  concurrent ten-second hidden startup smoke test.
- [x] Only the exact game and tracker processes started by the smoke test were
  stopped.
- [x] The archive contains no PDB files or temporary validation artifacts.
- [x] Repository whitespace validation passed.

## Result

Step 3.5 is complete. Ironmon 0.6.3 is ready for installation on Pokemon
Infinite Fusion 2 version 6.8.0.
