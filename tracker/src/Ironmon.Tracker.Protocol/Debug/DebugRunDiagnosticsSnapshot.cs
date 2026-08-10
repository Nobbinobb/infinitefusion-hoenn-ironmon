namespace Ironmon.Tracker.Protocol.Debug;

/// <summary>
/// Describes grouped game-owned run and randomizer diagnostics in authorized debug mode.
/// </summary>
public sealed class DebugRunDiagnosticsSnapshot
{
    /// <summary>
    /// Initializes an empty debug diagnostics snapshot for protocol serialization.
    /// </summary>
    public DebugRunDiagnosticsSnapshot()
    {
    }

    /// <summary>
    /// Gets or initializes runtime and run identity diagnostics.
    /// </summary>
    public required DebugRuntimeDiagnosticsSnapshot Runtime { get; init; }

    /// <summary>
    /// Gets or initializes the active run configuration.
    /// </summary>
    public required RunConfigurationPayload Configuration { get; init; }

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
    /// Gets or initializes persisted mapping counts.
    /// </summary>
    public required DebugMappingDiagnosticsSnapshot Mappings { get; init; }
}

/// <summary>
/// Describes runtime identity for an authorized debug run.
/// </summary>
public sealed class DebugRuntimeDiagnosticsSnapshot
{
    /// <summary>
    /// Gets or initializes the Infinite Fusion version.
    /// </summary>
    public required string GameVersion { get; init; }

    /// <summary>
    /// Gets or initializes the Ironmon version.
    /// </summary>
    public required string IronmonVersion { get; init; }

    /// <summary>
    /// Gets or initializes the tracker protocol schema version.
    /// </summary>
    public int ProtocolVersion { get; init; }

    /// <summary>
    /// Gets or initializes the current run identifier.
    /// </summary>
    public string? RunId { get; init; }

    /// <summary>
    /// Gets or initializes the current battle identifier.
    /// </summary>
    public string? BattleId { get; init; }

    /// <summary>
    /// Gets or initializes the active run seed.
    /// </summary>
    public long? RunSeed { get; init; }
}

/// <summary>
/// Describes persisted species mapping counts for an active run.
/// </summary>
public sealed class DebugMappingDiagnosticsSnapshot
{
    /// <summary>
    /// Gets or initializes the persisted wild mapping count.
    /// </summary>
    public int Wild { get; init; }

    /// <summary>
    /// Gets or initializes the persisted trainer mapping count.
    /// </summary>
    public int Trainer { get; init; }
}
