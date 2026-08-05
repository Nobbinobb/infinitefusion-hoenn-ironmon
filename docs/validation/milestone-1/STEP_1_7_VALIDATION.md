# Step 1.7 validation

## Automated embedded-Ruby checks

- [x] The game starts without a script error.
- [x] Gym Leaders are identified by an explicit list tied to game version
  6.8.0.
- [x] Roxanne expands from two Pokemon to six.
- [x] Added members use Roxanne's highest displayed party level.
- [x] Added members follow Normal Only and Custom Fusions Only trainer policies.
- [x] Added members have valid level-up moves.
- [x] Rebuilding the same leader party retains the stored added species.
- [x] The generated roster survives save serialization.
- [x] A rematch party expands only when it contains fewer than six Pokemon.
- [x] A representative ordinary trainer retains its original party size; the
  Rustboro Gym example remains part of the in-game pass.

The checks ran inside the game's embedded Ruby runtime against installed game
and map data. Roxanne expanded from two to six at displayed level 24. Normal
Only added Dunsparce, Phantump, Dusclops, and Lileep for the test seed; Custom
Fusions Only produced four validated custom-sprite fusions. Rebuilding her team
kept the same roster, a four-member rematch expanded, a six-member rematch was
unchanged, and Youngster Danny retained his one-Pokemon party. The temporary
automated test hook was removed afterward.

## In-game acceptance pass

- [x] Roxanne enters battle with six Pokemon.
- [x] Her four added Pokemon follow the configured trainer fusion policy.
- [x] Her added Pokemon match her highest displayed level.
- [x] Losing and retrying produces the same six-species roster.
- [x] An ordinary Trainer inside Rustboro Gym keeps the original party size.

Step 1.7 is complete. All automated and in-game acceptance checks passed. The
temporary F6 Gym shortcut was removed after the acceptance pass.
