namespace Ironmon.Tracker.Protocol.Lookup;

/// <summary>
/// Announces one or more newly discovered entries from a single area category.
/// </summary>
public sealed class AreaDiscoveryPackagePayload
{
    /// <summary>
    /// Initializes an empty discovery package for protocol serialization.
    /// </summary>
    public AreaDiscoveryPackagePayload()
    {
    }

    /// <summary>
    /// Gets or initializes the stable package identifier used for acknowledgments and duplicate suppression.
    /// </summary>
    public required string PackageId { get; init; }

    /// <summary>
    /// Gets or initializes the stable logical area identifier.
    /// </summary>
    public required string AreaId { get; init; }

    /// <summary>
    /// Gets or initializes the discovered content category.
    /// </summary>
    public AreaContentCategory Category { get; init; }

    /// <summary>
    /// Gets or initializes the stable entry keys discovered together.
    /// </summary>
    public IReadOnlyList<string> EntryKeys { get; init; } = [];
}

/// <summary>
/// Confirms that one discovery package has been persisted by the tracker.
/// </summary>
public sealed class AreaDiscoveryAcknowledgmentPayload
{
    /// <summary>
    /// Initializes an empty discovery acknowledgment for protocol serialization.
    /// </summary>
    public AreaDiscoveryAcknowledgmentPayload()
    {
    }

    /// <summary>
    /// Gets or initializes the acknowledged package identifier.
    /// </summary>
    public required string PackageId { get; init; }
}
