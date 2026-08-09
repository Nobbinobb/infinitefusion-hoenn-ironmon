namespace Ironmon.Tracker.Protocol.Lookup;

/// <summary>
/// Requests name-based Pokemon matches for one completed run.
/// </summary>
public sealed class PokemonSearchRequestPayload
{
    /// <summary>
    /// Initializes an empty Pokemon search request for protocol serialization.
    /// </summary>
    public PokemonSearchRequestPayload()
    {
    }

    /// <summary>
    /// Gets or initializes the user-entered search text.
    /// </summary>
    public required string Query { get; init; }

    /// <summary>
    /// Gets or initializes the zero-based result offset.
    /// </summary>
    public int Offset { get; init; }

    /// <summary>
    /// Gets or initializes the maximum number of matches to return.
    /// </summary>
    public int Limit { get; init; } = TrackerProtocol.DefaultSearchPageSize;

    /// <summary>
    /// Gets or initializes whether the search is restricted to normal species.
    /// </summary>
    public bool NormalOnly { get; init; }

    /// <summary>
    /// Gets or initializes the completed-run reconstruction recipe.
    /// </summary>
    public required CompletedRunRecipePayload Recipe { get; init; }
}
