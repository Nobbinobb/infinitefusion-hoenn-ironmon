namespace Ironmon.Tracker.Protocol.Lookup;

/// <summary>
/// Requests one filtered page of valid evolution candidates for a completed run.
/// </summary>
public sealed class EvolutionCandidateSearchRequestPayload
{
    /// <summary>
    /// Initializes an empty candidate request for protocol serialization.
    /// </summary>
    public EvolutionCandidateSearchRequestPayload()
    {
    }

    /// <summary>
    /// Gets or initializes the source species identifier.
    /// </summary>
    public required string SpeciesId { get; init; }

    /// <summary>
    /// Gets or initializes the normal or fusion-component list being requested.
    /// </summary>
    public EvolutionCandidateSide Side { get; init; }

    /// <summary>
    /// Gets or initializes the optional candidate name filter.
    /// </summary>
    public string Query { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the zero-based result offset.
    /// </summary>
    public int Offset { get; init; }

    /// <summary>
    /// Gets or initializes the maximum number of candidates to return.
    /// </summary>
    public int Limit { get; init; } = TrackerProtocol.EvolutionCandidatePageSize;

    /// <summary>
    /// Gets or initializes little-endian packed tracker-computed fusion candidate identifiers and base-stat totals when available.
    /// </summary>
    public byte[]? PackedCandidateAssignments { get; init; }

    /// <summary>
    /// Gets or initializes the completed-run reconstruction recipe.
    /// </summary>
    public required CompletedRunRecipePayload Recipe { get; init; }
}
