# Ironmon Ruby source

The canonical Ruby implementation is grouped by subsystem. Directory names are
for maintainers; they do not determine runtime load order.

- `foundation/` contains shared primitives and runtime dispatch.
- `run/` contains configuration, presets, checkpoints, and attempt lifecycle.
- `randomization/` contains one directory per randomized data domain.
- `encounters/`, `challenge/`, `pivot/`, and `progression/` contain gameplay
  policy and engine integration for those domains.
- `tracker/` contains live transport, completed-run reconstruction, diagnostic
  inspection, area data, and observed metrics.
- `presentation/` contains overlays, markers, selection UI, and sprite fixes.
- `diagnostics/` and `compatibility/` contain runtime checks and version-specific
  compatibility behavior.

`load_order.json` is the authoritative mapping from canonical source paths to
the flat, numbered Ruby filenames installed in `Data/Scripts/997_Ironmon`.
When adding or moving a Ruby source file, update the manifest in the same
change. `tools/Build-Distribution.ps1` rejects missing, duplicated, unlisted,
unsafe, or out-of-order entries.
