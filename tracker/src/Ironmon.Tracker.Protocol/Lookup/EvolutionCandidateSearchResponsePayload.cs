namespace Ironmon.Tracker.Protocol.Lookup;

/// <summary>
/// Returns one page from a generated evolution candidate list.
/// </summary>
public sealed class EvolutionCandidateSearchResponsePayload
{
    /// <summary>
    /// Initializes an empty candidate response for protocol serialization.
    /// </summary>
    public EvolutionCandidateSearchResponsePayload()
    {
    }

    /// <summary>
    /// Gets or initializes the candidates on the requested page.
    /// </summary>
    public IReadOnlyList<EvolutionCandidateSnapshot> Matches { get; init; } = [];

    /// <summary>
    /// Gets or initializes the number of candidates matching the current filter.
    /// </summary>
    public int Total { get; init; }
}
