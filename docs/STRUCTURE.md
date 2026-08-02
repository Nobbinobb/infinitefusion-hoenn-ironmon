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
4. `002_Custom_Fusion_Pool.rb`: validated, cached custom-fusion candidates.
5. `002_Randomization.rb`: Ironmon preset activation and run setup.
6. `003_Checkpoint_Reset.rb`: checkpoint persistence and F7 reset lifecycle.
7. `003_Species_Generators.rb`: deterministic wild and trainer policy mappings.
8. `004_Encounter_Hooks.rb`: wild, gift, starter, scripted, and trainer hooks.
9. `005_Mode_Hooks.rb`: mode-menu integration and preset activation.
10. `006_Scene_Hooks.rb`: map scene integration for resets and static refreshes.
11. `007_Difficulty_Enforcement.rb`: locked challenge settings and 1.6 scaling.
12. `008_Gym_Leader_Parties.rb`: persistent six-Pokemon Gym Leader expansion.
13. `009_Diagnostics.rb`: run, load, and reset diagnostic records.

Version `0.1.0` supports Infinite Fusion game version `6.8.0`. An unsupported
version emits a startup warning to the debug log without preventing startup.
