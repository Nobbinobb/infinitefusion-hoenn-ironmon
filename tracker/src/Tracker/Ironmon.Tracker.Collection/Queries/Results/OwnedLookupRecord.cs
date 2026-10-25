namespace Ironmon.Tracker.Collection.Queries.Results;

/// <summary>
/// Carries immutable configuration headers without expanding historical gameplay payloads.
/// </summary>
internal sealed class OwnedLookupRecord
{
    /// <summary>
    /// Gets or initializes the actual owned individual identity.
    /// </summary>
    public required long Individual { get; init; }

    /// <summary>
    /// Gets or initializes the monotonic per-individual revision number.
    /// </summary>
    public required long Revision { get; init; }

    /// <summary>
    /// Gets or initializes the interned species identity.
    /// </summary>
    public required long Species { get; init; }

    /// <summary>
    /// Gets or initializes the optional interned form identity.
    /// </summary>
    public long? Form { get; init; }

    /// <summary>
    /// Gets or initializes the complete immutable configuration block identity.
    /// </summary>
    public required long Detail { get; init; }
}
