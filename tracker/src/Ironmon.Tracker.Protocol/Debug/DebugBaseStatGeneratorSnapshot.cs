namespace Ironmon.Tracker.Protocol.Debug;

/// <summary>
/// Describes the active deterministic base-stat generator.
/// </summary>
public sealed class DebugBaseStatGeneratorSnapshot
{
    /// <summary>
    /// Initializes an empty base-stat generator snapshot for protocol serialization.
    /// </summary>
    public DebugBaseStatGeneratorSnapshot()
    {
    }

    /// <summary>
    /// Gets or initializes whether generated base stats are active for this run.
    /// </summary>
    public bool Enabled { get; init; }

    /// <summary>
    /// Gets or initializes the deterministic run seed.
    /// </summary>
    public long? RunSeed { get; init; }

    /// <summary>
    /// Gets or initializes the generator schema version when enabled.
    /// </summary>
    public int? SchemaVersion { get; init; }

    /// <summary>
    /// Gets or initializes the redistribution-rules version.
    /// </summary>
    public int RulesVersion { get; init; }

    /// <summary>
    /// Gets or initializes the minimum generated component stat.
    /// </summary>
    public int MinimumStat { get; init; }

    /// <summary>
    /// Gets or initializes the maximum generated component stat.
    /// </summary>
    public int MaximumStat { get; init; }

    /// <summary>
    /// Gets or initializes the source-stat fingerprint when enabled.
    /// </summary>
    public string? SourceFingerprint { get; init; }
}
