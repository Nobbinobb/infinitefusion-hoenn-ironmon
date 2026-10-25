# Ironmon configuration

Open the tracker's **Play** header action to manage runs, and **Settings** for
tracker preferences. The [mechanics manual](../IRONMON_MECHANICS.html) explains
how the rules affect gameplay.

## Start or continue a run

Start from the game's title screen or main menu. During gameplay, Play asks you
to return there first. The **New run** wizard has three steps:

1. **Save:** select **File A–H**. Replacing an occupied normal or Ironmon file
   requires a separate confirmation; the game's normal backup is retained.
2. **Rules:** choose a profile or set the rules below.
3. **Review:** check the selected file and rules, then start.

Saved runs list only valid Ironmon files, including **Auto 1** and **Auto 2**.
Choose **Continue** to load one. The game's native loading menus also require a
compatible connected tracker for Ironmon saves.

## Saved run rules

| Setting | Choices | Default |
| --- | --- | --- |
| Wild Pokémon | Mixed; Custom fusions only; Normal only | Mixed |
| Trainer Pokémon | Mixed; Custom fusions only; Normal only | Mixed |
| Unfusion | Random component; Player choice | Random component |

Wild and trainer policies are independent. **Random component** commits the
unfusion before revealing the seed-derived component you keep. **Player choice**
shows the two component identities and lets you choose without opening their
summaries. See the [pivot rules](../IRONMON_MECHANICS.html#pivot-system).

Profiles are reusable tracker-local templates. Save a new name to keep another
profile, or reuse a name to update it. Starting a run copies its rules into the
save; later profile edits do not change existing runs. F7 and automatic resets
retain those rules and generate a fresh seed.

## Tracker preferences

Changes in Settings are saved locally and apply automatically. They are separate
from saved run rules and shared seed identities.

**Game session**

- **Start tracker minimized:** off by default. Background services stay active.
- **Automatic reset after a loss:** on by default and changeable during play.
  A loss or draw is archived, then reset after battle and map-event cleanup.
  Wins never reset automatically. A failed reset leaves the completed run locked
  and shows an error rather than silently starting another attempt.

Opening the tracker starts the game once, reusing a normal or performance game
already running from the same installation. Closing the tracker leaves the game
open; active Ironmon play pauses until reconnection. Closing the game does not
cause the tracker to launch it again automatically.

**Starter selection**

- **Autoselect starter:** reveals all three starters and selects the Random Pick,
  or offers a choice when favorites qualify.
- **Maximum starter BST:** an inclusive limit for manual and automatic selection;
  leave it empty for no limit. Autoselect resets when no starter qualifies, and
  those attempts still count.
- **Favorite Clause:** manage your favorite normal species. A fusion qualifies
  when either component is a favorite.

**Evolution graph** controls expansion depth and maximum nodes per row.
**Custom sprite library** reviews and downloads missing or changed sprite sheets;
keep the game closed during synchronization. See [installation](INSTALLATION.md).

## Controls

Under **Settings > Controls**, choose Keyboard or a controller profile, then
select an action's binding. Press and release the desired key, button or
combination and choose **Use binding**. Each action has two binding slots.
**Clear binding** removes one; **Restore profile defaults** resets the selected
profile. Review any displayed conflict warnings before using the bindings.

## Shared seeds

Use the separate **Seeded runs** header action to export or import tokens. Import
requires an active Ironmon attempt and confirmation before replacing it. F7 and
automatic resets generate fresh seeds rather than replaying an imported seed.
See [sharing a seeded run](INSTALLATION.md#share-a-seeded-run).
