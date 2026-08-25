namespace Ironmon.Tracker.Protocol.Lookup;

/// <summary>
/// Describes the deterministic inputs needed to prepare exact fusion assignments.
/// </summary>
public sealed class FusionAssignmentRecipePayload
{
    /// <summary>
    /// Initializes an empty assignment recipe for protocol serialization.
    /// </summary>
    public FusionAssignmentRecipePayload()
    {
    }

    /// <summary>
    /// Gets or initializes the run seed.
    /// </summary>
    public long Seed { get; init; }

    /// <summary>
    /// Gets or initializes the player-fusion generator version.
    /// </summary>
    public int PlayerFusionGeneratorVersion { get; init; }

    /// <summary>
    /// Gets or initializes the fusion-evolution generator version.
    /// </summary>
    public int GeneratorVersion { get; init; }

    /// <summary>
    /// Gets or initializes the fusion-evolution rules version.
    /// </summary>
    public int RulesVersion { get; init; }

    /// <summary>
    /// Gets or initializes the normal evolution source fingerprint.
    /// </summary>
    public required string SourceFingerprint { get; init; }

    /// <summary>
    /// Gets or initializes the evolution taxonomy fingerprint.
    /// </summary>
    public required string TaxonomyFingerprint { get; init; }

    /// <summary>
    /// Gets or initializes the effective-method fingerprint.
    /// </summary>
    public required string MethodFingerprint { get; init; }

    /// <summary>
    /// Gets or initializes the base-stat source fingerprint.
    /// </summary>
    public required string BaseStatSourceFingerprint { get; init; }

    /// <summary>
    /// Gets or initializes the custom target-pool schema version.
    /// </summary>
    public int TargetPoolVersion { get; init; }

    /// <summary>
    /// Gets or initializes the custom target-pool size.
    /// </summary>
    public int TargetPoolSize { get; init; }

    /// <summary>
    /// Gets or initializes the custom target-pool fingerprint.
    /// </summary>
    public required string TargetPoolFingerprint { get; init; }
}
