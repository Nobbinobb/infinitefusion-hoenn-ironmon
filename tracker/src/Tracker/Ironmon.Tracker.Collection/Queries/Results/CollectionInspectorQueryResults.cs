namespace Ironmon.Tracker.Collection.Queries.Results;

/// <summary>
/// Carries the exact operational columns selected by the collection inspector.
/// </summary>
internal sealed class InspectorTableRecord
{
    /// <summary>
    /// Gets or initializes the exact physical table name.
    /// </summary>
    public required string Name { get; init; }
}

/// <summary>
/// Carries the exact operational columns selected by the collection inspector.
/// </summary>
internal sealed class InspectorFamilyRecord
{
    /// <summary>
    /// Gets or initializes the stable registered family key.
    /// </summary>
    public required string Key { get; init; }

    /// <summary>
    /// Gets or initializes the complete hot series row count.
    /// </summary>
    public required long Series { get; init; }

    /// <summary>
    /// Gets or initializes the complete hot partition row count.
    /// </summary>
    public required long Hot { get; init; }

    /// <summary>
    /// Gets or initializes the complete cold partition row count.
    /// </summary>
    public required long Cold { get; init; }
}

/// <summary>
/// Carries the exact operational columns selected by the collection inspector.
/// </summary>
internal sealed class InspectorReceiptRecord
{
    /// <summary>
    /// Gets or initializes the exact sixteen-byte stream identity.
    /// </summary>
    public required byte[] Stream { get; init; }

    /// <summary>
    /// Gets or initializes the exact eight-byte durable receipt prefix.
    /// </summary>
    public required byte[] Sequence { get; init; }

    /// <summary>
    /// Gets or initializes the exact eight-byte collection revision.
    /// </summary>
    public required byte[] Revision { get; init; }
}

/// <summary>
/// Carries the exact operational columns selected by the collection inspector.
/// </summary>
internal sealed class InspectorOperationsRecord
{
    /// <summary>
    /// Gets or initializes the accepted batch total.
    /// </summary>
    public required long Batches { get; init; }

    /// <summary>
    /// Gets or initializes the accepted event total.
    /// </summary>
    public required long Events { get; init; }

    /// <summary>
    /// Gets or initializes the duplicate event total.
    /// </summary>
    public required long Duplicates { get; init; }

    /// <summary>
    /// Gets or initializes the received byte total.
    /// </summary>
    public required long Bytes { get; init; }

    /// <summary>
    /// Gets or initializes the duration of the previous commit in milliseconds.
    /// </summary>
    public required double CommitMilliseconds { get; init; }

    /// <summary>
    /// Gets or initializes the last operation update time in UTC Unix milliseconds.
    /// </summary>
    public required long Updated { get; init; }
}

/// <summary>
/// Carries the exact operational columns selected by the collection inspector.
/// </summary>
internal sealed class InspectorMetadataRecord
{
    /// <summary>
    /// Gets or initializes the stored positive schema version.
    /// </summary>
    public required int Version { get; init; }

    /// <summary>
    /// Gets or initializes the exact eight-byte collection revision.
    /// </summary>
    public required byte[] Revision { get; init; }
}

/// <summary>
/// Carries the exact operational columns selected by the collection inspector.
/// </summary>
internal sealed class InspectorCoverageRecord
{
    /// <summary>
    /// Gets or initializes the stable registered family key.
    /// </summary>
    public required string Family { get; init; }

    /// <summary>
    /// Gets or initializes the declared measure key.
    /// </summary>
    public required string Measure { get; init; }

    /// <summary>
    /// Gets or initializes the actual scope kind.
    /// </summary>
    public required string Scope { get; init; }

    /// <summary>
    /// Gets or initializes the exact native integer or overflow known count.
    /// </summary>
    public required object Known { get; init; }

    /// <summary>
    /// Gets or initializes the exact native integer or overflow unknown count.
    /// </summary>
    public required object Unknown { get; init; }

    /// <summary>
    /// Gets or initializes the exact native integer or overflow unsupported count.
    /// </summary>
    public required object Unsupported { get; init; }

    /// <summary>
    /// Gets or initializes the exact native integer or overflow not-applicable count.
    /// </summary>
    public required object NotApplicable { get; init; }
}
