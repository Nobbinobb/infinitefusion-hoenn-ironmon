namespace Ironmon.Tracker.Access;

/// <summary>
/// Defines the stable diagnostic capability identifiers introduced in Ironmon 0.7.4.
/// </summary>
public static class DiagnosticCapabilities
{
    /// <summary>
    /// Gets permission to keep an attempt active and revive the current player Pokémon after defeat.
    /// </summary>
    public const string DevelopmentAutoRevive = "development.auto_revive";

    /// <summary>
    /// Gets permission to assign any game ability to the current player Pokémon.
    /// </summary>
    public const string DevelopmentChangeAbility = "development.change_ability";

    /// <summary>
    /// Gets permission to replace the current player's moves.
    /// </summary>
    public const string DevelopmentChangeMoves = "development.change_moves";

    /// <summary>
    /// Gets permission to evolve or devolve the current player Pokémon.
    /// </summary>
    public const string DevelopmentEvolution = "development.evolution";

    /// <summary>
    /// Gets permission to fully heal the current player Pokémon.
    /// </summary>
    public const string DevelopmentFullHeal = "development.full_heal";

    /// <summary>
    /// Gets permission to add an item to the player's Bag.
    /// </summary>
    public const string DevelopmentGiveItem = "development.give_item";

    /// <summary>
    /// Gets permission to adjust the current player Pokémon's level.
    /// </summary>
    public const string DevelopmentLevel = "development.level";

    /// <summary>
    /// Gets permission to replace the current player Pokémon from active-run lookup.
    /// </summary>
    public const string DevelopmentSwapPokemon = "development.swap_pokemon";

    /// <summary>
    /// Gets current-player Pokemon availability.
    /// </summary>
    public const string PokemonCurrentPlayer = "pokemon.current_player";

    /// <summary>
    /// Gets current-enemy Pokemon availability.
    /// </summary>
    public const string PokemonCurrentEnemies = "pokemon.current_enemies";

    /// <summary>
    /// Gets arbitrary active-run Pokemon availability.
    /// </summary>
    public const string PokemonAllActive = "pokemon.all_active";

    /// <summary>
    /// Gets Pokemon overview information access.
    /// </summary>
    public const string PokemonOverview = "pokemon.overview";

    /// <summary>
    /// Gets Pokemon ability-generation information access.
    /// </summary>
    public const string PokemonAbilities = "pokemon.abilities";

    /// <summary>
    /// Gets Pokemon base-stat information access.
    /// </summary>
    public const string PokemonBaseStats = "pokemon.base_stats";

    /// <summary>
    /// Gets Pokemon move-access information access.
    /// </summary>
    public const string PokemonMoveAccess = "pokemon.move_access";

    /// <summary>
    /// Gets exact evolution-result information access.
    /// </summary>
    public const string EvolutionResults = "evolution.results";

    /// <summary>
    /// Gets evolution candidate-pool information access.
    /// </summary>
    public const string EvolutionCandidates = "evolution.candidates";

    /// <summary>
    /// Gets evolution-generator diagnostic information access.
    /// </summary>
    public const string EvolutionGeneratorDetails = "evolution.generator_details";

    /// <summary>
    /// Gets active-run wild-encounter information access.
    /// </summary>
    public const string WorldWildEncounters = "world.wild_encounters";

    /// <summary>
    /// Gets active-run trainer-party information access.
    /// </summary>
    public const string WorldTrainerParties = "world.trainer_parties";

    /// <summary>
    /// Gets active-run ground-item result access.
    /// </summary>
    public const string WorldItems = "world.items";

    /// <summary>
    /// Gets arbitrary fusion-preview result access.
    /// </summary>
    public const string FusionPreviewResults = "fusion.preview_results";

    /// <summary>
    /// Gets fusion material-pair information access.
    /// </summary>
    public const string FusionMaterialPairs = "fusion.material_pairs";

    /// <summary>
    /// Gets active-run configuration access.
    /// </summary>
    public const string RunConfiguration = "run.configuration";

    /// <summary>
    /// Gets active-run seed access.
    /// </summary>
    public const string RunSeed = "run.seed";

    /// <summary>
    /// Gets active-run generator-manifest access.
    /// </summary>
    public const string RunGeneratorManifests = "run.generator_manifests";

    /// <summary>
    /// Gets tracker protocol-history access.
    /// </summary>
    public const string TrackerProtocolHistory = "tracker.protocol_history";

    /// <summary>
    /// Gets raw tracker-state access.
    /// </summary>
    public const string TrackerRawState = "tracker.raw_state";

    /// <summary>
    /// Gets persisted tracker-knowledge access.
    /// </summary>
    public const string TrackerPersistedKnowledge = "tracker.persisted_knowledge";
}
