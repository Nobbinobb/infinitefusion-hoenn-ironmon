namespace Ironmon.Tracker.Collection.Queries.Results;

/// <summary>
/// Carries the selected immutable dictionary columns without a runtime mapping framework.
/// </summary>
internal sealed class CollectionValueRecord
{
    /// <summary>
    /// Gets or initializes the selected primitive representation code.
    /// </summary>
    public required int Kind { get; init; }

    /// <summary>
    /// Gets or initializes the complete selected immutable payload.
    /// </summary>
    public required byte[] Payload { get; init; }

    /// <summary>
    /// Gets or initializes the selected eight-byte integrity digest.
    /// </summary>
    public required byte[] Digest { get; init; }
}

/// <summary>
/// Carries exactly the candidate identity and payload selected by an intern lookup.
/// </summary>
internal sealed class CollectionValueCandidate
{
    /// <summary>
    /// Gets or initializes the selected dictionary identity.
    /// </summary>
    public required long Id { get; init; }

    /// <summary>
    /// Gets or initializes the complete candidate bytes that must be compared after digest lookup.
    /// </summary>
    public required byte[] Payload { get; init; }
}

/// <summary>
/// Carries exactly the selected ordered-read columns with original value precision.
/// </summary>
internal sealed class CollectionStringRecord
{
    /// <summary>
    /// Gets or initializes the exact immutable string identity.
    /// </summary>
    public required long Id { get; init; }

    /// <summary>
    /// Gets or initializes the complete retained string bytes.
    /// </summary>
    public required byte[] Payload { get; init; }

    /// <summary>
    /// Gets or initializes the complete eight-byte integrity digest.
    /// </summary>
    public required byte[] Digest { get; init; }
}

/// <summary>
/// Carries exactly the selected ordered-read columns with original value precision.
/// </summary>
internal sealed class CollectionValueReferenceRecord
{
    /// <summary>
    /// Gets or initializes the exact immutable value identity.
    /// </summary>
    public required long Id { get; init; }

    /// <summary>
    /// Gets or initializes the stored primitive representation code.
    /// </summary>
    public required int Kind { get; init; }

    /// <summary>
    /// Gets or initializes the complete retained value bytes.
    /// </summary>
    public required byte[] Payload { get; init; }

    /// <summary>
    /// Gets or initializes the complete eight-byte integrity digest.
    /// </summary>
    public required byte[] Digest { get; init; }
}

/// <summary>
/// Carries exactly the selected registration or reference-maintenance columns.
/// </summary>
internal sealed class ReferenceBlockRecord
{
    /// <summary>
    /// Gets or initializes the exact immutable block identity.
    /// </summary>
    public required long Id { get; init; }

    /// <summary>
    /// Gets or initializes the positive retained block format.
    /// </summary>
    public required int Format { get; init; }

    /// <summary>
    /// Gets or initializes the retained block encoding identifier.
    /// </summary>
    public required string Codec { get; init; }

    /// <summary>
    /// Gets or initializes the complete retained block payload.
    /// </summary>
    public required byte[] Payload { get; init; }
}

/// <summary>
/// Retains the exact selected fields of a generated collection operation.
/// </summary>
internal sealed class ValueRootRecord
{
    /// <summary>
    /// Gets or initializes the selected value value.
    /// </summary>
    public long Value { get; init; }
}

/// <summary>
/// Retains the exact selected fields of a generated collection operation.
/// </summary>
internal sealed class BinaryRootRecord
{
    /// <summary>
    /// Gets or initializes the selected value value.
    /// </summary>
    public byte[]? Value { get; init; }
}
