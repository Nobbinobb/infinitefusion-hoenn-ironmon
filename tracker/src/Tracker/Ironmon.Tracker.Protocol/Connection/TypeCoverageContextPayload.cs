namespace Ironmon.Tracker.Protocol.Connection;

/// <summary>
/// Describes the active trainer policy and aggregate pool compatibility metadata used by type coverage.
/// </summary>
public sealed class TypeCoverageContextPayload
{
    /// <summary>
    /// Initializes an empty type-coverage context for protocol serialization.
    /// </summary>
    public TypeCoverageContextPayload()
    {
    }

    /// <summary>
    /// Gets or initializes the active trainer species policy identifier.
    /// </summary>
    public required string TrainerPolicy { get; init; }

    /// <summary>
    /// Gets or initializes the eligible normal-species pool size.
    /// </summary>
    public int NormalPoolSize { get; init; }

    /// <summary>
    /// Gets or initializes the eligible normal-species pool fingerprint.
    /// </summary>
    public required string NormalPoolFingerprint { get; init; }

    /// <summary>
    /// Gets or initializes the eligible custom-fusion pool schema version.
    /// </summary>
    public int FusionPoolSchemaVersion { get; init; }

    /// <summary>
    /// Gets or initializes the eligible custom-fusion pool size.
    /// </summary>
    public int FusionPoolSize { get; init; }

    /// <summary>
    /// Gets or initializes the eligible custom-fusion pool fingerprint.
    /// </summary>
    public required string FusionPoolFingerprint { get; init; }
}
