namespace Ironmon.Tracker.Collection;

/// <summary>
/// Carries exactly the selected delivery or partition columns.
/// </summary>
internal sealed class StreamDescriptorRecord
{
    /// <summary>
    /// Gets or initializes the complete immutable stream descriptor digest.
    /// </summary>
    public required byte[] Digest { get; init; }

    /// <summary>
    /// Gets or initializes the current producer stream state.
    /// </summary>
    public required string State { get; init; }
}

/// <summary>
/// Carries exactly the selected delivery or partition columns.
/// </summary>
internal sealed class StreamStateRecord
{
    /// <summary>
    /// Gets or initializes the current producer stream state.
    /// </summary>
    public required string State { get; init; }
}

/// <summary>
/// Carries exactly the selected delivery or partition columns.
/// </summary>
internal sealed class StreamReceiptRecord
{
    /// <summary>
    /// Gets or initializes the exact eight-byte durable contiguous frontier.
    /// </summary>
    public required byte[] Sequence { get; init; }
}

/// <summary>
/// Carries exactly the selected delivery or partition columns.
/// </summary>
internal sealed class ReducerCheckpointRecord
{
    /// <summary>
    /// Gets or initializes the complete compressed reducer checkpoint.
    /// </summary>
    public required byte[] Payload { get; init; }

    /// <summary>
    /// Gets or initializes the exact uncompressed checkpoint length in bytes.
    /// </summary>
    public required long Length { get; init; }
}
