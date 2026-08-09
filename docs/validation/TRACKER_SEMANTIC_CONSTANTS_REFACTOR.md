# Tracker semantic constants refactor validation

Validated on 2026-08-09 against the .NET 10 Windows tracker target.

## Refactor boundary

Behavior-driving literals were moved into responsibility-specific constant
classes. This includes protocol identifiers and policy limits, connection and
storage settings, native application settings, and component state identifiers.

The refactor intentionally does not turn the following into global constants:

- user-facing labels, messages, and accessibility copy;
- CSS measurements, colors, and static markup classes;
- type-chart rows and other explicit domain datasets;
- test fixture values; and
- natural algorithm boundaries such as zero-based indexes and empty counts.

Enums remain the preferred representation where the value is fully owned by
the tracker, such as connection status, debug pages, move category, and primary
tracker view.

## Constant ownership

- `TrackerProtocol`, `TrackerCommands`, and `TrackerEvents` own the shared wire
  contract.
- `TrackerStorageNames`, `TrackerDiagnosticConstants`, and
  `TrackerConnectionConstants` own persistence and connection implementation
  values.
- `TrackerApplicationConstants` owns native application configuration.
- Component-focused classes own move tabs, debug targets, keyboard shortcuts,
  Pokemon value IDs, sprite formats, move sentinels, and shared UI policy
  values.

## Automated validation

- All 35 tracker tests pass.
- The tracker app builds with 0 warnings and 0 errors.
- Protocol command and event literals exist only in their owning constant
  classes.
- Search limits, timeouts, request-ID format, storage paths, and diagnostic
  limits have no remaining inline production values.
