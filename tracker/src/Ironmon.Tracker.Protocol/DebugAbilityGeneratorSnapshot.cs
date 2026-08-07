namespace Ironmon.Tracker.Protocol;

/// <summary>
/// Describes the deterministic ability generator used by an inspected Pokemon.
/// </summary>
public sealed class DebugAbilityGeneratorSnapshot
{
    /// <summary>
    /// Initializes an empty debug generator snapshot for protocol serialization.
    /// </summary>
    public DebugAbilityGeneratorSnapshot()
    {
    }

    /// <summary>
    /// Gets or initializes the active run seed.
    /// </summary>
    public long? RunSeed { get; init; }

    /// <summary>
    /// Gets or initializes the generator schema version.
    /// </summary>
    public int SchemaVersion { get; init; }

    /// <summary>
    /// Gets or initializes the pool-rules version.
    /// </summary>
    public int PoolRulesVersion { get; init; }

    /// <summary>
    /// Gets or initializes the eligible ability-pool size.
    /// </summary>
    public int PoolSize { get; init; }

    /// <summary>
    /// Gets or initializes the ability-pool fingerprint.
    /// </summary>
    public required string PoolFingerprint { get; init; }
}
