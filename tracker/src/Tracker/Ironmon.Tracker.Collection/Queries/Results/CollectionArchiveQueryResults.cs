namespace Ironmon.Tracker.Collection;

/// <summary>
/// Carries exactly the selected archive or catalogue columns.
/// </summary>
internal sealed class AttemptScopeRecord
{
    /// <summary>
    /// Gets or initializes the exact retained scope identity.
    /// </summary>
    public required long Id { get; init; }
}

/// <summary>
/// Carries exactly the selected delivery or partition columns.
/// </summary>
internal sealed class ColdPartitionRecord
{
    /// <summary>
    /// Gets or initializes the immutable archived partition block identity.
    /// </summary>
    public required long Block { get; init; }
}

/// <summary>
/// Carries exactly the selected ordered-read columns with original value precision.
/// </summary>
internal sealed class ArchiveColdPartitionRecord
{
    /// <summary>
    /// Gets or initializes the registered family identity.
    /// </summary>
    public required int Family { get; init; }

    /// <summary>
    /// Gets or initializes the exact archived block identity.
    /// </summary>
    public required long Block { get; init; }

    /// <summary>
    /// Gets or initializes the exact immutable publication identity.
    /// </summary>
    public required long Publication { get; init; }
}

/// <summary>
/// Carries exactly the selected ordered-read columns with original value precision.
/// </summary>
internal sealed class ArchiveSeriesRecord
{
    /// <summary>
    /// Gets or initializes the exact dimension-series identity.
    /// </summary>
    public required long Series { get; init; }

    /// <summary>
    /// Gets or initializes the complete ordered tuple identity.
    /// </summary>
    public required long Tuple { get; init; }
}

/// <summary>
/// Carries exactly the selected ordered-read columns with original value precision.
/// </summary>
internal sealed class ArchiveCellRecord
{
    /// <summary>
    /// Gets or initializes the complete signed 64-bit measure ordinal.
    /// </summary>
    public required long Measure { get; init; }

    /// <summary>
    /// Gets or initializes the original scalar integer, overflow bytes or DBNull for unavailable data.
    /// </summary>
    public required object Value { get; init; }

    /// <summary>
    /// Gets or initializes the known population in its original integer or overflow storage class.
    /// </summary>
    public required object Known { get; init; }

    /// <summary>
    /// Gets or initializes the unknown population in its original integer or overflow storage class.
    /// </summary>
    public required object Unknown { get; init; }

    /// <summary>
    /// Gets or initializes the unsupported population in its original integer or overflow storage class.
    /// </summary>
    public required object Unsupported { get; init; }

    /// <summary>
    /// Gets or initializes the not-applicable population in its original integer or overflow storage class.
    /// </summary>
    public required object NotApplicable { get; init; }

    /// <summary>
    /// Gets or initializes the exact eight-byte collection revision.
    /// </summary>
    public required byte[] Revision { get; init; }
}

/// <summary>
/// Carries exactly the selected ordered-read columns with original value precision.
/// </summary>
internal sealed class ArchiveMemberRecord
{
    /// <summary>
    /// Gets or initializes the complete signed 64-bit measure ordinal.
    /// </summary>
    public required long Measure { get; init; }

    /// <summary>
    /// Gets or initializes the complete exact member identity.
    /// </summary>
    public required long Member { get; init; }
}

/// <summary>
/// Carries exactly the selected ordered-read columns with original value precision.
/// </summary>
internal sealed class ArchiveCoverageRecord
{
    /// <summary>
    /// Gets or initializes the complete signed 64-bit measure ordinal.
    /// </summary>
    public required long Measure { get; init; }

    /// <summary>
    /// Gets or initializes the known population in its original integer or overflow storage class.
    /// </summary>
    public required object Known { get; init; }

    /// <summary>
    /// Gets or initializes the unknown population in its original integer or overflow storage class.
    /// </summary>
    public required object Unknown { get; init; }

    /// <summary>
    /// Gets or initializes the unsupported population in its original integer or overflow storage class.
    /// </summary>
    public required object Unsupported { get; init; }

    /// <summary>
    /// Gets or initializes the not-applicable population in its original integer or overflow storage class.
    /// </summary>
    public required object NotApplicable { get; init; }

    /// <summary>
    /// Gets or initializes the exact eight-byte collection revision.
    /// </summary>
    public required byte[] Revision { get; init; }
}

/// <summary>
/// Carries exactly the selected ordered-read columns with original value precision.
/// </summary>
internal sealed class ArchiveOrderedRecord
{
    /// <summary>
    /// Gets or initializes the complete ordered tuple identity.
    /// </summary>
    public required long Tuple { get; init; }

    /// <summary>
    /// Gets or initializes the retained context identity, or null when absent.
    /// </summary>
    public required long? Context { get; init; }

    /// <summary>
    /// Gets or initializes the complete original positive format version.
    /// </summary>
    public required long Format { get; init; }

    /// <summary>
    /// Gets or initializes the original state encoding identifier.
    /// </summary>
    public required string Codec { get; init; }

    /// <summary>
    /// Gets or initializes the complete retained state bytes.
    /// </summary>
    public required byte[] Payload { get; init; }

    /// <summary>
    /// Gets or initializes the exact sixteen-byte source stream identity.
    /// </summary>
    public required byte[] Stream { get; init; }

    /// <summary>
    /// Gets or initializes the exact eight-byte source sequence.
    /// </summary>
    public required byte[] Sequence { get; init; }

    /// <summary>
    /// Gets or initializes the exact eight-byte collection revision.
    /// </summary>
    public required byte[] Revision { get; init; }
}

/// <summary>
/// Retains the exact selected fields of a generated collection operation.
/// </summary>
internal sealed class ScopePartitionRecord
{
    /// <summary>
    /// Gets or initializes the selected partition value.
    /// </summary>
    public long Partition { get; init; }

    /// <summary>
    /// Gets or initializes the selected family value.
    /// </summary>
    public int Family { get; init; }
}
