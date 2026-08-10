namespace Ironmon.Tracker.Protocol.Debug;

/// <summary>
/// Requests one page of wild occurrences from an authorized active run.
/// </summary>
public sealed class DebugWildOccurrenceSearchRequestPayload
{
    /// <summary>
    /// Initializes an empty active-run wild-occurrence request for protocol serialization.
    /// </summary>
    public DebugWildOccurrenceSearchRequestPayload()
    {
    }

    /// <summary>
    /// Gets or initializes the generated species identifier.
    /// </summary>
    public required string SpeciesId { get; init; }

    /// <summary>
    /// Gets or initializes the zero-based result offset.
    /// </summary>
    public int Offset { get; init; }

    /// <summary>
    /// Gets or initializes the maximum number of occurrences to return.
    /// </summary>
    public int Limit { get; init; } = TrackerProtocol.OccurrencePageSize;
}

/// <summary>
/// Requests one page of trainer occurrences from an authorized active run.
/// </summary>
public sealed class DebugTrainerOccurrenceSearchRequestPayload
{
    /// <summary>
    /// Initializes an empty active-run trainer-occurrence request for protocol serialization.
    /// </summary>
    public DebugTrainerOccurrenceSearchRequestPayload()
    {
    }

    /// <summary>
    /// Gets or initializes the generated species identifier.
    /// </summary>
    public required string SpeciesId { get; init; }

    /// <summary>
    /// Gets or initializes the zero-based result offset.
    /// </summary>
    public int Offset { get; init; }

    /// <summary>
    /// Gets or initializes the maximum number of occurrences to return.
    /// </summary>
    public int Limit { get; init; } = TrackerProtocol.OccurrencePageSize;
}
