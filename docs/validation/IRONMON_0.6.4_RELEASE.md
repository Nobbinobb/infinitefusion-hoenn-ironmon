# Ironmon 0.6.4 release validation

## Package

- Archive: `release/Ironmon-v0.6.4-randomization.zip`
- SHA-256: `6b073324420ae072d52c0d7d8054361e922b4ba7b2afea48dde9986b749ee414`
- Size: 72,073,266 bytes
- Entries: 457

## Milestone regression

- [x] All four Pokemon data randomizers ran together in the bundled game runtime.
- [x] All 576 eligible normal species and all 287 normal evolution branches
  passed cumulative validation.
- [x] Three seeds passed cache reconstruction, marshalled save-state
  reconstruction, and reseed-change checks.
- [x] Live and generated tracker inspection sections passed for normal and fused
  Pokemon.
- [x] Structured and text diagnostics contain the reproduction metadata for all
  generators.
- [x] Step 3.5's exhaustive fusion audit remains valid because 0.6.4 changes no
  generator schema or rule.

## Build and startup validation

- [x] All 33 canonical, distribution, and installed Ruby scripts match byte for
  byte.
- [x] Tracker Release build completed and all 41 tests passed.
- [x] Runtime and tracker metadata report 0.6.4; the tracker executable has file
  version `0.6.4.0`.
- [x] Two release builds produced the same SHA-256 checksum.
- [x] Game and packaged tracker remained running through a concurrent ten-second
  hidden startup smoke test; only the exact started processes were stopped.
- [x] The archive contains no PDB files or temporary validation artifacts.
- [x] Repository whitespace validation passes.

## Result

Step 3.6 and Milestone 3 are complete. Ironmon 0.6.4 is ready for installation
on Pokemon Infinite Fusion 2 version 6.8.0.
