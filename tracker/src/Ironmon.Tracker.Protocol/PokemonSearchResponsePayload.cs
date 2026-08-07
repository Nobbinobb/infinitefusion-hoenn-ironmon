namespace Ironmon.Tracker.Protocol;

/// <summary>
/// Returns stable Pokemon matches for a completed-run search.
/// </summary>
public sealed class PokemonSearchResponsePayload
{
    /// <summary>
    /// Initializes an empty Pokemon search response for protocol serialization.
    /// </summary>
    public PokemonSearchResponsePayload()
    {
    }

    /// <summary>
    /// Gets or initializes the matching Pokemon in display order.
    /// </summary>
    public IReadOnlyList<PokemonSearchMatch> Matches { get; init; } = [];

    /// <summary>
    /// Gets or initializes the total number of matches before paging.
    /// </summary>
    public int Total { get; init; }
}
