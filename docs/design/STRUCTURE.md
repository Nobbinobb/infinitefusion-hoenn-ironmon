# Ironmon project structure

Ironmon is maintained outside the game's script directory and packaged into the
game only as Ruby source files.

- `src/` contains the canonical, load-ordered Ruby source files.
- `docs/README.md` indexes all project documentation.
- `docs/guides/` contains operational documentation for users and developers.
- `docs/design/` contains stable behavior and architecture specifications.
- `docs/releases/` contains versioned release notes.
- `docs/validation/` contains milestone-specific validation evidence.
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
9. `003_Ability_Randomization.rb`: deterministic ability slots, fusion
   inheritance, evolution fallback, and hidden-ability paths.
10. `004_Encounter_Hooks.rb`: wild, gift, starter, scripted, and trainer hooks.
11. `005_Mode_Hooks.rb`: mode-menu integration and preset activation.
12. `006_Scene_Hooks.rb`: map scene integration for resets and static refreshes.
13. `007_Difficulty_Enforcement.rb`: locked challenge settings and 1.6 scaling.
14. `008_Gym_Leader_Parties.rb`: persistent six-Pokemon Gym Leader expansion.
15. `009_Diagnostics.rb`: run, load, and reset diagnostic records.
16. `010_Pivot_Acquisition.rb`: transactional catches, gifts, trades, party
    enforcement, exclusions, and PC movement safeguards.
17. `011_Blind_Pivot_Interface.rb`: legal-action selection, blind decisions,
    transactional pivot result preparation, and non-cancellable completion.
18. `012_Player_Fusion_Gamble.rb`: unordered-pair player-fusion mappings,
    reversible custom-result validation, orientation, and discovery tracking.
19. `013_Caught_Fusion_Transformations.rb`: acquisition-stable caught-fusion
    component selection and Ironmon-only DNA item blocking.
20. `015_Pivot_Integration.rb`: legacy mapping migration support, Egg-hatch and
    duplicate-evolution pivots, Day Care protection, and temporary-mode party
    eligibility safeguards.
21. `016_Progression_Support.rb`: deterministic temporary Pokemon for required
    gifts and trades without exposing the current party Pokemon.
22. `017_HM_Tools.rb`: HM reward interception and transactional migration to
    permanent field-tool key items.

Version `0.3.3` supports Infinite Fusion game version `6.8.0`. An unsupported
version emits a startup warning to the debug log without preventing startup.
