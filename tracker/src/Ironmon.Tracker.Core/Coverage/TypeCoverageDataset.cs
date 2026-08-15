namespace Ironmon.Tracker.Core.Coverage;

/// <summary>
/// Describes one release-precalculated type-coverage population dataset.
/// </summary>
public sealed class TypeCoverageDataset
{
    /// <summary>
    /// Initializes an empty dataset for deserialization.
    /// </summary>
    public TypeCoverageDataset()
    {
    }

    /// <summary>
    /// Gets or initializes the aggregate dataset schema version.
    /// </summary>
    public int SchemaVersion { get; init; }

    /// <summary>
    /// Gets or initializes the Infinite Fusion version supplying type data.
    /// </summary>
    public required string GameVersion { get; init; }

    /// <summary>
    /// Gets or initializes the eligible normal-species pool size.
    /// </summary>
    public int NormalPoolSize { get; init; }

    /// <summary>
    /// Gets or initializes the eligible normal-species pool fingerprint.
    /// </summary>
    public required string NormalPoolFingerprint { get; init; }

    /// <summary>
    /// Gets or initializes the custom-fusion pool schema version.
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

    /// <summary>
    /// Gets or initializes occupied defensive type profiles in stable chart order.
    /// </summary>
    public IReadOnlyList<TypeCoverageProfile> Profiles { get; init; } = [];
}
