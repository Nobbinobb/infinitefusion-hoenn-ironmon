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

1. `001_Core.rb`: constants, runtime state, metadata, and compatibility checks.
2. The `002_*` files: configuration, custom-fusion pools, pivot state, and run
   randomization setup.
3. The randomization-focused `003_*` files: ability, base-stat, move-access,
   species, normal-evolution, and fusion-evolution generation and integration.
4. `003_Checkpoint_Reset.rb`: checkpoint persistence and F7 reset lifecycle.
5. `003_Run_Lifecycle.rb`: attempt identity, results, duration, ledger, and
   failed-run enforcement.
6. `004_Encounter_Hooks.rb`: wild, gift, starter, scripted, and trainer hooks.
7. `005_Mode_Hooks.rb` and `006_Scene_Hooks.rb`: mode-menu and map-scene
   integration.
8. `007_Challenge_Failure.rb`: loss/draw completion and locked battle rejection.
9. `007_Difficulty_Enforcement.rb`: locked challenge settings and 1.6 scaling.
10. `008_Challenge_Statistics.rb`: authoritative progress, healing, item,
    trainer encounter, and defeated-BST statistics.
11. `008_Gym_Leader_Parties.rb` through `017_HM_Tools.rb`: difficulty, pivot,
    fusion, progression, acquisition, and permanent field-tool behavior.
12. `018_Tracker_Connection.rb`: local tracker transport, current-state
    recovery, and ordinary live snapshots.
13. `019_Tracker_Post_Run.rb`: completed-run recipes, statistics recovery, and
    authorized deterministic lookup reconstruction.
14. `020_Tracker_Debug.rb`: development-only inspector and diagnostics.
15. `021_Tracker_Move_Access_Metrics.rb` and
    `022_Tracker_Evolution_Metrics.rb`: observed completed-run analysis data.

Version `0.6.5` supports Infinite Fusion game version `6.8.0`. An unsupported
version emits a startup warning to the debug log without preventing startup.
