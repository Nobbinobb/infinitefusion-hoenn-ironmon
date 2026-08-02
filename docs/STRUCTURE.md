# Ironmon project structure

Ironmon is maintained outside the game's script directory and packaged into the
game only as Ruby source files.

- `src/` contains the canonical, load-ordered Ruby source files.
- `docs/` contains the roadmap and project documentation.
- `dist/` contains the copy-ready release layout.
- `tools/Build-Distribution.ps1` copies the canonical source into both the
  distribution and the local game installation.

The numeric filename prefixes define load order. Files in
`Data/Scripts/997_Ironmon` must remain Ruby-only because the game evaluates each
file in `Data/Scripts` as Ruby.

## Source responsibilities

1. `001_Core.rb`: constants, runtime state, metadata, and compatibility check.
2. `002_Configuration.rb`: save-backed policy settings and migrations.
3. `002_Configuration_Screen.rb`: transactional policy selection and summary.
4. `002_Pivot_State.rb`: save-backed pivot transactions, mappings, identifiers,
   and Pokemon transformation markers.
5. `002_Custom_Fusion_Pool.rb`: validated, cached custom-fusion candidates.
6. `002_Randomization.rb`: Ironmon preset activation and run setup.
7. `003_Checkpoint_Reset.rb`: checkpoint persistence and F7 reset lifecycle.
8. `003_Species_Generators.rb`: deterministic wild and trainer policy mappings.
9. `004_Encounter_Hooks.rb`: wild, gift, starter, scripted, and trainer hooks.
10. `005_Mode_Hooks.rb`: mode-menu integration and preset activation.
11. `006_Scene_Hooks.rb`: map scene integration for resets and static refreshes.
12. `007_Difficulty_Enforcement.rb`: locked challenge settings and 1.6 scaling.
13. `008_Gym_Leader_Parties.rb`: persistent six-Pokemon Gym Leader expansion.
14. `009_Diagnostics.rb`: run, load, and reset diagnostic records.
15. `010_Pivot_Acquisition.rb`: transactional catches, gifts, trades, party
    enforcement, exclusions, and PC movement safeguards.
16. `011_Blind_Pivot_Interface.rb`: legal-action selection, blind decisions,
    transactional pivot result preparation, and non-cancellable completion.
17. `012_Player_Fusion_Gamble.rb`: unordered-pair player-fusion mappings,
    reversible custom-result validation, orientation, and discovery tracking.
18. `013_Caught_Fusion_Transformations.rb`: acquisition-stable caught-fusion
    component selection and Ironmon-only DNA item blocking.
19. `014_Utility_Slaves.rb`: one persistent HM/gift-only party slot, replacement
    transactions, Wally eligibility, and combat exclusion.

Version `0.1.0` supports Infinite Fusion game version `6.8.0`. An unsupported
version emits a startup warning to the debug log without preventing startup.
