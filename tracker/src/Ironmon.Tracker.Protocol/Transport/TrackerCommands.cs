namespace Ironmon.Tracker.Protocol.Transport;

/// <summary>
/// Defines stable command identifiers sent from the tracker to the game.
/// </summary>
public static class TrackerCommands
{
    /// <summary>
    /// Gets the current-state recovery command.
    /// </summary>
    public const string CurrentState = "current_state";

    /// <summary>
    /// Gets the tracker-settings update command.
    /// </summary>
    public const string UpdateSettings = "update_settings";

    /// <summary>
    /// Gets the guarded active-run reset command.
    /// </summary>
    public const string ResetRun = "reset_run";

    /// <summary>
    /// Requests the active run's explicit seeded-run export recipe.
    /// </summary>
    public const string ExportSeededRun = "export_seeded_run";

    /// <summary>
    /// Gets the transactional seeded-run import command.
    /// </summary>
    public const string ImportSeededRun = "import_seeded_run";

    /// <summary>
    /// Gets the command that selects a bag item as the active battle action.
    /// </summary>
    public const string UseBattleItem = "use_battle_item";

    /// <summary>
    /// Gets the command selecting a revealed, eligible starter in the active scene.
    /// </summary>
    public const string SelectStarter = "select_starter";

    /// <summary>
    /// Gets the connected-game normal-Pokemon search command used by Favorite Clause settings.
    /// </summary>
    public const string FavoritePokemonSearch = "favorite_pokemon_search";

    /// <summary>
    /// Gets the compact area-summary lookup command.
    /// </summary>
    public const string AreaLookupSummary = "area_lookup_summary";

    /// <summary>
    /// Gets the lazy area-detail lookup command.
    /// </summary>
    public const string AreaLookupDetail = "area_lookup_detail";

    /// <summary>
    /// Gets the permission-free active-run calculation command, without diagnostic result queries.
    /// </summary>
    public const string PrepareRunLookup = "prepare_run_lookup";

    /// <summary>
    /// Gets the completed-run Pokemon search command.
    /// </summary>
    public const string PokemonSearch = "pokemon_search";

    /// <summary>
    /// Gets the completed-run Pokemon lookup command.
    /// </summary>
    public const string PokemonLookup = "pokemon_lookup";

    /// <summary>
    /// Gets the bounded completed-run obtainability calculation command.
    /// </summary>
    public const string PokemonObtainability = "pokemon_obtainability";

    /// <summary>
    /// Gets the completed-run evolution-candidate search command.
    /// </summary>
    public const string EvolutionCandidateSearch = "evolution_candidate_search";

    /// <summary>
    /// Gets the completed-run generated-evolution predecessor page command.
    /// </summary>
    public const string EvolutionPredecessorSearch = "evolution_predecessor_search";

    /// <summary>
    /// Gets the completed-run fusion-material page command.
    /// </summary>
    public const string FusionMaterialSearch = "fusion_material_search";

    /// <summary>
    /// Gets the completed-run wild-occurrence page command.
    /// </summary>
    public const string WildOccurrenceSearch = "wild_occurrence_search";

    /// <summary>
    /// Gets the completed-run trainer-occurrence page command.
    /// </summary>
    public const string TrainerOccurrenceSearch = "trainer_occurrence_search";

    /// <summary>
    /// Gets the completed-run fusion preview command.
    /// </summary>
    public const string FusionPreview = "fusion_preview";

    /// <summary>
    /// Gets the authorized current-Pokemon inspection command.
    /// </summary>
    public const string DebugInspectPokemon = "debug_inspect_pokemon";

    /// <summary>
    /// Gets the authorized run-diagnostics command.
    /// </summary>
    public const string DebugRunDiagnostics = "debug_run_diagnostics";

    /// <summary>
    /// Gets the authorized tracker-driven development-control state command.
    /// </summary>
    public const string DebugDevelopmentState = "debug_development_state";

    /// <summary>
    /// Gets the authorized tracker-driven development mutation command.
    /// </summary>
    public const string DebugDevelopmentAction = "debug_development_action";

    /// <summary>
    /// Gets the authorized active-run Pokemon search command.
    /// </summary>
    public const string DebugPokemonSearch = "debug_pokemon_search";

    /// <summary>
    /// Gets the authorized active-run Pokemon lookup command.
    /// </summary>
    public const string DebugPokemonLookup = "debug_pokemon_lookup";

    /// <summary>
    /// Gets the authorized active-run obtainability calculation command.
    /// </summary>
    public const string DebugPokemonObtainability = "debug_pokemon_obtainability";

    /// <summary>
    /// Gets the authorized active-run evolution-candidate search command.
    /// </summary>
    public const string DebugEvolutionCandidateSearch = "debug_evolution_candidate_search";

    /// <summary>
    /// Gets the authorized active-run generated-evolution predecessor page command.
    /// </summary>
    public const string DebugEvolutionPredecessorSearch = "debug_evolution_predecessor_search";

    /// <summary>
    /// Gets the authorized active-run fusion-material page command.
    /// </summary>
    public const string DebugFusionMaterialSearch = "debug_fusion_material_search";

    /// <summary>
    /// Gets the authorized active-run wild-occurrence page command.
    /// </summary>
    public const string DebugWildOccurrenceSearch = "debug_wild_occurrence_search";

    /// <summary>
    /// Gets the authorized active-run trainer-occurrence page command.
    /// </summary>
    public const string DebugTrainerOccurrenceSearch = "debug_trainer_occurrence_search";

    /// <summary>
    /// Gets the authorized active-run fusion preview command.
    /// </summary>
    public const string DebugFusionPreview = "debug_fusion_preview";
}
