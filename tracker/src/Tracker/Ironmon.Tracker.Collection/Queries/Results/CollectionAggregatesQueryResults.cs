namespace Ironmon.Tracker.Collection;

/// <summary>
/// Carries exactly the selected delivery or partition columns.
/// </summary>
internal sealed class HotPartitionRecord
{
    /// <summary>
    /// Gets or initializes the active relational partition identity.
    /// </summary>
    public required long Id { get; init; }
}

/// <summary>
/// Carries exactly the stored columns selected by the AggregateCellRecord query projection.
/// </summary>
internal sealed class AggregateCellRecord
{
    /// <summary>
    /// Gets or initializes the native integer, overflow bytes or DBNull for an unknown scalar.
    /// </summary>
    public required object Value { get; init; } = DBNull.Value;

    /// <summary>
    /// Gets or initializes the exact known population as an integer or overflow bytes.
    /// </summary>
    public required object Known { get; init; } = DBNull.Value;

    /// <summary>
    /// Gets or initializes the exact unknown population as an integer or overflow bytes.
    /// </summary>
    public required object Unknown { get; init; } = DBNull.Value;

    /// <summary>
    /// Gets or initializes the exact unsupported population as an integer or overflow bytes.
    /// </summary>
    public required object Unsupported { get; init; } = DBNull.Value;

    /// <summary>
    /// Gets or initializes the exact not-applicable population as an integer or overflow bytes.
    /// </summary>
    public required object NotApplicable { get; init; } = DBNull.Value;
}

/// <summary>
/// Carries exactly the stored columns selected by the AggregateCoverageRecord query projection.
/// </summary>
internal sealed class AggregateCoverageRecord
{
    /// <summary>
    /// Gets or initializes the exact known population as an integer or overflow bytes.
    /// </summary>
    public required object Known { get; init; } = DBNull.Value;

    /// <summary>
    /// Gets or initializes the exact unknown population as an integer or overflow bytes.
    /// </summary>
    public required object Unknown { get; init; } = DBNull.Value;

    /// <summary>
    /// Gets or initializes the exact unsupported population as an integer or overflow bytes.
    /// </summary>
    public required object Unsupported { get; init; } = DBNull.Value;

    /// <summary>
    /// Gets or initializes the exact not-applicable population as an integer or overflow bytes.
    /// </summary>
    public required object NotApplicable { get; init; } = DBNull.Value;
}

/// <summary>
/// Carries exactly the stored columns selected by the OrderedStateRecord query projection.
/// </summary>
internal sealed class OrderedStateRecord
{
    /// <summary>
    /// Gets or initializes the complete compressed sufficient state.
    /// </summary>
    public required byte[] Payload { get; init; } = [];
}
