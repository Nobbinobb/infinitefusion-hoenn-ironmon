# Ironmon 0.3.3

This patch refines Milestone 3 Step 3.1 ability inspection and fusion slots.

## Ironmon Inspector

- Replaces the message-window ability dump with a standalone summary-style
  development screen.
- Supports normal Pokemon and fusions through scrollable Overview and Abilities
  pages.
- Shows final fusion slots, displayed components, generated component slots,
  source information, generator metadata, and complete row details.
- Draws its full interface by script without inheriting normal-summary tabs or
  EXP graphics.

## Genuine fusion slots

- Includes secondary normal and hidden component abilities only when those
  slots genuinely exist.
- Removes primary, secondary, and hidden fallback duplicates from missing fusion
  slots.
- Preserves stable internal component-slot positions and hides empty positions
  from the inspector.
- Migrates schema-2 metadata and active legacy fallback indexes to schema 3
  without rerolling genuine component assignments.

The allowed pool and fingerprint remain unchanged. This cumulative release
retains all previous Ironmon functionality and targets Pokemon Infinite Fusion
2 version 6.8.0.
