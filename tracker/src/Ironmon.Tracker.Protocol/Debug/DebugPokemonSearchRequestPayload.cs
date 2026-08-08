namespace Ironmon.Tracker.Protocol.Debug;

/// <summary>
/// Requests name-based Pokemon matches from the active run in authorized debug mode.
/// </summary>
public sealed class DebugPokemonSearchRequestPayload
{
    /// <summary>
    /// Initializes an empty active-run search request for protocol serialization.
    /// </summary>
    public DebugPokemonSearchRequestPayload()
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
    public int Limit { get; init; } = 20;

    /// <summary>
    /// Gets or initializes whether the search is restricted to normal species.
    /// </summary>
    public bool NormalOnly { get; init; }
}
