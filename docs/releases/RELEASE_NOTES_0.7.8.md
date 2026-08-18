# Ironmon 0.7.8

Ironmon 0.7.8 refreshes the external tracker for its fixed 500 × 840 window,
reduces scrolling, and makes battle information easier to scan.

## Tracker layout

- Use a fixed, non-resizable 500 × 840 tracker window with a compact icon-only
  header, simplified navigation, and no redundant footer.
- Move keyboard hints before their tab labels and keep the primary views usable
  within the smaller window.
- Organize Archive into an expanded Run archive disclosure and a separate
  collapsed Save slots disclosure. Run content uses Summary, Areas, Pokémon,
  and Analysis tabs; area and entry lists use ten-row paging.
- Divide completed-run statistics into Overview, Trainers, and Items tabs, and
  retain long analysis, settings, access, and protocol content behind compact
  disclosures.

## Battle readability

- Align move power, remaining PP, and accuracy into shared columns with STAB
  badges and compact effectiveness chevrons.
- Color power by visible effectiveness and remaining PP at the configured low
  and critical thresholds. The PP tooltip retains the full current/max value.
- Apply live Accuracy and target Evasion stages to displayed move accuracy and
  show ACC/EVA stage chips with the other battle stages.

## Archive and diagnostics

- Record the save-slot identifier in new statistics snapshots so cumulative
  started, lost, won, and abandoned totals can be separated by slot. Older
  archives remain readable and are not assigned to a guessed slot.
- Collapse starter settings, Favorite Clause, diagnostic grants, raw tracker
  state, persisted knowledge, and individual protocol-history JSON values by
  default.
- Keep all disclosure headers arrow-free and fully clickable for a consistent
  tracker presentation.

## Compatibility

Ironmon 0.7.8 targets Pokemon Infinite Fusion 2 version 6.8.0. Existing 0.7
saves and completed-run recipes remain compatible. The tracker and game scripts
should be updated together so live Accuracy/Evasion stages and save-slot names
are available to the refreshed interface.
