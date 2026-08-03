# Progression support without utility Pokemon

Ironmon generates temporary Pokemon for required story gifts and trades instead
of asking the player to sacrifice the run's only usable Pokemon.

`Ironmon.generate_progression_pokemon` accepts an exact species, a normal-only
or fusion-only category, an eligibility filter, a level, and a deterministic
context identifier. Generated Pokemon are marked `:progression_trade`, do not
count as usable party members, and are removed after the story transaction.

The Wally gift bypasses the original demonstration-catch requirement,
two-party-member requirement, and party selection. This also prevents a
fainted sole Pokemon from blocking the story. Standard exact-species NPC trades
also bypass party selection.
Generic `npcTrade` calls generate a Pokemon satisfying their supplied filter.
If another trade reaches `pbStartTrade` with the real Ironmon Pokemon selected,
the interceptor generates a matching outgoing Pokemon and preserves the real
one. The received Pokemon then follows the normal forced-pivot acquisition
rules.

The generator is seed-consistent and can provide a normal Pokemon, a validated
custom fusion, an exact requested species, or the first deterministic candidate
that satisfies a trade predicate.

Development saves that still contain a `:utility_slave` party member migrate on
load. The old helper is removed from the party and retained in pivot-state
quarantine rather than being deleted.

Field-move progression is handled separately. HM rewards become permanent field
tools, and owned HMs in existing Ironmon saves are transactionally exchanged on
load. See `HM_TOOLS.md` for the complete mapping.
