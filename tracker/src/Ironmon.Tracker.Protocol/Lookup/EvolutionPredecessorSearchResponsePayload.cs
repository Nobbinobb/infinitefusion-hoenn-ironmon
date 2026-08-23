namespace Ironmon.Tracker.Protocol.Lookup;

/// <summary>
/// Returns one progressive page of generated evolution predecessors.
/// </summary>
public sealed class EvolutionPredecessorSearchResponsePayload
{
    /// <summary>
    /// Initializes an empty predecessor response for protocol serialization.
    /// </summary>
    public EvolutionPredecessorSearchResponsePayload()
    {
    }

    /// <summary>
    /// Gets or initializes the predecessors on the requested page.
    /// </summary>
    public IReadOnlyList<EvolutionTargetSnapshot> Matches { get; init; } = [];

    /// <summary>
    /// Gets or initializes the zero-based result offset represented by this page.
    /// </summary>
    public int Offset { get; init; }

    /// <summary>
    /// Gets or initializes the requested page limit.
    /// </summary>
    public int Limit { get; init; } = TrackerProtocol.EvolutionPredecessorPageSize;

    /// <summary>
    /// Gets or initializes whether another page is available, unresolved, or unnecessary.
    /// </summary>
    public EvolutionPredecessorContinuation Continuation { get; init; }

    /// <summary>
    /// Gets or initializes the next result offset when the lookup is not complete.
    /// </summary>
    public int? NextOffset { get; init; }
}
