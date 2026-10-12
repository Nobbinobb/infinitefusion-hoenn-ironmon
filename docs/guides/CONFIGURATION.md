# Ironmon configuration

## Tracker startup

Opening the installed tracker always starts its game, retaining an already
running normal or performance game from the same installation. There is no
optional game-start setting. **Settings > Game session > Start tracker minimized**
defaults to Off and keeps connection and background work active. Startup waits
for the existing installation compatibility check. Errors restore the tracker
window and show an explanation.

Closing the tracker leaves the game open. An active Ironmon game pauses until a
compatible tracker reconnects. Classic gameplay and the main menu remain
available without the tracker. Closing the game does not reopen it automatically.

## Start or continue

Use the **Play header action**. The four tracker tabs remain unchanged. Both
creation and tracker loading work from the game's title screen (Press a key)
or main menu. Title-screen actions skip the native menu and startup messages.
During gameplay, Play asks you to return to the title screen or main menu.

New Ironmon runs can only start through the tracker. Choose a manual file from
**File A** through **File H**, configure the rules, and review the result.
Occupied normal or Ironmon saves require a separate overwrite confirmation.
The game checks menu identity and exact save revision, stages and validates the
new native save before replacement, keeps the normal backup, and restores the
old save and starter checkpoint if creation fails.

Continue lists only valid Ironmon files, including **Auto 1** and **Auto 2**.
The game's own Continue and save-loading menus also require a compatible
connected tracker. A disconnect during play pauses the game and attempt timer
without unwinding the battle or event; reconnecting resumes the same attempt.

Shared seeds stay in the separate seed header action. Start an Ironmon run first,
then import into the active attempt. F7 and automatic resets use fresh seeds.

## Game configuration

Ironmon stores an `Ironmon::Configuration` object in `PokemonGlobalMetadata`.
It is serialized automatically with the rest of the save data.

## Schema version 1

The wild and trainer settings use separate stable identifiers:

- `:mixed`
- `:custom_fusions_only`
- `:normal_only`

Both settings default to `:mixed`.

The caught-fusion unfusion setting uses two stable identifiers:

- `:random_component` (default)
- `:player_choice`

Random Component commits the unfusion and then reveals one seed-derived
component. Player Choice shows only the two component identities and allows one
to be selected without opening either summary.

Invalid or missing values fall back to their documented defaults rather than
preventing a save from loading.

Automatic reset is a live tracker preference, enabled by default under
**Settings > Game session**. It can be changed during play and is excluded from
profiles and save-bound rules. Changing it does not change seed identity. A loss
or draw archives the completed attempt and schedules the checkpoint reset after
the battle and its map event finish native cleanup. A successful automatic
reset has no in-game confirmation message. If checkpoint loading, generation,
or saving fails, the completed run stays locked and the game shows the
actionable error. Wins never reset automatically.

A save without an `ironmon_configuration` value receives a schema-version-1
configuration with all defaults. A present serialized configuration must already
match schema version 1 and contain valid fields; incompatible saved objects are
rejected rather than migrated. Hash-shaped values remain accepted as explicit
new configuration input, such as a validated seeded-run import.

Before an F7 reset, Ironmon snapshots the current configuration. It restores
that snapshot after loading the pre-starter checkpoint and before generating
the new run mappings. The new run receives a different seed while retaining the
same policy and unfusion-setting identifiers. The connected tracker supplies
the live automatic-reset preference independently.
See the [Ironmon Mechanics Manual](../IRONMON_MECHANICS.html#pivot-system) for
the complete pivot and determinism rules.

## Selection flow

The new-run wizard has three steps: Save, Rules, and Review. It uses the
tracker's existing question-mark help popovers beside setting labels. Starter
selection groups autoselect, maximum starter BST, and Favorite Clause in one
settings card. Game session groups minimized startup and automatic reset.
Profiles are reusable tracker-local templates. Save under a new name to keep
another template, or reuse the name to update it. Rules are copied into the
selected game save; later profile edits do not change existing runs. F7 resets
retain the saved rules.

Schema-version-1 saves and seed recipes retain their historical
`automatic_reset` field for compatibility. The game no longer uses that field
to decide loss-reset behavior; the connected tracker's preference controls it.
Historical recipes remain readable without changing their identity.
