namespace Ironmon.Tracker.Collection.Queries.Results;

/// <summary>
/// Carries exactly the selected archive or catalogue columns.
/// </summary>
internal sealed class CollectionMetadataRecord
{
    /// <summary>
    /// Gets or initializes the exact sixteen-byte profile identity.
    /// </summary>
    public required byte[] Profile { get; init; }

    /// <summary>
    /// Gets or initializes the exact sixteen-byte collection epoch identity.
    /// </summary>
    public required byte[] Epoch { get; init; }

    /// <summary>
    /// Gets or initializes the exact sixteen-byte store incarnation identity.
    /// </summary>
    public required byte[] Incarnation { get; init; }

    /// <summary>
    /// Gets or initializes the exact eight-byte global collection revision.
    /// </summary>
    public required byte[] Revision { get; init; }
}

/// <summary>
/// Carries exactly the selected registration or reference-maintenance columns.
/// </summary>
internal sealed class StorageVersionRecord
{
    /// <summary>
    /// Gets or initializes the retained physical schema version.
    /// </summary>
    public required int Version { get; init; }

    /// <summary>
    /// Gets or initializes the exact eight-byte collection revision.
    /// </summary>
    public required byte[] Revision { get; init; }
}
