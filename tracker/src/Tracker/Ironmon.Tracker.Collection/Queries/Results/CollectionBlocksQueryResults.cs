namespace Ironmon.Tracker.Collection;

/// <summary>
/// Carries exactly the selected archive or catalogue columns.
/// </summary>
internal sealed class ImmutableBlockRecord
{
    /// <summary>
    /// Gets or initializes the complete stored immutable block bytes.
    /// </summary>
    public required byte[] Payload { get; init; }

    /// <summary>
    /// Gets or initializes the exact uncompressed length in bytes.
    /// </summary>
    public required long Length { get; init; }

    /// <summary>
    /// Gets or initializes the complete thirty-two-byte integrity digest.
    /// </summary>
    public required byte[] Digest { get; init; }

    /// <summary>
    /// Gets or initializes the positive retained block format version.
    /// </summary>
    public required int Format { get; init; }

    /// <summary>
    /// Gets or initializes the retained block encoding identifier.
    /// </summary>
    public required string Codec { get; init; }
}

/// <summary>
/// Retains the exact selected fields of a generated collection operation.
/// </summary>
internal sealed class BlockCandidateRecord
{
    /// <summary>
    /// Gets or initializes the selected id value.
    /// </summary>
    public long Id { get; init; }

    /// <summary>
    /// Gets or initializes the selected codec value.
    /// </summary>
    public string Codec { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the selected payload value.
    /// </summary>
    public byte[] Payload { get; init; } = [];
}
