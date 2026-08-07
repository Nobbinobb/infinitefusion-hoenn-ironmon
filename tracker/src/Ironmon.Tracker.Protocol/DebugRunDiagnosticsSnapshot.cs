namespace Ironmon.Tracker.Protocol;

/// <summary>
/// Describes game-owned run and randomizer diagnostics in authorized debug mode.
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

    /// <summary>
    /// Gets or initializes the wild randomization policy.
    /// </summary>
    public required string WildPolicy { get; init; }

    /// <summary>
    /// Gets or initializes the trainer randomization policy.
    /// </summary>
    public required string TrainerPolicy { get; init; }

    /// <summary>
    /// Gets or initializes the unfusion policy.
    /// </summary>
    public required string UnfusionSetting { get; init; }

    /// <summary>
    /// Gets or initializes the custom-fusion pool size.
    /// </summary>
    public int CustomFusionPoolSize { get; init; }

    /// <summary>
    /// Gets or initializes the custom-fusion pool fingerprint.
    /// </summary>
    public required string CustomFusionPoolFingerprint { get; init; }

    /// <summary>
    /// Gets or initializes the ability generator schema version.
    /// </summary>
    public int AbilityGeneratorVersion { get; init; }

    /// <summary>
    /// Gets or initializes the ability-pool size.
    /// </summary>
    public int AbilityPoolSize { get; init; }

    /// <summary>
    /// Gets or initializes the ability-pool fingerprint.
    /// </summary>
    public required string AbilityPoolFingerprint { get; init; }

    /// <summary>
    /// Gets or initializes the persisted wild mapping count.
    /// </summary>
    public int WildMappingCount { get; init; }

    /// <summary>
    /// Gets or initializes the persisted trainer mapping count.
    /// </summary>
    public int TrainerMappingCount { get; init; }
}
