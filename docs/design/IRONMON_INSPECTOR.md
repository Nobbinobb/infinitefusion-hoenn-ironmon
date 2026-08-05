# Ironmon Inspector

This document defines the agreed development-only inspection interface for
Milestone 3 Pokemon data randomization.

Status: **Core implemented; future Milestone 3 pages planned**

The standalone screen is implemented with Overview and Abilities pages. Stats,
Learnset, and Evolutions are added cumulatively when their corresponding
Milestone 3 randomizers are implemented.

## Purpose and access

The Ironmon Inspector is a standalone, summary-style screen for validating the
generated data of any selected Pokemon. Normal Pokemon and fusions use the same
screen. Normal Pokemon display their own generated data directly; fusions add
the displayed-component and inheritance details needed to explain their final
data. The interface must present this information clearly enough to inspect
during ordinary playtesting without exposing it in release play.

With development mode enabled, it is opened from:

1. Pause menu.
2. Pokemon.
3. Select a party Pokemon.
4. Debug.
5. Inspect Ironmon data.

The inspector is not added as another page of the normal Pokemon summary. This
keeps development information out of ordinary play and avoids conflicting with
the summary screen's existing controls.

## Presentation

The screen reuses the visual language of the normal Pokemon summary where
practical:

- The selected Pokemon's name, level, gender, sprite, and held item remain in a
  fixed area on the left.
- The active inspection page and its data appear in a panel on the right.
- Existing game fonts and summary-style colors are reused where practical.
- The inspector draws its complete background and panels by script. It does not
  reuse the normal summary background because that image contains fixed tabs
  and an EXP bar that do not belong in the inspector.
- The page name and page position are aligned inside the inspector's own header.

The Pokemon identity and page heading remain fixed while long content scrolls.
Only the rows in the right-hand data panel move.

Row height is based on the game font's rendered height. Section headers,
selection highlights, labels, and values share the same row geometry so text
cannot overlap the next row or section.

The layout must not reserve empty body/head fields for normal Pokemon. Fusion
component rows and source labels appear only when the selected Pokemon is a
fusion.

## Controls

- **Left/Right:** switch inspection pages.
- **Up/Down:** move the selected row and scroll long lists.
- **Confirm:** show additional details for the selected row when available.
- **Back:** close the inspector and return to the Pokemon Debug menu.

Changing pages resets that page to its first row. A page that fits entirely on
screen does not show or accept unnecessary scrolling. Long pages provide a
clear selected-row highlight and an indication that more rows exist above or
below the visible area.

Up and Down are reserved for inspector scrolling. Party switching remains the
responsibility of the surrounding Pokemon menu; it is not performed inside the
inspector.

## Planned pages

The interface grows cumulatively with Milestone 3:

1. **Overview** displays species and form identity, run seed, generator schema
   version, and pool-rules version or fingerprint. For a fusion, it also
   displays the body and head species.
2. **Abilities** displays original slots, generated slots, final available
   slots, and contextual-eligibility information. For a fusion, it additionally
   displays the component slot and source responsible for each final slot.
   Missing component slots and the removed legacy fallback duplicates are not
   displayed.
3. **Stats** displays original and generated base stats, per-stat differences,
   and base-stat totals. For a fusion, it also displays the component values
   used by the fusion calculation.
4. **Learnset** will display the generated level-up learnset as scrollable rows.
5. **Evolutions** will display generated branches, requirements, targets, and the
   component branch responsible for an evolution when the selected Pokemon is
   a fusion.

A page is hidden until its corresponding randomizer has been implemented. The
inspector must never invent placeholder generated values. Overview and
Abilities are currently available; Stats, Learnset, and Evolutions are hidden.

## Learnset scrolling

The Learnset page is the primary reason the inspector must support scrolling.
Each visible row contains, at minimum:

- learned level or other level-up trigger;
- move name;
- source. A normal Pokemon uses itself as the source; a fusion identifies its
  displayed body or displayed head where relevant.

The selected move can use a fixed footer or a secondary detail window to show
information that does not fit cleanly in the row, such as type, category,
power, accuracy, PP, and the original component entry. The list position is
preserved while viewing and closing those details.

## Data and safety requirements

- The screen reads generated results on demand through the same runtime
  resolution paths used by gameplay. It does not store a second mapping.
- Inspection must not consume random values, reroll data, mutate the Pokemon,
  or alter the run seed.
- Opening and closing the screen repeatedly must produce identical information
  for the same run and selected Pokemon.
- Normal Pokemon pages show their species-owned generated values without
  requiring or displaying fusion components.
- When the selected Pokemon is a fusion, pages identify its displayed body and
  head components and show how each final value was derived from them.
- Rows must use stable ordering so screenshots and validation reports can be
  compared reliably.
- The inspector is available only through development/debug access and is not
  included as an ordinary player-facing information source.

## Implementation direction

The inspector should be implemented as its own scene and screen rather than as
a sixth normal-summary page. It can reuse summary drawing helpers and assets,
but owns its page selection, row selection, scrolling, and detail-window state.
This leaves the normal summary behavior unchanged and allows future inspection
pages to contain more data than one screen can display.
