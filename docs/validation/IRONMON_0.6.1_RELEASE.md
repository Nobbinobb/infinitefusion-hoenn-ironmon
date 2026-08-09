# Ironmon 0.6.1 release validation

## Package

- Archive: `release/Ironmon-v0.6.1-evolutions.zip`
- SHA-256: `463c4bd24e6c9aababe68e2265fa7cff5fe2d3e684ceb4ecb455178bc0f1bd4f`
- Size: 72,060,143 bytes
- Entries: 457

## Validation

- [x] The canonical Ruby source was synchronized into the distribution and the
  local Infinite Fusion installation.
- [x] The archive contains Ironmon version 0.6.1 and the self-contained
  `Ironmon Tracker.exe`.
- [x] The tracker Release build completed with zero warnings and zero errors.
- [x] All 40 tracker tests passed.
- [x] Two consecutive release builds produced the identical SHA-256 checksum.
- [x] The game and packaged tracker remained running through a concurrent
  ten-second hidden startup smoke test.
- [x] The sprite-download lock passed bundled-runtime checks for old enabled
  values, direct assignments, and menu exclusion.
- [x] No temporary validation scripts, PDB files, or generated XML
  documentation files are present in the archive.
- [x] Repository whitespace validation passed.

## Result

Ironmon 0.6.1 is ready for installation on Pokemon Infinite Fusion 2 version
6.8.0.
