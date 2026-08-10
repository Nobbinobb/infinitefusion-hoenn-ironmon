# Ironmon 0.6.2 release validation

## Package

- Archive: `release/Ironmon-v0.6.2-evolutions.zip`
- SHA-256: `e32e3193188aed807ad13bf8dcce25f03777f030318c22916ac2cb6d3c78f67f`
- Size: 72,073,279 bytes
- Entries: 457

## Validation

- [x] The canonical Ruby source was synchronized into the distribution and the
  local Infinite Fusion installation.
- [x] The archive contains Ironmon version 0.6.2 and the self-contained
  `Ironmon Tracker.exe`.
- [x] The tracker and runtime release metadata both report version 0.6.2.
- [x] The tracker Release build completed with zero warnings and zero errors.
- [x] All 41 tracker tests passed.
- [x] Two consecutive release builds produced the identical SHA-256 checksum.
- [x] The game and packaged tracker remained running through a concurrent
  ten-second hidden startup smoke test.
- [x] The archive contains no PDB files, generated XML documentation files, or
  temporary validation artifacts.
- [x] Repository whitespace validation passed.

## Result

Ironmon 0.6.2 is ready for installation on Pokemon Infinite Fusion 2 version
6.8.0.
