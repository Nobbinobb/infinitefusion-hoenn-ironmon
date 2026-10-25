namespace Ironmon.Tracker.Collection.Queries.Results;

/// <summary>
/// Carries exactly the selected archive or catalogue columns.
/// </summary>
internal sealed class FamilyBindingRecord
{
    /// <summary>
    /// Gets or initializes the original registered aggregate family key.
    /// </summary>
    public required string Key { get; init; }

    /// <summary>
    /// Gets or initializes the complete original definition digest.
    /// </summary>
    public required byte[] Digest { get; init; }
}

/// <summary>
/// Carries exactly the selected ordered-read columns with original value precision.
/// </summary>
internal sealed class HistoricalDefinitionRecord
{
    /// <summary>
    /// Gets or initializes the complete original family, group or contract identity.
    /// </summary>
    public required long Id { get; init; }

    /// <summary>
    /// Gets or initializes the original retained block identity or complete JSON definition text.
    /// </summary>
    public required object Value { get; init; }
}

/// <summary>
/// Carries exactly the selected registration or reference-maintenance columns.
/// </summary>
internal sealed class SummaryGroupBindingRecord
{
    /// <summary>
    /// Gets or initializes the original summary projection key.
    /// </summary>
    public required string Key { get; init; }

    /// <summary>
    /// Gets or initializes the complete original definition digest.
    /// </summary>
    public required byte[] Digest { get; init; }
}
