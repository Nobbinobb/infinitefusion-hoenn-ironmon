# Ironmon 0.7.0

This release begins the 0.7 quality-of-life improvement cycle with a complete
tracker-assisted starter-selection flow and an optional Favorite Clause.

## Starter tracker

- Displays three hidden starter slots when the bag opens and reveals each
  candidate's sprite, name, and generated BST only when legally visible.
- Marks one stable seed-derived Random Pick without consuming gameplay random
  values.
- Adds a tracker Settings page and persistent Autoselect starter preference.
- Autoselect reveals all starters, waits two seconds, and normally selects the
  Random Pick without player input.

## Favorite Clause

- Adds an unlimited saved favorites list with debounced normal-Pokemon search
  suggestions, duplicate prevention, paging, and removal.
- Treats a fusion as a favorite when either its body or head is saved.
- Marks revealed favorite starters in the tracker.
- Leaves manual starter selection unrestricted.
- When autoselect's Random Pick is not a favorite, displays a named selection
  containing the Random Pick and every qualifying favorite. If the Random Pick
  is a favorite, it is selected automatically as usual.

## Compatibility

- Existing saves and deterministic Pokemon generator schemas remain unchanged
  from 0.6.5.
- Favorite and autoselect settings are tracker-owned and stored outside the
  release directory.
- Targets Pokemon Infinite Fusion 2 version 6.8.0.
