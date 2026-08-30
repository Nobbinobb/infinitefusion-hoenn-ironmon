# Ironmon 0.8.2

Ironmon 0.8.2 makes authored challenge battles and their lookup data agree,
adds early-Hoenn safeguards, and improves live tracking when more than one
opponent is active.

## Battle scaling and authored encounters

- Increase authored wild and trainer levels by 50%, using half-to-even rounding
  and a level-100 cap in both the actual battle and tracker lookup.
- Show the lowest effective opponent level on voluntary trainer prompts,
  including encounters whose base-game wrapper first scales to the player.
- Fill supported Gym Leader parties with an ascending lower-level sequence
  instead of placing every added member at the leader's maximum level.
- Preserve authored paired-trainer events as one-against-two battles or their
  original two-against-two form when a story partner is present, and record
  progress for both opponents after victory.

## Early-Hoenn and acquisition safeguards

- Make Wally's shortened Petalburg sequence follow the selected trainer policy:
  Normal Only supplies one persistent normal partner, while Mixed and Custom
  Fusions Only visibly use the deterministic materials of the mapped fusion.
- Add repeatable full-party healers in Petalburg Woods and on Route 116 during
  an active Ironmon run.
- Ask for confirmation immediately before an NPC gift or static Pokemon starts
  a mandatory pivot, with No selected by default.
- Strengthen ordinary wild-capture assistance after damage while retaining the
  species catch-rate ordering and the base game's Ball and status modifiers.

## Multi-opponent tracker behavior

- Follow the opponent selected by the game when presenting effectiveness,
  evasion, and other target-dependent move information.
- Let the controller Enemy shortcut cycle through active opposing cards.
- Scope observed enemy PP to the exact battle, battler position, and trainer
  party slot so duplicate species and later encounters never share PP counts.
- Present Infinite Fusion's composite Hidden Power identifiers as Neutral,
  matching their attacking matchup behavior.

## Lookup consistency

- Show the effective Ironmon-scaled level range for wild rows and the effective
  level of revealed trainer-party members.
- Keep simultaneous trainer progress and generated party lookup aligned with
  the battle that actually occurred.

## Compatibility

Ironmon 0.8.2 targets Pokemon Infinite Fusion 2 version 6.8.2. Existing 0.8.1
saves, seeded-run tokens, completed-run recipes, and tracker persistence remain
compatible when their recorded generator metadata matches the installed game
data. Install the game scripts and tracker from the same 0.8.2 archive.
