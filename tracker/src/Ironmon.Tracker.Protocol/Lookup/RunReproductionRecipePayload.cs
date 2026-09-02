namespace Ironmon.Tracker.Protocol.Lookup;

/// <summary>
/// Carries the deterministic run inputs shared by completed-run reconstruction and seeded-run sharing.
/// </summary>
public abstract class RunReproductionRecipePayload
{
    /// <summary>
    /// Gets or initializes the deterministic run seed.
    /// </summary>
    public long Seed { get; init; }

    /// <summary>
    /// Gets or initializes the immutable generation profile pinned when the run started.
    /// </summary>
    public required string GenerationProfileId { get; init; }

    /// <summary>
    /// Gets or initializes the Infinite Fusion version.
    /// </summary>
    public required string GameVersion { get; init; }

    /// <summary>
    /// Gets or initializes the Ironmon version.
    /// </summary>
    public required string IronmonVersion { get; init; }

    /// <summary>
    /// Gets or initializes the typed run configuration.
    /// </summary>
    public required RunConfigurationPayload Configuration { get; init; }

    /// <summary>
    /// Gets or initializes the classic, remix, or expert data mode.
    /// </summary>
    public string DataMode { get; init; } = "classic";

    /// <summary>
    /// Gets or initializes species-generator compatibility metadata.
    /// </summary>
    public required SpeciesGeneratorRecipePayload SpeciesGenerator { get; init; }

    /// <summary>
    /// Gets or initializes ability-generator compatibility metadata.
    /// </summary>
    public required AbilityGeneratorRecipePayload AbilityGenerator { get; init; }

    /// <summary>
    /// Gets or initializes base-stat-generator compatibility metadata when enabled.
    /// </summary>
    public BaseStatGeneratorRecipePayload? BaseStatGenerator { get; init; }

    /// <summary>
    /// Gets or initializes evolution-generator compatibility metadata when enabled.
    /// </summary>
    public EvolutionGeneratorRecipePayload? EvolutionGenerator { get; init; }

    /// <summary>
    /// Gets or initializes move-access-generator compatibility metadata when enabled.
    /// </summary>
    public MoveAccessGeneratorRecipePayload? MoveAccessGenerator { get; init; }

    /// <summary>
    /// Gets or initializes player-fusion-generator compatibility metadata.
    /// </summary>
    public required PlayerFusionGeneratorRecipePayload PlayerFusionGenerator { get; init; }

    /// <summary>
    /// Gets or initializes deterministic item-slot generator metadata when enabled.
    /// </summary>
    public ItemGeneratorRecipePayload? ItemGenerator { get; init; }
}
