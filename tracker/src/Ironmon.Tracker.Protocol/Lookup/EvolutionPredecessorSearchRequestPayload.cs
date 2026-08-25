namespace Ironmon.Tracker.Protocol.Lookup;

/// <summary>
/// Requests one progressive page of generated evolution predecessors for a completed run.
/// </summary>
public sealed class EvolutionPredecessorSearchRequestPayload
{
    /// <summary>
    /// Initializes an empty predecessor request for protocol serialization.
    /// </summary>
    public EvolutionPredecessorSearchRequestPayload()
    {
    }

    /// <summary>
    /// Gets or initializes the target species identifier.
    /// </summary>
    public required string SpeciesId { get; init; }

    /// <summary>
    /// Gets or initializes the zero-based result offset.
    /// </summary>
    public int Offset { get; init; }

    /// <summary>
    /// Gets or initializes the maximum number of predecessors to return.
    /// </summary>
    public int Limit { get; init; } = TrackerProtocol.EvolutionPredecessorPageSize;

    /// <summary>
    /// Gets or initializes the exact tracker-computed fusion predecessor assignments, or null when tracker-side assignment data is unavailable.
    /// </summary>
    public IReadOnlyList<EvolutionPredecessorAssignmentPayload>? PredecessorAssignments { get; init; }

    /// <summary>
    /// Gets or initializes the completed-run reconstruction recipe.
    /// </summary>
    public required CompletedRunRecipePayload Recipe { get; init; }
}
